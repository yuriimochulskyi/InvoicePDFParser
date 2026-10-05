using System.Diagnostics;
using System.Text.Json;
using InvoiceAgent.Api.Data;
using InvoiceAgent.Api.Models;
using InvoiceAgent.Api.Tools;

namespace InvoiceAgent.Api.Agents;

/// <summary>The stored record plus the typed invoice the run produced (null when extraction failed).</summary>
public sealed record ProcessingResult(InvoiceRecord Record, InvoiceDto? Invoice)
{
    public Guid Id => Record.Id;
    public InvoiceStatus Status => Record.Status;
    public string? ReviewReason => Record.ReviewReason;
}

/// <summary>
/// The whole pipeline: store file → preflight → agent → deterministic review → persist.
/// Used by the API and the evals.
/// </summary>
public sealed class InvoiceProcessingService(
    PdfFileStore store,
    IPdfTextSource textSource,
    InvoiceExtractionAgent agent,
    ChatClientFactory.ModelInfo model,
    AiOptions options,
    InvoiceDbContext db,
    ILogger<InvoiceProcessingService> logger)
{
    /// <exception cref="UnreadablePdfException">The upload is not a readable PDF (maps to 400).</exception>
    /// <exception cref="LlmUnavailableException">The model endpoint failed (maps to 503).</exception>
    public async Task<ProcessingResult> ProcessAsync(Stream pdf, string fileName, CancellationToken ct = default)
    {
        var fileId = await store.SaveAsync(pdf, ct);

        // Decide in code whether this document is worth a model call at all.
        var sw = Stopwatch.StartNew();
        ExtractionRun run;
        try
        {
            var preflight = DocumentPreflight.Check(textSource.Extract(fileId), options.MaxDocumentChars);
            run = preflight.Ok
                ? await agent.RunAsync(fileId, ct)
                : new ExtractionRun(null, preflight.Document.Text, null, model.Provider, model.Model, 0, 0, sw.ElapsedMilliseconds, [], preflight.ReviewReason);
        }
        catch
        {
            // No record will reference this upload (rejected file, provider outage, cancellation): do not keep it.
            store.Delete(fileId);
            throw;
        }

        var (status, reason) = InvoiceReviewPolicy.Decide(run);

        var record = new InvoiceRecord
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            FileName = fileName,
            Status = status,
            ReviewReason = reason,
            RawText = run.RawText,
            ExtractedJson = run.Invoice is null ? run.RawJson : JsonSerializer.Serialize(run.Invoice, JsonSerializerOptions.Web),
            Provider = run.Provider,
            Model = run.Model,
            InputTokens = run.InputTokens,
            OutputTokens = run.OutputTokens,
            LatencyMs = run.LatencyMs,
            ToolCalls = string.Join(",", run.ToolCalls),
        };
        db.Invoices.Add(record);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Agent run {InvoiceId} {FileName}: {Provider}/{Model}, tokens in={InputTokens} out={OutputTokens}, latency={LatencyMs} ms, tools=[{ToolCalls}], status={Status}, reason={ReviewReason}",
            record.Id, fileName, run.Provider, run.Model, run.InputTokens, run.OutputTokens, run.LatencyMs,
            record.ToolCalls, status, reason);

        return new(record, run.Invoice);
    }
}
