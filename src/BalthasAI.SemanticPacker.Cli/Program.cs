using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SemanticPacker.Core.Contracts;
using SemanticPacker.Core.Services;
using SemanticPacker.Core.Models;
using SemanticPacker.Extractors;
using System.Diagnostics.CodeAnalysis;

// Parse CLI arguments
if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0].ToLowerInvariant();

// Configure services
var services = new ServiceCollection();
var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables("SEMANTICPACKER_")
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        { "SemanticPacker:BgeM3ModelPath", Environment.GetEnvironmentVariable("BGE_M3_MODEL_PATH") 
            ?? Path.Combine("E:", "bge-m3") },
    })
    .Build();

services.AddSingleton<IConfiguration>(configuration);
services.AddLogging(builder => builder
    .AddConsole()
    .SetMinimumLevel(args.Contains("-v") || args.Contains("--verbose") ? LogLevel.Debug : LogLevel.Information));

services.AddSemanticPacker<BgeM3EmbeddingService, SemanticChunker, ParquetChunkStorage>();

using var serviceProvider = services.BuildServiceProvider();
var processor = serviceProvider.GetRequiredService<IDocumentProcessor>();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

// Parse options
var options = new DocumentProcessingOptions
{
    OverwriteExisting = args.Contains("-f") || args.Contains("--force"),
    OutputDirectory = GetArgValue(args, "-o", "--output"),
    Version = GetArgValue(args, "--version") ?? "1.0.0",
    ChunkingOptions = new SemanticChunkingOptions
    {
        SimilarityThreshold = float.TryParse(GetArgValue(args, "-t", "--threshold"), out var t) ? t : 0.5f,
        MinChunkSize = int.TryParse(GetArgValue(args, "--min-chunk"), out var min) ? min : 50,
        MaxChunkSize = int.TryParse(GetArgValue(args, "--max-chunk"), out var max) ? max : 500
    }
};

try
{
    return command switch
    {
        "file" => await ProcessFileCommand(args.Skip(1).ToArray(), processor, options, logger),
        "dir" or "directory" => await ProcessDirectoryCommand(args.Skip(1).ToArray(), processor, options, logger),
        "url" => await ProcessUrlCommand(args.Skip(1).ToArray(), processor, options, logger, serviceProvider),
        "help" or "-h" or "--help" => PrintUsage(),
        _ => PrintUnknownCommand(command)
    };
}
catch (Exception ex)
{
    logger.LogError(ex, "Error occurred during processing");
    return 1;
}

// === Command handlers ===

static async Task<int> ProcessFileCommand(string[] args, IDocumentProcessor processor, DocumentProcessingOptions options, ILogger logger)
{
    var files = args.Where(a => !a.StartsWith('-')).ToList();
    if (files.Count == 0)
    {
        Console.WriteLine("Error: Please specify file path(s).");
        return 1;
    }

    var success = true;
    foreach (var file in files)
    {
        Console.WriteLine($"Processing: {file}");
        var result = await processor.ProcessFileAsync(file, options);
        
        if (result.Success)
        {
            Console.WriteLine($"  ✓ Done: {result.ChunkCount} chunks → {result.OutputPath}");
            Console.WriteLine($"    Duration: {result.Duration.TotalMilliseconds:N0}ms");
        }
        else
        {
            Console.WriteLine($"  ✗ Failed: {result.ErrorMessage}");
            success = false;
        }
    }

    return success ? 0 : 1;
}

static async Task<int> ProcessDirectoryCommand(string[] args, IDocumentProcessor processor, DocumentProcessingOptions options, ILogger logger)
{
    var dirs = args.Where(a => !a.StartsWith('-')).ToList();
    if (dirs.Count == 0)
    {
        Console.WriteLine("Error: Please specify directory path(s).");
        return 1;
    }

    var recursive = args.Contains("-r") || args.Contains("--recursive");
    var pattern = GetArgValue([.. args], "-p", "--pattern") ?? "*.*";

    var totalFiles = 0;
    var successCount = 0;

    foreach (var dir in dirs)
    {
        Console.WriteLine($"Processing directory: {dir}");
        
        await foreach (var result in processor.ProcessDirectoryAsync(dir, pattern, recursive, options))
        {
            totalFiles++;
            if (result.Success)
            {
                successCount++;
                Console.WriteLine($"  ✓ {result.Metadata?.SourceName}: {result.ChunkCount} chunks");
            }
            else
            {
                Console.WriteLine($"  ✗ Failed: {result.ErrorMessage}");
            }
        }
    }

    Console.WriteLine($"\nCompleted: {successCount}/{totalFiles} files processed");
    return successCount == totalFiles ? 0 : 1;
}

