using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using SemanticPacker.Core.Contracts;
using SemanticPacker.Core.Models;

namespace SemanticPacker.Core.Services;

/// <summary>
/// Pipeline implementation for chunking documents and saving to Parquet
/// </summary>
public class DocumentProcessor : IDocumentProcessor
{
    private readonly IEnumerable<ITextExtractor> _extractors;
    private readonly ISemanticChunker _chunker;
    private readonly IChunkStorage _storage;
    private readonly ILogger<DocumentProcessor> _logger;

    public DocumentProcessor(
        IEnumerable<ITextExtractor> extractors,
        ISemanticChunker chunker,
        IChunkStorage storage,
        ILogger<DocumentProcessor> logger)
    {
        _extractors = extractors;
        _chunker = chunker;
        _storage = storage;
        _logger = logger;
    }

    public async Task<DocumentProcessingResult> ProcessFileAsync(
        string filePath,
        DocumentProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DocumentProcessingOptions();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!File.Exists(filePath))
            {
                return new DocumentProcessingResult
                {
                    Success = false,
                    ErrorMessage = $"File not found: {filePath}",
                    Duration = stopwatch.Elapsed
                };
            }

            // Find appropriate extractor
            var extractor = _extractors.FirstOrDefault(e => e.CanProcess(filePath));
            if (extractor == null)
            {
                return new DocumentProcessingResult
                {
                    Success = false,
                    ErrorMessage = $"No extractor available for file: {filePath}",
                    Duration = stopwatch.Elapsed
                };
            }

            // Collect file metadata
            var fileInfo = new FileInfo(filePath);
            var fileHash = await ComputeFileHashAsync(filePath, cancellationToken);
            
            var metadata = new ChunkMetadata
            {
                SourceId = fileHash,
                SourceName = fileInfo.Name,
                Version = options.Version,
                CreatedAt = DateTime.UtcNow,
                SourceFileSize = fileInfo.Length,
                SourceFileHash = fileHash
            };

            // Extract text and chunk
            var allChunks = new List<SemanticChunk>();
            var chunkIndex = 0;

            await foreach (var extraction in extractor.ExtractAsync(filePath, cancellationToken))
            {
                metadata.SourceContentType ??= extraction.ContentType;

                var chunks = await _chunker.ChunkAsync(
                    extraction.Text, 
                    options.ChunkingOptions, 
                    cancellationToken);

                // Update page info and index
                foreach (var chunk in chunks)
                {
                    allChunks.Add(chunk with
                    {
                        ChunkIndex = chunkIndex++,
                        PageNumber = extraction.PageNumber,
                        SourceLocation = extraction.SourceLocation
                    });
                }
            }

            if (allChunks.Count == 0)
            {
                return new DocumentProcessingResult
                {
                    Success = false,
                    ErrorMessage = "No text content extracted from file",
                    Metadata = metadata,
                    Duration = stopwatch.Elapsed
                };
            }

            // Determine output path
            var outputPath = GetOutputPath(filePath, options);
            
            // Check existing file
            if (File.Exists(outputPath) && !options.OverwriteExisting)
            {
                return new DocumentProcessingResult
                {
                    Success = false,
                    ErrorMessage = $"Output file already exists: {outputPath}",
                    Metadata = metadata,
                    Duration = stopwatch.Elapsed
                };
            }

            // Save to Parquet
            await _storage.SaveAsync(allChunks, metadata, outputPath, cancellationToken);

            _logger.LogInformation(
                "Processed {FileName}: {ChunkCount} chunks saved to {OutputPath}",
                fileInfo.Name, allChunks.Count, outputPath);

            return new DocumentProcessingResult
            {
                Success = true,
                OutputPath = outputPath,
                ChunkCount = allChunks.Count,
                Metadata = metadata,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process file: {FilePath}", filePath);
            return new DocumentProcessingResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    public async IAsyncEnumerable<DocumentProcessingResult> ProcessDirectoryAsync(
        string directoryPath,
        string searchPattern = "*.*",
        bool recursive = false,
        DocumentProcessingOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(directoryPath, searchPattern, searchOption);

        var supportedExtensions = _extractors
            .SelectMany(e => e.SupportedExtensions)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extension = Path.GetExtension(file);
            if (!supportedExtensions.Contains(extension))
            {
                _logger.LogDebug("Skipping unsupported file: {FilePath}", file);
                continue;
            }

            yield return await ProcessFileAsync(file, options, cancellationToken);
        }
    }

    public async Task<DocumentProcessingResult> ProcessStreamAsync(
        Stream stream,
        ChunkMetadata metadata,
        string outputPath,
        DocumentProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DocumentProcessingOptions();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Find extractor matching content type
            var contentType = metadata.SourceContentType ?? "text/plain";
            var extractor = _extractors.FirstOrDefault(e => 
                e.SupportedExtensions.Any(ext => 
                    GetMimeTypeForExtension(ext) == contentType));

            if (extractor == null)
            {
                // Use default text extractor
                extractor = _extractors.FirstOrDefault(e => e.CanProcess(".txt"));
            }

            if (extractor == null)
            {
                return new DocumentProcessingResult
                {
                    Success = false,
                    ErrorMessage = $"No extractor available for content type: {contentType}",
                    Duration = stopwatch.Elapsed
                };
            }

            var allChunks = new List<SemanticChunk>();
            var chunkIndex = 0;

            await foreach (var extraction in extractor.ExtractAsync(stream, contentType, cancellationToken))
            {
                var chunks = await _chunker.ChunkAsync(
                    extraction.Text,
                    options.ChunkingOptions,
                    cancellationToken);

                foreach (var chunk in chunks)
                {
                    allChunks.Add(chunk with
                    {
                        ChunkIndex = chunkIndex++,
                        PageNumber = extraction.PageNumber,
                        SourceLocation = extraction.SourceLocation
                    });
                }
            }

            metadata.CreatedAt = DateTime.UtcNow;
            await _storage.SaveAsync(allChunks, metadata, outputPath, cancellationToken);

            return new DocumentProcessingResult
            {
                Success = true,
                OutputPath = outputPath,
                ChunkCount = allChunks.Count,
                Metadata = metadata,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process stream");
            return new DocumentProcessingResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    private static string GetOutputPath(string inputPath, DocumentProcessingOptions options)
    {
        var directory = options.OutputDirectory ?? Path.GetDirectoryName(inputPath) ?? ".";
        var nameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
        var outputFileName = options.OutputFilePattern.Replace("{name}", nameWithoutExt);
        return Path.Combine(directory, outputFileName);
    }

    private static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GetMimeTypeForExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".txt" => "text/plain",
            ".md" or ".markdown" => "text/markdown",
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".html" or ".htm" => "text/html",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
    }
}
