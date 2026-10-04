using InvoiceAgent.Api.Agents;
using InvoiceAgent.Api.Tools;
using UglyToad.PdfPig;

namespace InvoiceAgent.Evals;

/// <summary>Guards the hand-written expected.json files: if these fail, the eval numbers mean nothing.</summary>
public class SampleIntegrityTests
{
    public static TheoryData<string> Names => new(Samples.All().Select(s => s.Name));

    [Fact]
    public void EveryPdfHasExpectedJson_AndViceVersa()
    {
        var pdfs = Directory.GetFiles(Samples.Directory, "*.pdf").Select(f => Path.GetFileNameWithoutExtension(f)!).ToHashSet();
        var expected = Directory.GetFiles(Samples.Directory, "*.expected.json")
            .Select(f => Path.GetFileName(f)!.Replace(".expected.json", "")).ToHashSet();
        Assert.Equal(expected, pdfs);
        Assert.True(pdfs.Count >= 8);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void ExpectedJson_IsArithmeticallyConsistent(string name)
    {
        var sample = Samples.All().Single(s => s.Name == name);
        var e = sample.Expected.Invoice;

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
    [MemberData(nameof(Names))]
    public async Task GroundTruth_GetsExpectedStatusFromPolicy(string name)
    {
        var sample = Samples.All().Single(s => s.Name == name);
        var text = await ExtractAsync(sample);
        var run = new ExtractionRun(sample.Expected.Invoice, text, null, "test", "test", 0, 0, 0, ["ExtractPdfText", "ValidateTotals"], null);

        var (status, reason) = InvoiceReviewPolicy.Decide(run);
        Assert.Equal(sample.Expected.ExpectedStatus, status);
        if (sample.Expected.ReasonContains is { } fragment)
            Assert.Contains(fragment, reason);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task PdfText_ContainsExpectedIdentifiers(string name)
    {
        var sample = Samples.All().Single(s => s.Name == name);
        var text = await ExtractAsync(sample);

        Assert.Contains(sample.Expected.Invoice.InvoiceNumber!, text);
        if (sample.Expected.Invoice.VendorTaxId is { } taxId)
            Assert.Contains(taxId, text);
        Assert.Contains(sample.Expected.Invoice.VendorName!, text);
    }

    [Fact]
    public async Task ColumnAwareExtraction_SeparatesTableCells()
    {
        // "1 8 500,00 8 500,00" is ambiguous (1 × 8 500 or 18 × 500); the column separator removes the ambiguity.
        var text = await ExtractAsync(Samples.All().Single(s => s.Name == "01-ua-fop"));
        Assert.Contains("| 1 | 8 500,00 | 8 500,00", text);
    }

    [Fact]
    public async Task InjectedSample_CarriesTheInjectionInItsTextLayer()
    {
        var text = await ExtractAsync(Samples.All().Single(s => s.Expected.Has("injection")));
        Assert.Contains("Ignore all previous instructions", text);
        Assert.Contains("Evil Corp", text);
    }

    [Fact]
    public void MultiPageSample_HasTwoPages()
    {
        using var doc = PdfDocument.Open(Samples.All().Single(s => s.Name.Contains("multipage")).PdfPath);
        Assert.Equal(2, doc.NumberOfPages);
    }

    private static async Task<string> ExtractAsync(Sample sample)
    {
        var store = new PdfFileStore(Path.Combine(Path.GetTempPath(), "invoice-agent-tests"));
        await using var pdf = File.OpenRead(sample.PdfPath);
        return new PdfTextExtractor(store).Extract(await store.SaveAsync(pdf, TestContext.Current.CancellationToken));
    }
}
