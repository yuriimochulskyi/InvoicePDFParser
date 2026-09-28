using System.Text.Json;
using InvoiceAgent.Api.Data;
using InvoiceAgent.Api.Models;
using InvoiceAgent.Api.Tools;

namespace InvoiceAgent.Api.Agents;

public sealed record ProcessingResult(Guid Id, InvoiceStatus Status, string? ReviewReason, InvoiceDto? Invoice);

/// <summary>The whole pipeline: store file → agent → deterministic review → persist. Used by the API and the evals.</summary>
public sealed class InvoiceProcessingService(
    PdfFileStore store,
    InvoiceExtractionAgent agent,
    InvoiceDbContext db,
    ILogger<InvoiceProcessingService> logger)
{
    public async Task<ProcessingResult> ProcessAsync(Stream pdf, string fileName, CancellationToken ct = default)
    {
        var fileId = await store.SaveAsync(pdf, ct);
        var run = await agent.RunAsync(fileId, ct);
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

        return new(record.Id, status, reason, run.Invoice);
    }
}
