namespace InvoicePdfParser.Api.Agents;

public sealed record TotalsLine(decimal? Quantity, decimal? UnitPrice, decimal? Amount);
