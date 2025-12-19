namespace SemanticPacker.Core.Contracts;

/// <summary>
/// Embedding service interface
/// </summary>
public interface IEmbeddingService : IDisposable
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
    Task<float[][]> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);
}
