namespace InvoiceAgent.Api.Agents;

public sealed class AiOptions
{
    public const string Section = "Ai";

    public string Provider { get; set; } = "Ollama";

    /// <summary>Hard limit for one agent run; a local model partly offloaded to CPU needs ~2 min for a long invoice.</summary>
    public int RunTimeoutSeconds { get; set; } = 300;
    public OllamaOptions Ollama { get; set; } = new();
    public AzureOpenAIOptions AzureOpenAI { get; set; } = new();

    public sealed class OllamaOptions
    {
        public string Endpoint { get; set; } = "http://localhost:11434";
        public string Model { get; set; } = "qwen3:8b";
    }

    public sealed class AzureOpenAIOptions
    {
        public string Endpoint { get; set; } = "";
        public string Deployment { get; set; } = "gpt-4o-mini";
        public string ApiKey { get; set; } = "";
    }
}
