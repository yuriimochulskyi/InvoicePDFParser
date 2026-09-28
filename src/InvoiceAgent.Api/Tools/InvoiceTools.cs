using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace InvoiceAgent.Api.Tools;

/// <summary>
/// Tools exposed to the agent. One instance per agent run, so it can record
/// which tools were called and keep the raw text for persistence.
/// </summary>
public sealed class InvoiceTools(PdfTextExtractor pdf)
{
    private readonly List<string> _calls = [];

    public IReadOnlyList<string> Calls => _calls;
    public string? ExtractedText { get; private set; }

    [Description("Extracts the plain text of an uploaded PDF invoice. Call this first.")]
    public string ExtractPdfText([Description("The fileId of the uploaded PDF")] string fileId)
    {
        _calls.Add(nameof(ExtractPdfText));
        try
        {
            ExtractedText = pdf.Extract(fileId);
            return ExtractedText;
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public IList<AITool> AsAITools() =>
    [
        AIFunctionFactory.Create(ExtractPdfText),
    ];
}
