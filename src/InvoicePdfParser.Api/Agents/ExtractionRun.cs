using InvoicePdfParser.Api.Models;

namespace InvoicePdfParser.Api.Agents;

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
