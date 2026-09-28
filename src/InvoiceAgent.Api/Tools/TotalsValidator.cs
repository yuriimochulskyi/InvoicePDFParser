using System.Globalization;
using InvoiceAgent.Api.Models;

namespace InvoiceAgent.Api.Tools;

public sealed record TotalsCheck(bool Ok, string Details);

/// <summary>
/// Deterministic arithmetic check. Used both as an agent tool (so the model can
/// catch its own misreads) and by code for the final status — the LLM never decides.
/// </summary>
public static class TotalsValidator
{
    public const decimal Tolerance = 0.01m;

    public static TotalsCheck Validate(InvoiceDto invoice)
    {
        if (invoice.Total is not { } total)
            return new(false, "total is missing");

        var tax = invoice.TaxAmount ?? 0m;
        var discount = invoice.DiscountAmount ?? 0m;
        var problems = new List<string>();

        decimal baseAmount;
        if (invoice.LineItems.Count > 0)
        {
            if (invoice.LineItems.Any(l => l.Amount is null))
                return new(false, "one or more line items have no amount");

            baseAmount = invoice.LineItems.Sum(l => l.Amount!.Value);

            for (var i = 0; i < invoice.LineItems.Count; i++)
                if (invoice.LineItems[i] is { Quantity: { } qty, UnitPrice: { } unit, Amount: { } amount }
                    && Math.Abs(Math.Round(qty * unit, 2) - amount) > Tolerance)
                    problems.Add(Inv($"line {i + 1} '{invoice.LineItems[i].Description}': quantity {qty} × unitPrice {unit:0.00} = {qty * unit:0.00}, but amount is {amount:0.00}"));

            if (invoice.Subtotal is { } subtotal && Math.Abs(subtotal - baseAmount) > Tolerance)
                problems.Add(Inv($"sum of line items {baseAmount:0.00} != subtotal {subtotal:0.00}"));
        }
        else if (invoice.Subtotal is { } subtotal)
        {
            baseAmount = subtotal;
        }
        else
        {
            return new(false, "no line items and no subtotal, total cannot be verified");
        }

        var expected = baseAmount + tax - discount;
        if (Math.Abs(expected - total) > Tolerance)
            problems.Add(Inv($"lines {baseAmount:0.00} + tax {tax:0.00} - discount {discount:0.00} = {expected:0.00}, but total is {total:0.00} (diff {total - expected:0.00})"));

        return problems.Count == 0
            ? new(true, Inv($"lines {baseAmount:0.00} + tax {tax:0.00} - discount {discount:0.00} = total {total:0.00}"))
            : new(false, string.Join("; ", problems));
    }

    private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
