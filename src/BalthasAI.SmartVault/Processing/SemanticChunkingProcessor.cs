using SemanticPacker.Core.Contracts;
using SemanticPacker.Core.Models;
using BalthasAI.SmartVault.VectorStore;

namespace BalthasAI.SmartVault.Processing;

/// <summary>
/// Processor that chunks files using SemanticPacker and stores them in the vector store
/// </summary>
public class SemanticChunkingProcessor : IFileProcessor
{
    private readonly IDocumentProcessor _documentProcessor;
    private readonly SqliteVectorStore _vectorStore;
    private readonly ILogger<SemanticChunkingProcessor> _logger;
    private readonly string _parquetBasePath;

    public SemanticChunkingProcessor(
        IDocumentProcessor documentProcessor,
        SqliteVectorStore vectorStore,
        ILogger<SemanticChunkingProcessor> logger,
        string parquetBasePath)
    {
        _documentProcessor = documentProcessor;
        _vectorStore = vectorStore;
        _logger = logger;
        _parquetBasePath = parquetBasePath;
    }

    public async Task<ProcessingResult> ProcessAsync(FileProcessingTask task, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing file: {RelativePath}", task.RelativePath);

        try
        {
            // Delete previous data (for changed files)
            var existingSource = await _vectorStore.GetSourceFileAsync(task.RelativePath, cancellationToken);
            if (existingSource is not null && existingSource.Hash != task.FileHash)
            {
                _logger.LogInformation("File changed, removing old chunks: {RelativePath}", task.RelativePath);
                await _vectorStore.DeleteChunksBySourcePathAsync(task.RelativePath, cancellationToken);
            }
            else if (existingSource?.Hash == task.FileHash && existingSource.Status == ProcessingStatus.Completed)
            {
                _logger.LogDebug("File already processed with same hash: {RelativePath}", task.RelativePath);
                return ProcessingResult.Skipped;
            }

            // Update source file status (processing)
            await _vectorStore.UpsertSourceFileAsync(new SourceFileRecord
            {
                Path = task.RelativePath,
                Hash = task.FileHash,
                FileSize = new FileInfo(task.PhysicalPath).Length,
                Status = ProcessingStatus.Processing,
                ProcessedAtUtc = DateTime.UtcNow
            }, cancellationToken);

            // Determine Parquet output path
            var parquetPath = GetParquetPath(task.RelativePath);
            var parquetDir = Path.GetDirectoryName(parquetPath);
            if (!string.IsNullOrEmpty(parquetDir) && !Directory.Exists(parquetDir))
            {
                Directory.CreateDirectory(parquetDir);
            }

            // Chunk with SemanticPacker
            var options = new DocumentProcessingOptions
            {
                OutputDirectory = Path.GetDirectoryName(parquetPath),
                OutputFilePattern = Path.GetFileName(parquetPath),
                OverwriteExisting = true
            };

            var result = await _documentProcessor.ProcessFileAsync(task.PhysicalPath, options, cancellationToken);

            if (!result.Success)
            {
                _logger.LogWarning("Failed to process file: {RelativePath} - {Error}", task.RelativePath, result.ErrorMessage);

                await _vectorStore.UpsertSourceFileAsync(new SourceFileRecord
                {
                    Path = task.RelativePath,
                    Hash = task.FileHash,
                    FileSize = new FileInfo(task.PhysicalPath).Length,
                    Status = ProcessingStatus.Failed,
                    ProcessedAtUtc = DateTime.UtcNow
                }, cancellationToken);

                return ProcessingResult.Failed;
            }

            // Save chunking results to vector store
            var chunks = await LoadChunksFromParquetAsync(parquetPath, task, cancellationToken);
            await _vectorStore.InsertChunksAsync(chunks, cancellationToken);

            // Update source file status (completed)
            await _vectorStore.UpsertSourceFileAsync(new SourceFileRecord
            {
                Path = task.RelativePath,
                Hash = task.FileHash,
                FileSize = new FileInfo(task.PhysicalPath).Length,
                ChunkCount = result.ChunkCount,
                ParquetPath = parquetPath,
                Status = ProcessingStatus.Completed,
                ProcessedAtUtc = DateTime.UtcNow,
                IsSyncedToVectorDb = false
            }, cancellationToken);

            _logger.LogInformation(
                "Processed file: {RelativePath}, {ChunkCount} chunks created",
                task.RelativePath, result.ChunkCount);

            return ProcessingResult.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file: {RelativePath}", task.RelativePath);

            await _vectorStore.UpsertSourceFileAsync(new SourceFileRecord
            {
                Path = task.RelativePath,
                Hash = task.FileHash,
                FileSize = File.Exists(task.PhysicalPath) ? new FileInfo(task.PhysicalPath).Length : 0,
                Status = ProcessingStatus.Failed,
                ProcessedAtUtc = DateTime.UtcNow
            }, cancellationToken);

            return ProcessingResult.Failed;
        }
    }

    public async Task ProcessDeletionAsync(FileProcessingTask task, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing deletion: {RelativePath}", task.RelativePath);

        // Delete chunks
        await _vectorStore.DeleteChunksBySourcePathAsync(task.RelativePath, cancellationToken);

        // Delete Parquet file
        var parquetPath = GetParquetPath(task.RelativePath);
        if (File.Exists(parquetPath))
        {
            File.Delete(parquetPath);
        }

        // Source file record deletion needs separate handling (or status update)
    }

    private string GetParquetPath(string relativePath)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(relativePath);
        var directory = Path.GetDirectoryName(relativePath) ?? "";
        return Path.Combine(_parquetBasePath, directory, $"{nameWithoutExt}.chunks.parquet");
    }

    private async Task<List<VectorChunkRecord>> LoadChunksFromParquetAsync(
        string parquetPath,
        FileProcessingTask task,
        CancellationToken cancellationToken)
    {
        // Load chunks from Parquet (using IChunkStorage)
        var storage = new SemanticPacker.Core.Services.ParquetChunkStorage(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SemanticPacker.Core.Services.ParquetChunkStorage>.Instance);
        var semanticChunks = await storage.LoadAsync(parquetPath, cancellationToken);

        // Convert to VectorChunkRecord
        return semanticChunks.Select(chunk => new VectorChunkRecord
        {
            Id = chunk.Id,
            SourcePath = task.RelativePath,
            SourceHash = task.FileHash,
            ChunkIndex = chunk.ChunkIndex,
            Text = chunk.Text,
            ContentHash = chunk.ContentHash,
            PageNumber = chunk.PageNumber,
            SourceLocation = chunk.SourceLocation,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        }).ToList();
    }
}
