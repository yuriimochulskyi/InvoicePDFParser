using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace InvoiceAgent.Api.Tools;

/// <summary>Text of a PDF plus what the preflight needs to know about it.</summary>
public sealed record PdfText(string Text, int Pages, int WordCount)
{
    /// <summary>No text layer at all: a scan or an image-only export. OCR would be needed.</summary>
    public bool HasTextLayer => WordCount > 0;
}

/// <summary>
/// Where the document text comes from. Today PdfPig over the text layer; an OCR
/// implementation (e.g. Azure Document Intelligence) plugs in here without touching
/// the agent, the tools or the review policy.
/// </summary>
public interface IPdfTextSource
{
    /// <exception cref="UnreadablePdfException">The file is corrupt, encrypted or not a PDF.</exception>
    PdfText Extract(string fileId);
}

public sealed class UnreadablePdfException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class PdfTextExtractor(PdfFileStore store) : IPdfTextSource
{
    public PdfText Extract(string fileId)
    {
        var path = store.GetPath(fileId);
        try
        {
            using var document = PdfDocument.Open(path);
            var sb = new StringBuilder();
            var words = 0;
            foreach (var page in document.GetPages())
            {
                sb.AppendLine($"--- page {page.Number} ---");
                foreach (var (line, lineWords) in Lines(page))
                {
                    sb.AppendLine(line);
                    words += lineWords;
                }
            }
            return new PdfText(sb.ToString(), document.NumberOfPages, words);
        }
        catch (Exception ex) when (ex is not (ArgumentException or FileNotFoundException))
        {
            // PdfPig has its own exception types for corrupt and encrypted files.
            throw new UnreadablePdfException("The file is not a readable PDF (corrupt, encrypted or not a PDF).", ex);
        }
    }

    /// <summary>
    /// Rebuilds lines from positioned words and marks column gaps with " | ".
    /// Plain text extraction turns a table row into "1 8 500,00 8 500,00", which is
    /// ambiguous (1 × 8 500,00 or 18 × 500,00); with gaps it reads "1 | 8 500,00 | 8 500,00".
    /// </summary>
    private static IEnumerable<(string Line, int Words)> Lines(Page page)
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
            yield return (sb.ToString(), row.Count);
        }
    }
}
