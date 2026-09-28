using InvoiceAgent.Api.Tools;
using UglyToad.PdfPig;

namespace InvoiceAgent.Evals;

/// <summary>Guards the hand-written expected.json files: if these fail, the eval numbers mean nothing.</summary>
public class SampleIntegrityTests
{
    public static TheoryData<string> Names => new(Samples.All().Select(s => s.Name));

    [Fact]
    public void AllSixSamplesExist() => Assert.Equal(6, Samples.All().Count);

    [Theory]
    [MemberData(nameof(Names))]
    public void ExpectedJson_IsArithmeticallyConsistent(string name)
    {
        var e = Samples.All().Single(s => s.Name == name).Expected;

        foreach (var line in e.LineItems)
            Assert.True(Math.Round(line.Quantity!.Value * line.UnitPrice!.Value, 2) == line.Amount, $"{line.Description}: qty × unit != amount");

        var check = TotalsValidator.Validate(e);
        if (e.LineItems.Count == 0 && e.Subtotal is null)
            Assert.False(check.Ok); // receipt: nothing to verify against, must go to review
        else
            Assert.True(check.Ok, check.Details);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task PdfText_ContainsExpectedIdentifiers(string name)
    {
        var sample = Samples.All().Single(s => s.Name == name);
        var store = new PdfFileStore(Path.Combine(Path.GetTempPath(), "invoice-agent-tests"));
        await using var pdf = File.OpenRead(sample.PdfPath);
        var text = new PdfTextExtractor(store).Extract(await store.SaveAsync(pdf, TestContext.Current.CancellationToken));

        Assert.Contains(sample.Expected.InvoiceNumber!, text);
        if (sample.Expected.VendorTaxId is { } taxId)
            Assert.Contains(taxId, text);
        Assert.Contains(sample.Expected.VendorName!, text);
    }

    [Fact]
    public void MultiPageSample_HasTwoPages()
    {
        using var doc = PdfDocument.Open(Samples.All().Single(s => s.Name.Contains("multipage")).PdfPath);
        Assert.Equal(2, doc.NumberOfPages);
    }
}
