using System.Collections.Concurrent;
using BalthasAI.SmartVault.Processing;
using BalthasAI.SmartVault.WebDav;

namespace BalthasAI.SmartVault;

/// <summary>
/// Registry that manages SmartVault endpoints
/// </summary>
public class SmartVaultEndpointRegistry
{
    private readonly ConcurrentDictionary<string, SmartVaultEndpoint> _endpoints = new();

    /// <summary>
    /// All registered endpoints
    /// </summary>
    public IReadOnlyDictionary<string, SmartVaultEndpoint> Endpoints => _endpoints;

    /// <summary>
    /// Registers an endpoint.
    /// </summary>
    internal void Register(string path, SmartVaultEndpoint endpoint)
    {
        var normalizedPath = NormalizePath(path);
        _endpoints[normalizedPath] = endpoint;
    }

    /// <summary>
    /// Finds an endpoint matching the request path.
    /// </summary>
    public SmartVaultEndpoint? FindEndpoint(string requestPath)
    {
        var normalizedPath = NormalizePath(requestPath);

        // Find endpoint that exactly matches or is a parent path
        foreach (var (basePath, endpoint) in _endpoints)
        {
            if (normalizedPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                return endpoint;
            }
        }

        return null;
    }

    private static string NormalizePath(string path)
    {
        path = path.TrimEnd('/');
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }
        return path.ToLowerInvariant();
    }
}

/// <summary>
/// Individual SmartVault endpoint information
/// </summary>
public class SmartVaultEndpoint
{
    public required string BasePath { get; init; }
    public required string RootDirectory { get; init; }
    public required SmartVaultEndpointOptions Options { get; init; }
    public FileChangeNotificationService? NotificationService { get; set; }
    public FileChangeQueueBridge? QueueBridge { get; set; }
}
