using Azure;

namespace InvoicePdfParser.Api.Documents;

/// <summary>
/// Text layer first; OCR only when there is none and an engine is configured. Without an
/// engine a scan stays empty and the preflight sends it to review; an OCR failure does the
/// same with the reason attached, rather than failing the request.
/// </summary>
public sealed class OcrFallbackTextSource(PdfTextExtractor textLayer, PdfFileStore store, ILogger<OcrFallbackTextSource> logger, IOcrEngine? ocr = null) : IPdfTextSource
{
    public async Task<PdfText> ExtractAsync(string fileId, CancellationToken ct = default)
    {
        var text = textLayer.Extract(fileId);
        if (text.HasTextLayer || ocr is null)
            return text;

        try
        {
            var recognised = await ocr.RecognizeAsync(store.GetPath(fileId), ct);
            logger.LogInformation("OCR {FileId}: {Pages} page(s), {Words} words", fileId, recognised.Pages, recognised.WordCount);
            return recognised;
        }
        catch (RequestFailedException ex)
        {
            logger.LogWarning(ex, "OCR {FileId} failed", fileId);
            return text with { OcrError = $"{ex.Status} {ex.ErrorCode}".Trim() };
        }
    }
}
