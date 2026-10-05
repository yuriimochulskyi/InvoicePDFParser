using System.Text.Json;

namespace InvoicePdfParser.Tests;

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
            if (File.Exists(Path.Combine(dir.FullName, "InvoicePdfParser.sln")))
                return dir.FullName;
        throw new InvalidOperationException("InvoicePdfParser.sln not found above " + AppContext.BaseDirectory);
    }
}
