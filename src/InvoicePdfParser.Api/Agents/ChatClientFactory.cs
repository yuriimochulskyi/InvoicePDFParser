using System.ClientModel;
using InvoicePdfParser.Api.Configuration;
using Microsoft.Extensions.AI;
using OpenAI;

namespace InvoicePdfParser.Api.Agents;

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
            case AiProvider.Ollama:
                clientOptions.Endpoint = new Uri(options.Ollama.Endpoint.TrimEnd('/') + "/v1");
                // Ollama ignores the key, but the client requires one.
                var ollama = new OpenAIClient(new ApiKeyCredential("ollama"), clientOptions);
                return (ollama.GetChatClient(options.Ollama.Model).AsIChatClient(), new(nameof(AiProvider.Ollama), options.Ollama.Model));

            case AiProvider.AzureOpenAI:
                var az = options.AzureOpenAI;

                var endpoint = az.Endpoint.TrimEnd('/');
                if (!endpoint.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
                    endpoint += "/openai/v1";
                clientOptions.Endpoint = new Uri(endpoint + "/");
                var azure = new OpenAIClient(new ApiKeyCredential(az.ApiKey), clientOptions);
                return (azure.GetChatClient(az.Deployment).AsIChatClient(), new(nameof(AiProvider.AzureOpenAI), az.Deployment));

            default:
                throw new InvalidOperationException($"Unknown Ai:Provider '{options.Provider}'.");
        }
    }
}
