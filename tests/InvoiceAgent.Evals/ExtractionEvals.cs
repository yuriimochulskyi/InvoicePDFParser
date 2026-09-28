using System.Globalization;
using System.Text;
using InvoiceAgent.Api.Agents;
using InvoiceAgent.Api.Data;
using InvoiceAgent.Api.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InvoiceAgent.Evals;

/// <summary>
/// End-to-end eval: every samples/invoices/*.pdf goes through the real pipeline
/// (agent + tools + review policy + SQLite) and is scored field by field against
/// its hand-written expected.json.
/// </summary>
public class ExtractionEvals
{
    private const double RequiredAccuracy = 0.80;

    [Fact]
    public async Task FieldAccuracy_IsAtLeast80Percent()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = LoadAiOptions();
        if (options.Provider == "Ollama")
            await SkipUnlessOllamaReadyAsync(options.Ollama, ct);

        var (chatClient, model) = ChatClientFactory.Create(options);
        var workDir = Path.Combine(Path.GetTempPath(), "invoice-agent-evals", Guid.NewGuid().ToString("N"));
        var store = new PdfFileStore(Path.Combine(workDir, "uploads"));
        using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole().SetMinimumLevel(LogLevel.Information));
        await using var db = new InvoiceDbContext(new DbContextOptionsBuilder<InvoiceDbContext>()
            .UseSqlite($"Data Source={Path.Combine(workDir, "evals.db")}").Options);
        Directory.CreateDirectory(workDir);
        await db.Database.EnsureCreatedAsync(ct);

        var service = new InvoiceProcessingService(
            store,
            new InvoiceExtractionAgent(chatClient, model, new PdfTextExtractor(store), loggerFactory.CreateLogger<InvoiceExtractionAgent>()),
            db,
            loggerFactory.CreateLogger<InvoiceProcessingService>());

        var rows = new List<(string File, string Status, string? Reason, FieldScore Score, long LatencyMs)>();
        foreach (var sample in Samples.All())
        {
            await using var pdf = File.OpenRead(sample.PdfPath);
            var result = await service.ProcessAsync(pdf, Path.GetFileName(sample.PdfPath), ct);
            var latency = (await db.Invoices.FindAsync([result.Id], ct))!.LatencyMs;
            rows.Add((sample.Name, result.Status.ToString(), result.ReviewReason, FieldComparer.Compare(sample.Expected, result.Invoice), latency));
        }

        var correct = rows.Sum(r => r.Score.Correct);
        var total = rows.Sum(r => r.Score.Total);
        var accuracy = (double)correct / total;

        var report = Report(model, rows, correct, total, accuracy);
        TestContext.Current.TestOutputHelper?.WriteLine(report);
        Console.WriteLine(report);
        var reportPath = Path.Combine(Samples.Directory, "..", "..", "eval-report.md");
        await File.WriteAllTextAsync(reportPath, report, ct);

        Assert.True(accuracy >= RequiredAccuracy,
            $"Field accuracy {accuracy:P1} is below the required {RequiredAccuracy:P0}. See {Path.GetFullPath(reportPath)}.");
    }

    private static string Report(ChatClientFactory.ModelInfo model,
        List<(string File, string Status, string? Reason, FieldScore Score, long LatencyMs)> rows, int correct, int total, double accuracy)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Eval: {model.Provider} / {model.Model}, {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine("| File | Status | Fields correct | Latency | Mismatched fields | Review reason |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var r in rows)
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {r.File} | {r.Status} | {r.Score.Correct}/{r.Score.Total} | {r.LatencyMs / 1000.0:0.0} s | {(r.Score.Mismatched.Count == 0 ? "—" : string.Join(", ", r.Score.Mismatched))} | {r.Reason ?? "—"} |");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Overall field accuracy: {correct}/{total} = {accuracy:P1}** (required ≥ {RequiredAccuracy:P0})");
        return sb.ToString();
    }

    private static AiOptions LoadAiOptions()
    {
        var apiDir = Path.Combine(Samples.Directory, "..", "..", "src", "InvoiceAgent.Api");
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.GetFullPath(Path.Combine(apiDir, "appsettings.json")))
            .AddUserSecrets(typeof(AiOptions).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();
        return config.GetSection(AiOptions.Section).Get<AiOptions>() ?? new();
    }

    private static async Task SkipUnlessOllamaReadyAsync(AiOptions.OllamaOptions ollama, CancellationToken ct)
    {
        const string Help = "Install and start Ollama, then run:\n  ollama pull {0}\n" +
                            "and set OLLAMA_CONTEXT_LENGTH=8192 before starting Ollama (see README).";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        string tags;
        try
        {
            tags = await http.GetStringAsync(ollama.Endpoint.TrimEnd('/') + "/api/tags", ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Assert.Skip($"Ollama is not reachable at {ollama.Endpoint}. " + string.Format(CultureInfo.InvariantCulture, Help, ollama.Model));
            return;
        }
        if (!tags.Contains($"\"{ollama.Model}\"", StringComparison.Ordinal))
            Assert.Skip($"Ollama is running but model '{ollama.Model}' is not pulled. " + string.Format(CultureInfo.InvariantCulture, Help, ollama.Model));
    }
}
