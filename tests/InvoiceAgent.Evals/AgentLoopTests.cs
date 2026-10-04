using InvoiceAgent.Api.Agents;
using InvoiceAgent.Api.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InvoiceAgent.Evals;

/// <summary>
/// The agent loop without a model: a scripted IChatClient plays the LLM, while the
/// function-invocation loop, both tools, the real PDF and the review policy are real.
/// </summary>
public class AgentLoopTests
{
    private static readonly string DePdf = Samples.All().Single(s => s.Name == "02-de-rechnung").PdfPath;

    private static async Task<(ExtractionRun Run, ScriptedChatClient Client, InvoiceReviewPolicyResult Decision)> RunAsync(
        params Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatMessage>[] script)
    {
        var client = new ScriptedChatClient(script);
        var workDir = Path.Combine(Path.GetTempPath(), "invoice-agent-tests", Guid.NewGuid().ToString("N"));
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddInvoiceAgent(new AiOptions { RunTimeoutSeconds = 30 }, Path.Combine(workDir, "uploads"), client, new("Scripted", "fake"))
            .BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var store = scope.ServiceProvider.GetRequiredService<PdfFileStore>();
        await using var pdf = File.OpenRead(DePdf);
        var fileId = await store.SaveAsync(pdf, TestContext.Current.CancellationToken);

        var run = await scope.ServiceProvider.GetRequiredService<InvoiceExtractionAgent>().RunAsync(fileId, TestContext.Current.CancellationToken);
        var (status, reason) = InvoiceReviewPolicy.Decide(run);
        return (run, client, new(status, reason));
    }

    private const string DeInvoiceJson = """
        {"vendorName":"Müller Webdesign GmbH","vendorTaxId":"DE287654321","invoiceNumber":"RE-2026-0147",
         "invoiceDate":"2026-08-15","dueDate":"2026-09-14","currency":"EUR",
         "lineItems":[{"description":"Webentwicklung (Stunden)","quantity":12,"unitPrice":85.00,"amount":1020.00},
                      {"description":"Hosting-Paket Business (12 Monate)","quantity":1,"unitPrice":240.00,"amount":240.00},
                      {"description":"SSL-Zertifikat","quantity":1,"unitPrice":49.90,"amount":49.90}],
         "subtotal":1309.90,"taxAmount":248.88,"discountAmount":null,"total":1558.78}
        """;

    [Fact]
    public async Task HappyPath_ToolTurnThenSchemaTurn()
    {
        var (run, client, decision) = await RunAsync(
            // turn 1: the "model" reads the PDF, checks the numbers, says done
            (messages, _) => ScriptedChatClient.ToolCall("ExtractPdfText", new { fileId = FileIdFrom(messages) })(messages, null),
            ScriptedChatClient.ToolCall("ValidateTotals", new
            {
                lineItems = new[] { new { quantity = 12, unitPrice = 85.00, amount = 1020.00 }, new { quantity = 1, unitPrice = 240.00, amount = 240.00 }, new { quantity = 1, unitPrice = 49.90, amount = 49.90 } },
                subtotal = 1309.90, taxAmount = 248.88, discountAmount = (double?)null, total = 1558.78,
            }),
            ScriptedChatClient.Text("done"),
            // turn 2: formatting
            ScriptedChatClient.Text(DeInvoiceJson));

        Assert.Null(run.Error);
        Assert.Equal(["ExtractPdfText", "ValidateTotals"], run.ToolCalls);
        Assert.Contains("Rechnungsbetrag", run.RawText);
        Assert.Equal(1558.78m, run.Invoice!.Total);
        Assert.Equal(InvoiceStatus.Parsed, decision.Status);

        // Usage is summed over every model call (3 in turn 1, 1 in turn 2).
        Assert.Equal(400, run.InputTokens);
        Assert.Equal(40, run.OutputTokens);

        // Contract of the two turns: tools and no schema first, schema and no tools last.
        Assert.Equal(4, client.Requests.Count);
        Assert.All(client.Requests.Take(3), r => Assert.Null(r.Options?.ResponseFormat));
        Assert.IsType<ChatResponseFormatJson>(client.Requests[^1].Options?.ResponseFormat);
        Assert.IsType<NoneChatToolMode>(client.Requests[^1].Options?.ToolMode);

        // The tool result really went back to the model.
        var toolResults = client.Requests[1].Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().ToList();
        Assert.Single(toolResults);
        var toolResult = toolResults[0].Result?.ToString();
        Assert.Contains("Rechnungsbetrag", toolResult);
        Assert.StartsWith("<document", toolResult);          // delimited as untrusted data for the model...
        Assert.DoesNotContain("<document", run.RawText);     // ...but stored raw for grounding
    }

