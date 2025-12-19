using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using SemanticPacker.Core.Contracts;
using SemanticPacker.Core.Models;

namespace SemanticPacker.Core.Services;

/// <summary>
/// Parquet chunk storage (self-contained schema, incremental update support, Zstd compression)
/// </summary>
public class ParquetChunkStorage(ILogger<ParquetChunkStorage> logger) : IChunkStorage
{
    public async Task SaveAsync(List<SemanticChunk> chunks, ChunkMetadata metadata, string path, CancellationToken cancellationToken = default)
    {
        // Define self-contained schema
        var schema = new ParquetSchema(
            // Chunk identification
            new DataField<string>("id"),
            new DataField<string>("content_hash"),
            
            // Source document information
            new DataField<string>("source_id"),
            new DataField<string>("source_name"),
            new DataField<string>("version"),
            new DataField<DateTime>("created_at"),
            new DataField<string?>("source_content_type"),
            new DataField<long?>("source_file_size"),
            new DataField<string?>("source_file_hash"),
            
            // Chunk content
            new DataField<string>("text"),
            new DataField<int>("chunk_index"),
            
            // Location information (all nullable)
            new DataField<int?>("start_index"),
            new DataField<int?>("end_index"),
            new DataField<int?>("page_number"),
            new DataField<string?>("source_location")
        );

        // Prepare data
        var ids = new string[chunks.Count];
        var contentHashes = new string[chunks.Count];
        var sourceIds = new string[chunks.Count];
        var sourceNames = new string[chunks.Count];
        var versions = new string[chunks.Count];
        var createdAts = new DateTime[chunks.Count];
        var sourceContentTypes = new string?[chunks.Count];
        var sourceFileSizes = new long?[chunks.Count];
        var sourceFileHashes = new string?[chunks.Count];
        var texts = new string[chunks.Count];
        var chunkIndices = new int[chunks.Count];
        var startIndices = new int?[chunks.Count];
        var endIndices = new int?[chunks.Count];
        var pageNumbers = new int?[chunks.Count];
        var sourceLocations = new string?[chunks.Count];

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var contentHash = ComputeContentHash(chunk.Text);
            
            ids[i] = ComputeDeterministicId(metadata.SourceId, contentHash);
            contentHashes[i] = contentHash;
            sourceIds[i] = metadata.SourceId;
            sourceNames[i] = metadata.SourceName;
            versions[i] = metadata.Version;
            createdAts[i] = metadata.CreatedAt;
            sourceContentTypes[i] = metadata.SourceContentType;
            sourceFileSizes[i] = metadata.SourceFileSize;
            sourceFileHashes[i] = metadata.SourceFileHash;
            texts[i] = chunk.Text;
            chunkIndices[i] = chunk.ChunkIndex;
            startIndices[i] = chunk.StartIndex;
            endIndices[i] = chunk.EndIndex;
            pageNumbers[i] = chunk.PageNumber;
            sourceLocations[i] = chunk.SourceLocation;
        }

        // Write Parquet file with Zstd compression
        using var stream = File.Create(path);
        using var writer = await ParquetWriter.CreateAsync(schema, stream, cancellationToken: cancellationToken);
        
        writer.CompressionMethod = CompressionMethod.Zstd;
        writer.CompressionLevel = System.IO.Compression.CompressionLevel.Optimal;
        
