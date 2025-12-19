using SemanticPacker.Core.Models;

namespace SemanticPacker.Core.Contracts;

/// <summary>
/// Document processing result
/// </summary>
public class DocumentProcessingResult
{
    /// <summary>
    /// Whether processing succeeded
    /// </summary>
    public bool Success { get; init; }
    
    /// <summary>
    /// Output file path
    /// </summary>
    public string? OutputPath { get; init; }
    
    /// <summary>
    /// Number of chunks created
    /// </summary>
    public int ChunkCount { get; init; }
    
    /// <summary>
    /// Source file metadata
    /// </summary>
    public ChunkMetadata? Metadata { get; init; }
    
    /// <summary>
    /// Error message (on failure)
    /// </summary>
    public string? ErrorMessage { get; init; }
    
    /// <summary>
    /// Processing duration
    /// </summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Document processing options
/// </summary>
public class DocumentProcessingOptions
{
    /// <summary>
    /// Output directory (if null, uses same directory as input file)
    /// </summary>
    public string? OutputDirectory { get; set; }
    
    /// <summary>
    /// Output filename pattern ({name} is replaced with original filename)
    /// </summary>
    public string OutputFilePattern { get; set; } = "{name}.chunks.parquet";
    
    /// <summary>
    /// Semantic chunking options
    /// </summary>
    public SemanticChunkingOptions ChunkingOptions { get; set; } = new();
    
    /// <summary>
    /// Dataset version
    /// </summary>
    public string Version { get; set; } = "1.0.0";
    
    /// <summary>
    /// Whether to overwrite existing files
    /// </summary>
    public bool OverwriteExisting { get; set; } = false;
}

/// <summary>
/// Pipeline for chunking documents and saving to Parquet
/// </summary>
public interface IDocumentProcessor
{
    /// <summary>
    /// Process a single file
    /// </summary>
    Task<DocumentProcessingResult> ProcessFileAsync(
        string filePath,
        DocumentProcessingOptions? options = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Process all supported files in a directory
    /// </summary>
    IAsyncEnumerable<DocumentProcessingResult> ProcessDirectoryAsync(
        string directoryPath,
        string searchPattern = "*.*",
        bool recursive = false,
        DocumentProcessingOptions? options = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Process directly from a stream
    /// </summary>
    Task<DocumentProcessingResult> ProcessStreamAsync(
        Stream stream,
        ChunkMetadata metadata,
        string outputPath,
        DocumentProcessingOptions? options = null,
        CancellationToken cancellationToken = default);
}
