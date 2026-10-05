using System.Text;

namespace InvoicePdfParser.Api.Documents;

/// <summary>
/// Rebuilds text lines from positioned words and marks column gaps with " | ".
/// Plain extraction turns a table row into "1 8 500,00 8 500,00", which is ambiguous
/// (1 × 8 500,00 or 18 × 500,00); with gaps it reads "1 | 8 500,00 | 8 500,00".
/// Shared by the text-layer reader and the OCR reader, so both feed the model the same shape.
/// </summary>
public static class TextLayout
{
    public static IEnumerable<(string Line, int Words)> Lines(IEnumerable<LayoutWord> words)
    {
        var rows = new List<List<LayoutWord>>();
        foreach (var word in words.Where(w => !string.IsNullOrWhiteSpace(w.Text)).OrderBy(w => w.CenterY))
        {
            // Same row if the vertical centres are within half a line height.
            var row = rows.FirstOrDefault(r => Math.Abs(r[0].CenterY - word.CenterY) < r[0].Height / 2);
            if (row is null) rows.Add([word]);
            else row.Add(word);
        }

        foreach (var row in rows)
        {
            var sb = new StringBuilder();
            LayoutWord? previous = null;
            foreach (var word in row.OrderBy(w => w.Left))
            {
                if (previous is { } p)
                {
                    var gap = word.Left - p.Right;
                    var charWidth = (p.Right - p.Left) / Math.Max(1, p.Text.Length);
                    sb.Append(gap > charWidth * 2 ? " | " : " ");
                }
                sb.Append(word.Text);
                previous = word;
            }
            yield return (sb.ToString(), row.Count);
        }
    }

    /// <summary>Removes a small page rotation (a scan that went in slightly crooked) so rows stay rows.</summary>
    public static IReadOnlyList<LayoutWord> Deskew(IReadOnlyList<LayoutWord> words, double angleRadians)
    {
        if (Math.Abs(angleRadians) < 0.001)
            return words;
        var (sin, cos) = Math.SinCos(-angleRadians);
        return words.Select(w =>
        {
            var cx = (w.Left + w.Right) / 2;
            var half = (w.Right - w.Left) / 2;
            var x = cx * cos - w.CenterY * sin;
            var y = cx * sin + w.CenterY * cos;
            return w with { Left = x - half, Right = x + half, CenterY = y };
        }).ToList();
    }
}
