namespace SemanticPacker.Core.Contracts;

/// <summary>
/// Text extraction result
/// </summary>
public class TextExtractionResult
{
    /// <summary>
    /// Extracted text
    /// </summary>
    public required string Text { get; init; }
    
    /// <summary>
    /// MIME type
    /// </summary>
    public required string ContentType { get; init; }
    
    /// <summary>
    /// Page number (if applicable)
    /// </summary>
    public int? PageNumber { get; init; }
    
    /// <summary>
    /// Source location information (section name, slide number, etc.)
    /// </summary>
    public string? SourceLocation { get; init; }
}

/// <summary>
/// Interface for extracting text from files
/// </summary>
public interface ITextExtractor
{
    /// <summary>
    /// List of supported file extensions
    /// </summary>
    IReadOnlyCollection<string> SupportedExtensions { get; }
    
    /// <summary>
    /// Check if this extractor can process the given file
    /// </summary>
    bool CanProcess(string filePath);
    
    /// <summary>
    /// Extract text from a file (can be split by page/section)
    /// </summary>
    IAsyncEnumerable<TextExtractionResult> ExtractAsync(
        string filePath, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Extract text from a stream
    /// </summary>
    IAsyncEnumerable<TextExtractionResult> ExtractAsync(
        Stream stream, 
        string contentType,
        CancellationToken cancellationToken = default);
}
