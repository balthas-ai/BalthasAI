using System.Security.Cryptography;

namespace BalthasAI.SmartVault.Processing;

/// <summary>
/// File hash computation utility
/// </summary>
public static class FileHasher
{
    /// <summary>
    /// Computes the SHA256 hash of a file.
    /// </summary>
    public static async Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return string.Empty;
        }

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 81920,
            useAsync: true);

        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Computes the SHA256 hash of file content.
    /// </summary>
    public static string ComputeHash(byte[] content)
    {
        var hashBytes = SHA256.HashData(content);
        return Convert.ToHexString(hashBytes);
    }
}
