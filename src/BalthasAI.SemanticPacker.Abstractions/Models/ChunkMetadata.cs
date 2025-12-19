namespace SemanticPacker.Core.Models;

/// <summary>
/// Chunk metadata including source document information
/// </summary>
public class ChunkMetadata
{
    /// <summary>
    /// Unique identifier for the source document
    /// </summary>
    public string SourceId { get; set; } = string.Empty;
    
    /// <summary>
    /// Source document name
    /// </summary>
    public string SourceName { get; set; } = string.Empty;
    
    /// <summary>
    /// Dataset version
    /// </summary>
    public string Version { get; set; } = "1.0.0";
    
    /// <summary>
    /// Chunk creation timestamp (UTC)
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Source file MIME type (e.g., application/pdf, image/png)
    /// </summary>
    public string? SourceContentType { get; set; }
    
    /// <summary>
    /// Source file size in bytes
    /// </summary>
    public long? SourceFileSize { get; set; }
    
    /// <summary>
    /// Source file SHA256 hash
    /// </summary>
    public string? SourceFileHash { get; set; }
}
