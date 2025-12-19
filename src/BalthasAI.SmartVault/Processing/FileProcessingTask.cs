namespace BalthasAI.SmartVault.Processing;

/// <summary>
/// File processing task information
/// </summary>
public class FileProcessingTask
{
    /// <summary>
    /// File relative path
    /// </summary>
    public required string RelativePath { get; init; }

    /// <summary>
    /// File physical path
    /// </summary>
    public required string PhysicalPath { get; init; }

    /// <summary>
    /// File hash at task creation time (SHA256)
    /// </summary>
    public required string FileHash { get; init; }

    /// <summary>
    /// Task creation time (UTC)
    /// </summary>
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Retry count
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Whether this is a deletion task
    /// </summary>
    public bool IsDeleted { get; init; }
}
