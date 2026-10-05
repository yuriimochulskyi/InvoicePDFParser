namespace InvoicePdfParser.Api.Documents;

/// <summary>Recognises the text of a PDF that has no text layer.</summary>
public interface IOcrEngine
{
    Task<PdfText> RecognizeAsync(string pdfPath, CancellationToken ct = default);
}
