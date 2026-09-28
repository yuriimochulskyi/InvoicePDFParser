using System.Diagnostics;
using System.Text.Json;
using InvoiceAgent.Api.Models;
using InvoiceAgent.Api.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using ChatClient = OpenAI.Chat.ChatClient;

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
    ChatClient chatClient,
    ChatClientFactory.ModelInfo model,
    PdfTextExtractor pdf,
    ILogger<InvoiceExtractionAgent> logger)
{
    public const string SystemInstructions = "You extract structured data from invoices of any layout and language.";

    private const string Rules = """

        Rules:
        - Always call ExtractPdfText with the given fileId first; work only from the text it returns.
        - If a value is not present in the document, return null. Never invent or guess values.
        - Dates must be yyyy-MM-dd. Currency must be an ISO 4217 code (e.g. "â‚´"/"Ð³Ñ€Ð½" â†’ UAH, "â‚¬" â†’ EUR, "$" â†’ USD).
        - Amounts are plain numbers without currency symbols or thousands separators.
        - discountAmount is a positive number. Shipping/delivery charges are line items.
        - lineItems is an empty array if the document has no itemised lines.
        """;

    public async Task<ExtractionRun> RunAsync(string fileId, CancellationToken ct = default)
    {
        // Tools are created per run so we can see exactly what this run called.
        var tools = new InvoiceTools(pdf);
        AIAgent agent = chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "InvoiceExtractor",
            ChatOptions = new ChatOptions
            {
                Instructions = SystemInstructions + Rules,
                Tools = tools.AsAITools(),
                Temperature = 0,
                // qwen3 "thinks" by default: ~8x the tokens and latency for no accuracy gain on extraction.
                Reasoning = model.Provider == "Ollama" ? new ReasoningOptions { Effort = ReasoningEffort.None } : null,
            },
        });

        var sw = Stopwatch.StartNew();
        InvoiceDto? invoice = null;
        string? rawJson = null, error = null;
        long inTokens = 0, outTokens = 0;
        try
        {
            // Two turns in one session. A JSON-schema response format constrains every
            // model turn, so with it set from the start the model cannot call tools and
            // invents data. Turn 1 is the free agentic loop (tool calls), turn 2 only
            // formats what is already in the conversation into the InvoiceDto schema.
            AgentSession session = await agent.CreateSessionAsync(ct);

            AgentResponse work = await agent.RunAsync(
                $"Extract the invoice from the uploaded PDF with fileId \"{fileId}\". " +
                "Read it with the tools, then list every invoice field and line item you found.",
                session, cancellationToken: ct);
            AddUsage(work);

            AgentResponse<InvoiceDto> final = await agent.RunAsync<InvoiceDto>(
                "Now return the extracted invoice as JSON matching the schema. Use null for anything not in the document.",
                session, cancellationToken: ct);
            AddUsage(final);

            rawJson = final.Text;
            invoice = final.Result;
        }
        catch (JsonException ex)
        {
            error = $"Model returned invalid JSON: {ex.Message}";
        }
        sw.Stop();

        void AddUsage(AgentResponse r)
        {
            inTokens += r.Usage?.InputTokenCount ?? 0;
            outTokens += r.Usage?.OutputTokenCount ?? 0;
        }

        var run = new ExtractionRun(invoice, tools.ExtractedText, rawJson, model.Provider, model.Model,
            inTokens, outTokens, sw.ElapsedMilliseconds, tools.Calls, error);

        logger.LogInformation(
            "Agent run {Provider}/{Model}: tokens in={InputTokens} out={OutputTokens}, latency={LatencyMs} ms, tools=[{ToolCalls}], error={Error}",
            run.Provider, run.Model, run.InputTokens, run.OutputTokens, run.LatencyMs, string.Join(", ", run.ToolCalls), run.Error);

        return run;
    }
}
