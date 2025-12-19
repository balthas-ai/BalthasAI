using BalthasAI.SmartVault.Processing;
using BalthasAI.SmartVault.VectorStore;
using BalthasAI.SmartVault.WebDav;
using SemanticPacker.Core.Contracts;
using SemanticPacker.Core.Services;

namespace BalthasAI.SmartVault;

/// <summary>
/// SmartVault service registration extension methods
/// </summary>
public static class SmartVaultServiceExtensions
{
    /// <summary>
    /// Registers SmartVault services (call before Build).
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configure">Global options configuration</param>
    public static IServiceCollection AddSmartVault(
        this IServiceCollection services,
        Action<SmartVaultOptions>? configure = null)
    {
        var options = new SmartVaultOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<SmartVaultEndpointRegistry>();

        // In-process queue manager
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<SmartVaultOptions>();
            return new InProcessQueueManager(new FileProcessingOptions
            {
                DataPath = opts.DataPath
            });
        });

        // SQLite vector store
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<SmartVaultOptions>();
            var logger = sp.GetRequiredService<ILogger<SqliteVectorStore>>();
            var dbPath = Path.Combine(opts.DataPath, "vectors.db");
            return new SqliteVectorStore(dbPath, opts.EmbeddingDimension, logger);
        });

        // SemanticPacker services
        services.AddSingleton<IChunkStorage, ParquetChunkStorage>();
        services.AddSingleton<ISemanticChunker, SemanticChunker>();

        // Default file processor (can be replaced with SemanticChunkingProcessor)
        services.AddSingleton<IFileProcessor, DefaultFileProcessor>();

        // Background worker
        services.AddHostedService<FileProcessingWorker>();

        return services;
    }

    /// <summary>
    /// Uses SemanticPacker-based chunking processor.
    /// </summary>
    public static IServiceCollection UseSemanticChunking(
        this IServiceCollection services,
        Action<SemanticChunkingOptions>? configure = null)
    {
        var chunkingOptions = new SemanticChunkingOptions();
        configure?.Invoke(chunkingOptions);
        services.AddSingleton(chunkingOptions);

        // Register IDocumentProcessor
        services.AddSingleton<IDocumentProcessor, DocumentProcessor>();

        // Replace DefaultFileProcessor with SemanticChunkingProcessor
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IFileProcessor));
        if (descriptor is not null)
        {
            services.Remove(descriptor);
        }

        services.AddSingleton<IFileProcessor>(sp =>
        {
            var docProcessor = sp.GetRequiredService<IDocumentProcessor>();
            var vectorStore = sp.GetRequiredService<SqliteVectorStore>();
            var logger = sp.GetRequiredService<ILogger<SemanticChunkingProcessor>>();
            var opts = sp.GetRequiredService<SmartVaultOptions>();
            var parquetPath = Path.Combine(opts.DataPath, "parquet");

            return new SemanticChunkingProcessor(docProcessor, vectorStore, logger, parquetPath);
        });

        return services;
    }

    /// <summary>
    /// Registers embedding service and sync worker.
    /// </summary>
    public static IServiceCollection UseEmbedding<TEmbeddingService>(
        this IServiceCollection services,
        TimeSpan? syncInterval = null)
        where TEmbeddingService : class, IEmbeddingService
    {
        services.AddSingleton<IEmbeddingService, TEmbeddingService>();

        services.AddHostedService(sp =>
        {
            var vectorStore = sp.GetRequiredService<SqliteVectorStore>();
            var embeddingService = sp.GetRequiredService<IEmbeddingService>();
            var logger = sp.GetRequiredService<ILogger<EmbeddingSyncWorker>>();

            return new EmbeddingSyncWorker(vectorStore, embeddingService, logger, syncInterval);
        });

        return services;
    }

    /// <summary>
    /// Registers a custom file processor.
    /// </summary>
    public static IServiceCollection AddSmartVaultProcessor<TProcessor>(this IServiceCollection services)
        where TProcessor : class, IFileProcessor
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IFileProcessor));
        if (descriptor is not null)
        {
            services.Remove(descriptor);
        }

        services.AddSingleton<IFileProcessor, TProcessor>();
        return services;
    }
}

/// <summary>
/// Semantic chunking options
/// </summary>
public class SemanticChunkingOptions
{
    /// <summary>
    /// Maximum chunk size (tokens)
    /// </summary>
    public int MaxChunkSize { get; set; } = 512;