        using var rowGroup = writer.CreateRowGroup();
        await rowGroup.WriteColumnAsync(new DataColumn(schema.DataFields[0], ids), cancellationToken);
        await rowGroup.WriteColumnAsync(new DataColumn(schema.DataFields[1], contentHashes), cancellationToken);
        await rowGroup.WriteColumnAsync(new DataColumn(schema.DataFields[2], sourceIds), cancellationToken);
        await rowGroup.WriteColumnAsync(new DataColumn(schema.DataFields[3], sourceNames), cancellationToken);
        await rowGroup.WriteColumnAsync(new DataColumn(schema.DataFields[4], versions), cancellationToken);
        await rowGroup.WriteColumnAsync(new DataColumn(schema.DataFields[5], createdAts), cancellationToken);
        await rowGroup.WriteColumnAsync(new DataColumn(schema.DataFields[6], sourceContentTypes), cancellationToken);
        await rowGroup.WriteColumnAsync(new DataColumn(schema.DataFields[7], sourceFileSizes), cancellationToken);
        await rowGroup.WriteColumnAsync(new DataColumn(schema.DataFields[8], sourceFileHashes), cancellationToken);
        await rowGroup.WriteColumnAsync(new DataColumn(schema.DataFields[9], texts), cancellationToken);
        await rowGroup.WriteColumnAsync(new DataColumn(schema.DataFields[10], chunkIndices), cancellationToken);
        await rowGroup.WriteColumnAsync(new DataColumn(schema.DataFields[11], startIndices), cancellationToken);
        await rowGroup.WriteColumnAsync(new DataColumn(schema.DataFields[12], endIndices), cancellationToken);
        await rowGroup.WriteColumnAsync(new DataColumn(schema.DataFields[13], pageNumbers), cancellationToken);
        await rowGroup.WriteColumnAsync(new DataColumn(schema.DataFields[14], sourceLocations), cancellationToken);

        logger.LogDebug("Saved {Count} chunks to {Path} (version: {Version}, compression: Zstd)", chunks.Count, path, metadata.Version);
    }

    public async Task<List<SemanticChunk>> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var chunks = new List<SemanticChunk>();

        using var stream = File.OpenRead(path);
        using var reader = await ParquetReader.CreateAsync(stream, cancellationToken: cancellationToken);

        for (int g = 0; g < reader.RowGroupCount; g++)
        {
            using var rowGroupReader = reader.OpenRowGroupReader(g);

            var idColumn = await rowGroupReader.ReadColumnAsync(reader.Schema.DataFields[0], cancellationToken);
            var contentHashColumn = await rowGroupReader.ReadColumnAsync(reader.Schema.DataFields[1], cancellationToken);
            var textColumn = await rowGroupReader.ReadColumnAsync(reader.Schema.DataFields[9], cancellationToken);
            var chunkIndexColumn = await rowGroupReader.ReadColumnAsync(reader.Schema.DataFields[10], cancellationToken);
            var startIndexColumn = await rowGroupReader.ReadColumnAsync(reader.Schema.DataFields[11], cancellationToken);
            var endIndexColumn = await rowGroupReader.ReadColumnAsync(reader.Schema.DataFields[12], cancellationToken);
            var pageNumberColumn = await rowGroupReader.ReadColumnAsync(reader.Schema.DataFields[13], cancellationToken);
            var sourceLocationColumn = await rowGroupReader.ReadColumnAsync(reader.Schema.DataFields[14], cancellationToken);

            var ids = (string[])idColumn.Data;
            var contentHashes = (string[])contentHashColumn.Data;
            var texts = (string[])textColumn.Data;
            var chunkIndices = (int[])chunkIndexColumn.Data;
            var startIndices = (int?[])startIndexColumn.Data;
            var endIndices = (int?[])endIndexColumn.Data;
            var pageNumbers = (int?[])pageNumberColumn.Data;
            var sourceLocations = (string?[])sourceLocationColumn.Data;

            for (int i = 0; i < texts.Length; i++)
            {
                chunks.Add(new SemanticChunk
                {
                    Id = ids[i],
                    ContentHash = contentHashes[i],
                    Text = texts[i],
                    ChunkIndex = chunkIndices[i],
                    StartIndex = startIndices[i],
                    EndIndex = endIndices[i],
                    PageNumber = pageNumbers[i],
                    SourceLocation = sourceLocations[i]
                });
            }
        }

        logger.LogDebug("Loaded {Count} chunks from {Path}", chunks.Count, path);
        return chunks;
    }

    /// <summary>
    /// Generate SHA256 hash based on text content
    /// </summary>
    private static string ComputeContentHash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Generate deterministic ID based on source_id + content_hash
    /// </summary>
    private static string ComputeDeterministicId(string sourceId, string contentHash)
    {
        var combined = $"{sourceId}:{contentHash}";
        var bytes = Encoding.UTF8.GetBytes(combined);
        var hash = SHA256.HashData(bytes);
        // Convert first 16 bytes to GUID format (UUID v5 style)
        return new Guid(hash[..16]).ToString();
    }
}
