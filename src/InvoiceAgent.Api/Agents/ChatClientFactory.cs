using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace InvoiceAgent.Api.Agents;

/// <summary>
/// Both providers speak the OpenAI Chat Completions protocol, so one client type
/// covers them: Ollama via its /v1 endpoint, Azure OpenAI / Foundry via /openai/v1.
/// The result is exposed as <see cref="IChatClient"/>, the Microsoft.Extensions.AI
/// abstraction the agent depends on, so tests can substitute a scripted client.
/// </summary>
public static class ChatClientFactory
{
    public sealed record ModelInfo(string Provider, string Model);

    public static (IChatClient Client, ModelInfo Info) Create(AiOptions options)
    {
        // One network timeout for the whole run: a per-call timeout would surface as a
        // TaskCanceledException that no caller can tell apart from the run timeout.
        var clientOptions = new OpenAIClientOptions { NetworkTimeout = TimeSpan.FromSeconds(options.RunTimeoutSeconds) };

        switch (options.Provider)
        {
            case "Ollama":
                clientOptions.Endpoint = new Uri(options.Ollama.Endpoint.TrimEnd('/') + "/v1");
                // Ollama ignores the key, but the client requires one.
                var ollama = new OpenAIClient(new ApiKeyCredential("ollama"), clientOptions);
                return (ollama.GetChatClient(options.Ollama.Model).AsIChatClient(), new("Ollama", options.Ollama.Model));

            case "AzureOpenAI":
                var az = options.AzureOpenAI;
                if (string.IsNullOrWhiteSpace(az.Endpoint) || string.IsNullOrWhiteSpace(az.ApiKey))
                    throw new InvalidOperationException("Ai:AzureOpenAI:Endpoint and Ai:AzureOpenAI:ApiKey must be set (use dotnet user-secrets for the key).");

                var endpoint = az.Endpoint.TrimEnd('/');
                if (!endpoint.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
                    endpoint += "/openai/v1";
                clientOptions.Endpoint = new Uri(endpoint + "/");
                var azure = new OpenAIClient(new ApiKeyCredential(az.ApiKey), clientOptions);
                return (azure.GetChatClient(az.Deployment).AsIChatClient(), new("AzureOpenAI", az.Deployment));

            default:
                throw new InvalidOperationException($"Unknown Ai:Provider '{options.Provider}'. Use 'Ollama' or 'AzureOpenAI'.");
        }
    }
}
