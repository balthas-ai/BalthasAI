using System.Xml.Linq;

namespace BalthasAI.SmartVault.WebDav;

/// <summary>
/// WebDAV 요청을 처리하는 핸들러
/// </summary>
public class WebDavRequestHandler
{
    private readonly WebDavOptions _options;
    private readonly FileChangeNotificationService? _notificationService;
    private static readonly XNamespace DavNamespace = "DAV:";

    public WebDavRequestHandler(WebDavOptions options, FileChangeNotificationService? notificationService = null)
    {
        _options = options;
        _notificationService = notificationService;

        if (!Directory.Exists(_options.RootDirectory))
        {
            Directory.CreateDirectory(_options.RootDirectory);
        }
    }

    public async Task HandleAsync(HttpContext context)
    {
        var method = context.Request.Method.ToUpperInvariant();
        var relativePath = GetRelativePath(context.Request.Path);
        var physicalPath = GetPhysicalPath(relativePath);

        var result = method switch
        {
            "GET" => await HandleGetAsync(context, physicalPath),
            "PUT" => await HandlePutAsync(context, physicalPath),
            "DELETE" => await HandleDeleteAsync(context, physicalPath),
            "OPTIONS" => HandleOptions(context),
            WebDavMethods.PropFind => await HandlePropFindAsync(context, relativePath, physicalPath),
            WebDavMethods.PropPatch => HandlePropPatch(context),
            WebDavMethods.MkCol => HandleMkCol(context, physicalPath),
            WebDavMethods.Copy => await HandleCopyAsync(context, physicalPath),
            WebDavMethods.Move => await HandleMoveAsync(context, physicalPath),
            WebDavMethods.Lock => HandleLock(context),
            WebDavMethods.Unlock => HandleUnlock(context),
            _ => Results.StatusCode(StatusCodes.Status405MethodNotAllowed)
        };

        await result.ExecuteAsync(context);
    }

    private string GetRelativePath(PathString requestPath)
    {
        var basePath = _options.BasePath.TrimEnd('/');
        var path = requestPath.Value ?? "/";

        if (path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
        {
            path = path[basePath.Length..];
        }

        return string.IsNullOrEmpty(path) ? "/" : path;
    }

    private string GetPhysicalPath(string relativePath)
    {
        var normalizedPath = relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_options.RootDirectory, normalizedPath);
    }

    private async Task<IResult> HandleGetAsync(HttpContext context, string physicalPath)
    {
        if (File.Exists(physicalPath))
        {
            var content = await File.ReadAllBytesAsync(physicalPath);
            var contentType = GetContentType(physicalPath);
            return Results.File(content, contentType);
        }

        if (Directory.Exists(physicalPath))
        {
            return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);
        }

