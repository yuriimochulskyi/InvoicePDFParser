using System.ClientModel;
using OpenAI;
using OpenAI.Chat;

namespace InvoiceAgent.Api.Agents;

/// <summary>
/// Both providers speak the OpenAI Chat Completions protocol, so one client type
/// covers them: Ollama via its /v1 endpoint, Azure OpenAI / Foundry via /openai/v1.
/// </summary>
public static class ChatClientFactory
{
    public sealed record ModelInfo(string Provider, string Model)
    {
        public TimeSpan RunTimeout { get; init; } = TimeSpan.FromMinutes(5);
    }

    public static (ChatClient Client, ModelInfo Info) Create(AiOptions options)
    {
        var runTimeout = TimeSpan.FromSeconds(options.RunTimeoutSeconds);
        var clientOptions = new OpenAIClientOptions { NetworkTimeout = runTimeout };

        switch (options.Provider)
        {
            case "Ollama":
                clientOptions.Endpoint = new Uri(options.Ollama.Endpoint.TrimEnd('/') + "/v1");
                // Ollama ignores the key, but the client requires one.
                var ollama = new OpenAIClient(new ApiKeyCredential("ollama"), clientOptions);
                return (ollama.GetChatClient(options.Ollama.Model), new("Ollama", options.Ollama.Model) { RunTimeout = runTimeout });

            case "AzureOpenAI":
                var az = options.AzureOpenAI;
                if (string.IsNullOrWhiteSpace(az.Endpoint) || string.IsNullOrWhiteSpace(az.ApiKey))
                    throw new InvalidOperationException("Ai:AzureOpenAI:Endpoint and Ai:AzureOpenAI:ApiKey must be set (use dotnet user-secrets for the key).");

                var endpoint = az.Endpoint.TrimEnd('/');
                if (!endpoint.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
                    endpoint += "/openai/v1";
                clientOptions.Endpoint = new Uri(endpoint + "/");
                var azure = new OpenAIClient(new ApiKeyCredential(az.ApiKey), clientOptions);
                return (azure.GetChatClient(az.Deployment), new("AzureOpenAI", az.Deployment) { RunTimeout = runTimeout });

            default:
                throw new InvalidOperationException($"Unknown Ai:Provider '{options.Provider}'. Use 'Ollama' or 'AzureOpenAI'.");
        }
    }
}
