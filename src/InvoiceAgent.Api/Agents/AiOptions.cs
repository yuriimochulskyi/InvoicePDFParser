using Microsoft.Extensions.AI;

namespace InvoiceAgent.Api.Agents;

public enum AiProvider { Ollama, AzureOpenAI }

public sealed class AiOptions
{
    public const string Section = "Ai";

    public AiProvider Provider { get; set; } = AiProvider.Ollama;

    /// <summary>Hard limit for one agent run; a local model partly offloaded to CPU needs ~2 min for a long invoice.</summary>
    public int RunTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Longest document text sent to the model. ~14k characters is roughly 4-6k tokens
    /// (Cyrillic tokenises worse than Latin), which with the two-turn loop fits an 8k context.
    /// </summary>
    public int MaxDocumentChars { get; set; } = 14000;

    public OllamaOptions Ollama { get; set; } = new();
    public AzureOpenAIOptions AzureOpenAI { get; set; } = new();

    /// <summary>Generation settings of the selected provider.</summary>
    public GenerationOptions Generation => Provider == AiProvider.AzureOpenAI ? AzureOpenAI : Ollama;

    /// <summary>
    /// Configuration problems, checked once at startup so a bad setting fails the host
    /// with a clear message instead of the first upload with a provider error.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (RunTimeoutSeconds is < 10 or > 3600)
            errors.Add($"Ai:RunTimeoutSeconds must be between 10 and 3600 (was {RunTimeoutSeconds}).");
        if (MaxDocumentChars < 1000)
            errors.Add($"Ai:MaxDocumentChars must be at least 1000 (was {MaxDocumentChars}).");
        if (Generation.Temperature is < 0 or > 2)
            errors.Add($"Ai:{Provider}:Temperature must be between 0 and 2, or null to send none.");

        if (Provider == AiProvider.Ollama)
        {
            if (!Uri.TryCreate(Ollama.Endpoint, UriKind.Absolute, out _))
                errors.Add("Ai:Ollama:Endpoint must be an absolute URL, e.g. http://localhost:11434.");
            if (string.IsNullOrWhiteSpace(Ollama.Model))
                errors.Add("Ai:Ollama:Model must be set, e.g. qwen3:8b.");
        }
        else
        {
            if (!Uri.TryCreate(AzureOpenAI.Endpoint, UriKind.Absolute, out _))
                errors.Add("Ai:AzureOpenAI:Endpoint must be set to the resource URL (dotnet user-secrets).");
            if (string.IsNullOrWhiteSpace(AzureOpenAI.ApiKey))
                errors.Add("Ai:AzureOpenAI:ApiKey must be set (dotnet user-secrets, never appsettings.json).");
            if (string.IsNullOrWhiteSpace(AzureOpenAI.Deployment))
                errors.Add("Ai:AzureOpenAI:Deployment must be set to the deployment name.");
        }
        return errors;
    }

    /// <summary>
    /// Sampling settings differ per model family, so they are data, not code:
    /// reasoning models (gpt-5-*, o-series) reject <c>temperature</c> and need a reasoning effort,
    /// classic models accept temperature 0 and ignore reasoning.
    /// </summary>
    public abstract class GenerationOptions
    {
        /// <summary>0 for repeatable extraction; null to send nothing (required by reasoning models).</summary>
        public float? Temperature { get; set; }

        /// <summary>Reasoning effort to request, or null to send nothing.</summary>
        public ReasoningEffort? ReasoningEffort { get; set; }
    }

    public sealed class OllamaOptions : GenerationOptions
    {
        public string Endpoint { get; set; } = "http://localhost:11434";
        public string Model { get; set; } = "qwen3:8b";

        public OllamaOptions()
        {
            Temperature = 0;
            // qwen3 "thinks" by default: ~8x the tokens and latency for no accuracy gain on extraction.
            ReasoningEffort = Microsoft.Extensions.AI.ReasoningEffort.None;
        }
    }

    public sealed class AzureOpenAIOptions : GenerationOptions
    {
        public string Endpoint { get; set; } = "";
        public string Deployment { get; set; } = "gpt-4.1-mini";
        public string ApiKey { get; set; } = "";
    }
}
