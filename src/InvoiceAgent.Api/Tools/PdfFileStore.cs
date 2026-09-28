namespace InvoiceAgent.Api.Tools;

/// <summary>Stores uploaded PDFs on disk under an opaque id that the agent passes back to its tools.</summary>
public sealed class PdfFileStore(string rootPath)
{
    private readonly string _root = Path.GetFullPath(rootPath);

    public async Task<string> SaveAsync(Stream content, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_root);
        var fileId = Guid.NewGuid().ToString("N");
        await using var file = File.Create(PathFor(fileId));
        await content.CopyToAsync(file, ct);
        return fileId;
    }

    public string GetPath(string fileId)
    {
        // fileId arrives from the LLM via a tool call, so never trust it as a path.
        if (!Guid.TryParseExact(fileId, "N", out _))
            throw new ArgumentException($"Unknown fileId '{fileId}'.", nameof(fileId));

        var path = PathFor(fileId);
        return File.Exists(path) ? path : throw new FileNotFoundException($"Unknown fileId '{fileId}'.");
    }

    private string PathFor(string fileId) => Path.Combine(_root, fileId + ".pdf");
}
