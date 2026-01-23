using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using PhotoCopy.Abstractions;
using PhotoCopy.Checkpoint;
using PhotoCopy.Commands;
using PhotoCopy.Configuration;
using PhotoCopy.Directories;
using PhotoCopy.Duplicates;
using PhotoCopy.Files;
using PhotoCopy.Files.Geo;
using PhotoCopy.Files.Geo.Boundaries;
using PhotoCopy.Files.Metadata;
using PhotoCopy.Files.Sidecar;
using PhotoCopy.Logging;
using PhotoCopy.Progress;
using PhotoCopy.Rollback;
using PhotoCopy.Validators;

namespace PhotoCopy.Extensions;

/// <summary>
/// Extension methods for configuring PhotoCopy services in a dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all PhotoCopy services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="config">The PhotoCopy configuration.</param>
    /// <param name="diagnostics">Optional configuration diagnostics for tracking config sources.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPhotoCopyServices(
        this IServiceCollection services,
        PhotoCopyConfig config,
        ConfigurationDiagnostics? diagnostics = null)
    {
        // Add logging
        services.AddPhotoCopyLogging(config);

        // Register configuration
        services.AddPhotoCopyConfiguration(config, diagnostics);

        // Register core services
        services.AddPhotoCopyCoreServices();

        // Register copiers
        services.AddPhotoCopyCopiers();

        // Register duplicate detection (transient - has mutable state)
        services.AddTransient<IDuplicateDetector, DuplicateDetector>();

        // Register rollback services (transient - has mutable transaction state)
        services.AddTransient<ITransactionLogger, TransactionLogger>();
        services.AddSingleton<IRollbackService, RollbackService>();

        // Register progress reporter
        services.AddPhotoCopyProgressReporter(config);

        // Register commands that use pure DI (no runtime parameters)
        services.AddTransient<CopyCommand>();
        services.AddTransient<ValidateCommand>();
        services.AddTransient<ValidateConfigCommand>();

        // Register checkpoint services for resume support
        services.AddPhotoCopyCheckpointServices(config);

        return services;
    }

    /// <summary>
    /// Adds PhotoCopy logging configuration.
    /// </summary>
    public static IServiceCollection AddPhotoCopyLogging(this IServiceCollection services, PhotoCopyConfig config)
    {
        services.AddLogging(builder =>
        {
            // Configure console logging based on format
            if (config.LogFormat == LogFormat.Json)
            {
                builder.AddConsoleFormatter<JsonConsoleFormatter, PhotoCopyJsonFormatterOptions>();
                builder.AddConsole(options =>
                {
                    options.FormatterName = JsonConsoleFormatter.FormatterName;
                });
            }
            else
            {
                builder.AddSimpleConsole(options =>
                {
                    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss.fff] ";
                    options.SingleLine = true;
                });
            }

            // Add file logging if configured
            if (!string.IsNullOrEmpty(config.LogFilePath))
            {
                builder.AddProvider(new FileLoggerProvider(config.LogFilePath, config.LogFormat));
            }

            var minLevel = config.LogLevel switch
            {
                OutputLevel.Verbose => LogLevel.Trace,
                OutputLevel.Important => LogLevel.Information,
                OutputLevel.ErrorsOnly => LogLevel.Error,
                _ => LogLevel.Information
            };
            builder.SetMinimumLevel(minLevel);
        });

        return services;
    }

    /// <summary>
    /// Adds PhotoCopy configuration services.
    /// </summary>
    public static IServiceCollection AddPhotoCopyConfiguration(
        this IServiceCollection services,
        PhotoCopyConfig config,
        ConfigurationDiagnostics? diagnostics = null)
    {
        services.AddSingleton<IOptions<PhotoCopyConfig>>(Options.Create(config));
        services.AddSingleton(config); // Also register config directly for services that need it
        services.AddSingleton(diagnostics ?? new ConfigurationDiagnostics());

        return services;
    }

    /// <summary>
    /// Adds core PhotoCopy services (file system, metadata, validation).
    /// </summary>
    public static IServiceCollection AddPhotoCopyCoreServices(this IServiceCollection services)
    {
        // Boundary service - singleton for country boundary detection
        services.AddSingleton<IBoundaryService, BoundaryIndex>();
        
        // Geocoding service - singleton using tiered chunked loading (only loads cells on-demand)
        // BoundaryAwareGeocodingService wraps TieredGeocodingService with country filtering
        services.AddSingleton<TieredGeocodingService>();
        services.AddSingleton<IReverseGeocodingService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<BoundaryAwareGeocodingService>>();
            var geocodingLogger = sp.GetRequiredService<ILogger<TieredGeocodingService>>();
            var boundaryLogger = sp.GetRequiredService<ILogger<BoundaryIndex>>();
            var config = sp.GetRequiredService<PhotoCopyConfig>();
            return new BoundaryAwareGeocodingService(logger, geocodingLogger, boundaryLogger, config);
        });
        
        // Checksum calculator - singleton (stateless)
        services.AddSingleton<IChecksumCalculator, Sha256ChecksumCalculator>();
        
        // GPS location index - singleton for companion GPS fallback (shared across all file processing)
        services.AddSingleton<IGpsLocationIndex, GpsLocationIndex>();
        
        // Live Photo enricher - singleton for pairing .heic photos with companion .mov videos
        services.AddSingleton<ILivePhotoEnricher, LivePhotoEnricher>();
        
        // Companion GPS enricher - singleton for second-pass GPS enrichment
        services.AddSingleton<ICompanionGpsEnricher, CompanionGpsEnricher>();
        
        // Sidecar parsing services
        services.AddSingleton<ISidecarParser, GoogleTakeoutJsonParser>();
        services.AddSingleton<ISidecarParser, XmpSidecarParser>();
        services.AddSingleton<ISidecarMetadataService, SidecarMetadataService>();
        
        // Metadata extraction pipeline
        // Use CachedFileMetadataExtractor to avoid re-reading files multiple times
        // during enrichment (DateTime, Coordinates, Camera, Album extraction).
        // Singleton with internal LRU cache (size 8) - safe for console app lifetime
        // as cache size is bounded and old entries are evicted.
        services.AddSingleton<IFileMetadataExtractor, CachedFileMetadataExtractor>();
        services.AddTransient<IMetadataEnricher, MetadataEnricher>();
        services.AddTransient<IMetadataEnrichmentStep, DateTimeMetadataEnrichmentStep>();
        services.AddTransient<IMetadataEnrichmentStep, TimeOffsetEnrichmentStep>();
        services.AddTransient<IMetadataEnrichmentStep, TimezoneEnrichmentStep>();
        services.AddTransient<IMetadataEnrichmentStep, SidecarMetadataEnrichmentStep>();
        services.AddTransient<IMetadataEnrichmentStep, LocationMetadataEnrichmentStep>();
        services.AddTransient<IMetadataEnrichmentStep, ChecksumMetadataEnrichmentStep>();
        services.AddTransient<IMetadataEnrichmentStep, CameraMetadataEnrichmentStep>();
        services.AddTransient<IMetadataEnrichmentStep, AlbumMetadataEnrichmentStep>();
        
        // File system services
        services.AddTransient<IFileFactory, FileFactory>();
        services.AddTransient<IFileSystem, FileSystem>();
        services.AddTransient<IDirectoryScanner, DirectoryScanner>();
        
        // Validation services
        services.AddTransient<IValidatorFactory, ValidatorFactory>();
        services.AddTransient<IFileValidationService, FileValidationService>();
        services.AddSingleton<IConfigurationValidator, ConfigurationValidator>();

        // Input validation (user interaction for copy command)
        services.AddSingleton<IConsoleInteraction, ConsoleInteraction>();
        services.AddTransient<IInputValidator, InputValidator>();

        return services;
    }

    /// <summary>
    /// Adds directory copier services.
    /// </summary>
    public static IServiceCollection AddPhotoCopyCopiers(this IServiceCollection services)
    {
        services.AddTransient<IDirectoryCopier, DirectoryCopier>();
        services.AddTransient<IDirectoryCopierAsync, DirectoryCopierAsync>();

        return services;
    }

    /// <summary>
    /// Adds the progress reporter based on configuration.
    /// </summary>
    public static IServiceCollection AddPhotoCopyProgressReporter(this IServiceCollection services, PhotoCopyConfig config)
    {
        services.AddSingleton<IProgressReporter>(sp =>
        {
            if (config.ShowProgress)
            {
                var logger = sp.GetRequiredService<ILogger<ConsoleProgressReporter>>();
                return new ConsoleProgressReporter(logger, config.LogLevel == OutputLevel.Verbose);
            }
            return NullProgressReporter.Instance;
        });

        return services;
    }

    /// <summary>
    /// Adds checkpoint and resume support services.
    /// </summary>
    public static IServiceCollection AddPhotoCopyCheckpointServices(this IServiceCollection services, PhotoCopyConfig config)
    {
        // System clock - singleton for testability
        services.AddSingleton<ISystemClock>(SystemClock.Instance);

        // Checkpoint store - handles file persistence
        services.AddSingleton<ICheckpointStore>(sp =>
        {
            var clock = sp.GetRequiredService<ISystemClock>();
            return new BinaryCheckpointStore(clock, config.CheckpointDirectory);
        });

        // Checkpoint validator - validates checkpoints for resume
        services.AddSingleton<ICheckpointValidator>(sp =>
        {
            var clock = sp.GetRequiredService<ISystemClock>();
            return new CheckpointValidator(clock);
        });

        // Resume orchestrator - coordinates the resume flow
        services.AddTransient<ResumeOrchestrator>();

        return services;
    }
}
