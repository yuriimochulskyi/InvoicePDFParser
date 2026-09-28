using InvoiceAgent.Api.Agents;

namespace InvoiceAgent.Api.Data;

public sealed class InvoiceRecord
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string FileName { get; set; } = "";
    public InvoiceStatus Status { get; set; }
    public string? ReviewReason { get; set; }
    public string? RawText { get; set; }
    public string? ExtractedJson { get; set; }
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public long LatencyMs { get; set; }
    public string ToolCalls { get; set; } = "";
}
