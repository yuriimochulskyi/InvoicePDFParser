using Microsoft.Extensions.AI;

namespace InvoiceAgent.Api.Agents;

public sealed class AiOptions
{
    public const string Section = "Ai";

    public string Provider { get; set; } = "Ollama";

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
    public GenerationOptions Generation => Provider == "AzureOpenAI" ? AzureOpenAI : Ollama;

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
