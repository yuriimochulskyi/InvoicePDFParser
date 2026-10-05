using System.Text.Json.Serialization;
using InvoicePdfParser.Api.Configuration;
using Microsoft.Extensions.AI;

namespace InvoicePdfParser.Tests;

/// <summary>One row of the model comparison: which deployment, how to sample, what it costs.</summary>
public sealed record EvalModel(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] AiProvider Provider,
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
            DocumentIntelligence = baseOptions.DocumentIntelligence,
        };
        if (Provider == AiProvider.Ollama) o.Ollama.Model = Model; else o.AzureOpenAI.Deployment = Model;
        o.Generation.Temperature = Temperature;
        o.Generation.ReasoningEffort = ReasoningEffort;
        return o;
    }
}
