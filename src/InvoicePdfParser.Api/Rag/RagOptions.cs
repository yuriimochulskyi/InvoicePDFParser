namespace InvoicePdfParser.Api.Rag;

public sealed class RagOptions
{
    public const string Section = "Rag";

    /// <summary>Folder with the markdown knowledge base, relative to the content root. Its parent's README.md is indexed too.</summary>
    public string DocsPath { get; set; } = "docs";

    /// <summary>Ollama instance that serves the embedding model; independent of the chat provider.</summary>
    public string EmbeddingEndpoint { get; set; } = "http://localhost:11434";

    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    public int ChunkWords { get; set; } = 300;

    public int OverlapWords { get; set; } = 50;

    public int TopK { get; set; } = 3;
}
