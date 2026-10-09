using System.Diagnostics;

namespace InvoicePdfParser.Api.Rag;

/// <summary>
/// Reads every markdown file of the knowledge base at startup, chunks and embeds it,
/// and fills the <see cref="VectorIndex"/>. Runs in the background so the invoice API
/// is up even when the embedding model is missing; /ask reports 503 until the index is ready.
/// </summary>
public sealed class KnowledgeBaseIndexer(
    RagOptions options, string contentRootPath, OllamaEmbeddingClient embeddings, VectorIndex index,
    ILogger<KnowledgeBaseIndexer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var files = FindFiles(out var root);
            var chunks = new List<DocumentChunk>();
            foreach (var path in files)
            {
                var name = Path.GetRelativePath(root, path).Replace('\\', '/');
                var text = await File.ReadAllTextAsync(path, stoppingToken);
                foreach (var piece in MarkdownChunker.Split(text, options.ChunkWords, options.OverlapWords))
                    chunks.Add(new DocumentChunk(name, piece, await embeddings.EmbedDocumentAsync(piece, stoppingToken)));
            }

            index.Load(chunks);
            logger.LogInformation("Knowledge base indexed: {Files} files, {Chunks} chunks, model {Model}, {Elapsed} ms",
                files.Count, chunks.Count, options.EmbeddingModel, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Knowledge base indexing failed after {Elapsed} ms; POST /api/ask will answer 503", sw.ElapsedMilliseconds);
        }
    }

    /// <summary>All *.md under the docs folder plus the README.md next to it; <paramref name="root"/> is the folder names are relative to.</summary>
    private List<string> FindFiles(out string root)
    {
        var docs = Path.GetFullPath(Path.Combine(contentRootPath, options.DocsPath));
        root = Path.GetDirectoryName(docs.TrimEnd(Path.DirectorySeparatorChar)) ?? docs;

        var files = new List<string>();
        if (Directory.Exists(docs))
            files.AddRange(Directory.EnumerateFiles(docs, "*.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        else
            logger.LogWarning("Knowledge base folder {Docs} does not exist (Rag:DocsPath)", docs);

        var readme = Path.Combine(root, "README.md");
        if (File.Exists(readme))
            files.Add(readme);
        return files;
    }
}
