namespace BalthasAI.SmartVault.VectorStore;

/// <summary>
/// Chunk record stored in the vector store
/// </summary>
public class VectorChunkRecord
{
    /// <summary>
    /// Unique chunk ID
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Source file relative path
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// Source file hash
    /// </summary>
    public required string SourceHash { get; init; }

    /// <summary>
    /// Chunk index
    /// </summary>
    public int ChunkIndex { get; init; }

    /// <summary>
    /// Chunk text content
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Content hash
    /// </summary>
    public required string ContentHash { get; init; }

    /// <summary>
    /// Embedding vector (null if not yet embedded)
    /// </summary>
    public float[]? Embedding { get; set; }

    /// <summary>
    /// Page number (if available)
    /// </summary>
    public int? PageNumber { get; init; }

    /// <summary>
    /// Source location information
    /// </summary>
    public string? SourceLocation { get; init; }

    /// <summary>
    /// Creation time (UTC)
    /// </summary>
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Last update time (UTC)
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Source file status tracking
/// </summary>
public class SourceFileRecord
{
    /// <summary>
    /// File relative path
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// File hash
    /// </summary>
    public required string Hash { get; init; }

    /// <summary>
    /// File size
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// Number of chunks created
    /// </summary>
    public int ChunkCount { get; init; }

    /// <summary>
    /// Parquet file path
    /// </summary>
    public string? ParquetPath { get; init; }

    /// <summary>
    /// Processing status
    /// </summary>
    public ProcessingStatus Status { get; set; }

    /// <summary>
    /// Last processing time
    /// </summary>
    public DateTime ProcessedAtUtc { get; set; }

    /// <summary>
    /// Whether synced to vector DB
    /// </summary>
    public bool IsSyncedToVectorDb { get; set; }
}

/// <summary>
/// Processing status
/// </summary>
public enum ProcessingStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}
