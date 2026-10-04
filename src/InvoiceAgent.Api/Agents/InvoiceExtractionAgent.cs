using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using InvoiceAgent.Api.Models;
using InvoiceAgent.Api.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace InvoiceAgent.Api.Agents;

public sealed record ExtractionRun(
    InvoiceDto? Invoice,
    string? RawText,
    string? RawJson,
    string Provider,
    string Model,
    long? InputTokens,
    long? OutputTokens,
    long LatencyMs,
    IReadOnlyList<string> ToolCalls,
    string? Error);

public sealed class InvoiceExtractionAgent(
    IChatClient chatClient,
    ChatClientFactory.ModelInfo model,
    AiOptions options,
    PdfTextExtractor pdf,
    ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<InvoiceExtractionAgent>();

    public const string SystemInstructions = "You extract structured data from invoices of any layout and language.";

    private const string Rules = """

        Rules:
        - Always call ExtractPdfText with the given fileId first; work only from the text it returns.
        - If a value is not present in the document, return null. Never invent or guess values.
        - Dates must be yyyy-MM-dd. Currency must be an ISO 4217 code (e.g. "₴"/"грн" → UAH, "€" → EUR, "$" → USD).
        - Amounts are plain numbers without currency symbols or thousands separators.
        - discountAmount is a positive number, or null if the document shows no discount (not 0).
          Same for taxAmount, subtotal and dueDate: null when absent. Shipping/delivery charges are line items.
        - lineItems is an empty array if the document has no itemised lines.
        - invoiceNumber is only the identifier, without labels such as "Nr.", "No.", "№", "#" or "Invoice".
        - After drafting, call ValidateTotals. On a mismatch, re-read the text and fix misread numbers.
          Never change numbers just to make the check pass: if the document itself does not add up, keep its values.
        """;

    public async Task<ExtractionRun> RunAsync(string fileId, CancellationToken ct = default)
    {
        var callerCt = ct;
        var runTimeout = TimeSpan.FromSeconds(options.RunTimeoutSeconds);
        // Tools are created per run so we can see exactly what this run called.
        var tools = new InvoiceTools(pdf, _logger);
        AIAgent agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "InvoiceExtractor",
            // The injected client already carries the function-invocation loop with hard caps
            // (see AddInvoiceAgent); without this the agent would wrap it in a second, uncapped one.
            UseProvidedChatClientAsIs = true,
            ChatOptions = new ChatOptions
            {
                Instructions = SystemInstructions + Rules,
                Tools = tools.AsAITools(),
                Temperature = 0,
                // A runaway generation (e.g. after a context overflow) must fail fast, not loop for minutes.
                MaxOutputTokens = 4096,
                // qwen3 "thinks" by default: ~8x the tokens and latency for no accuracy gain on extraction.
                Reasoning = model.Provider == "Ollama" ? new ReasoningOptions { Effort = ReasoningEffort.None } : null,
            },
        }, loggerFactory);

        var sw = Stopwatch.StartNew();
        InvoiceDto? invoice = null;
        string? rawJson = null, error = null;
        long inTokens = 0, outTokens = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(runTimeout);
        ct = timeout.Token;
        try
        {
            // Two turns in one session. A JSON-schema response format constrains every
            // model turn, so with it set from the start the model cannot call tools and
            // invents data. Turn 1 is the free agentic loop (tool calls), turn 2 only
            // formats what is already in the conversation into the InvoiceDto schema.
            AgentSession session = await agent.CreateSessionAsync(ct);

            AgentResponse work = await agent.RunAsync(
                $"Extract the invoice from the uploaded PDF with fileId \"{fileId}\". " +
                "Read it with ExtractPdfText, then pass the amounts you read to ValidateTotals and fix misreads. " +
                "Do not write out the invoice in your reply; when the check is done, reply only with \"done\".",
                session, cancellationToken: ct);
            AddUsage(work);

            // No tools on the formatting turn: providers that allow tools alongside a JSON
            // schema (Azure) must not start a second tool loop here.
            var formatOnly = new ChatClientAgentRunOptions(new ChatOptions { ToolMode = ChatToolMode.None });
            AgentResponse<InvoiceDto> final = await agent.RunAsync<InvoiceDto>(
                "Now return the final extracted invoice as JSON matching the schema. Use null for anything not in the document.",
                session, options: formatOnly, cancellationToken: ct);
            AddUsage(final);

            rawJson = final.Text;
            try
            {
                invoice = final.Result;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // Empty or non-JSON output is a model failure for this document, not an outage.
                error = $"Model output could not be parsed as an invoice: {ex.Message}";
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !callerCt.IsCancellationRequested)
        {
            error = $"Agent run timed out after {runTimeout.TotalSeconds:0} s (slow model or a very long document).";
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            // Endpoint down, bad key, quota: an infrastructure failure, not a document that needs review.
            throw new LlmUnavailableException(model.Provider, model.Model, ex);
        }
        sw.Stop();

        return new ExtractionRun(invoice, tools.ExtractedText, rawJson, model.Provider, model.Model,
            inTokens, outTokens, sw.ElapsedMilliseconds, tools.Calls, error);

        void AddUsage(AgentResponse r)
        {
            inTokens += r.Usage?.InputTokenCount ?? 0;
            outTokens += r.Usage?.OutputTokenCount ?? 0;
        }
    }

    /// <summary>The OpenAI client retries and then wraps the failures in an AggregateException.</summary>
    private static bool IsTransportFailure(Exception ex) => ex switch
    {
        ClientResultException or HttpRequestException or IOException => true,
        AggregateException agg => agg.InnerExceptions.Count > 0 && agg.InnerExceptions.All(IsTransportFailure),
        _ => false,
    };
}
