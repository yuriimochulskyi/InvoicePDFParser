namespace InvoicePdfParser.Api.Models;

/// <summary>The outcome of a run, always decided by <c>InvoiceReviewPolicy</c>, never by the model.</summary>
public enum InvoiceStatus
{
    /// <summary>Every deterministic check passed; the extracted data can be used.</summary>
    Parsed,

    /// <summary>Something could not be verified; a person should look, guided by the review reason.</summary>
    NeedsReview,
}
