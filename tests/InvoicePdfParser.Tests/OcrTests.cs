using Azure;
using InvoicePdfParser.Api.Configuration;
using InvoicePdfParser.Api.Documents;
using Microsoft.Extensions.Logging.Abstractions;

namespace InvoicePdfParser.Tests;

/// <summary>The OCR fallback and the shared layout code, without calling any OCR service.</summary>
public class OcrTests
{
    private sealed class FakeOcr(Func<PdfText> result) : IOcrEngine
    {
        public int Calls { get; private set; }

        public Task<PdfText> RecognizeAsync(string pdfPath, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(result());
        }
    }

    private static async Task<(OcrFallbackTextSource Source, string FileId)> SourceForAsync(string sampleName, IOcrEngine? ocr)
    {
        var store = new PdfFileStore(Path.Combine(Path.GetTempPath(), "invoice-pdf-parser-tests"));
        await using var pdf = File.OpenRead(Samples.All().Single(s => s.Name == sampleName).PdfPath);
        var fileId = await store.SaveAsync(pdf, TestContext.Current.CancellationToken);
        return (new OcrFallbackTextSource(new PdfTextExtractor(store), store, NullLogger<OcrFallbackTextSource>.Instance, ocr), fileId);
    }

    [Fact]
    public async Task TextLayerPdf_NeverCallsOcr()
    {
        var ocr = new FakeOcr(() => throw new InvalidOperationException("must not be called"));
        var (source, fileId) = await SourceForAsync("02-de-rechnung", ocr);

        var text = await source.ExtractAsync(fileId, TestContext.Current.CancellationToken);

        Assert.True(text.HasTextLayer);
        Assert.False(text.FromOcr);
        Assert.Equal(0, ocr.Calls);
    }

    [Fact]
    public async Task Scan_WithOcrConfigured_UsesTheRecognisedText_AndPassesPreflight()
    {
        var ocr = new FakeOcr(() => new PdfText("--- page 1 (OCR) ---\nRechnungsbetrag | 1.558,78 €\n", 1, 3) { FromOcr = true });
        var (source, fileId) = await SourceForAsync("09-de-rechnung-scan", ocr);

        var text = await source.ExtractAsync(fileId, TestContext.Current.CancellationToken);

        Assert.True(text.FromOcr);
        Assert.Equal(1, ocr.Calls);
        Assert.True(DocumentPreflight.Check(text, new AiOptions().MaxDocumentChars).Ok);
    }

    [Fact]
    public async Task Scan_WithoutOcr_GoesToReview_AsNotConfigured()
    {
        var (source, fileId) = await SourceForAsync("09-de-rechnung-scan", ocr: null);

        var result = DocumentPreflight.Check(await source.ExtractAsync(fileId, TestContext.Current.CancellationToken), 14000);

        Assert.False(result.Ok);
        Assert.Contains("OCR is not configured", result.ReviewReason);
    }

    [Fact]
    public async Task Scan_WhenOcrFails_GoesToReview_WithTheReason_NotAnError()
    {
        var ocr = new FakeOcr(() => throw new RequestFailedException(429, "quota exceeded", "TooManyRequests", null));
        var (source, fileId) = await SourceForAsync("09-de-rechnung-scan", ocr);

        var result = DocumentPreflight.Check(await source.ExtractAsync(fileId, TestContext.Current.CancellationToken), 14000);

        Assert.False(result.Ok);
        Assert.Contains("OCR failed (429 TooManyRequests)", result.ReviewReason);
    }

    [Fact]
    public void Layout_SeparatesColumns_AndKeepsRowsTogether()
    {
        // Two rows of a table: description, quantity, amount. Y grows downwards.
        LayoutWord[] words =
        [
            new("Webentwicklung", 10, 110, 100, 10), new("12", 300, 315, 100.5, 10), new("1.020,00", 400, 460, 99.5, 10),
            new("SSL-Zertifikat", 10, 105, 120, 10), new("1", 300, 307, 120, 10), new("49,90", 400, 440, 120, 10),
        ];

        var lines = TextLayout.Lines(words).Select(l => l.Line).ToList();

        Assert.Equal(["Webentwicklung | 12 | 1.020,00", "SSL-Zertifikat | 1 | 49,90"], lines);
    }

    [Fact]
    public void Deskew_BringsARotatedRowBackOntoOneLine()
    {
        // A row scanned 2 degrees crooked: the right end sits ~14 units lower than the left, more than a line height.
        const double angle = 2 * Math.PI / 180;
        LayoutWord[] row = [new("Total", 0, 40, 100, 10), new("1.558,78", 400, 460, 100, 10)];
        var crooked = row.Select(w =>
        {
            var cx = (w.Left + w.Right) / 2;
            return w with { CenterY = cx * Math.Sin(angle) + w.CenterY * Math.Cos(angle) };
        }).ToList();
        Assert.Equal(2, TextLayout.Lines(crooked).Count()); // without deskew the row falls apart

        var lines = TextLayout.Lines(TextLayout.Deskew(crooked, angle)).ToList();

        Assert.Single(lines);
        Assert.Equal("Total | 1.558,78", lines[0].Line);
    }
}