    [Fact]
    public async Task ModelReturnsNoJson_IsNeedsReview_Not503()
    {
        var (run, _, decision) = await RunAsync(
            (messages, _) => ScriptedChatClient.ToolCall("ExtractPdfText", new { fileId = FileIdFrom(messages) })(messages, null),
            ScriptedChatClient.Text("done"),
            ScriptedChatClient.Text("Sorry, I cannot do that."));

        Assert.Null(run.Invoice);
        Assert.Contains("could not be parsed", run.Error);
        Assert.Equal(InvoiceStatus.NeedsReview, decision.Status);
        Assert.Equal(run.Error, decision.Reason);
    }

    [Fact]
    public async Task ModelNeverReadsThePdf_IsNeedsReview()
    {
        var (run, _, decision) = await RunAsync(
            ScriptedChatClient.Text("done"),
            ScriptedChatClient.Text(DeInvoiceJson));

        Assert.NotNull(run.Invoice);
        Assert.Empty(run.ToolCalls);
        Assert.Equal(InvoiceStatus.NeedsReview, decision.Status);
        Assert.Contains("did not read", decision.Reason);
    }

    [Fact]
    public async Task HttpFailure_ThrowsLlmUnavailable()
    {
        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() =>
            RunAsync(ScriptedChatClient.Throw(new HttpRequestException("connection refused"))));
        Assert.Contains("Scripted", ex.Message);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task RunawayToolCalls_StopAtIterationCap()
    {
        // A model that keeps calling ValidateTotals forever must be stopped by the hard cap,
        // not by the run timeout. 20 scripted calls, cap is 8.
        // Like a real model, the script only emits a tool call while tools are offered;
        // once the loop withdraws them (cap reached, or the ToolMode.None formatting turn) it answers with text.
        var call = ScriptedChatClient.ToolCall("ValidateTotals", new { lineItems = Array.Empty<object>(), subtotal = (double?)null, taxAmount = (double?)null, discountAmount = (double?)null, total = 1.0 });
        Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatMessage> callWhileAllowed = (messages, options) =>
            options?.Tools is { Count: > 0 } && options.ToolMode is not NoneChatToolMode
                ? call(messages, options)
                : ScriptedChatClient.Text(DeInvoiceJson)(messages, options);

        var (run, client, decision) = await RunAsync([.. Enumerable.Repeat(callWhileAllowed, 22)]);

        Assert.InRange(run.ToolCalls.Count, 1, ServiceCollectionExtensions.MaxToolIterations);
        Assert.True(client.Requests.Count <= ServiceCollectionExtensions.MaxToolIterations + 2, $"{client.Requests.Count} model calls");
        Assert.Equal(InvoiceStatus.NeedsReview, decision.Status);
    }

    private static string FileIdFrom(IReadOnlyList<ChatMessage> messages)
    {
        var text = messages.Last(m => m.Role == ChatRole.User).Text;
        var start = text.IndexOf('"') + 1;
        return text[start..text.IndexOf('"', start)];
    }

    public sealed record InvoiceReviewPolicyResult(InvoiceStatus Status, string? Reason);
}
