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
    [RequestSizeLimit(MaxPdfBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxPdfBytes + 64 * 1024)]
    public async Task<ActionResult<ProcessingResult>> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Problem("Upload one PDF file in the 'file' form field.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid upload");
        if (file.Length > MaxPdfBytes)
            return Problem($"The file is larger than {MaxPdfBytes / (1024 * 1024)} MB.", statusCode: StatusCodes.Status413PayloadTooLarge, title: "File too large");

        await using var stream = file.OpenReadStream();
        var header = new byte[5];
        if (await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct) < header.Length
            || !header.AsSpan().SequenceEqual("%PDF-"u8))
            return Problem("Only PDF files are supported.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid upload");
        stream.Position = 0;

        try
        {
            var result = await processing.ProcessAsync(stream, file.FileName, ct);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }
        catch (LlmUnavailableException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "LLM provider unavailable");
        }
    }

    private const long MaxPdfBytes = 10 * 1024 * 1024;

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
