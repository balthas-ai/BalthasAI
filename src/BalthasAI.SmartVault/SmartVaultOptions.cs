namespace BalthasAI.SmartVault;

/// <summary>
/// SmartVault global options (configured before Build)
/// </summary>
public class SmartVaultOptions
{
    /// <summary>
    /// Data storage path (version info, vector DB, Parquet files, etc.)
    /// </summary>
    public string DataPath { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "smartvault-data");

    /// <summary>
    /// Embedding vector dimension (depends on the model used)
    /// BGE-M3: 1024
    /// </summary>
    public int EmbeddingDimension { get; set; } = 1024;
}
