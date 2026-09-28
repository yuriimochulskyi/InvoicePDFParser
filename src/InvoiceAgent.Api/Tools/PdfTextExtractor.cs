using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

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
            sb.AppendLine(ContentOrderTextExtractor.GetText(page));
        }
        return sb.ToString();
    }
}
