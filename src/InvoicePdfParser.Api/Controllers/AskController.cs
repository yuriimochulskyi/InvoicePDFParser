using System.ClientModel;
using InvoicePdfParser.Api.Rag;
using Microsoft.AspNetCore.Mvc;

namespace InvoicePdfParser.Api.Controllers;

[ApiController]
[Route("api/ask")]
public sealed class AskController(AskService ask, VectorIndex index) : ControllerBase
{
    private const int MaxQuestionChars = 1000;

    /// <summary>Answers a question from the project's markdown docs, with the source files used.</summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<AskResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<AskResponse>> Ask([FromBody] AskRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return Problem("'question' must not be empty.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid question");
        if (request.Question.Length > MaxQuestionChars)
            return Problem($"'question' is longer than {MaxQuestionChars} characters.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid question");
        if (!index.IsReady || index.Count == 0)
            return Problem("The knowledge base is not indexed yet; see the startup log.", statusCode: StatusCodes.Status503ServiceUnavailable, title: "Knowledge base unavailable");

        try
        {
            return await ask.AskAsync(request.Question.Trim(), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or ClientResultException)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Model provider unavailable");
        }
    }
}
