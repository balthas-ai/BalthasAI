namespace BalthasAI.SmartVault;

/// <summary>
/// Authentication context information
/// </summary>
public class AuthenticationContext
{
    /// <summary>
    /// Username
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// Password
    /// </summary>
    public required string Password { get; init; }

    /// <summary>
    /// Requested endpoint path
    /// </summary>
    public required string EndpointPath { get; init; }

    /// <summary>
    /// Requested resource path
    /// </summary>
    public required string ResourcePath { get; init; }

    /// <summary>
    /// HTTP method
    /// </summary>
    public required string Method { get; init; }
}

/// <summary>
/// Authentication result
/// </summary>
public class AuthenticationResult
{
    /// <summary>
    /// Whether authentication succeeded
    /// </summary>
    public bool IsAuthenticated { get; init; }

    /// <summary>
    /// Authenticated username (on success)
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// User roles (optional)
    /// </summary>
    public IReadOnlyList<string>? Roles { get; init; }

    /// <summary>
    /// Creates a successful authentication result
    /// </summary>
    public static AuthenticationResult Success(string username, IReadOnlyList<string>? roles = null)
        => new() { IsAuthenticated = true, Username = username, Roles = roles };

    /// <summary>
    /// Authentication failure result
    /// </summary>
    public static AuthenticationResult Failed { get; } = new() { IsAuthenticated = false };
}

/// <summary>
/// Authentication handler delegate
/// </summary>
/// <param name="context">Authentication context</param>
/// <param name="cancellationToken">Cancellation token</param>
/// <returns>Authentication result</returns>
public delegate Task<AuthenticationResult> AuthenticationHandler(
    AuthenticationContext context,
    CancellationToken cancellationToken = default);
