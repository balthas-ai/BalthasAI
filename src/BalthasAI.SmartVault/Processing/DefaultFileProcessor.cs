namespace BalthasAI.SmartVault.Processing;

/// <summary>
/// Default file processor implementation (for testing/development).
/// For production use, implement IFileProcessor with actual chunking/embedding logic.
/// </summary>
public class DefaultFileProcessor : IFileProcessor
{
    private readonly ILogger<DefaultFileProcessor> _logger;

    public DefaultFileProcessor(ILogger<DefaultFileProcessor> logger)
    {
        _logger = logger;
    }

    public async Task<ProcessingResult> ProcessAsync(FileProcessingTask task, CancellationToken cancellationToken = default)
    {
        // Check if file exists
        if (!File.Exists(task.PhysicalPath))
        {
            _logger.LogWarning("File not found: {PhysicalPath}", task.PhysicalPath);
            return ProcessingResult.Skipped;
        }

        try
        {
            // Read file (SharedRead allows concurrent access)
            await using var stream = new FileStream(
                task.PhysicalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);

            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync(cancellationToken);

            // TODO: Implement actual chunking/embedding logic
            // Here we just log file size and line count
            var lineCount = content.Count(c => c == '\n') + 1;
            _logger.LogInformation(
                "Processing file: {RelativePath}, Size: {Size} bytes, Lines: {Lines}",
                task.RelativePath, content.Length, lineCount);

            // Processing simulation (in practice, call embedding API, etc.)
            await Task.Delay(100, cancellationToken);

            return ProcessingResult.Success;
        }
        catch (IOException ex) when (ex.HResult == -2147024864) // File in use
        {
            _logger.LogWarning("File is in use: {PhysicalPath}", task.PhysicalPath);
            return ProcessingResult.Failed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file: {PhysicalPath}", task.PhysicalPath);
            return ProcessingResult.Failed;
        }
    }

    public Task ProcessDeletionAsync(FileProcessingTask task, CancellationToken cancellationToken = default)
    {
        // TODO: Implement actual deletion handling (remove from vector DB, etc.)
        _logger.LogInformation("Processing deletion: {RelativePath}", task.RelativePath);
        return Task.CompletedTask;
    }
}
