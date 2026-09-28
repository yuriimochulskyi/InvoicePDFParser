using InvoiceAgent.Api.Agents;
using InvoiceAgent.Api.Models;
using InvoiceAgent.Api.Tools;

namespace InvoiceAgent.Evals;

/// <summary>Deterministic checks, no LLM needed.</summary>
public class ReviewPolicyTests
{
    private static InvoiceDto Valid() => new()
    {
        VendorName = "Müller Webdesign GmbH",
        InvoiceNumber = "RE-2026-0147",
        InvoiceDate = "2026-08-15",
        Currency = "EUR",
        LineItems = [new() { Amount = 1020m }, new() { Amount = 240m }, new() { Amount = 49.90m }],
        Subtotal = 1309.90m,
        TaxAmount = 248.88m,
        Total = 1558.78m,
    };

    private static ExtractionRun Run(InvoiceDto? invoice, params string[] tools) =>
        new(invoice, "raw text", null, "Ollama", "qwen3:8b", 0, 0, 0, tools.Length > 0 ? tools : ["ExtractPdfText"], null);

    [Fact]
    public void ValidInvoice_IsParsed()
    {
        var (status, reason) = InvoiceReviewPolicy.Decide(Run(Valid()));
        Assert.Equal(InvoiceStatus.Parsed, status);
        Assert.Null(reason);
    }

    [Fact]
    public void TotalsWithinTolerance_Pass()
    {
        Assert.True(TotalsValidator.Validate(Valid() with { Total = 1558.79m }).Ok);
        Assert.False(TotalsValidator.Validate(Valid() with { Total = 1558.80m }).Ok);
    }

    [Fact]
    public void DiscountIsSubtracted()
    {
        var invoice = Valid() with { DiscountAmount = 100m, Total = 1458.78m };
        Assert.True(TotalsValidator.Validate(invoice).Ok);
    }

    [Fact]
    public void TotalMismatch_NeedsReview()
    {
        var (status, reason) = InvoiceReviewPolicy.Decide(Run(Valid() with { Total = 1600m }));
        Assert.Equal(InvoiceStatus.NeedsReview, status);
        Assert.Contains("totals check failed", reason);
    }

    [Fact]
    public void MissingRequiredField_NeedsReview()
    {
        var (status, reason) = InvoiceReviewPolicy.Decide(Run(Valid() with { InvoiceNumber = null }));
        Assert.Equal(InvoiceStatus.NeedsReview, status);
        Assert.Contains("invoiceNumber", reason);
    }

    [Fact]
    public void NonIsoDateOrCurrency_NeedsReview()
    {
        Assert.Equal(InvoiceStatus.NeedsReview, InvoiceReviewPolicy.Decide(Run(Valid() with { InvoiceDate = "15.08.2026" })).Status);
        Assert.Equal(InvoiceStatus.NeedsReview, InvoiceReviewPolicy.Decide(Run(Valid() with { Currency = "€" })).Status);
    }

    [Fact]
    public void AgentThatNeverReadThePdf_NeedsReview()
    {
        var (status, reason) = InvoiceReviewPolicy.Decide(Run(Valid(), "ValidateTotals"));
        Assert.Equal(InvoiceStatus.NeedsReview, status);
        Assert.Contains("did not read", reason);
    }

    [Fact]
    public void ReceiptWithoutLinesOrSubtotal_NeedsReview()
    {
        var receipt = Valid() with { LineItems = [], Subtotal = null, TaxAmount = null };
        var (status, reason) = InvoiceReviewPolicy.Decide(Run(receipt));
        Assert.Equal(InvoiceStatus.NeedsReview, status);
        Assert.Contains("cannot be verified", reason);
    }

    [Fact]
    public void ValidateTotalsTool_ReportsMismatch()
    {
        var tools = new InvoiceTools(null!);
        var result = tools.ValidateTotals("""{"lineItems":[{"amount":10}],"taxAmount":2,"total":13}""");
        Assert.StartsWith("mismatch", result);
        Assert.Equal(["ValidateTotals"], tools.Calls);
    }
}
