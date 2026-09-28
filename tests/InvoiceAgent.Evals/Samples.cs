using System.Text.Json;
using InvoiceAgent.Api.Models;

namespace InvoiceAgent.Evals;

public sealed record Sample(string Name, string PdfPath, InvoiceDto Expected);

public static class Samples
{
    public static string Directory { get; } = Path.Combine(FindRepoRoot(), "samples", "invoices");

    public static IReadOnlyList<Sample> All() =>
        System.IO.Directory.GetFiles(Directory, "*.pdf")
            .Order(StringComparer.Ordinal)
            .Select(pdf =>
            {
                var name = Path.GetFileNameWithoutExtension(pdf);
                var json = File.ReadAllText(Path.Combine(Directory, name + ".expected.json"));
                return new Sample(name, pdf, JsonSerializer.Deserialize<InvoiceDto>(json, JsonSerializerOptions.Web)!);
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
