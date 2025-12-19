using SemanticPacker.Core.Contracts;
using BalthasAI.SmartVault.VectorStore;

namespace BalthasAI.SmartVault.Processing;

/// <summary>
/// Background service that periodically generates embeddings for chunks without them
/// </summary>
public class EmbeddingSyncWorker : BackgroundService
{
    private readonly SqliteVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<EmbeddingSyncWorker> _logger;
    private readonly TimeSpan _syncInterval;
    private readonly int _batchSize;

    public EmbeddingSyncWorker(
        SqliteVectorStore vectorStore,
        IEmbeddingService embeddingService,
        ILogger<EmbeddingSyncWorker> logger,
        TimeSpan? syncInterval = null,
        int batchSize = 50)
    {
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
        _logger = logger;
        _syncInterval = syncInterval ?? TimeSpan.FromSeconds(30);
        _batchSize = batchSize;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Embedding sync worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingEmbeddingsAsync(stoppingToken);
                await Task.Delay(_syncInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in embedding sync worker");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _logger.LogInformation("Embedding sync worker stopped");
    }

    private async Task ProcessPendingEmbeddingsAsync(CancellationToken cancellationToken)
    {
        // Get chunks without embeddings
        var chunks = await _vectorStore.GetChunksWithoutEmbeddingAsync(_batchSize, cancellationToken);

        if (chunks.Count == 0)
        {
            return;
        }

        _logger.LogDebug("Processing {Count} chunks for embedding", chunks.Count);

        // Generate batch embeddings
        var texts = chunks.Select(c => c.Text).ToList();
        try
        {
            var embeddingResults = await _embeddingService.GenerateEmbeddingsAsync(texts, cancellationToken);

            var embeddings = chunks
                .Zip(embeddingResults, (chunk, embedding) => (chunk.Id, embedding))
                .ToList();

            if (embeddings.Count > 0)
            {
                await _vectorStore.SaveEmbeddingsBatchAsync(embeddings, cancellationToken);
                _logger.LogInformation("Saved {Count} embeddings", embeddings.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate embeddings for batch");

            // Fallback to individual processing
            var embeddings = new List<(string ChunkId, float[] Embedding)>();
            foreach (var chunk in chunks)
            {
                try
                {
                    var embedding = await _embeddingService.GenerateEmbeddingAsync(chunk.Text, cancellationToken);
                    embeddings.Add((chunk.Id, embedding));
                }
                catch (Exception chunkEx)
                {
                    _logger.LogWarning(chunkEx, "Failed to embed chunk: {ChunkId}", chunk.Id);
                }
            }

            if (embeddings.Count > 0)
            {
                await _vectorStore.SaveEmbeddingsBatchAsync(embeddings, cancellationToken);
                _logger.LogInformation("Saved {Count} embeddings (fallback)", embeddings.Count);
            }
        }

        // Update sync status for completed source files
        await UpdateSyncStatusAsync(cancellationToken);
    }

    private async Task UpdateSyncStatusAsync(CancellationToken cancellationToken)
    {
        var unsyncedFiles = await _vectorStore.GetUnsyncedSourceFilesAsync(50, cancellationToken);

        foreach (var file in unsyncedFiles)
        {
            // Check if all chunks for this file have been embedded
            var chunksWithoutEmbedding = await _vectorStore.GetChunksWithoutEmbeddingAsync(1, cancellationToken);
            var hasUnembeddedChunks = chunksWithoutEmbedding.Exists(c => c.SourcePath == file.Path);

            if (!hasUnembeddedChunks)
            {
                await _vectorStore.MarkSourceFileAsSyncedAsync(file.Path, cancellationToken);
                _logger.LogInformation("Marked source file as synced: {Path}", file.Path);
            }
        }
    }
}
