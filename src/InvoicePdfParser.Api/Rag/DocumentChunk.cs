namespace InvoicePdfParser.Api.Rag;

/// <summary>One window of a markdown file with its embedding. <paramref name="File"/> is the path relative to the repository root.</summary>
public sealed record DocumentChunk(string File, string Text, float[] Embedding);
