using System.Text.Json;

namespace InvoicePdfParser.Tests;

public sealed record EvalModelsConfig(List<EvalModel> Models, string? PricesAsOf, string? PricingSource)
{
    public static EvalModelsConfig Load()
    {
        var path = Path.Combine(Samples.RepoRoot, "tests", "InvoicePdfParser.Tests", "evals.json");
        var config = JsonSerializer.Deserialize<EvalModelsConfig>(File.ReadAllText(path), JsonSerializerOptions.Web)!;

        // EVAL_MODELS=qwen3:8b,gpt-5-mini runs a subset; default is every configured model.
        var filter = Environment.GetEnvironmentVariable("EVAL_MODELS");
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var wanted = filter.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            config = config with { Models = config.Models.Where(m => wanted.Contains(m.Model, StringComparer.OrdinalIgnoreCase)).ToList() };
        }
        return config;
    }
}
