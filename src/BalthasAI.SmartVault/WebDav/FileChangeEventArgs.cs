namespace BalthasAI.SmartVault.WebDav;

/// <summary>
/// File/directory change type
/// </summary>
public enum FileChangeType
{
    Created,
    Modified,
    Deleted,
    Renamed,
    Copied,
    Moved
}

/// <summary>
/// Source of the change event
/// </summary>
public enum FileChangeSource
{
    /// <summary>
    /// Change via WebDAV request
    /// </summary>
    WebDav,

    /// <summary>
    /// Change directly from file system (external process)
    /// </summary>
    FileSystem
}

/// <summary>
/// File/directory change event arguments
/// </summary>
public class FileChangeEventArgs : EventArgs
{
    /// <summary>
    /// Change type
    /// </summary>
    public required FileChangeType ChangeType { get; init; }

    /// <summary>
    /// Change source
    /// </summary>
    public required FileChangeSource Source { get; init; }

    /// <summary>
    /// Relative path of the changed file/directory
    /// </summary>
    public required string RelativePath { get; init; }

    /// <summary>
    /// Physical path of the changed file/directory
    /// </summary>
    public required string PhysicalPath { get; init; }

    /// <summary>
    /// Whether this is a directory
    /// </summary>
    public bool IsDirectory { get; init; }

    /// <summary>
    /// Previous path (for rename, move operations)
    /// </summary>
    public string? OldRelativePath { get; init; }

    /// <summary>
    /// Previous physical path (for rename, move operations)
    /// </summary>
    public string? OldPhysicalPath { get; init; }

    /// <summary>
    /// Event timestamp (UTC)
    /// </summary>
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Additional metadata
    /// </summary>
    public IDictionary<string, object>? Metadata { get; init; }
}
