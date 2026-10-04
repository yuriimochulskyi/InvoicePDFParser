using System.Text.Json;
using System.Text.Json.Serialization;
using InvoiceAgent.Api.Agents;
using InvoiceAgent.Api.Models;

namespace InvoiceAgent.Evals;

/// <summary>
/// One sample's ground truth. <see cref="Invoice"/> holds the values as printed in the PDF,
/// even for a tampered document: the model must report what it reads, and the review
/// policy, not the model, is expected to flag it.
/// </summary>
public sealed record ExpectedCase(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] InvoiceStatus ExpectedStatus,
    string? ReasonContains,
    string[] Tags,
    InvoiceDto? Invoice)
{
    public bool Has(string tag) => Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);
}

public sealed record Sample(string Name, string PdfPath, ExpectedCase Expected);

public static class Samples
{
    public static string RepoRoot { get; } = FindRepoRoot();
    public static string Directory { get; } = Path.Combine(RepoRoot, "samples", "invoices");

    public static IReadOnlyList<Sample> All() =>
        System.IO.Directory.GetFiles(Directory, "*.pdf")
            .Order(StringComparer.Ordinal)
            .Select(pdf =>
            {
                var name = Path.GetFileNameWithoutExtension(pdf);
                var json = File.ReadAllText(Path.Combine(Directory, name + ".expected.json"));
                return new Sample(name, pdf, JsonSerializer.Deserialize<ExpectedCase>(json, JsonSerializerOptions.Web)!);
            })
            .ToList();

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "InvoiceAgent.sln")))
                return dir.FullName;
        throw new InvalidOperationException("InvoiceAgent.sln not found above " + AppContext.BaseDirectory);
    }
}
