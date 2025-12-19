namespace BalthasAI.SmartVault.Processing;

/// <summary>
/// Background worker that performs file processing tasks
/// </summary>
public class FileProcessingWorker : BackgroundService
{
    private readonly InProcessQueueManager _queueManager;
    private readonly IFileProcessor _fileProcessor;
    private readonly ILogger<FileProcessingWorker> _logger;

    // Default retry count
    private const int DefaultMaxRetries = 3;

    public FileProcessingWorker(
        InProcessQueueManager queueManager,
        IFileProcessor fileProcessor,
        ILogger<FileProcessingWorker> logger)
    {
        _queueManager = queueManager;
        _fileProcessor = fileProcessor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("File processing worker started");

        try
        {
            await _queueManager.InitializeAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize queue manager. File processing worker will not start.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextTaskAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in file processing worker");
                await Task.Delay(1000, stoppingToken);
            }
        }

        _logger.LogInformation("File processing worker stopped");
    }

    private async Task ProcessNextTaskAsync(CancellationToken stoppingToken)
    {
        // Get task from queue (non-blocking polling)
        var task = await _queueManager.TryDequeueAsync(stoppingToken);

        if (task is null)
        {
            // Wait briefly if no tasks available
            await Task.Delay(100, stoppingToken);
            return;
        }

        var lockValue = Guid.NewGuid().ToString();

        try
        {
            // Try to acquire lock
            if (!await _queueManager.TryAcquireLockAsync(task.RelativePath, lockValue, stoppingToken))
            {
                _logger.LogDebug("Could not acquire lock for {RelativePath}, requeueing", task.RelativePath);
                await _queueManager.RequeueAsync(task, stoppingToken);
                return;
            }

            await ProcessWithLockAsync(task, lockValue, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process {RelativePath}", task.RelativePath);
            await HandleFailureAsync(task, stoppingToken);
        }
        finally
        {
            // Release lock
            await _queueManager.ReleaseLockAsync(task.RelativePath, lockValue, stoppingToken);
        }
    }

    private async Task ProcessWithLockAsync(FileProcessingTask task, string lockValue, CancellationToken stoppingToken)
    {
        if (task.IsDeleted)
        {
            await _fileProcessor.ProcessDeletionAsync(task, stoppingToken);
            await _queueManager.RemoveVersionAsync(task.RelativePath, stoppingToken);
            _logger.LogInformation("Processed deletion: {RelativePath}", task.RelativePath);
            return;
        }

        // Check if already processed version
        var storedVersion = await _queueManager.GetVersionAsync(task.RelativePath, stoppingToken);
        if (storedVersion == task.FileHash)
        {
            _logger.LogDebug("Skipping {RelativePath}, already at version {Hash}", task.RelativePath, task.FileHash);
            return;
        }

        // Process file
        var result = await _fileProcessor.ProcessAsync(task, stoppingToken);

        switch (result)
        {
            case ProcessingResult.Success:
                // Verify current file hash after processing
                var currentHash = await FileHasher.ComputeHashAsync(task.PhysicalPath, stoppingToken);

                if (currentHash == task.FileHash)
                {
                    // Version matches, processing complete
                    await _queueManager.SetVersionAsync(task.RelativePath, task.FileHash, stoppingToken);
                    _logger.LogInformation("Processed: {RelativePath} (Hash: {Hash})", task.RelativePath, task.FileHash);
                }
                else
                {
                    // File changed during processing - needs reprocessing with new version
                    _logger.LogInformation(
                        "File changed during processing: {RelativePath}. Requeueing with new hash.",
                        task.RelativePath);

                    var newTask = new FileProcessingTask
                    {
                        RelativePath = task.RelativePath,
                        PhysicalPath = task.PhysicalPath,
                        FileHash = currentHash,
                        IsDeleted = false,
                        RetryCount = 0
                    };
                    await _queueManager.EnqueueDirectAsync(newTask, stoppingToken);
                }
                break;

            case ProcessingResult.VersionMismatch:
                // Processor detected version mismatch
                _logger.LogInformation("Version mismatch for {RelativePath}, requeueing", task.RelativePath);
                await _queueManager.RequeueAsync(task, stoppingToken);
                break;

            case ProcessingResult.Failed:
                await HandleFailureAsync(task, stoppingToken);
                break;

            case ProcessingResult.Skipped:
                _logger.LogDebug("Processing skipped: {RelativePath}", task.RelativePath);
                break;
        }
    }

    private async Task HandleFailureAsync(FileProcessingTask task, CancellationToken stoppingToken)
    {
        if (task.RetryCount < DefaultMaxRetries)
        {
            _logger.LogWarning(
                "Processing failed for {RelativePath}, retry {RetryCount}/{MaxRetries}",
                task.RelativePath, task.RetryCount + 1, DefaultMaxRetries);

            await _queueManager.RequeueAsync(task, stoppingToken);
        }
        else
        {
            _logger.LogError(
                "Processing failed for {RelativePath} after {MaxRetries} retries, giving up",
                task.RelativePath, DefaultMaxRetries);
        }
    }
}
