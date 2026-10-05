namespace InvoicePdfParser.Api.Documents;

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
