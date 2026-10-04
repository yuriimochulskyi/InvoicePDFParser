using InvoiceAgent.Api.Agents;
using InvoiceAgent.Api.Data;
using InvoiceAgent.Api.Models;
using InvoiceAgent.Api.Tools;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceAgent.Api.Controllers;

[ApiController]
[Route("api/invoices")]
public sealed class InvoicesController(InvoiceProcessingService processing, InvoiceDbContext db) : ControllerBase
{
    private const long MaxPdfBytes = 10 * 1024 * 1024;

    /// <summary>Extracts structured data from one PDF invoice and decides whether it needs human review.</summary>
    /// <remarks>
    /// 201 means the pipeline ran; <c>status</c> tells whether the result can be trusted.
    /// A document or model problem is <c>NeedsReview</c> with a reason, never an HTTP error.
    /// </remarks>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxPdfBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxPdfBytes + 64 * 1024)]
    [ProducesResponseType<InvoiceResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<InvoiceResponse>> Upload(IFormFile file, CancellationToken ct)
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
            return CreatedAtAction(nameof(Get), new { id = result.Id }, InvoiceResponse.From(result.Record));
        }
        catch (UnreadablePdfException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Invalid upload");
        }
        catch (LlmUnavailableException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "LLM provider unavailable");
        }
    }

    /// <summary>Returns a stored result. Add <c>?includeText=true</c> for the extracted document text.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<InvoiceResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<InvoiceResponse>> Get(Guid id, [FromQuery] bool includeText = false, CancellationToken ct = default)
    {
        var record = await db.Invoices.FindAsync([id], ct);
        return record is null ? NotFound() : InvoiceResponse.From(record, includeText);
    }
}
