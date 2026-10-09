using InvoicePdfParser.Api.Rag;

namespace InvoicePdfParser.Tests;

public sealed class RagTests
{
    [Fact]
    public void Chunker_overlaps_windows_and_keeps_every_word()
    {
        var text = string.Join(' ', Enumerable.Range(1, 700).Select(i => $"w{i}"));

        var chunks = MarkdownChunker.Split(text, chunkWords: 300, overlapWords: 50);

        Assert.Equal(3, chunks.Count);                       // 0-299, 250-549, 500-699
        Assert.StartsWith("w1 ", chunks[0]);
        Assert.StartsWith("w251 ", chunks[1]);
        Assert.EndsWith(" w700", chunks[2]);
        Assert.Equal(300, chunks[0].Split(' ').Length);
        Assert.Equal(200, chunks[2].Split(' ').Length);
    }

    [Fact]
    public void Chunker_returns_one_chunk_for_a_short_text()
    {
        var chunks = MarkdownChunker.Split("# Title\n\nA few words.", 300, 50);

        Assert.Single(chunks);
        Assert.Equal("# Title A few words.", chunks[0]);
    }

    [Fact]
    public void Cosine_is_one_for_parallel_and_zero_for_orthogonal_vectors()
    {
        Assert.Equal(1.0, VectorIndex.Cosine([1, 2, 3], [2, 4, 6]), precision: 9);
        Assert.Equal(0.0, VectorIndex.Cosine([1, 0], [0, 1]), precision: 9);
        Assert.Equal(-1.0, VectorIndex.Cosine([1, 0], [-1, 0]), precision: 9);
    }

    [Fact]
    public void Search_ranks_by_similarity_and_honours_top_k()
    {
        var index = new VectorIndex();
        index.Load(
        [
            new DocumentChunk("a.md", "far", [0, 1]),
            new DocumentChunk("b.md", "near", [1, 0.1f]),
            new DocumentChunk("c.md", "exact", [1, 0]),
        ]);

        var hits = index.Search([1, 0], topK: 2);

        Assert.Equal(["c.md", "b.md"], hits.Select(h => h.Chunk.File));
        Assert.True(hits[0].Score > hits[1].Score);
    }
}
