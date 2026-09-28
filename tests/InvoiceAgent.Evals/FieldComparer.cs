using InvoiceAgent.Api.Models;

namespace InvoiceAgent.Evals;

public sealed record FieldScore(int Correct, int Total, IReadOnlyList<string> Mismatched);

/// <summary>
/// Field-level accuracy: exact match for strings/dates/currency (trimmed, case-insensitive),
/// 0.01 tolerance for amounts, line-item count must match, then each line's amount.
/// </summary>
public static class FieldComparer
{
    public const decimal Tolerance = 0.01m;

    public static FieldScore Compare(InvoiceDto expected, InvoiceDto? actual)
    {
        actual ??= new InvoiceDto();
        var results = new List<(string Field, bool Ok)>
        {
            ("vendorName", Str(expected.VendorName, actual.VendorName)),
            ("vendorTaxId", Str(expected.VendorTaxId, actual.VendorTaxId)),
            ("invoiceNumber", Str(expected.InvoiceNumber, actual.InvoiceNumber)),
            ("invoiceDate", Str(expected.InvoiceDate, actual.InvoiceDate)),
            ("dueDate", Str(expected.DueDate, actual.DueDate)),
            ("currency", Str(expected.Currency, actual.Currency)),
            ("subtotal", Num(expected.Subtotal, actual.Subtotal)),
            ("taxAmount", Num(expected.TaxAmount, actual.TaxAmount)),
            ("discountAmount", Num(expected.DiscountAmount, actual.DiscountAmount)),
            ("total", Num(expected.Total, actual.Total)),
            ("lineItems.count", expected.LineItems.Count == actual.LineItems.Count),
        };

        // Per-line amounts are only comparable when the counts line up.
        if (expected.LineItems.Count == actual.LineItems.Count)
            for (var i = 0; i < expected.LineItems.Count; i++)
                results.Add(($"lineItems[{i}].amount", Num(expected.LineItems[i].Amount, actual.LineItems[i].Amount)));
        else
            for (var i = 0; i < expected.LineItems.Count; i++)
                results.Add(($"lineItems[{i}].amount", false));

        var mismatched = results.Where(r => !r.Ok).Select(r => r.Field).ToList();
        return new FieldScore(results.Count - mismatched.Count, results.Count, mismatched);
    }

    private static bool Str(string? expected, string? actual) =>
        string.Equals(Normalize(expected), Normalize(actual), StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static bool Num(decimal? expected, decimal? actual) =>
        expected is null ? actual is null : actual is not null && Math.Abs(expected.Value - actual.Value) <= Tolerance;
}
