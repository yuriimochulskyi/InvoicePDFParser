using InvoicePdfParser.Api.Data;
using InvoicePdfParser.Api.Models;

namespace InvoicePdfParser.Api.Pipeline;

/// <summary>The stored record plus the typed invoice the run produced (null when extraction failed).</summary>
public sealed record ProcessingResult(InvoiceRecord Record, InvoiceDto? Invoice)
{
    public Guid Id => Record.Id;
    public InvoiceStatus Status => Record.Status;
    public string? ReviewReason => Record.ReviewReason;
}
