using System.Text;
using Azure;
using Azure.AI.DocumentIntelligence;

namespace InvoicePdfParser.Api.Documents;

/// <summary>
/// Azure AI Document Intelligence, <c>prebuilt-read</c>: words with positions, which go
/// through the same <see cref="TextLayout"/> as the text-layer reader, so a scanned table
/// reaches the model in the same " | "-separated shape.
/// </summary>
public sealed class AzureDocumentIntelligenceOcr(DocumentIntelligenceClient client) : IOcrEngine
{
    public async Task<PdfText> RecognizeAsync(string pdfPath, CancellationToken ct = default)
    {
        var bytes = await File.ReadAllBytesAsync(pdfPath, ct);
        var operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-read", BinaryData.FromBytes(bytes), ct);

        var sb = new StringBuilder();
        var words = 0;
        foreach (var page in operation.Value.Pages)
        {
            sb.AppendLine($"--- page {page.PageNumber} (OCR) ---");
            var layout = page.Words.Select(ToLayoutWord).ToList();
            // The service reports the page rotation in degrees; undo it before grouping rows.
            var deskewed = TextLayout.Deskew(layout, (page.Angle ?? 0) * Math.PI / 180);
            foreach (var (line, lineWords) in TextLayout.Lines(deskewed))
            {
                sb.AppendLine(line);
                words += lineWords;
            }
        }
        return new PdfText(sb.ToString(), operation.Value.Pages.Count, words) { FromOcr = true };
    }

    /// <summary>The polygon is four corner points (x1,y1 … x4,y4) with Y growing downwards.</summary>
    private static LayoutWord ToLayoutWord(DocumentWord word)
    {
        var xs = word.Polygon.Where((_, i) => i % 2 == 0).Select(v => (double)v).ToArray();
        var ys = word.Polygon.Where((_, i) => i % 2 == 1).Select(v => (double)v).ToArray();
        return new LayoutWord(word.Content, xs.Min(), xs.Max(), ys.Average(), ys.Max() - ys.Min());
    }
}
