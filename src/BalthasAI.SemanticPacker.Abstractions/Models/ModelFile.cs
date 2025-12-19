namespace SemanticPacker.Core.Models;

/// <summary>
/// Hugging Face file information
/// </summary>
public class ModelFile
{
    public string Type { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
}
