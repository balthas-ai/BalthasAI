namespace BalthasAI.SmartVault.WebDav;

/// <summary>
/// WebDAV service configuration options (internal use)
/// </summary>
public class WebDavOptions
{
    /// <summary>
    /// Base path where WebDAV service is mapped (e.g., "/dav")
    /// </summary>
    public string BasePath { get; set; } = "/dav";

    /// <summary>
    /// Root directory where WebDAV files are stored
    /// </summary>
    public string RootDirectory { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "webdav-files");

    /// <summary>
    /// Allow anonymous access (authentication is handled by SmartVaultMiddleware)
    /// </summary>
    public bool AllowAnonymous { get; set; } = true;
}
