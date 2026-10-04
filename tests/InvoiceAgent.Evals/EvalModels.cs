using System.Text.Json;
using System.Text.Json.Serialization;
using InvoiceAgent.Api.Agents;
using Microsoft.Extensions.AI;

namespace InvoiceAgent.Evals;

/// <summary>One row of the model comparison: which deployment, how to sample, what it costs.</summary>
public sealed record EvalModel(
    string Provider,
    string Model,
    float? Temperature,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ReasoningEffort? ReasoningEffort,
    decimal? InputPricePerM,
    decimal? OutputPricePerM,
    string? PricingNote)
{
    public string Key => $"{Provider}/{Model}";

    public bool HasPrices => InputPricePerM is not null && OutputPricePerM is not null;

    public decimal? Cost(long inputTokens, long outputTokens) =>
        HasPrices ? (inputTokens * InputPricePerM!.Value + outputTokens * OutputPricePerM!.Value) / 1_000_000m : null;

    /// <summary>The API's options (endpoints, keys, limits) with this model's provider and sampling settings.</summary>
    public AiOptions ApplyTo(AiOptions baseOptions)
    {
        var o = new AiOptions
        {
            Provider = Provider,
            RunTimeoutSeconds = baseOptions.RunTimeoutSeconds,
            MaxDocumentChars = baseOptions.MaxDocumentChars,
            Ollama = new() { Endpoint = baseOptions.Ollama.Endpoint, Model = baseOptions.Ollama.Model },
            AzureOpenAI = new() { Endpoint = baseOptions.AzureOpenAI.Endpoint, ApiKey = baseOptions.AzureOpenAI.ApiKey, Deployment = baseOptions.AzureOpenAI.Deployment },
        };
        if (Provider == "Ollama") o.Ollama.Model = Model; else o.AzureOpenAI.Deployment = Model;
        o.Generation.Temperature = Temperature;
        o.Generation.ReasoningEffort = ReasoningEffort;
        return o;
    }
}

public sealed record EvalModelsConfig(List<EvalModel> Models, string? PricesAsOf, string? PricingSource)
{
    public static EvalModelsConfig Load()
    {
        var path = Path.Combine(Samples.RepoRoot, "tests", "InvoiceAgent.Evals", "evals.json");
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
