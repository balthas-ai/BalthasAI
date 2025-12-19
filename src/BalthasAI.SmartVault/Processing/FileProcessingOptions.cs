namespace BalthasAI.SmartVault.Processing;

/// <summary>
/// File processing pipeline configuration options
/// </summary>
public class FileProcessingOptions
{
    /// <summary>
    /// Data storage path
    /// </summary>
    public string DataPath { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "smartvault-data");

    /// <summary>
    /// Debounce delay in milliseconds
    /// </summary>
    public int DebounceDelayMs { get; set; } = 1000;

    /// <summary>
    /// Lock timeout in seconds
    /// </summary>
    public int LockTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Maximum retry count on processing failure
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Key prefix
    /// </summary>
    public string KeyPrefix { get; set; } = "smartvault:";

    /// <summary>
    /// File extension filter for processing (null means all files)
    /// </summary>
    public HashSet<string>? AllowedExtensions { get; set; }

    /// <summary>
    /// Patterns for excluded files/directories
    /// </summary>
    public List<string> ExcludePatterns { get; set; } = [".git", ".vs", "node_modules", "bin", "obj"];
}
