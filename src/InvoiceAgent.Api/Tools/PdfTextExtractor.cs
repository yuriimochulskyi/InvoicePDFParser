using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace InvoiceAgent.Api.Tools;

/// <summary>Text of a PDF plus what the preflight needs to know about it.</summary>
public sealed record PdfText(string Text, int Pages, int WordCount)
{
    /// <summary>No text at all: a scan or an image-only export that was not (or could not be) recognised.</summary>
    public bool HasTextLayer => WordCount > 0;

    /// <summary>The text was recognised from page images rather than read from the PDF's text layer.</summary>
    public bool FromOcr { get; init; }

    /// <summary>Why OCR was attempted and produced nothing, if it was.</summary>
    public string? OcrError { get; init; }
}

/// <summary>
/// Where the document text comes from: PdfPig over the text layer, with OCR as a
/// fallback for scans when it is configured (see <see cref="OcrFallbackTextSource"/>).
/// The agent, the tools and the review policy only ever see <see cref="PdfText"/>.
/// </summary>
public interface IPdfTextSource
{
    /// <exception cref="UnreadablePdfException">The file is corrupt, encrypted or not a PDF.</exception>
    Task<PdfText> ExtractAsync(string fileId, CancellationToken ct = default);
}

public sealed class UnreadablePdfException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A positioned word, in a coordinate system where Y grows downwards.</summary>
public readonly record struct LayoutWord(string Text, double Left, double Right, double CenterY, double Height);

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

/// <summary>Reads the PDF's own text layer with PdfPig.</summary>
public sealed class PdfTextExtractor(PdfFileStore store) : IPdfTextSource
{
    public Task<PdfText> ExtractAsync(string fileId, CancellationToken ct = default) => Task.FromResult(Extract(fileId));

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
                // PDF coordinates grow upwards; flip Y so the top of the page comes first.
                var layout = page.GetWords().Select(w => new LayoutWord(
                    w.Text, w.BoundingBox.Left, w.BoundingBox.Right, -w.BoundingBox.Centroid.Y, w.BoundingBox.Height));
                foreach (var (line, lineWords) in TextLayout.Lines(layout))
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
}
