using System.Text.Json;
using InvoiceAgent.Api.Agents;
using InvoiceAgent.Api.Data;
using InvoiceAgent.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceAgent.Api.Controllers;

[ApiController]
[Route("api/invoices")]
public sealed class InvoicesController(InvoiceProcessingService processing, InvoiceDbContext db) : ControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ProcessingResult>> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("Upload one PDF file in the 'file' form field.");
        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only PDF files are supported.");

        await using var stream = file.OpenReadStream();
        var result = await processing.ProcessAsync(stream, file.FileName, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var r = await db.Invoices.FindAsync([id], ct);
        if (r is null)
            return NotFound();

        InvoiceDto? invoice = null;
        try { invoice = r.ExtractedJson is null ? null : JsonSerializer.Deserialize<InvoiceDto>(r.ExtractedJson, JsonSerializerOptions.Web); }
        catch (JsonException) { /* raw model output that failed to parse; still returned below */ }

        return Ok(new
        {
            r.Id,
            r.Status,
            r.ReviewReason,
            invoice,
            r.FileName,
            r.CreatedAt,
            telemetry = new { r.Provider, r.Model, r.InputTokens, r.OutputTokens, r.LatencyMs, toolCalls = r.ToolCalls.Split(',', StringSplitOptions.RemoveEmptyEntries) },
            r.ExtractedJson,
            r.RawText,
        });
    }
}
