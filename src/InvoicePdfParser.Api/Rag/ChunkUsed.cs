namespace InvoicePdfParser.Api.Rag;

public sealed record ChunkUsed(string File, double Score, string Excerpt);
