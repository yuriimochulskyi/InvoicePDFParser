namespace InvoicePdfParser.Api.Models;

/// <summary>What one agent run cost and did.</summary>
public sealed record RunTelemetry(
    string Provider,
    string Model,
    long? InputTokens,
    long? OutputTokens,
    long LatencyMs,
    IReadOnlyList<string> ToolCalls);
