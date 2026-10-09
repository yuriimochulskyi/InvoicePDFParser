namespace InvoicePdfParser.Api.Rag;

/// <summary>In-memory vector store: a list of chunks and a brute-force cosine search. Rebuilt on every start.</summary>
public sealed class VectorIndex
{
    private volatile IReadOnlyList<DocumentChunk> _chunks = [];

    public bool IsReady { get; private set; }

    public int Count => _chunks.Count;

    public void Load(IReadOnlyList<DocumentChunk> chunks)
    {
        _chunks = chunks;
        IsReady = true;
    }

    public IReadOnlyList<(DocumentChunk Chunk, double Score)> Search(float[] query, int topK) =>
        _chunks.Select(c => (Chunk: c, Score: Cosine(query, c.Embedding)))
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();

    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException($"Vector sizes differ: {a.Length} vs {b.Length}.");

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            normA += (double)a[i] * a[i];
            normB += (double)b[i] * b[i];
        }
        return normA == 0 || normB == 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
