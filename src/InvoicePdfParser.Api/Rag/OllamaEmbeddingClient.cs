using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace InvoicePdfParser.Api.Rag;

/// <summary>
/// Calls Ollama's embeddings endpoint directly. nomic-embed-text is trained with task
/// prefixes, so documents and queries are embedded with different ones.
/// </summary>
public sealed class OllamaEmbeddingClient(HttpClient http, RagOptions options)
{
    public Task<float[]> EmbedDocumentAsync(string text, CancellationToken ct) => EmbedAsync("search_document: " + text, ct);

    public Task<float[]> EmbedQueryAsync(string text, CancellationToken ct) => EmbedAsync("search_query: " + text, ct);

    private async Task<float[]> EmbedAsync(string prompt, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("/api/embeddings", new EmbeddingRequest(options.EmbeddingModel, prompt), ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new HttpRequestException($"Embedding model '{options.EmbeddingModel}' is not available in Ollama. Run: ollama pull {options.EmbeddingModel}");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(ct);
        if (body?.Embedding is not { Length: > 0 } vector)
            throw new HttpRequestException("Ollama returned an empty embedding.");
        return vector;
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt);

    private sealed record EmbeddingResponse([property: JsonPropertyName("embedding")] float[]? Embedding);
}
