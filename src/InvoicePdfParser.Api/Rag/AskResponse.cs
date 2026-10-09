namespace InvoicePdfParser.Api.Rag;

public sealed record AskResponse(string Answer, IReadOnlyList<string> Sources, IReadOnlyList<ChunkUsed> ChunksUsed);