    /// <summary>
    /// Overlap between chunks (tokens)
    /// </summary>
    public int ChunkOverlap { get; set; } = 50;
}

/// <summary>
/// SmartVault endpoint mapping extension methods
/// </summary>
public static class SmartVaultApplicationExtensions
{
    private static bool _middlewareRegistered;
    private static bool _vectorStoreInitialized;

    /// <summary>
    /// Activates SmartVault middleware (only needs to be called once).
    /// </summary>
    public static async Task<IApplicationBuilder> UseSmartVaultAsync(this IApplicationBuilder app)
    {
        if (!_middlewareRegistered)
        {
            app.UseMiddleware<SmartVaultMiddleware>();
            _middlewareRegistered = true;
        }

        // Initialize vector store
        if (!_vectorStoreInitialized)
        {
            var vectorStore = app.ApplicationServices.GetRequiredService<SqliteVectorStore>();
            await vectorStore.InitializeAsync();
            _vectorStoreInitialized = true;
        }

        return app;
    }

    /// <summary>
    /// Activates SmartVault middleware (synchronous version).
    /// </summary>
    public static IApplicationBuilder UseSmartVault(this IApplicationBuilder app)
    {
        if (!_middlewareRegistered)
        {
            app.UseMiddleware<SmartVaultMiddleware>();
            _middlewareRegistered = true;
        }

        // Initialize vector store (synchronous)
        if (!_vectorStoreInitialized)
        {
            var vectorStore = app.ApplicationServices.GetRequiredService<SqliteVectorStore>();
            vectorStore.InitializeAsync().GetAwaiter().GetResult();
            _vectorStoreInitialized = true;
        }

        return app;
    }

    /// <summary>
    /// Maps a WebDAV endpoint to the specified path.
    /// </summary>
    public static IApplicationBuilder MapSmartVault(
        this IApplicationBuilder app,
        string path,
        string rootDirectory,
        Action<SmartVaultEndpointOptions>? configure = null)
    {
        // Register middleware if not already done
        app.UseSmartVault();

        var registry = app.ApplicationServices.GetRequiredService<SmartVaultEndpointRegistry>();
        var queueManager = app.ApplicationServices.GetRequiredService<InProcessQueueManager>();
        var loggerFactory = app.ApplicationServices.GetRequiredService<ILoggerFactory>();

        var options = new SmartVaultEndpointOptions
        {
            RootDirectory = rootDirectory
        };
        configure?.Invoke(options);

        // Create directory
        if (!Directory.Exists(rootDirectory))
        {
            Directory.CreateDirectory(rootDirectory);
        }

        // Create WebDavOptions
        var webDavOptions = new WebDavOptions
        {
            BasePath = path,
            RootDirectory = rootDirectory,
            AllowAnonymous = true
        };

        // Create FileChangeNotificationService
        var notificationService = new FileChangeNotificationService(webDavOptions);

        // Create FileChangeQueueBridge (if processing is enabled)
        FileChangeQueueBridge? queueBridge = null;
        if (options.EnableFileProcessing)
        {
            var processingOptions = new FileProcessingOptions
            {
                DebounceDelayMs = options.DebounceDelayMs,
                LockTimeoutSeconds = options.LockTimeoutSeconds,
                MaxRetries = options.MaxRetries,
                AllowedExtensions = options.AllowedExtensions,
                ExcludePatterns = options.ExcludePatterns,
                KeyPrefix = $"smartvault:{path.Trim('/').Replace('/', ':')}:"
            };

            queueBridge = new FileChangeQueueBridge(
                notificationService,
                queueManager,
                processingOptions,
                webDavOptions,
                loggerFactory.CreateLogger<FileChangeQueueBridge>());
        }

        // Register endpoint
        var endpoint = new SmartVaultEndpoint
        {
            BasePath = path,
            RootDirectory = rootDirectory,
            Options = options,
            NotificationService = notificationService,
            QueueBridge = queueBridge
        };

        registry.Register(path, endpoint);

        var logger = loggerFactory.CreateLogger("SmartVault");
        logger.LogInformation(
            "Mapped SmartVault endpoint: {Path} -> {Directory} (Processing: {Processing})",
            path, rootDirectory, options.EnableFileProcessing);

        return app;
    }
}
