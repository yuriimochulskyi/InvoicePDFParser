using System.ComponentModel;
using System.Text.Json;
using InvoiceAgent.Api.Models;
using Microsoft.Extensions.AI;

namespace InvoiceAgent.Api.Tools;

/// <summary>
/// Tools exposed to the agent. One instance per agent run, so it can record
/// which tools were called and keep the raw text for persistence.
/// </summary>
public sealed class InvoiceTools(PdfTextExtractor pdf)
{
    private readonly List<string> _calls = [];

    public IReadOnlyList<string> Calls => _calls;
    public string? ExtractedText { get; private set; }

    [Description("Extracts the plain text of an uploaded PDF invoice. Call this first.")]
    public string ExtractPdfText([Description("The fileId of the uploaded PDF")] string fileId)
    {
        _calls.Add(nameof(ExtractPdfText));
        try
        {
            ExtractedText = pdf.Extract(fileId);
            return ExtractedText;
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [Description("Checks the arithmetic of a drafted invoice: sum(lineItems.amount) + taxAmount - discountAmount must equal total. Returns ok or mismatch with details.")]
    public string ValidateTotals([Description("The drafted invoice as JSON with the invoice fields (lineItems, subtotal, taxAmount, discountAmount, total)")] string invoiceJson)
    {
        _calls.Add(nameof(ValidateTotals));
        InvoiceDto? invoice;
        try
        {
            invoice = JsonSerializer.Deserialize<InvoiceDto>(invoiceJson, JsonSerializerOptions.Web);
        }
        catch (JsonException ex)
        {
            return $"ERROR: invoiceJson is not valid JSON: {ex.Message}";
        }
        if (invoice is null)
            return "ERROR: invoiceJson is empty";

        var check = TotalsValidator.Validate(invoice);
        return (check.Ok ? "ok: " : "mismatch: ") + check.Details;
    }

    public IList<AITool> AsAITools() =>
    [
        AIFunctionFactory.Create(ExtractPdfText),
        AIFunctionFactory.Create(ValidateTotals),
    ];
}
