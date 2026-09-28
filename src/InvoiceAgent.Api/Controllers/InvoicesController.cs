using InvoiceAgent.Api.Agents;
using InvoiceAgent.Api.Tools;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceAgent.Api.Controllers;

[ApiController]
[Route("api/invoices")]
public sealed class InvoicesController(PdfFileStore store, InvoiceExtractionAgent agent) : ControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("Upload one PDF file in the 'file' form field.");
        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only PDF files are supported.");

        await using var stream = file.OpenReadStream();
        var fileId = await store.SaveAsync(stream, ct);
        var run = await agent.RunAsync(fileId, ct);

        return Ok(new
        {
            fileId,
            run.Invoice,
            telemetry = new { run.Provider, run.Model, run.InputTokens, run.OutputTokens, run.LatencyMs, run.ToolCalls, run.Error },
        });
    }
}
