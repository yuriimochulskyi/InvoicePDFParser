using System.ComponentModel;

namespace InvoicePdfParser.Api.Models;

[Description("Structured data extracted from an invoice. Use null for any value not present in the document; never invent values.")]
public sealed record InvoiceDto
{
    [Description("Name of the seller / supplier issuing the invoice")]
    public string? VendorName { get; init; }

    [Description("Seller tax identifier (VAT ID, EIN, ЄДРПОУ/ІПН, USt-IdNr, etc.), or null")]
    public string? VendorTaxId { get; init; }

    [Description("Invoice or receipt number")]
    public string? InvoiceNumber { get; init; }

    [Description("Issue date in yyyy-MM-dd format")]
    public string? InvoiceDate { get; init; }

    [Description("Payment due date in yyyy-MM-dd format, or null")]
    public string? DueDate { get; init; }

    [Description("ISO 4217 currency code, e.g. UAH, EUR, USD")]
    public string? Currency { get; init; }

    [Description("Invoice lines. Empty array if the document has no itemised lines.")]
    public List<LineItemDto> LineItems { get; init; } = [];

    [Description("Sum before tax and discount, or null")]
    public decimal? Subtotal { get; init; }

    [Description("Total tax amount (VAT, MwSt, ПДВ, sales tax), or null")]
    public decimal? TaxAmount { get; init; }

    [Description("Total discount as a positive number, or null")]
    public decimal? DiscountAmount { get; init; }

    [Description("Grand total payable")]
    public decimal? Total { get; init; }
}
