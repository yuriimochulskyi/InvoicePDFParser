using System.Text.Json.Serialization;
using InvoicePdfParser.Api.Models;

namespace InvoicePdfParser.Tests;

/// <summary>
/// One sample's ground truth. <see cref="Invoice"/> holds the values as printed in the PDF,
/// even for a tampered document: the model must report what it reads, and the review
/// policy, not the model, is expected to flag it.
/// </summary>
public sealed record ExpectedCase(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] InvoiceStatus ExpectedStatus,
    string? ReasonContains,
    string[] Tags,
    InvoiceDto? Invoice)
{
    public bool Has(string tag) => Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);

    /// <summary>A scan can only be read when OCR is configured; without it the correct outcome is review.</summary>
    public InvoiceStatus StatusWhen(bool ocrAvailable) =>
        Has("scan") && !ocrAvailable ? InvoiceStatus.NeedsReview : ExpectedStatus;
}
