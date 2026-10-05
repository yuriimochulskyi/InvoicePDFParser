using System.Globalization;
using System.Text.RegularExpressions;
using InvoicePdfParser.Api.Agents;
using InvoicePdfParser.Api.Models;

namespace InvoicePdfParser.Api.Validation;

/// <summary>Decides the final status in code. The model's own opinion is never consulted.</summary>
public static partial class InvoiceReviewPolicy
{
    public static (InvoiceStatus Status, string? Reason) Decide(ExtractionRun run)
    {
        if (run.Error is not null)
            return (InvoiceStatus.NeedsReview, run.Error);
        if (!run.ToolCalls.Contains(nameof(InvoiceTools.ExtractPdfText)) || string.IsNullOrWhiteSpace(run.RawText))
            return (InvoiceStatus.NeedsReview, "Agent did not read the PDF text; output is not grounded in the document.");
        if (run.Invoice is not { } invoice)
            return (InvoiceStatus.NeedsReview, "Agent returned no invoice.");

        var problems = new List<string>();

        var missing = MissingRequiredFields(invoice);
        if (missing.Count > 0)
            problems.Add("missing required fields: " + string.Join(", ", missing));

        if (invoice.InvoiceDate is { } d && !IsIsoDate(d))
            problems.Add($"invoiceDate '{d}' is not yyyy-MM-dd");
        if (invoice.DueDate is { } due && !IsIsoDate(due))
            problems.Add($"dueDate '{due}' is not yyyy-MM-dd");
        if (invoice.Currency is { } c && !CurrencyCode().IsMatch(c))
            problems.Add($"currency '{c}' is not an ISO 4217 code");

        var ungrounded = AmountGrounding.UngroundedFields(invoice, run.RawText);
        if (ungrounded.Count > 0)
            problems.Add("amounts not found in the document text: " + string.Join(", ", ungrounded));

        var totals = TotalsValidator.Validate(invoice);
        if (!totals.Ok)
            problems.Add("totals check failed: " + totals.Details);

        return problems.Count == 0
            ? (InvoiceStatus.Parsed, null)
            : (InvoiceStatus.NeedsReview, string.Join("; ", problems));
    }

    private static List<string> MissingRequiredFields(InvoiceDto i)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(i.VendorName)) missing.Add("vendorName");
        if (string.IsNullOrWhiteSpace(i.InvoiceNumber)) missing.Add("invoiceNumber");
        if (string.IsNullOrWhiteSpace(i.InvoiceDate)) missing.Add("invoiceDate");
        if (string.IsNullOrWhiteSpace(i.Currency)) missing.Add("currency");
        if (i.Total is null) missing.Add("total");
        return missing;
    }

    private static bool IsIsoDate(string s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCode();
}
