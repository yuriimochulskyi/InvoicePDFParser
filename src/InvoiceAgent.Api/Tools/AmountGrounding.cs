using System.Globalization;
using System.Text.RegularExpressions;
using InvoiceAgent.Api.Models;

namespace InvoiceAgent.Api.Tools;

/// <summary>
/// Deterministic hallucination guard: every amount the model reports must literally
/// appear in the document text. Catches invented values such as a subtotal or a 0.00 tax
/// on a receipt that shows only a total.
/// </summary>
public static partial class AmountGrounding
{
    public static IReadOnlyList<string> UngroundedFields(InvoiceDto invoice, string rawText)
    {
        var inText = NumbersIn(rawText);
        var ungrounded = new List<string>();

        Check("subtotal", invoice.Subtotal);
        Check("taxAmount", invoice.TaxAmount);
        Check("discountAmount", invoice.DiscountAmount);
        Check("total", invoice.Total);
        return ungrounded;

        void Check(string field, decimal? value)
        {
            if (value is { } v && !inText.Contains(Math.Abs(v)))
                ungrounded.Add(Inv($"{field} {v:0.00}"));
        }
    }

    /// <summary>
    /// All values the text could mean. Formats vary by locale ("1.558,78", "1,558.78",
    /// "13 700,00", "1'234.50"), and a space may be a thousands separator or a column gap,
    /// so each run of up to three space-separated chunks is tried as one number too.
    /// </summary>
    private static HashSet<decimal> NumbersIn(string text)
    {
        var values = new HashSet<decimal>();
        foreach (Match run in NumberRun().Matches(text))
        {
            var chunks = run.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var start = 0; start < chunks.Length; start++)
                for (var len = 1; len <= 3 && start + len <= chunks.Length; len++)
                    if (TryParse(string.Concat(chunks.Skip(start).Take(len)), out var v))
                        values.Add(v);
        }
        return values;
    }

    private static bool TryParse(string s, out decimal value)
    {
        s = s.Replace("'", "").Replace(" ", "");
        var lastComma = s.LastIndexOf(',');
        var lastDot = s.LastIndexOf('.');
        var decimalSep = Math.Max(lastComma, lastDot);

        // A separator followed by exactly 1-2 digits at the end is the decimal point; the rest are grouping.
        string normalized;
        if (decimalSep >= 0 && s.Length - decimalSep - 1 is 1 or 2)
            normalized = s[..decimalSep].Replace(",", "").Replace(".", "") + "." + s[(decimalSep + 1)..];
        else
            normalized = s.Replace(",", "").Replace(".", "");

        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\d[\d.,'  ]*\d|\d")]
    private static partial Regex NumberRun();
}
