using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace InvoicePdfParser.Api.Documents;

/// <summary>Stores uploaded PDFs on disk under an opaque id that the agent passes back to its tools.</summary>
public sealed partial class PdfFileStore(string rootPath)
{
    private readonly string _root = Path.GetFullPath(rootPath);

    public async Task<string> SaveAsync(Stream content, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_root);
        // Short on purpose: the model has to copy this id into a tool call, and it
        // occasionally mistyped a 32-char GUID. 48 random bits is plenty for a local store.
        var fileId = "f" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6));
        await using var file = File.Create(PathFor(fileId));
        await content.CopyToAsync(file, ct);
        return fileId;
    }

    public string GetPath(string fileId)
    {
        // fileId arrives from the LLM via a tool call, so never trust it as a path.
        if (!FileIdFormat().IsMatch(fileId))
            throw new ArgumentException($"Unknown fileId '{fileId}'.", nameof(fileId));

        var path = PathFor(fileId);
        return File.Exists(path) ? path : throw new FileNotFoundException($"Unknown fileId '{fileId}'.");
    }

    /// <summary>Removes an upload that produced no stored result (rejected file, provider outage).</summary>
    public void Delete(string fileId)
    {
        if (FileIdFormat().IsMatch(fileId))
            File.Delete(PathFor(fileId));
    }

    private string PathFor(string fileId) => Path.Combine(_root, fileId + ".pdf");

    [GeneratedRegex("^f[0-9a-f]{12}$")]
    private static partial Regex FileIdFormat();
}
