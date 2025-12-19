using System.Runtime.CompilerServices;
using SemanticPacker.Core.Contracts;

namespace SemanticPacker.Extractors;

/// <summary>
/// Plain text file extractor (.txt, .md, .csv, etc.)
/// </summary>
public class PlainTextExtractor : ITextExtractor
{
    private static readonly Dictionary<string, string> ExtensionToMimeType = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".txt", "text/plain" },
        { ".md", "text/markdown" },
        { ".markdown", "text/markdown" },
        { ".csv", "text/csv" },
        { ".json", "application/json" },
        { ".xml", "application/xml" },
        { ".html", "text/html" },
        { ".htm", "text/html" },
        { ".log", "text/plain" },
        { ".ini", "text/plain" },
        { ".cfg", "text/plain" },
        { ".yaml", "text/yaml" },
        { ".yml", "text/yaml" },
    };

    public IReadOnlyCollection<string> SupportedExtensions => ExtensionToMimeType.Keys;

    public bool CanProcess(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return ExtensionToMimeType.ContainsKey(extension);
    }

    public async IAsyncEnumerable<TextExtractionResult> ExtractAsync(
        string filePath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(filePath);
        var contentType = ExtensionToMimeType.GetValueOrDefault(extension, "text/plain");
        
        var text = await File.ReadAllTextAsync(filePath, cancellationToken);
        
        yield return new TextExtractionResult
        {
            Text = text,
            ContentType = contentType,
            SourceLocation = Path.GetFileName(filePath)
        };
    }

    public async IAsyncEnumerable<TextExtractionResult> ExtractAsync(
        Stream stream,
        string contentType,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync(cancellationToken);
        
        yield return new TextExtractionResult
        {
            Text = text,
            ContentType = contentType
        };
    }
}
