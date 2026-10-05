namespace InvoicePdfParser.Api.Documents;

/// <summary>Outcome of the checks that run before any model call.</summary>
public sealed record PreflightResult(PdfText Document, string? ReviewReason)
{
    public bool Ok => ReviewReason is null;
}
