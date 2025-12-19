using System.Text;
using BalthasAI.SmartVault.WebDav;

namespace BalthasAI.SmartVault;

/// <summary>
/// Unified SmartVault middleware
/// </summary>
public class SmartVaultMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SmartVaultEndpointRegistry _registry;

    public SmartVaultMiddleware(
        RequestDelegate next,
        SmartVaultOptions options,
        SmartVaultEndpointRegistry registry,
        IServiceProvider serviceProvider)
    {
        _next = next;
        _registry = registry;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        var endpoint = _registry.FindEndpoint(path);

        if (endpoint is null)
        {
            await _next(context);
            return;
        }

        // Authentication check
        var authResult = await AuthenticateAsync(context, endpoint);
        if (!authResult.IsAuthenticated)
        {
            SetUnauthorizedResponse(context, endpoint.Options.Realm);
            return;
        }

        // Store authenticated user info in HttpContext (for later use)
        if (authResult.Username is not null)
        {
            context.Items["SmartVault.Username"] = authResult.Username;
            context.Items["SmartVault.Roles"] = authResult.Roles;
        }

        // Handle WebDAV request
        var handler = new WebDavRequestHandler(
            CreateWebDavOptions(endpoint),
            endpoint.NotificationService);

        await handler.HandleAsync(context);
    }

    private async Task<AuthenticationResult> AuthenticateAsync(HttpContext context, SmartVaultEndpoint endpoint)
    {
        var options = endpoint.Options;

        // Allow anonymous access
        if (options.AllowAnonymous)
        {
            return AuthenticationResult.Success("anonymous");
        }

        // No handler means authentication failure
        if (options.AuthenticationHandler is null)
        {
            return AuthenticationResult.Failed;
        }

        // Parse Basic authentication header
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticationResult.Failed;
        }

        try
        {
            var encodedCredentials = authHeader["Basic ".Length..].Trim();
            var decodedBytes = Convert.FromBase64String(encodedCredentials);
            var decodedCredentials = Encoding.UTF8.GetString(decodedBytes);

            var separatorIndex = decodedCredentials.IndexOf(':');
            if (separatorIndex < 0)
            {
                return AuthenticationResult.Failed;
            }

            var username = decodedCredentials[..separatorIndex];
            var password = decodedCredentials[(separatorIndex + 1)..];

            // Create authentication context
            var authContext = new AuthenticationContext
            {
                Username = username,
                Password = password,
                EndpointPath = endpoint.BasePath,
                ResourcePath = context.Request.Path.Value ?? "",
                Method = context.Request.Method
            };

            // Invoke handler
            return await options.AuthenticationHandler(authContext, context.RequestAborted);
        }
        catch
        {
            return AuthenticationResult.Failed;
        }
    }

    private static void SetUnauthorizedResponse(HttpContext context, string realm)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = $"Basic realm=\"{realm}\", charset=\"UTF-8\"";
    }

    private static WebDavOptions CreateWebDavOptions(SmartVaultEndpoint endpoint)
    {
        return new WebDavOptions
        {
            BasePath = endpoint.BasePath,
            RootDirectory = endpoint.RootDirectory,
            AllowAnonymous = true // Authentication is handled by middleware
        };
    }
}
