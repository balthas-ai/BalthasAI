using SemanticPacker.Core.Models;

namespace SemanticPacker.Core.Contracts;

/// <summary>
/// Chunk storage interface
/// </summary>
public interface IChunkStorage
{
    Task SaveAsync(List<SemanticChunk> chunks, ChunkMetadata metadata, string path, CancellationToken cancellationToken = default);
    Task<List<SemanticChunk>> LoadAsync(string path, CancellationToken cancellationToken = default);
}
