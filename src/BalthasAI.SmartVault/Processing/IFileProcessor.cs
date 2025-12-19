namespace BalthasAI.SmartVault.Processing;

/// <summary>
/// Processing result
/// </summary>
public enum ProcessingResult
{
    /// <summary>
    /// Processing succeeded
    /// </summary>
    Success,

    /// <summary>
    /// File changed during processing, needs reprocessing
    /// </summary>
    VersionMismatch,

    /// <summary>
    /// Processing failed (can retry)
    /// </summary>
    Failed,

    /// <summary>
    /// Processing skipped (already at latest version)
    /// </summary>
    Skipped
}

/// <summary>
/// File processor interface
/// </summary>
public interface IFileProcessor
{
    /// <summary>
    /// Processes a file (chunking, embedding, etc.).
    /// </summary>
    /// <param name="task">Processing task information</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processing result</returns>
    Task<ProcessingResult> ProcessAsync(FileProcessingTask task, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes file deletion (removes from index, etc.).
    /// </summary>
    /// <param name="task">Deletion task information</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ProcessDeletionAsync(FileProcessingTask task, CancellationToken cancellationToken = default);
}
