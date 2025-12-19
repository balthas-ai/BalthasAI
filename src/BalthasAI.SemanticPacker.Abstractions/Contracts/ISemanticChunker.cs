using SemanticPacker.Core.Models;

namespace SemanticPacker.Core.Contracts;

/// <summary>
/// Semantic chunker interface
/// </summary>
public interface ISemanticChunker
{
    Task<List<SemanticChunk>> ChunkAsync(string text, SemanticChunkingOptions? options = null, CancellationToken cancellationToken = default);
}
