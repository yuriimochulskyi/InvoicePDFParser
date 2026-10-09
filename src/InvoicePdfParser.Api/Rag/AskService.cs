using System.Diagnostics;
using System.Globalization;
using System.Text;
using InvoicePdfParser.Api.Configuration;
using Microsoft.Extensions.AI;

namespace InvoicePdfParser.Api.Rag;

/// <summary>Embed the question, take the nearest chunks, answer only from them. Same chat model as the invoice agent.</summary>
public sealed class AskService(
    OllamaEmbeddingClient embeddings, VectorIndex index, IChatClient chatClient, AiOptions aiOptions, RagOptions options,
    ILogger<AskService> logger)
{
    private const string Instructions =
        "Answer ONLY from the context below. If the answer is not in the context, say you don't know. " +
        "Cite the source file names.";

    private const int ExcerptChars = 200;

    public async Task<AskResponse> AskAsync(string question, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var hits = index.Search(await embeddings.EmbedQueryAsync(question, ct), options.TopK);

        var context = new StringBuilder();
        foreach (var (chunk, _) in hits)
            context.Append("[source: ").Append(chunk.File).Append("]\n").Append(chunk.Text).Append("\n\n");

        var response = await chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, Instructions + "\n\nContext:\n\n" + context),
                new ChatMessage(ChatRole.User, question),
            ],
            new ChatOptions
            {
                Temperature = aiOptions.Generation.Temperature,
                Reasoning = aiOptions.Generation.ReasoningEffort is { } effort ? new ReasoningOptions { Effort = effort } : null,
                MaxOutputTokens = 1024,
            },
            ct);

        var scores = string.Join(", ", hits.Select(h => h.Score.ToString("0.000", CultureInfo.InvariantCulture)));
        logger.LogInformation("Ask {Question} -> top scores [{Scores}], tokens {In}/{Out}, {Elapsed} ms",
            question, scores, response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0, sw.ElapsedMilliseconds);

        return new AskResponse(
            response.Text.Trim(),
            hits.Select(h => h.Chunk.File).Distinct().ToList(),
            hits.Select(h => new ChunkUsed(h.Chunk.File, Math.Round(h.Score, 3), Excerpt(h.Chunk.Text))).ToList());
    }

    private static string Excerpt(string text) =>
        text.Length <= ExcerptChars ? text : text[..ExcerptChars].TrimEnd() + "…";
}
