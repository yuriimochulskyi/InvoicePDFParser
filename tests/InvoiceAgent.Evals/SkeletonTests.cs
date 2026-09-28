using System.Text.Json;
using InvoiceAgent.Api.Models;

namespace InvoiceAgent.Evals;

public class SkeletonTests
{
    [Fact]
    public void InvoiceDto_RoundTripsThroughJson()
    {
        var dto = new InvoiceDto { VendorName = "ACME", Total = 10.5m, LineItems = [new() { Description = "x", Amount = 10.5m }] };
        var json = JsonSerializer.Serialize(dto, JsonSerializerOptions.Web);
        var back = JsonSerializer.Deserialize<InvoiceDto>(json, JsonSerializerOptions.Web)!;
        Assert.Equal("ACME", back.VendorName);
        Assert.Single(back.LineItems);
    }
}
