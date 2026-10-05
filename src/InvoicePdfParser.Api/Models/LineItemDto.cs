using System.ComponentModel;

namespace InvoicePdfParser.Api.Models;

public sealed record LineItemDto
{
    public string? Description { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? UnitPrice { get; init; }

    [Description("Line total (quantity × unit price)")]
    public decimal? Amount { get; init; }
}
