using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SemanticPacker.Core.Contracts;
using SemanticPacker.Core.Models;

namespace SemanticPacker.Core.Services;

/// <summary>
/// Downloads models from Hugging Face Hub
/// </summary>
public class HuggingFaceDownloader(IHttpClientFactory httpClientFactory, ILogger<HuggingFaceDownloader> logger)
    : IModelDownloader
{
    private const string ApiBase = "https://huggingface.co/api/models";
    private const string DownloadBase = "https://huggingface.co";

    public async Task<List<ModelFile>> GetFilesAsync(string repoId, string revision = "main", CancellationToken cancellationToken = default)
    {
        var files = new List<ModelFile>();
        await FetchFilesRecursiveAsync(repoId, revision, null, files, cancellationToken);
        return files;
    }

    private async Task FetchFilesRecursiveAsync(string repoId, string revision, string? path, List<ModelFile> files, CancellationToken cancellationToken)
    {
        var url = string.IsNullOrEmpty(path)
            ? $"{ApiBase}/{repoId}/tree/{revision}"
            : $"{ApiBase}/{repoId}/tree/{revision}/{path}";

        using var httpClient = httpClientFactory.CreateClient("semantic-packer-client");
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        foreach (var element in document.RootElement.EnumerateArray())
        {
            var type = element.GetProperty("type").GetString() ?? string.Empty;
            var filePath = element.GetProperty("path").GetString() ?? string.Empty;
            
            if (type == "directory")
            {
                await FetchFilesRecursiveAsync(repoId, revision, filePath, files, cancellationToken);
            }
            else
            {
                var size = element.TryGetProperty("size", out var sizeProperty) 
                    ? sizeProperty.GetInt64() 
                    : 0;
                    
                files.Add(new ModelFile
                {
                    Type = type,
                    Path = filePath,
                    Size = size
                });
            }
        }
    }

    public async Task DownloadRepositoryAsync(string repoId, string outputDir, string revision = "main", int maxParallel = 4, Func<ModelFile, bool>? filter = null, CancellationToken cancellationToken = default)
    {
        var files = await GetFilesAsync(repoId, revision, cancellationToken);
        if (filter != null) files = files.Where(filter).ToList();

        logger.LogInformation("Found {Count} files ({Size})", files.Count, FormatBytes(files.Sum(f => f.Size)));
        Directory.CreateDirectory(outputDir);

        using var semaphore = new SemaphoreSlim(maxParallel);
        var tasks = files.Select(async file =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                await DownloadFileAsync(repoId, file, outputDir, revision, cancellationToken: cancellationToken);
                logger.LogDebug("Downloaded: {Path}", file.Path);
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);
    }

    public async Task DownloadFileAsync(string repoId, ModelFile file, string outputDir, string revision = "main", IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var outputPath = Path.Combine(outputDir, file.Path);

        if (File.Exists(outputPath) && new FileInfo(outputPath).Length == file.Size)
        {
            logger.LogDebug("Skipped (exists): {Path}", file.Path);
            return;
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tempPath = outputPath + ".tmp";
        long existingSize = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{DownloadBase}/{repoId}/resolve/{revision}/{file.Path}");
        if (existingSize > 0)
            request.Headers.Range = new RangeHeaderValue(existingSize, null);

        using var httpClient = httpClientFactory.CreateClient("semantic-packer-client");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            if (File.Exists(tempPath)) File.Move(tempPath, outputPath, true);
            return;
        }
        response.EnsureSuccessStatusCode();

        var append = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fs = new FileStream(tempPath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            progress?.Report(read);
        }

        fs.Close();
        File.Move(tempPath, outputPath, true);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int i = 0;
        while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
        return $"{size:0.##} {units[i]}";
    }
}
