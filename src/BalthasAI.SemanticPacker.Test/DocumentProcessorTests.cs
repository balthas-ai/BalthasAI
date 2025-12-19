using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SemanticPacker.Core.Contracts;
using SemanticPacker.Core.Services;
using SemanticPacker.Extractors;

namespace SemanticPacker.Test;

[TestClass]
public sealed class DocumentProcessorTests
{
    private static ServiceProvider? _serviceProvider;
    private static IDocumentProcessor? _processor;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        var services = new ServiceCollection();
        
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "SemanticPacker:BgeM3ModelPath", Path.Combine("E:", "bge-m3") },
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        
        services.AddSingleton<IEmbeddingService, BgeM3EmbeddingService>();
        services.AddSingleton<ISemanticChunker, SemanticChunker>();
        services.AddSingleton<IChunkStorage, ParquetChunkStorage>();
        services.AddSingleton<ITextExtractor, PlainTextExtractor>();
        services.AddSingleton<IDocumentProcessor, DocumentProcessor>();

        _serviceProvider = services.BuildServiceProvider();
        _processor = _serviceProvider.GetRequiredService<IDocumentProcessor>();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _serviceProvider?.Dispose();
    }

    [TestMethod]
    public async Task ProcessFile_WithValidTextFile_ShouldCreateParquetFile()
    {
        // Arrange
        var sampleDir = Path.Combine(Path.GetTempPath(), "SemanticPackerTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(sampleDir);

        var sampleFile = Path.Combine(sampleDir, "ai-overview.txt");
        await File.WriteAllTextAsync(sampleFile, """
            Artificial Intelligence (AI) is a technology where computer systems mimic human intelligence to learn and solve problems.
            Machine learning is a subfield of AI that learns patterns from data. Deep learning is a type of machine learning that uses neural networks.

            Natural Language Processing (NLP) is a technology for computers to understand and generate human language.
            Text classification, sentiment analysis, and machine translation are major NLP applications.
            Recently, Large Language Models (LLMs) have been revolutionizing the NLP field.

            Embeddings are techniques to convert text into vectors.
            This enables calculating semantic similarity.
            BGE-M3 is an embedding model that supports multiple languages.

            Semantic chunking splits text into meaningful units.
            Unlike fixed-size chunking, it considers context.
            It improves retrieval quality in RAG systems.
            """);

        var options = new DocumentProcessingOptions
        {
            ChunkingOptions = new SemanticChunkingOptions
            {
                SimilarityThreshold = 0.5f,
                MinChunkSize = 50,
                MaxChunkSize = 500
            },
            OverwriteExisting = true
        };

        try
        {
            // Act
            var result = await _processor!.ProcessFileAsync(sampleFile, options);

            // Assert
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.IsNotNull(result.OutputPath);
            Assert.IsTrue(File.Exists(result.OutputPath));
            Assert.IsGreaterThan(0, result.ChunkCount);
            Assert.IsNotNull(result.Metadata?.SourceFileHash);
            
            Console.WriteLine($"Output: {result.OutputPath}");
            Console.WriteLine($"Chunks: {result.ChunkCount}");
            Console.WriteLine($"Duration: {result.Duration.TotalMilliseconds:N0}ms");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(sampleDir))
                Directory.Delete(sampleDir, true);
        }
    }

    [TestMethod]
    public async Task ProcessDirectory_WithMultipleFiles_ShouldProcessAll()
    {
        // Arrange
        var sampleDir = Path.Combine(Path.GetTempPath(), "SemanticPackerTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(sampleDir);

        await File.WriteAllTextAsync(Path.Combine(sampleDir, "file1.txt"), "This is the first text file. It contains test content.");
        await File.WriteAllTextAsync(Path.Combine(sampleDir, "file2.md"), "# Markdown File\n\nThis is the second file content.");

        var options = new DocumentProcessingOptions { OverwriteExisting = true };

        try
        {
            // Act
            var results = new List<DocumentProcessingResult>();
            await foreach (var result in _processor!.ProcessDirectoryAsync(sampleDir, "*.*", false, options))
            {
                results.Add(result);
            }

            // Assert
            Assert.HasCount(2, results);
            Assert.IsTrue(results.All(r => r.Success));
            
            foreach (var result in results)
            {
                Console.WriteLine($"✓ {result.Metadata?.SourceName}: {result.ChunkCount} chunks");
            }
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(sampleDir))
                Directory.Delete(sampleDir, true);
        }
    }

    [TestMethod]
    public async Task ProcessFile_WithNonExistentFile_ShouldReturnError()
    {
        // Act
        var result = await _processor!.ProcessFileAsync("/nonexistent/file.txt");

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.ErrorMessage);
        Assert.Contains("not found", result.ErrorMessage);
    }

    [TestMethod]
    public async Task ProcessFile_WithUnsupportedFormat_ShouldReturnError()
    {
        // Arrange
        var tempFile = Path.GetTempFileName() + ".xyz";
        await File.WriteAllTextAsync(tempFile, "test content");

        try
        {
            // Act
            var result = await _processor!.ProcessFileAsync(tempFile);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.IsNotNull(result.ErrorMessage);
            Assert.Contains("No extractor", result.ErrorMessage);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
