namespace InvoicePdfParser.Api.Documents;

/// <summary>
/// Deterministic checks a document must pass before it is worth an LLM call. A scan
/// without a text layer or a document longer than the model context would only
/// produce a misleading answer, so they go straight to review with the real reason.
/// </summary>
public static class DocumentPreflight
{
    public static PreflightResult Check(PdfText document, int maxDocumentChars)
    {
        if (!document.HasTextLayer)
            return new(document, $"no text layer: scanned or image-only PDF ({document.Pages} page(s)); " +
                (document.OcrError is { } error ? $"OCR failed ({error})" : "OCR is not configured"));

        if (document.Text.Length > maxDocumentChars)
            return new(document, $"document too long: {document.Text.Length} characters exceed the configured limit of {maxDocumentChars} (Ai:MaxDocumentChars); it would not fit the model context");

        return new(document, null);
    }
}
