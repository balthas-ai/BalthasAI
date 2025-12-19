using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BalthasAI.SmartVault.VectorStore;

/// <summary>
/// SQLite-based chunk and vector store.
/// Supports vector search via sqlite-vec extension.
/// </summary>
public class SqliteVectorStore : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly int _embeddingDimension;
    private readonly ILogger<SqliteVectorStore> _logger;
    private SqliteConnection? _connection;

    public SqliteVectorStore(
        string databasePath,
        int embeddingDimension,
        ILogger<SqliteVectorStore> logger)
    {
        _connectionString = $"Data Source={databasePath}";
        _embeddingDimension = embeddingDimension;
        _logger = logger;
    }

    /// <summary>
    /// Initializes the database.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _connection = new SqliteConnection(_connectionString);
        await _connection.OpenAsync(cancellationToken);

        // Try to load sqlite-vec extension (if installed)
        try
        {
            await using var loadCmd = _connection.CreateCommand();
            loadCmd.CommandText = "SELECT load_extension('vec0')";
            await loadCmd.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("sqlite-vec extension loaded");
        }
        catch (SqliteException)
        {
            _logger.LogWarning("sqlite-vec extension not available. Vector search will be disabled.");
        }

        // Create tables
        await CreateTablesAsync(cancellationToken);
    }

    private async Task CreateTablesAsync(CancellationToken cancellationToken)
    {
        var sql = $"""
            -- Source file tracking table
            CREATE TABLE IF NOT EXISTS source_files (
                path TEXT PRIMARY KEY,
                hash TEXT NOT NULL,
                file_size INTEGER NOT NULL,
                chunk_count INTEGER NOT NULL DEFAULT 0,
                parquet_path TEXT,
                status TEXT NOT NULL DEFAULT 'Pending',
                processed_at TEXT,
                is_synced INTEGER NOT NULL DEFAULT 0
            );

            -- Chunks table
            CREATE TABLE IF NOT EXISTS chunks (
                id TEXT PRIMARY KEY,
                source_path TEXT NOT NULL,
                source_hash TEXT NOT NULL,
                chunk_index INTEGER NOT NULL,
                text TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                page_number INTEGER,
                source_location TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY (source_path) REFERENCES source_files(path) ON DELETE CASCADE
            );

            -- Embeddings table (managed separately - replaced with virtual table when using sqlite-vec)
            CREATE TABLE IF NOT EXISTS embeddings (
                chunk_id TEXT PRIMARY KEY,
                embedding BLOB NOT NULL,
                FOREIGN KEY (chunk_id) REFERENCES chunks(id) ON DELETE CASCADE
            );

            -- Create indexes
            CREATE INDEX IF NOT EXISTS idx_chunks_source_path ON chunks(source_path);
            CREATE INDEX IF NOT EXISTS idx_chunks_source_hash ON chunks(source_hash);
            CREATE INDEX IF NOT EXISTS idx_source_files_status ON source_files(status);
            CREATE INDEX IF NOT EXISTS idx_source_files_synced ON source_files(is_synced);
            """;

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Upserts a source file record.
    /// </summary>
    public async Task UpsertSourceFileAsync(SourceFileRecord record, CancellationToken cancellationToken = default)
    {
        var sql = """
            INSERT INTO source_files (path, hash, file_size, chunk_count, parquet_path, status, processed_at, is_synced)
            VALUES (@path, @hash, @file_size, @chunk_count, @parquet_path, @status, @processed_at, @is_synced)
            ON CONFLICT(path) DO UPDATE SET
                hash = excluded.hash,
                file_size = excluded.file_size,
                chunk_count = excluded.chunk_count,
                parquet_path = excluded.parquet_path,
                status = excluded.status,
                processed_at = excluded.processed_at,
                is_synced = excluded.is_synced
            """;

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@path", record.Path);
        cmd.Parameters.AddWithValue("@hash", record.Hash);
        cmd.Parameters.AddWithValue("@file_size", record.FileSize);
        cmd.Parameters.AddWithValue("@chunk_count", record.ChunkCount);
        cmd.Parameters.AddWithValue("@parquet_path", record.ParquetPath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@status", record.Status.ToString());
        cmd.Parameters.AddWithValue("@processed_at", record.ProcessedAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("@is_synced", record.IsSyncedToVectorDb ? 1 : 0);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Gets source file information.
    /// </summary>
    public async Task<SourceFileRecord?> GetSourceFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var sql = "SELECT * FROM source_files WHERE path = @path";

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@path", path);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadSourceFileRecord(reader);
        }

        return null;
    }

    /// <summary>
    /// Batch inserts chunks.
    /// </summary>
    public async Task InsertChunksAsync(IEnumerable<VectorChunkRecord> chunks, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _connection!.BeginTransactionAsync(cancellationToken);

        try
        {
            var sql = """
                INSERT INTO chunks (id, source_path, source_hash, chunk_index, text, content_hash, page_number, source_location, created_at, updated_at)
                VALUES (@id, @source_path, @source_hash, @chunk_index, @text, @content_hash, @page_number, @source_location, @created_at, @updated_at)
                ON CONFLICT(id) DO UPDATE SET
                    text = excluded.text,
                    content_hash = excluded.content_hash,
                    updated_at = excluded.updated_at
                """;

            foreach (var chunk in chunks)
            {
                await using var cmd = _connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", chunk.Id);
                cmd.Parameters.AddWithValue("@source_path", chunk.SourcePath);
                cmd.Parameters.AddWithValue("@source_hash", chunk.SourceHash);
                cmd.Parameters.AddWithValue("@chunk_index", chunk.ChunkIndex);
                cmd.Parameters.AddWithValue("@text", chunk.Text);
                cmd.Parameters.AddWithValue("@content_hash", chunk.ContentHash);
                cmd.Parameters.AddWithValue("@page_number", chunk.PageNumber ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@source_location", chunk.SourceLocation ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@created_at", chunk.CreatedAtUtc.ToString("O"));
                cmd.Parameters.AddWithValue("@updated_at", chunk.UpdatedAtUtc.ToString("O"));

                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Saves an embedding.
    /// </summary>
    public async Task SaveEmbeddingAsync(string chunkId, float[] embedding, CancellationToken cancellationToken = default)
    {
        var sql = """
            INSERT INTO embeddings (chunk_id, embedding)
            VALUES (@chunk_id, @embedding)
            ON CONFLICT(chunk_id) DO UPDATE SET embedding = excluded.embedding
            """;

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@chunk_id", chunkId);
        cmd.Parameters.AddWithValue("@embedding", EmbeddingToBytes(embedding));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Batch saves embeddings.
    /// </summary>
    public async Task SaveEmbeddingsBatchAsync(
        IEnumerable<(string ChunkId, float[] Embedding)> embeddings,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _connection!.BeginTransactionAsync(cancellationToken);

        try
        {
            var sql = """
                INSERT INTO embeddings (chunk_id, embedding)
                VALUES (@chunk_id, @embedding)
                ON CONFLICT(chunk_id) DO UPDATE SET embedding = excluded.embedding
                """;

            foreach (var (chunkId, embedding) in embeddings)
            {
                await using var cmd = _connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@chunk_id", chunkId);
                cmd.Parameters.AddWithValue("@embedding", EmbeddingToBytes(embedding));

                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Deletes all chunks for a source file.
    /// </summary>
    public async Task DeleteChunksBySourcePathAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        // Delete embeddings first
        var deleteEmbeddingsSql = """
            DELETE FROM embeddings WHERE chunk_id IN (
                SELECT id FROM chunks WHERE source_path = @source_path
            )
            """;

        await using (var cmd = _connection!.CreateCommand())
        {
            cmd.CommandText = deleteEmbeddingsSql;
            cmd.Parameters.AddWithValue("@source_path", sourcePath);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Delete chunks
        var deleteChunksSql = "DELETE FROM chunks WHERE source_path = @source_path";
        await using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = deleteChunksSql;
            cmd.Parameters.AddWithValue("@source_path", sourcePath);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Gets chunks without embeddings.
    /// </summary>
    public async Task<List<VectorChunkRecord>> GetChunksWithoutEmbeddingAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var sql = """
            SELECT c.* FROM chunks c
            LEFT JOIN embeddings e ON c.id = e.chunk_id
            WHERE e.chunk_id IS NULL
            LIMIT @limit
            """;

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@limit", limit);

        var chunks = new List<VectorChunkRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            chunks.Add(ReadChunkRecord(reader));
        }

        return chunks;
    }

    /// <summary>
    /// Gets source files that need synchronization.
    /// </summary>
    public async Task<List<SourceFileRecord>> GetUnsyncedSourceFilesAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var sql = """
            SELECT * FROM source_files 
            WHERE status = 'Completed' AND is_synced = 0
            LIMIT @limit
            """;

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@limit", limit);

        var records = new List<SourceFileRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadSourceFileRecord(reader));
        }

        return records;
    }

    /// <summary>
    /// Marks a source file as synced.
    /// </summary>
    public async Task MarkSourceFileAsSyncedAsync(string path, CancellationToken cancellationToken = default)
    {
        var sql = "UPDATE source_files SET is_synced = 1 WHERE path = @path";

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@path", path);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SourceFileRecord ReadSourceFileRecord(SqliteDataReader reader)
    {
        return new SourceFileRecord
        {
            Path = reader.GetString(reader.GetOrdinal("path")),
            Hash = reader.GetString(reader.GetOrdinal("hash")),
            FileSize = reader.GetInt64(reader.GetOrdinal("file_size")),
            ChunkCount = reader.GetInt32(reader.GetOrdinal("chunk_count")),
            ParquetPath = reader.IsDBNull(reader.GetOrdinal("parquet_path"))
                ? null
                : reader.GetString(reader.GetOrdinal("parquet_path")),
            Status = Enum.Parse<ProcessingStatus>(reader.GetString(reader.GetOrdinal("status"))),
            ProcessedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("processed_at"))),
            IsSyncedToVectorDb = reader.GetInt32(reader.GetOrdinal("is_synced")) == 1
        };
    }

    private static VectorChunkRecord ReadChunkRecord(SqliteDataReader reader)
    {
        return new VectorChunkRecord
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            SourcePath = reader.GetString(reader.GetOrdinal("source_path")),
            SourceHash = reader.GetString(reader.GetOrdinal("source_hash")),
            ChunkIndex = reader.GetInt32(reader.GetOrdinal("chunk_index")),
            Text = reader.GetString(reader.GetOrdinal("text")),
            ContentHash = reader.GetString(reader.GetOrdinal("content_hash")),
            PageNumber = reader.IsDBNull(reader.GetOrdinal("page_number"))
                ? null
                : reader.GetInt32(reader.GetOrdinal("page_number")),
            SourceLocation = reader.IsDBNull(reader.GetOrdinal("source_location"))
                ? null
                : reader.GetString(reader.GetOrdinal("source_location")),
            CreatedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
            UpdatedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("updated_at")))
        };
    }

    private static byte[] EmbeddingToBytes(float[] embedding)
    {
        var bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] BytesToEmbedding(byte[] bytes)
    {
        var embedding = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, embedding, 0, bytes.Length);
        return embedding;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