        return Results.NotFound();
    }

    private async Task<IResult> HandlePutAsync(HttpContext context, string physicalPath)
    {
        var directory = Path.GetDirectoryName(physicalPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            return Results.StatusCode(StatusCodes.Status409Conflict);
        }

        var isNew = !File.Exists(physicalPath);
        var relativePath = GetRelativePath(context.Request.Path);

        await using var fileStream = new FileStream(physicalPath, FileMode.Create);
        await context.Request.Body.CopyToAsync(fileStream);

        // 변경 이벤트 발행
        _notificationService?.NotifyWebDavChange(
            isNew ? FileChangeType.Created : FileChangeType.Modified,
            relativePath,
            physicalPath);

        return isNew
            ? Results.StatusCode(StatusCodes.Status201Created)
            : Results.NoContent();
    }

    private Task<IResult> HandleDeleteAsync(HttpContext context, string physicalPath)
    {
        var relativePath = GetRelativePath(context.Request.Path);

        if (File.Exists(physicalPath))
        {
            File.Delete(physicalPath);
            _notificationService?.NotifyWebDavChange(FileChangeType.Deleted, relativePath, physicalPath);
            return Task.FromResult(Results.NoContent());
        }

        if (Directory.Exists(physicalPath))
        {
            Directory.Delete(physicalPath, recursive: true);
            _notificationService?.NotifyWebDavChange(FileChangeType.Deleted, relativePath, physicalPath, isDirectory: true);
            return Task.FromResult(Results.NoContent());
        }

        return Task.FromResult(Results.NotFound());
    }

    private IResult HandleOptions(HttpContext context)
    {
        context.Response.Headers["Allow"] = "OPTIONS, GET, HEAD, PUT, DELETE, PROPFIND, PROPPATCH, MKCOL, COPY, MOVE, LOCK, UNLOCK";
        context.Response.Headers["DAV"] = "1, 2";
        context.Response.Headers["MS-Author-Via"] = "DAV";
        return Results.Ok();
    }

    private async Task<IResult> HandlePropFindAsync(HttpContext context, string relativePath, string physicalPath)
    {
        var depth = context.Request.Headers["Depth"].FirstOrDefault() ?? "infinity";

        if (!File.Exists(physicalPath) && !Directory.Exists(physicalPath))
        {
            return Results.NotFound();
        }

        var responses = new List<XElement>();
        await CollectPropertiesAsync(relativePath, physicalPath, depth, responses);

        var multiStatus = new XElement(DavNamespace + "multistatus",
            new XAttribute(XNamespace.Xmlns + "D", DavNamespace),
            responses);

        context.Response.ContentType = "application/xml; charset=utf-8";
        return Results.Text(multiStatus.ToString(), "application/xml", statusCode: StatusCodes.Status207MultiStatus);
    }

    private async Task CollectPropertiesAsync(string relativePath, string physicalPath, string depth, List<XElement> responses)
    {
        var href = Path.Combine(_options.BasePath, relativePath.TrimStart('/')).Replace('\\', '/');
        var isDirectory = Directory.Exists(physicalPath);

        responses.Add(CreateResponseElement(href, physicalPath, isDirectory));

        if (isDirectory && depth != "0")
        {
            foreach (var entry in Directory.GetFileSystemEntries(physicalPath))
            {
                var entryName = Path.GetFileName(entry);
                var entryRelativePath = Path.Combine(relativePath, entryName).Replace('\\', '/');
                var entryIsDirectory = Directory.Exists(entry);

                if (depth == "1")
                {
                    responses.Add(CreateResponseElement(
                        Path.Combine(_options.BasePath, entryRelativePath.TrimStart('/')).Replace('\\', '/'),
                        entry,
                        entryIsDirectory));
                }
                else
                {
                    await CollectPropertiesAsync(entryRelativePath, entry, depth, responses);
                }
            }
        }
    }

    private XElement CreateResponseElement(string href, string physicalPath, bool isDirectory)
    {
        var props = new List<XElement>
        {
            new(DavNamespace + "displayname", Path.GetFileName(physicalPath))
        };

        if (isDirectory)
        {
            props.Add(new XElement(DavNamespace + "resourcetype",
                new XElement(DavNamespace + "collection")));
        }
        else
        {
            props.Add(new XElement(DavNamespace + "resourcetype"));

            var fileInfo = new FileInfo(physicalPath);
            props.Add(new XElement(DavNamespace + "getcontentlength", fileInfo.Length));
            props.Add(new XElement(DavNamespace + "getlastmodified", fileInfo.LastWriteTimeUtc.ToString("R")));
            props.Add(new XElement(DavNamespace + "getcontenttype", GetContentType(physicalPath)));
        }

        return new XElement(DavNamespace + "response",
            new XElement(DavNamespace + "href", href),
            new XElement(DavNamespace + "propstat",
                new XElement(DavNamespace + "prop", props),
                new XElement(DavNamespace + "status", "HTTP/1.1 200 OK")));
    }

    private IResult HandlePropPatch(HttpContext context)
    {
        // 기본 구현: 속성 수정 요청 성공 응답
        return Results.StatusCode(StatusCodes.Status207MultiStatus);
    }

    private IResult HandleMkCol(HttpContext context, string physicalPath)
    {
        if (Directory.Exists(physicalPath) || File.Exists(physicalPath))
        {
            return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);
        }

        var parentDir = Path.GetDirectoryName(physicalPath);
        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
        {
            return Results.StatusCode(StatusCodes.Status409Conflict);
        }

        var relativePath = GetRelativePath(context.Request.Path);
        Directory.CreateDirectory(physicalPath);

        // 변경 이벤트 발행
        _notificationService?.NotifyWebDavChange(FileChangeType.Created, relativePath, physicalPath, isDirectory: true);

        return Results.StatusCode(StatusCodes.Status201Created);
    }

    private async Task<IResult> HandleCopyAsync(HttpContext context, string sourcePath)
    {
        var destination = GetDestinationPath(context);
        if (destination is null)
        {
            return Results.BadRequest();
        }

        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
        {
            return Results.NotFound();
        }

        var overwrite = context.Request.Headers["Overwrite"].FirstOrDefault() != "F";
        var destinationExists = File.Exists(destination) || Directory.Exists(destination);

        if (destinationExists && !overwrite)
        {
            return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        var isDirectory = Directory.Exists(sourcePath);
        await CopyFileOrDirectoryAsync(sourcePath, destination);

        // 변경 이벤트 발행
        var destRelativePath = GetRelativePathFromPhysical(destination);
        _notificationService?.NotifyWebDavChange(FileChangeType.Copied, destRelativePath, destination, isDirectory);

        return destinationExists
            ? Results.NoContent()
            : Results.StatusCode(StatusCodes.Status201Created);
    }

    private async Task<IResult> HandleMoveAsync(HttpContext context, string sourcePath)
    {
        var destination = GetDestinationPath(context);
        if (destination is null)
        {
            return Results.BadRequest();
        }

        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
        {
            return Results.NotFound();
        }

        var overwrite = context.Request.Headers["Overwrite"].FirstOrDefault() != "F";
        var destinationExists = File.Exists(destination) || Directory.Exists(destination);

        if (destinationExists && !overwrite)
        {
            return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        if (destinationExists)
        {
            if (File.Exists(destination)) File.Delete(destination);
            else Directory.Delete(destination, true);
        }

        var isDirectory = Directory.Exists(sourcePath);
        var sourceRelativePath = GetRelativePath(context.Request.Path);

        if (File.Exists(sourcePath))
        {
            File.Move(sourcePath, destination);
        }
        else
        {
            Directory.Move(sourcePath, destination);
        }

        // 변경 이벤트 발행
        var destRelativePath = GetRelativePathFromPhysical(destination);
        _notificationService?.NotifyWebDavChange(
            FileChangeType.Moved,
            destRelativePath,
            destination,
            isDirectory,
            sourceRelativePath,
            sourcePath);

        return destinationExists
            ? Results.NoContent()
            : Results.StatusCode(StatusCodes.Status201Created);
    }

    private string GetRelativePathFromPhysical(string physicalPath)
    {
        var relativePath = Path.GetRelativePath(_options.RootDirectory, physicalPath);
        return "/" + relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    private string? GetDestinationPath(HttpContext context)
    {
        var destinationHeader = context.Request.Headers["Destination"].FirstOrDefault();
        if (string.IsNullOrEmpty(destinationHeader))
        {
            return null;
        }

        if (Uri.TryCreate(destinationHeader, UriKind.Absolute, out var uri))
        {
            var relativePath = GetRelativePath(uri.AbsolutePath);
            return GetPhysicalPath(relativePath);
        }

        return null;
    }

    private async Task CopyFileOrDirectoryAsync(string source, string destination)
    {
        if (File.Exists(source))
        {
            var destDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            await using var sourceStream = File.OpenRead(source);
            await using var destStream = File.Create(destination);
            await sourceStream.CopyToAsync(destStream);
        }
        else if (Directory.Exists(source))
        {
            Directory.CreateDirectory(destination);

            foreach (var entry in Directory.GetFileSystemEntries(source))
            {
                var entryName = Path.GetFileName(entry);
                await CopyFileOrDirectoryAsync(entry, Path.Combine(destination, entryName));
            }
        }
    }

    private IResult HandleLock(HttpContext context)
    {
        // 간단한 Lock 토큰 응답
        var lockToken = $"urn:uuid:{Guid.NewGuid()}";
        context.Response.Headers["Lock-Token"] = $"<{lockToken}>";

        var lockDiscovery = new XElement(DavNamespace + "prop",
            new XAttribute(XNamespace.Xmlns + "D", DavNamespace),
            new XElement(DavNamespace + "lockdiscovery",
                new XElement(DavNamespace + "activelock",
                    new XElement(DavNamespace + "locktype",
                        new XElement(DavNamespace + "write")),
                    new XElement(DavNamespace + "lockscope",
                        new XElement(DavNamespace + "exclusive")),
                    new XElement(DavNamespace + "locktoken",
                        new XElement(DavNamespace + "href", lockToken)))));

        return Results.Text(lockDiscovery.ToString(), "application/xml", statusCode: StatusCodes.Status200OK);
    }

    private IResult HandleUnlock(HttpContext context)
    {
        return Results.NoContent();
    }

    private static string GetContentType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".txt" => "text/plain",
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }
}