static async Task<int> ProcessUrlCommand(string[] args, IDocumentProcessor processor, DocumentProcessingOptions options, ILogger logger, IServiceProvider sp)
{
    var urls = args.Where(a => !a.StartsWith('-') && Uri.TryCreate(a, UriKind.Absolute, out _)).ToList();
    if (urls.Count == 0)
    {
        Console.WriteLine("Error: Please specify URL(s).");
        return 1;
    }

    var httpClientFactory = sp.GetService<IHttpClientFactory>();
    if (httpClientFactory == null)
    {
        Console.WriteLine("Error: Failed to initialize HTTP client.");
        return 1;
    }

    var client = httpClientFactory.CreateClient("semantic-packer-client");
    var storage = sp.GetRequiredService<IChunkStorage>();
    var chunker = sp.GetRequiredService<ISemanticChunker>();

    var success = true;
    foreach (var url in urls)
    {
        Console.WriteLine($"Downloading: {url}");
        
        try
        {
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "text/html";
            
            var uri = new Uri(url);
            var fileName = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrEmpty(fileName) || !fileName.Contains('.'))
                fileName = uri.Host.Replace(".", "_") + ".html";

            var outputDir = options.OutputDirectory ?? Environment.CurrentDirectory;
            var outputPath = Path.Combine(outputDir, Path.ChangeExtension(fileName, ".chunks.parquet"));

            var chunks = await chunker.ChunkAsync(content, options.ChunkingOptions);
            
            var metadata = new ChunkMetadata
            {
                SourceId = url,
                SourceName = fileName,
                SourceContentType = contentType,
                Version = options.Version,
                CreatedAt = DateTime.UtcNow
            };

            await storage.SaveAsync(chunks, metadata, outputPath);
            
            Console.WriteLine($"  ✓ Done: {chunks.Count} chunks → {outputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Failed: {ex.Message}");
            success = false;
        }
    }

    return success ? 0 : 1;
}

static int PrintUsage()
{
    Console.WriteLine("""
        SemanticPacker - Semantic Chunking Tool

        Usage:
          semanticpacker <command> [options] <inputs...>

        Commands:
          file <files...>       Chunk files and save as Parquet
          dir <dirs...>         Batch process files in directory
          url <urls...>         Download and chunk web pages

        Common Options:
          -o, --output <dir>    Output directory
          -f, --force           Overwrite existing files
          -v, --verbose         Verbose logging
          -t, --threshold <n>   Similarity threshold (default: 0.5)
          --min-chunk <n>       Minimum chunk size (default: 50)
          --max-chunk <n>       Maximum chunk size (default: 500)
          --version <ver>       Dataset version (default: 1.0.0)

        Directory Options:
          -r, --recursive       Include subdirectories
          -p, --pattern <pat>   File pattern (default: *.*)

        Environment Variables:
          BGE_M3_MODEL_PATH     Path to BGE-M3 model

        Examples:
          semanticpacker file document.txt
          semanticpacker file *.md -o ./output -f
          semanticpacker dir ./docs -r -p "*.md"
          semanticpacker url https://example.com/page.html
        """);
    return 0;
}

static int PrintUnknownCommand(string command)
{
    Console.WriteLine($"Unknown command: {command}");
    Console.WriteLine("Run 'semanticpacker help' for usage information.");
    return 1;
}

static string? GetArgValue(string[] args, params string[] names)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (names.Contains(args[i]))
            return args[i + 1];
    }
    return null;
}

public partial class Program { }

public static class SemanticPackerExtensions
{
    /// <summary>
    /// Registers SemanticPacker services.
    /// </summary>
    public static IServiceCollection AddSemanticPacker<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TEmbeddingService,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSemanticChunker,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TChunkStore>(
        this IServiceCollection services)
        where TEmbeddingService : class, IEmbeddingService
        where TSemanticChunker : class, ISemanticChunker
        where TChunkStore : class, IChunkStorage
    {
        // Register core services
        services.AddSingleton<IEmbeddingService, TEmbeddingService>();
        services.AddSingleton<ISemanticChunker, TSemanticChunker>();
        services.AddSingleton<IChunkStorage, TChunkStore>();

        // Register text extractors
        services.AddSingleton<ITextExtractor, PlainTextExtractor>();

        // Register document processor
        services.AddSingleton<IDocumentProcessor, DocumentProcessor>();

        // Register HTTP client
        services.AddHttpClient("semantic-packer-client",
            c => c.DefaultRequestHeaders.UserAgent.ParseAdd("SemanticPacker/1.0"));

        return services;
    }
    
    /// <summary>
    /// Registers an additional text extractor.
    /// </summary>
    public static IServiceCollection AddTextExtractor<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TExtractor>(
        this IServiceCollection services)
        where TExtractor : class, ITextExtractor
    {
        services.AddSingleton<ITextExtractor, TExtractor>();
        return services;
    }
}

