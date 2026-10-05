namespace InvoicePdfParser.Api.Documents;

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
