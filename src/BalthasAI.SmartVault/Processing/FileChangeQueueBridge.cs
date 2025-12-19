using BalthasAI.SmartVault.WebDav;

namespace BalthasAI.SmartVault.Processing;

/// <summary>
/// Bridge that connects file change events to the processing queue
/// </summary>
public class FileChangeQueueBridge : IDisposable
{
    private readonly FileChangeNotificationService _notificationService;
    private readonly InProcessQueueManager _queueManager;
    private readonly FileProcessingOptions _options;
    private readonly WebDavOptions _webDavOptions;
    private readonly ILogger<FileChangeQueueBridge> _logger;
    private bool _disposed;

    public FileChangeQueueBridge(
        FileChangeNotificationService notificationService,
        InProcessQueueManager queueManager,
        FileProcessingOptions options,
        WebDavOptions webDavOptions,
        ILogger<FileChangeQueueBridge> logger)
    {
        _notificationService = notificationService;
        _queueManager = queueManager;
        _options = options;
        _webDavOptions = webDavOptions;
        _logger = logger;

        _notificationService.FileChanged += OnFileChanged;
    }

    /// <summary>
    /// WebDAV root directory
    /// </summary>
    public string RootDirectory => _webDavOptions.RootDirectory;

    private async void OnFileChanged(object? sender, FileChangeEventArgs e)
    {
        try
        {
            // Ignore directory changes (process at file level)
            if (e.IsDirectory && e.ChangeType != FileChangeType.Deleted)
            {
                return;
            }

            // Check exclusion patterns
            if (IsExcluded(e.RelativePath))
            {
                _logger.LogDebug("File excluded by pattern: {RelativePath}", e.RelativePath);
                return;
            }

            // Check extension filter
            if (!IsAllowedExtension(e.RelativePath))
            {
                _logger.LogDebug("File extension not allowed: {RelativePath}", e.RelativePath);
                return;
            }

            var task = await CreateTaskAsync(e);
            if (task is null)
            {
                return;
            }

            await _queueManager.EnqueueChangeAsync(task);

            _logger.LogDebug(
                "File change queued: {ChangeType} {RelativePath} (Hash: {Hash})",
                e.ChangeType, e.RelativePath, task.FileHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue file change: {RelativePath}", e.RelativePath);
        }
    }

    private bool IsExcluded(string relativePath)
    {
        var pathParts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return _options.ExcludePatterns.Exists(pattern =>
            pathParts.Any(part => part.Equals(pattern, StringComparison.OrdinalIgnoreCase)));
    }

    private bool IsAllowedExtension(string relativePath)
    {
        if (_options.AllowedExtensions is null || _options.AllowedExtensions.Count == 0)
        {
            return true;
        }

        var extension = Path.GetExtension(relativePath);
        return _options.AllowedExtensions.Contains(extension.ToLowerInvariant());
    }

    private async Task<FileProcessingTask?> CreateTaskAsync(FileChangeEventArgs e)
    {
        var isDeleted = e.ChangeType == FileChangeType.Deleted;
        var hash = isDeleted ? string.Empty : await FileHasher.ComputeHashAsync(e.PhysicalPath);

        // Ignore if file doesn't exist and is not a deletion
        if (string.IsNullOrEmpty(hash) && !isDeleted)
        {
            return null;
        }

        return new FileProcessingTask
        {
            RelativePath = e.RelativePath,
            PhysicalPath = e.PhysicalPath,
            FileHash = hash,
            IsDeleted = isDeleted
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _notificationService.FileChanged -= OnFileChanged;
    }
}
