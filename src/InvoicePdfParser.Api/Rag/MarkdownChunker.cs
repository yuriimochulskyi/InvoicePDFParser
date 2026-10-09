namespace InvoicePdfParser.Api.Rag;

/// <summary>Fixed-size word windows with overlap. Headings are not used; see the README for what a better chunker would do.</summary>
public static class MarkdownChunker
{
    public static IReadOnlyList<string> Split(string text, int chunkWords, int overlapWords)
    {
        if (chunkWords <= 0 || overlapWords < 0 || overlapWords >= chunkWords)
            throw new ArgumentOutOfRangeException(nameof(overlapWords), "0 <= overlap < chunk size");

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>();
        var step = chunkWords - overlapWords;
        for (var start = 0; start < words.Length; start += step)
        {
            var count = Math.Min(chunkWords, words.Length - start);
            chunks.Add(string.Join(' ', words, start, count));
            if (start + count >= words.Length)
                break;
        }
        return chunks;
    }
}
