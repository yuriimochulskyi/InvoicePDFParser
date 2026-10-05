using System.Text;
using UglyToad.PdfPig;

namespace InvoicePdfParser.Api.Documents;

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
