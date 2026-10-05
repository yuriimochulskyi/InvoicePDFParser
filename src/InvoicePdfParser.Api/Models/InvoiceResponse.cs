using System.Text.Json;
using InvoicePdfParser.Api.Data;

namespace InvoicePdfParser.Api.Models;

/// <summary>The API contract for a processed invoice, returned by both POST and GET.</summary>
public sealed record InvoiceResponse(
    Guid Id,
    InvoiceStatus Status,
    string? ReviewReason,
    InvoiceDto? Invoice,
    string FileName,
    DateTimeOffset CreatedAt,
    RunTelemetry Telemetry)
{
    /// <summary>Extracted document text. Only with <c>?includeText=true</c>: it is the bulk of the payload and may hold personal data.</summary>
    public string? RawText { get; init; }

    /// <summary>The model's output when it could not be parsed into an invoice. Only with <c>?includeText=true</c>.</summary>
    public string? UnparsedModelOutput { get; init; }

    public static InvoiceResponse From(InvoiceRecord record, bool includeText = false)
    {
        var invoice = TryParseInvoice(record.ExtractedJson);
        return new InvoiceResponse(
            record.Id,
            record.Status,
            record.ReviewReason,
            invoice,
            record.FileName,
            record.CreatedAt,
            new RunTelemetry(record.Provider, record.Model, record.InputTokens, record.OutputTokens, record.LatencyMs,
                record.ToolCalls.Split(',', StringSplitOptions.RemoveEmptyEntries)))
        {
            RawText = includeText ? record.RawText : null,
            UnparsedModelOutput = includeText && invoice is null ? record.ExtractedJson : null,
        };
    }

    /// <summary>ExtractedJson holds either the invoice or, for a failed run, whatever the model returned.</summary>
    private static InvoiceDto? TryParseInvoice(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<InvoiceDto>(json, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
