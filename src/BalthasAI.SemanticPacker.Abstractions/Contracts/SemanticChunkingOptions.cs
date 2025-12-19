namespace SemanticPacker.Core.Contracts;

/// <summary>
/// Semantic chunking options
/// </summary>
public class SemanticChunkingOptions
{
    /// <summary>
    /// Similarity threshold between sentences (chunks are split if below this value)
    /// </summary>
    public float SimilarityThreshold { get; set; } = 0.5f;

    /// <summary>
    /// Minimum chunk size in characters
    /// </summary>
    public int MinChunkSize { get; set; } = 100;

    /// <summary>
    /// Maximum chunk size in characters
    /// </summary>
    public int MaxChunkSize { get; set; } = 1000;

    /// <summary>
    /// Sentence delimiter patterns
    /// </summary>
    public string[] SentenceDelimiters { get; set; } = [".", "!", "?", "。", "！", "？", "\n\n"];
}
