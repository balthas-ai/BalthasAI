namespace SemanticPacker.Core.Models;

/// <summary>
/// Semantic chunk result with incremental update support
/// </summary>
public record SemanticChunk
{
    /// <summary>
    /// Deterministic ID based on source_id + content_hash
    /// </summary>
    public string Id { get; init; } = string.Empty;
    
    /// <summary>
    /// Text content hash for change detection
    /// </summary>
    public string ContentHash { get; init; } = string.Empty;
    
    /// <summary>
    /// Chunk text content
    /// </summary>
    public required string Text { get; init; }
    
    /// <summary>
    /// Chunk sequence index
    /// </summary>
    public int ChunkIndex { get; init; }
    
    /// <summary>
    /// Start position in original text (for text-based documents, nullable)
    /// </summary>
    public int? StartIndex { get; init; }
    
    /// <summary>
    /// End position in original text (for text-based documents, nullable)
    /// </summary>
    public int? EndIndex { get; init; }
    
    /// <summary>
    /// Page/slide/image number (for PDF, PPT, images, etc., nullable)
    /// </summary>
    public int? PageNumber { get; init; }
    
    /// <summary>
    /// Free-form location identifier (section name, URL fragment, timestamp, etc., nullable)
    /// </summary>
    public string? SourceLocation { get; init; }
}
