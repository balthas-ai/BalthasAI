using System.Text.Json;
using System.Collections.Concurrent;

namespace BalthasAI.SmartVault.Processing;

/// <summary>
/// Pure .NET-based in-process file processing queue manager.
/// Uses ConcurrentDictionary and file-based persistence without external dependencies.
/// </summary>
public class InProcessQueueManager : IAsyncDisposable
{
    private readonly FileProcessingOptions _options;
    private readonly string _versionFilePath;

    // In-process queue (using ConcurrentQueue)
    private readonly ConcurrentQueue<FileProcessingTask> _taskQueue = new();

    // Pending task management (for debouncing)
    private readonly ConcurrentDictionary<string, (FileProcessingTask Task, DateTime EnqueueTime)> _pendingTasks = new();

    // Version store (memory + file persistence)
    private ConcurrentDictionary<string, string> _versions = new();

    // Lock management
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    private readonly Timer _debounceTimer;
    private readonly Timer _persistTimer;
    private bool _disposed;

    public InProcessQueueManager(FileProcessingOptions options)
    {
        _options = options;

        // Create data directory
        if (!Directory.Exists(options.DataPath))
        {
            Directory.CreateDirectory(options.DataPath);
        }

        _versionFilePath = Path.Combine(options.DataPath, "versions.json");

        // Debounce processing timer (every 100ms)
        _debounceTimer = new Timer(ProcessPendingTasks, null, 100, 100);

        // Version persistence timer (every 30 seconds)
        _persistTimer = new Timer(_ => PersistVersionsAsync().Wait(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Initializes the store.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadVersionsAsync(cancellationToken);
    }

    private async Task LoadVersionsAsync(CancellationToken cancellationToken)
    {
        // Try main file
        if (await TryLoadFromFileAsync(_versionFilePath, cancellationToken))
            return;

        // Try backup file
        var backupPath = _versionFilePath + ".bak";
        if (await TryLoadFromFileAsync(backupPath, cancellationToken))
            return;

        // Start with empty state if both fail
        _versions = new ConcurrentDictionary<string, string>();
    }

    private async Task<bool> TryLoadFromFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var versions = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (versions is not null)
            {
                _versions = new ConcurrentDictionary<string, string>(versions);
                return true;
            }
        }
        catch
        {
            // File corrupted
        }

        return false;
    }

    private async Task PersistVersionsAsync()
    {
        if (_disposed)
            return;

        try
        {
            var json = JsonSerializer.Serialize(
                _versions.ToDictionary(x => x.Key, x => x.Value),
                new JsonSerializerOptions { WriteIndented = true });

            // Atomic file write (temp file -> rename)
            var tempPath = _versionFilePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);

            // Backup existing file
            if (File.Exists(_versionFilePath))
            {
                var backupPath = _versionFilePath + ".bak";
                File.Move(_versionFilePath, backupPath, overwrite: true);
            }

            File.Move(tempPath, _versionFilePath);
        }
        catch
        {
            // Ignore save failure (retry on next cycle)
        }
    }

    /// <summary>
    /// Enqueues a file change (with debounce applied).
    /// </summary>
    public Task EnqueueChangeAsync(FileProcessingTask task, CancellationToken cancellationToken = default)
    {
        var key = task.RelativePath;
        var enqueueTime = DateTime.UtcNow.AddMilliseconds(_options.DebounceDelayMs);

        _pendingTasks.AddOrUpdate(key,
            (task, enqueueTime),
            (_, _) => (task, enqueueTime));

        return Task.CompletedTask;
    }

    private void ProcessPendingTasks(object? state)
    {
        var now = DateTime.UtcNow;

        foreach (var (key, (task, enqueueTime)) in _pendingTasks)
        {
            if (now >= enqueueTime)
            {
                if (_pendingTasks.TryRemove(key, out _))
                {
                    _taskQueue.Enqueue(task);
                }
            }
        }
    }

    /// <summary>
    /// Dequeues a task from the processing queue (non-blocking).
    /// </summary>
    public Task<FileProcessingTask?> TryDequeueAsync(CancellationToken cancellationToken = default)
    {
        if (_taskQueue.TryDequeue(out var task))
        {
            return Task.FromResult<FileProcessingTask?>(task);
        }

        return Task.FromResult<FileProcessingTask?>(null);
    }

    /// <summary>
    /// Acquires a lock for a file.
    /// </summary>
    public async Task<bool> TryAcquireLockAsync(string relativePath, string lockValue, CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(relativePath, _ => new SemaphoreSlim(1, 1));
        return await semaphore.WaitAsync(0, cancellationToken);
    }

    /// <summary>
    /// Releases a lock for a file.
    /// </summary>
    public Task ReleaseLockAsync(string relativePath, string lockValue, CancellationToken cancellationToken = default)
    {
        if (_locks.TryGetValue(relativePath, out var semaphore))
        {
            try
            {
                semaphore.Release();
            }
            catch (SemaphoreFullException)
            {
                // Already released
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Saves the processed version (hash) of a file.
    /// </summary>
    public Task SetVersionAsync(string relativePath, string hash, CancellationToken cancellationToken = default)
    {
        _versions[relativePath] = hash;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets the processed version (hash) of a file.
    /// </summary>
    public Task<string?> GetVersionAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        _versions.TryGetValue(relativePath, out var hash);
        return Task.FromResult(hash);
    }

    /// <summary>
    /// Removes version information when a file is deleted.
    /// </summary>
    public Task RemoveVersionAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        _versions.TryRemove(relativePath, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Requeues a task (for retry).
    /// </summary>
    public Task RequeueAsync(FileProcessingTask task, CancellationToken cancellationToken = default)
    {
        task.RetryCount++;
        _taskQueue.Enqueue(task);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds a task directly to the queue (without debounce).
    /// </summary>
    public Task EnqueueDirectAsync(FileProcessingTask task, CancellationToken cancellationToken = default)
    {
        _taskQueue.Enqueue(task);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await _debounceTimer.DisposeAsync();
        await _persistTimer.DisposeAsync();

        // Save versions before shutdown
        await PersistVersionsAsync();

        foreach (var semaphore in _locks.Values)
        {
            semaphore.Dispose();
        }
    }
}
