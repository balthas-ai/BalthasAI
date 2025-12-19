using SemanticPacker.Core.Models;

namespace SemanticPacker.Core.Contracts;

/// <summary>
/// Machine learning model downloader interface
/// </summary>
public interface IModelDownloader
{
    Task<List<ModelFile>> GetFilesAsync(string repoId, string revision = "main", CancellationToken cancellationToken = default);
    Task DownloadRepositoryAsync(string repoId, string outputDir, string revision = "main", int maxParallel = 4, Func<ModelFile, bool>? filter = null, CancellationToken cancellationToken = default);
    Task DownloadFileAsync(string repoId, ModelFile file, string outputDir, string revision = "main", IProgress<long>? progress = null, CancellationToken cancellationToken = default);
}
