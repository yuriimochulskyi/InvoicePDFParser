using InvoiceAgent.Api.Agents;
using InvoiceAgent.Api.Models;
using InvoiceAgent.Api.Tools;
using UglyToad.PdfPig;

namespace InvoiceAgent.Evals;

/// <summary>Guards the hand-written expected.json files: if these fail, the eval numbers mean nothing.</summary>
public class SampleIntegrityTests
{
    public static TheoryData<string> Names => new(Samples.All().Select(s => s.Name));
    public static TheoryData<string> NamesWithInvoice => new(Samples.All().Where(s => s.Expected.Invoice is not null).Select(s => s.Name));

    [Fact]
    public void EveryPdfHasExpectedJson_AndViceVersa()
    {
        var pdfs = Directory.GetFiles(Samples.Directory, "*.pdf").Select(f => Path.GetFileNameWithoutExtension(f)!).ToHashSet();
        var expected = Directory.GetFiles(Samples.Directory, "*.expected.json")
            .Select(f => Path.GetFileName(f)!.Replace(".expected.json", "")).ToHashSet();
        Assert.Equal(expected, pdfs);
        Assert.True(pdfs.Count >= 9);
    }

    [Theory]
    [MemberData(nameof(NamesWithInvoice))]
    public void ExpectedJson_IsArithmeticallyConsistent(string name)
    {
        var sample = Samples.All().Single(s => s.Name == name);
        var e = sample.Expected.Invoice!;

        foreach (var line in e.LineItems)
            Assert.True(Math.Round(line.Quantity!.Value * line.UnitPrice!.Value, 2) == line.Amount, $"{line.Description}: qty × unit != amount");

        var check = TotalsValidator.Validate(e);
        if (sample.Expected.Has("tampered"))
            Assert.False(check.Ok, "a tampered sample's printed numbers must not add up");
        else if (e.LineItems.Count == 0 && e.Subtotal is null)
            Assert.False(check.Ok); // receipt: nothing to verify against, must go to review
        else
            Assert.True(check.Ok, check.Details);
    }

    /// <summary>The policy over the ground truth itself must produce the expected status (no model involved).</summary>
    [Theory]
    [MemberData(nameof(NamesWithInvoice))]
    public async Task GroundTruth_GetsExpectedStatusFromPolicy(string name)
    {
        var sample = Samples.All().Single(s => s.Name == name);
        var text = (await ExtractAsync(sample)).Text;
        var run = new ExtractionRun(sample.Expected.Invoice, text, null, "test", "test", 0, 0, 0, ["ExtractPdfText", "ValidateTotals"], null);

        var (status, reason) = InvoiceReviewPolicy.Decide(run);
        Assert.Equal(sample.Expected.ExpectedStatus, status);
        if (sample.Expected.ReasonContains is { } fragment)
            Assert.Contains(fragment, reason);
    }

    [Theory]
    [MemberData(nameof(NamesWithInvoice))]
    public async Task PdfText_ContainsExpectedIdentifiers(string name)
    {
        var sample = Samples.All().Single(s => s.Name == name);
        var text = (await ExtractAsync(sample)).Text;
        var e = sample.Expected.Invoice!;

        Assert.Contains(e.InvoiceNumber!, text);
        if (e.VendorTaxId is { } taxId)
            Assert.Contains(taxId, text);
        Assert.Contains(e.VendorName!, text);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task Preflight_MatchesExpectedStatus(string name)
    {
        // Text-layer samples pass the preflight; the scan is stopped before any model call.
        var sample = Samples.All().Single(s => s.Name == name);
        var result = DocumentPreflight.Check(await ExtractAsync(sample), new AiOptions().MaxDocumentChars);

        if (sample.Expected.Has("scan"))
        {
            Assert.False(result.Ok);
            Assert.Contains(sample.Expected.ReasonContains!, result.ReviewReason);
        }
        else
        {
            Assert.True(result.Ok, result.ReviewReason);
        }
    }

    [Fact]
    public async Task ScannedSample_HasNoTextLayer()
    {
        var doc = await ExtractAsync(Samples.All().Single(s => s.Expected.Has("scan")));
        Assert.Equal(0, doc.WordCount);
        Assert.Equal(1, doc.Pages);
    }

    [Fact]
    public void Preflight_RejectsOverlongDocuments()
    {
        var doc = new PdfText(new string('x', 15000), 40, 3000);
        var result = DocumentPreflight.Check(doc, 14000);
        Assert.False(result.Ok);
        Assert.Contains("too long", result.ReviewReason);
    }

    [Fact]
    public async Task GarbageWithPdfHeader_IsUnreadable()
    {
        var store = new PdfFileStore(Path.Combine(Path.GetTempPath(), "invoice-agent-tests"));
        var fileId = await store.SaveAsync(new MemoryStream("%PDF-1.7 this is not really a pdf"u8.ToArray()), TestContext.Current.CancellationToken);
        Assert.Throws<UnreadablePdfException>(() => new PdfTextExtractor(store).Extract(fileId));
    }

    [Fact]
    public async Task ColumnAwareExtraction_SeparatesTableCells()
    {
        // "1 8 500,00 8 500,00" is ambiguous (1 × 8 500 or 18 × 500); the column separator removes the ambiguity.
        var text = (await ExtractAsync(Samples.All().Single(s => s.Name == "01-ua-fop"))).Text;
        Assert.Contains("| 1 | 8 500,00 | 8 500,00", text);
    }

    [Fact]
    public async Task InjectedSample_CarriesTheInjectionInItsTextLayer()
    {
        var text = (await ExtractAsync(Samples.All().Single(s => s.Expected.Has("injection")))).Text;
        Assert.Contains("Ignore all previous instructions", text);
        Assert.Contains("Evil Corp", text);
    }

    [Fact]
    public void MultiPageSample_HasTwoPages()
    {
        using var doc = PdfDocument.Open(Samples.All().Single(s => s.Name.Contains("multipage")).PdfPath);
        Assert.Equal(2, doc.NumberOfPages);
    }

    private static async Task<PdfText> ExtractAsync(Sample sample)
    {
        var store = new PdfFileStore(Path.Combine(Path.GetTempPath(), "invoice-agent-tests"));
        await using var pdf = File.OpenRead(sample.PdfPath);
        return new PdfTextExtractor(store).Extract(await store.SaveAsync(pdf, TestContext.Current.CancellationToken));
    }
}
