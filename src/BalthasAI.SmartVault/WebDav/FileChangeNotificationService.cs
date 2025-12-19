using System.Threading.Channels;

namespace BalthasAI.SmartVault.WebDav;

/// <summary>
/// Service that manages file change events
/// </summary>
public class FileChangeNotificationService : IDisposable
{
    private readonly WebDavOptions _options;
    private readonly FileSystemWatcher _watcher;
    private readonly Channel<FileChangeEventArgs> _channel;
    private readonly HashSet<string> _recentWebDavChanges = [];
    private readonly object _lock = new();
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    /// <summary>
    /// Event raised when a file change occurs
    /// </summary>
    public event EventHandler<FileChangeEventArgs>? FileChanged;

    /// <summary>
    /// Reader for the async event stream
    /// </summary>
    public ChannelReader<FileChangeEventArgs> ChangeStream => _channel.Reader;

    public FileChangeNotificationService(WebDavOptions options)
    {
        _options = options;

        // Create channel (with buffer size limit)
        _channel = Channel.CreateBounded<FileChangeEventArgs>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        });

        // Create root directory if it doesn't exist
        if (!Directory.Exists(_options.RootDirectory))
        {
            Directory.CreateDirectory(_options.RootDirectory);
        }

        // Configure FileSystemWatcher
        _watcher = new FileSystemWatcher(_options.RootDirectory)
        {
            NotifyFilter = NotifyFilters.FileName
                         | NotifyFilters.DirectoryName
                         | NotifyFilters.LastWrite
                         | NotifyFilters.Size
                         | NotifyFilters.CreationTime,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileSystemCreated;
        _watcher.Changed += OnFileSystemChanged;
        _watcher.Deleted += OnFileSystemDeleted;
        _watcher.Renamed += OnFileSystemRenamed;
        _watcher.Error += OnFileSystemError;

        // Timer to cleanup old WebDAV change records (every 5 seconds)
        _cleanupTimer = new Timer(_ => CleanupRecentChanges(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Notifies a change made via WebDAV.
    /// </summary>
    public void NotifyWebDavChange(FileChangeType changeType, string relativePath, string physicalPath,
        bool isDirectory = false, string? oldRelativePath = null, string? oldPhysicalPath = null)
    {
        // Record to prevent duplicate events
        var key = $"{changeType}:{physicalPath}:{DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond}";
        lock (_lock)
        {
            _recentWebDavChanges.Add(key);
        }

        var args = new FileChangeEventArgs
        {
            ChangeType = changeType,
            Source = FileChangeSource.WebDav,
            RelativePath = relativePath,
            PhysicalPath = physicalPath,
            IsDirectory = isDirectory,
            OldRelativePath = oldRelativePath,
            OldPhysicalPath = oldPhysicalPath
        };

        PublishEvent(args);
    }

    private void OnFileSystemCreated(object sender, FileSystemEventArgs e)
    {
        if (IsRecentWebDavChange(FileChangeType.Created, e.FullPath))
            return;

        var args = CreateEventArgs(FileChangeType.Created, e.FullPath);
        PublishEvent(args);
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        if (IsRecentWebDavChange(FileChangeType.Modified, e.FullPath))
            return;

        // Ignore directory changes (handled via child file changes)
        if (Directory.Exists(e.FullPath))
            return;

        var args = CreateEventArgs(FileChangeType.Modified, e.FullPath);
        PublishEvent(args);
    }

    private void OnFileSystemDeleted(object sender, FileSystemEventArgs e)
    {
        if (IsRecentWebDavChange(FileChangeType.Deleted, e.FullPath))
            return;

        var args = CreateEventArgs(FileChangeType.Deleted, e.FullPath);
        PublishEvent(args);
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        if (IsRecentWebDavChange(FileChangeType.Renamed, e.FullPath))
            return;

        var args = new FileChangeEventArgs
        {
            ChangeType = FileChangeType.Renamed,
            Source = FileChangeSource.FileSystem,
            RelativePath = GetRelativePath(e.FullPath),
            PhysicalPath = e.FullPath,
            IsDirectory = Directory.Exists(e.FullPath),
            OldRelativePath = GetRelativePath(e.OldFullPath),
            OldPhysicalPath = e.OldFullPath
        };

        PublishEvent(args);
    }

    private void OnFileSystemError(object sender, ErrorEventArgs e)
    {
        // Restart watcher on errors like buffer overflow
        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            // Ignore restart failure
        }
    }

    private FileChangeEventArgs CreateEventArgs(FileChangeType changeType, string physicalPath)
    {
        return new FileChangeEventArgs
        {
            ChangeType = changeType,
            Source = FileChangeSource.FileSystem,
            RelativePath = GetRelativePath(physicalPath),
            PhysicalPath = physicalPath,
            IsDirectory = changeType != FileChangeType.Deleted && Directory.Exists(physicalPath)
        };
    }

    private string GetRelativePath(string physicalPath)
    {
        var relativePath = Path.GetRelativePath(_options.RootDirectory, physicalPath);
        return "/" + relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    private bool IsRecentWebDavChange(FileChangeType changeType, string physicalPath)
    {
        var currentSecond = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;

        lock (_lock)
        {
            // Check both current and previous second (to prevent timing issues)
            return _recentWebDavChanges.Contains($"{changeType}:{physicalPath}:{currentSecond}")
                || _recentWebDavChanges.Contains($"{changeType}:{physicalPath}:{currentSecond - 1}");
        }
    }

    private void CleanupRecentChanges()
    {
        var threshold = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond - 5;

        lock (_lock)
        {
            _recentWebDavChanges.RemoveWhere(key =>
            {
                var parts = key.Split(':');
                return parts.Length >= 3 && long.TryParse(parts[^1], out var ticks) && ticks < threshold;
            });
        }
    }

    private void PublishEvent(FileChangeEventArgs args)
    {
        // Publish sync event
        FileChanged?.Invoke(this, args);

        // Send to channel async (ignore failure)
        _channel.Writer.TryWrite(args);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _cleanupTimer.Dispose();
        _channel.Writer.Complete();
    }
}
