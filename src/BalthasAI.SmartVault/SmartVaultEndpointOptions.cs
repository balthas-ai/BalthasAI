namespace BalthasAI.SmartVault;

/// <summary>
/// Individual WebDAV endpoint options (configured after Build)
/// </summary>
public class SmartVaultEndpointOptions
{
    /// <summary>
    /// Root directory where WebDAV files are stored
    /// </summary>
    public string RootDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Basic authentication realm name
    /// </summary>
    public string Realm { get; set; } = "SmartVault";

    /// <summary>
    /// Allow anonymous access (default: false)
    /// </summary>
    public bool AllowAnonymous { get; set; } = false;

    /// <summary>
    /// Authentication handler (if null, handled based on AllowAnonymous)
    /// </summary>
    public AuthenticationHandler? AuthenticationHandler { get; set; }

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
    /// File extension filter for processing (null means all files)
    /// </summary>
    public HashSet<string>? AllowedExtensions { get; set; }

    /// <summary>
    /// Patterns for excluded files/directories
    /// </summary>
    public List<string> ExcludePatterns { get; set; } = [".git", ".vs", "node_modules", "bin", "obj"];

    /// <summary>
    /// Enable file processing for this endpoint
    /// </summary>
    public bool EnableFileProcessing { get; set; } = true;

    /// <summary>
    /// Read-only mode (disables PUT, DELETE, MKCOL, etc.)
    /// </summary>
    public bool ReadOnly { get; set; } = false;

    /// <summary>
    /// Configures simple username/password authentication.
    /// </summary>
    /// <param name="credentials">Username-password pairs</param>
    public SmartVaultEndpointOptions UseBasicAuthentication(params (string username, string password)[] credentials)
    {
        var credentialDict = credentials.ToDictionary(c => c.username, c => c.password);

        AuthenticationHandler = (context, _) =>
        {
            if (credentialDict.TryGetValue(context.Username, out var password) &&
                password == context.Password)
            {
                return Task.FromResult(AuthenticationResult.Success(context.Username));
            }

            return Task.FromResult(AuthenticationResult.Failed);
        };

        AllowAnonymous = false;
        return this;
    }

    /// <summary>
    /// Configures a custom authentication handler.
    /// </summary>
    /// <param name="handler">Authentication handler</param>
    public SmartVaultEndpointOptions UseAuthentication(AuthenticationHandler handler)
    {
        AuthenticationHandler = handler;
        AllowAnonymous = false;
        return this;
    }

    /// <summary>
    /// Allows anonymous access.
    /// </summary>
    public SmartVaultEndpointOptions UseAnonymousAccess()
    {
        AllowAnonymous = true;
        AuthenticationHandler = null;
        return this;
    }
}
