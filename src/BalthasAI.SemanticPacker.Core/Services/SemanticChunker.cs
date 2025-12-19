using Microsoft.Extensions.Logging;
using SemanticPacker.Core.Contracts;
using SemanticPacker.Core.Models;

namespace SemanticPacker.Core.Services;

/// <summary>
/// Semantic chunker implementation (embeddings are used internally only)
/// </summary>
public class SemanticChunker(IEmbeddingService embeddingService, ILogger<SemanticChunker> logger) : ISemanticChunker
{
    public async Task<List<SemanticChunk>> ChunkAsync(string text, SemanticChunkingOptions? options, CancellationToken cancellationToken)
    {
        options ??= new SemanticChunkingOptions();

        // 1. Split into sentences
        var sentences = SplitIntoSentences(text, options.SentenceDelimiters);
        logger.LogDebug("Split into {Count} sentences", sentences.Count);

        if (sentences.Count == 0)
            return [];

        if (sentences.Count == 1)
            return [new SemanticChunk { Text = text.Trim(), ChunkIndex = 0, StartIndex = 0, EndIndex = text.Length }];

        // 2. Generate embeddings for each sentence (for chunk boundary detection - internal use only)
        var sentenceTexts = sentences.Select(s => s.Text).ToArray();
        var embeddings = await embeddingService.GenerateEmbeddingsAsync(sentenceTexts, cancellationToken);

        for (int i = 0; i < sentences.Count; i++)
        {
            sentences[i].Embedding = embeddings[i];
        }

        // 3. Calculate similarity between adjacent sentences and determine chunk boundaries
        var breakPoints = FindBreakPoints(sentences, options);
        logger.LogDebug("Found {Count} break points", breakPoints.Count);

        // 4. Create chunks (text only, no embeddings)
        var chunks = CreateChunks(text, sentences, breakPoints, options);

        return chunks;
    }

    private List<SentenceInfo> SplitIntoSentences(string text, string[] delimiters)
    {
        var sentences = new List<SentenceInfo>();
        int currentStart = 0;

        for (int i = 0; i < text.Length; i++)
        {
            foreach (var delimiter in delimiters)
            {
                if (i + delimiter.Length <= text.Length && 
                    text.Substring(i, delimiter.Length) == delimiter)
                {
                    var sentenceText = text[currentStart..(i + delimiter.Length)].Trim();
                    if (!string.IsNullOrWhiteSpace(sentenceText))
                    {
                        sentences.Add(new SentenceInfo
                        {
                            Text = sentenceText,
                            StartIndex = currentStart,
                            EndIndex = i + delimiter.Length
                        });
                    }
                    currentStart = i + delimiter.Length;
                    break;
                }
            }
        }

        // Handle last sentence
        if (currentStart < text.Length)
        {
            var remaining = text[currentStart..].Trim();
            if (!string.IsNullOrWhiteSpace(remaining))
            {
                sentences.Add(new SentenceInfo
                {
                    Text = remaining,
                    StartIndex = currentStart,
                    EndIndex = text.Length
                });
            }
        }

        return sentences;
    }

    private List<int> FindBreakPoints(List<SentenceInfo> sentences, SemanticChunkingOptions options)
    {
        var breakPoints = new List<int>();

        for (int i = 0; i < sentences.Count - 1; i++)
        {
            var similarity = CosineSimilarity(sentences[i].Embedding!, sentences[i + 1].Embedding!);
            logger.LogTrace("Similarity between sentence {A} and {B}: {Sim:F4}", i, i + 1, similarity);

            if (similarity < options.SimilarityThreshold)
            {
                breakPoints.Add(i + 1);
            }
        }

        return breakPoints;
    }

    private List<SemanticChunk> CreateChunks(string originalText, List<SentenceInfo> sentences, List<int> breakPoints, SemanticChunkingOptions options)
    {
        var chunks = new List<SemanticChunk>();
        var currentChunkSentences = new List<SentenceInfo>();
        int breakIndex = 0;
        int chunkIndex = 0;

        for (int i = 0; i < sentences.Count; i++)
        {
            currentChunkSentences.Add(sentences[i]);

            bool isBreakPoint = breakIndex < breakPoints.Count && breakPoints[breakIndex] == i + 1;
            bool isLastSentence = i == sentences.Count - 1;
            int currentLength = currentChunkSentences.Sum(s => s.Text.Length);

            bool shouldCreateChunk = isLastSentence ||
                (isBreakPoint && currentLength >= options.MinChunkSize) ||
                currentLength >= options.MaxChunkSize;

            if (shouldCreateChunk && currentChunkSentences.Count > 0)
            {
                var startIdx = currentChunkSentences.First().StartIndex;
                var endIdx = currentChunkSentences.Last().EndIndex;
                var chunkText = originalText[startIdx..endIdx].Trim();

                chunks.Add(new SemanticChunk
                {
                    Text = chunkText,
                    ChunkIndex = chunkIndex++,
                    StartIndex = startIdx,
                    EndIndex = endIdx
                });

                currentChunkSentences.Clear();
                
                if (isBreakPoint)
                    breakIndex++;
            }
            else if (isBreakPoint)
            {
                breakIndex++;
            }
        }

        return chunks;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }

    private class SentenceInfo
    {
        public required string Text { get; init; }
        public int StartIndex { get; init; }
        public int EndIndex { get; init; }
        public float[]? Embedding { get; set; }
    }
}
