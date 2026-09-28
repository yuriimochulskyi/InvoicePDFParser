using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace InvoiceAgent.Api.Tools;

public sealed class PdfTextExtractor(PdfFileStore store)
{
    public string Extract(string fileId)
    {
        using var document = PdfDocument.Open(store.GetPath(fileId));
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            sb.AppendLine($"--- page {page.Number} ---");
            foreach (var line in Lines(page))
                sb.AppendLine(line);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Rebuilds lines from positioned words and marks column gaps with " | ".
    /// Plain text extraction turns a table row into "1 8 500,00 8 500,00", which is
    /// ambiguous (1 × 8 500,00 or 18 × 500,00); with gaps it reads "1 | 8 500,00 | 8 500,00".
    /// </summary>
    private static IEnumerable<string> Lines(Page page)
    {
        var words = page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
        var rows = new List<List<Word>>();
        foreach (var word in words.OrderByDescending(w => w.BoundingBox.Bottom))
        {
            // Same row if the vertical centres are within half a line height.
            var row = rows.FirstOrDefault(r =>
                Math.Abs(r[0].BoundingBox.Centroid.Y - word.BoundingBox.Centroid.Y) < r[0].BoundingBox.Height / 2);
            if (row is null) rows.Add([word]);
            else row.Add(word);
        }

        foreach (var row in rows)
        {
            var sb = new StringBuilder();
            Word? previous = null;
            foreach (var word in row.OrderBy(w => w.BoundingBox.Left))
            {
                if (previous is not null)
                {
                    var gap = word.BoundingBox.Left - previous.BoundingBox.Right;
                    var charWidth = previous.BoundingBox.Width / Math.Max(1, previous.Text.Length);
                    sb.Append(gap > charWidth * 2 ? " | " : " ");
                }
                sb.Append(word.Text);
                previous = word;
            }
            yield return sb.ToString();
        }
    }
}
