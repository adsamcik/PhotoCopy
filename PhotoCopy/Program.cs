using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy.Abstractions;
using PhotoCopy.Commands;
using PhotoCopy.Configuration;
using PhotoCopy.Directories;
using PhotoCopy.Duplicates;
using PhotoCopy.Files;
using PhotoCopy.Files.Metadata;
using PhotoCopy.Progress;
using PhotoCopy.Rollback;
using PhotoCopy.Validators;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoCopy;

public class Program
{
    static async Task<int> Main(string[] args)
    {
        // Parse with verb-based options
        var parseResult = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        return await parseResult.MapResult<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions, Task<int>>(
            async copy => await RunCopyCommand(copy),
            async scan => await RunScanCommand(scan),
            async validate => await RunValidateCommand(validate),
            async config => await RunConfigCommand(config),
            async rollback => await RunRollbackCommand(rollback),
            async errors => await Task.FromResult(1));
    }

    private static async Task<int> RunCopyCommand(CopyOptions options)
    {
        var config = LoadConfiguration(options);
        ApplyCopyOverrides(config, options);

        var serviceProvider = BuildServiceProvider(config);
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        // Initialize geocoding service
        var geocodingService = serviceProvider.GetRequiredService<IReverseGeocodingService>();
        await geocodingService.InitializeAsync();

        if (!ValidateInput(config, logger)) return 1;
        PrintConfiguration(config, logger);

        var command = serviceProvider.GetRequiredService<CopyCommand>();
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            logger.LogWarning("Cancellation requested...");
        };

        return await command.ExecuteAsync(cts.Token);
    }

    private static async Task<int> RunScanCommand(ScanOptions options)
    {
        var config = LoadConfiguration(options);
        ApplyScanOverrides(config, options);

        var serviceProvider = BuildServiceProvider(config);
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        // Initialize geocoding service
        var geocodingService = serviceProvider.GetRequiredService<IReverseGeocodingService>();
        await geocodingService.InitializeAsync();

        if (string.IsNullOrEmpty(config.Source))
        {
            logger.LogError("Source path is required");
            return 1;
        }

        var command = ActivatorUtilities.CreateInstance<ScanCommand>(serviceProvider, options.OutputJson);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        return await command.ExecuteAsync(cts.Token);
    }

    private static async Task<int> RunValidateCommand(ValidateOptions options)
    {
        var config = LoadConfiguration(options);
        ApplyValidateOverrides(config, options);

        var serviceProvider = BuildServiceProvider(config);
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        // Initialize geocoding service
        var geocodingService = serviceProvider.GetRequiredService<IReverseGeocodingService>();
        await geocodingService.InitializeAsync();

        if (string.IsNullOrEmpty(config.Source))
        {
            logger.LogError("Source path is required");
            return 1;
        }

        var command = serviceProvider.GetRequiredService<ValidateCommand>();
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        return await command.ExecuteAsync(cts.Token);
    }

    private static async Task<int> RunConfigCommand(ConfigOptions options)
    {
        var (config, diagnostics) = LoadConfigurationWithDiagnostics(options);
        ApplyConfigOverrides(config, options, diagnostics);

        var serviceProvider = BuildServiceProvider(config, diagnostics);
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        var command = ActivatorUtilities.CreateInstance<ConfigCommand>(serviceProvider, options.OutputJson);

        return await command.ExecuteAsync();
    }

    private static async Task<int> RunRollbackCommand(RollbackOptions options)
    {
        var config = LoadConfiguration(options);
        var serviceProvider = BuildServiceProvider(config);
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        var rollbackService = serviceProvider.GetRequiredService<IRollbackService>();
        var command = new RollbackCommand(
            serviceProvider.GetRequiredService<ILogger<RollbackCommand>>(),
            rollbackService,
            options.TransactionLogPath,
            options.LogDirectory,
            options.ListLogs);

        return await command.ExecuteAsync();
    }

    private static PhotoCopyConfig LoadConfiguration<TOptions>(TOptions options) where TOptions : CommonOptions
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        if (!string.IsNullOrEmpty(options.ConfigPath))
        {
            var ext = Path.GetExtension(options.ConfigPath).ToLower();
            if (ext == ".yaml" || ext == ".yml")
            {
                builder.AddYamlFile(options.ConfigPath, optional: false);
            }
            else
            {
                builder.AddJsonFile(options.ConfigPath, optional: false);
            }
        }

        var configuration = builder.Build();
        var config = new PhotoCopyConfig();
        configuration.Bind(config);

        // Apply common overrides
        if (options.LogLevel.HasValue) config.LogLevel = options.LogLevel.Value;

        return config;
    }

    private static (PhotoCopyConfig Config, ConfigurationDiagnostics Diagnostics) LoadConfigurationWithDiagnostics<TOptions>(TOptions options) 
        where TOptions : CommonOptions
    {
        var diagnostics = new ConfigurationDiagnostics();
        var config = new PhotoCopyConfig();

        // Record defaults first
        foreach (var prop in typeof(PhotoCopyConfig).GetProperties())
        {
            var defaultValue = prop.GetValue(config)?.ToString();
            diagnostics.RecordSource(prop.Name, defaultValue, ConfigSourceType.Default);
        }

        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        string? configFilePath = null;
        if (!string.IsNullOrEmpty(options.ConfigPath))
        {
            configFilePath = options.ConfigPath;
            var ext = Path.GetExtension(options.ConfigPath).ToLower();
            if (ext == ".yaml" || ext == ".yml")
            {
                builder.AddYamlFile(options.ConfigPath, optional: false);
            }
            else
            {
                builder.AddJsonFile(options.ConfigPath, optional: false);
            }
        }

        var configuration = builder.Build();
        
        // Track which values came from config file
        foreach (var section in configuration.GetChildren())
        {
            if (section.Value is not null)
            {
                diagnostics.RecordSource(section.Key, section.Value, ConfigSourceType.ConfigFile, configFilePath);
            }
        }

        configuration.Bind(config);

        // Apply common overrides
        if (options.LogLevel.HasValue)
        {
            config.LogLevel = options.LogLevel.Value;
            diagnostics.RecordSource("LogLevel", options.LogLevel.Value.ToString(), ConfigSourceType.CommandLine, "--log-level");
        }

        return (config, diagnostics);
    }

    private static void ApplyConfigOverrides(PhotoCopyConfig config, ConfigOptions options, ConfigurationDiagnostics diagnostics)
    {
        if (!string.IsNullOrEmpty(options.Source))
        {
            config.Source = options.Source;
            diagnostics.RecordSource("Source", options.Source, ConfigSourceType.CommandLine, "--source");
        }
        if (!string.IsNullOrEmpty(options.Destination))
        {
            config.Destination = options.Destination;
            diagnostics.RecordSource("Destination", options.Destination, ConfigSourceType.CommandLine, "--destination");
        }
    }

    private static void ApplyCopyOverrides(PhotoCopyConfig config, CopyOptions options)
    {
        if (!string.IsNullOrEmpty(options.Source)) config.Source = options.Source;
        if (!string.IsNullOrEmpty(options.Destination)) config.Destination = options.Destination;
        if (options.DryRun.HasValue) config.DryRun = options.DryRun.Value;
        if (options.SkipExisting.HasValue) config.SkipExisting = options.SkipExisting.Value;
        if (options.Overwrite.HasValue) config.Overwrite = options.Overwrite.Value;
        if (options.NoDuplicateSkip.HasValue) config.NoDuplicateSkip = options.NoDuplicateSkip.Value;
        if (options.Mode.HasValue) config.Mode = options.Mode.Value;
        if (options.RelatedFileMode.HasValue) config.RelatedFileMode = options.RelatedFileMode.Value;
        if (!string.IsNullOrEmpty(options.DuplicatesFormat)) config.DuplicatesFormat = options.DuplicatesFormat;
        if (options.MinDate.HasValue) config.MinDate = options.MinDate.Value;
        if (options.MaxDate.HasValue) config.MaxDate = options.MaxDate.Value;
        if (!string.IsNullOrEmpty(options.GeonamesPath)) config.GeonamesPath = options.GeonamesPath;
        if (options.CalculateChecksums.HasValue) config.CalculateChecksums = options.CalculateChecksums.Value;
        if (options.Parallelism.HasValue) config.Parallelism = options.Parallelism.Value;
        if (options.DuplicateHandling.HasValue)
        {
            config.DuplicateHandling = options.DuplicateHandling.Value switch
            {
                DuplicateHandlingOption.Skip => DuplicateHandling.SkipDuplicates,
                DuplicateHandlingOption.Prompt => DuplicateHandling.Prompt,
                DuplicateHandlingOption.Report => DuplicateHandling.ReportOnly,
                _ => DuplicateHandling.None
            };
        }
        if (options.EnableRollback.HasValue) config.EnableRollback = options.EnableRollback.Value;
    }

    private static void ApplyScanOverrides(PhotoCopyConfig config, ScanOptions options)
    {
        if (!string.IsNullOrEmpty(options.Source)) config.Source = options.Source;
        if (options.MinDate.HasValue) config.MinDate = options.MinDate.Value;
        if (options.MaxDate.HasValue) config.MaxDate = options.MaxDate.Value;
        if (!string.IsNullOrEmpty(options.GeonamesPath)) config.GeonamesPath = options.GeonamesPath;
        if (options.CalculateChecksums.HasValue) config.CalculateChecksums = options.CalculateChecksums.Value;
    }

    private static void ApplyValidateOverrides(PhotoCopyConfig config, ValidateOptions options)
    {
        if (!string.IsNullOrEmpty(options.Source)) config.Source = options.Source;
        if (options.MinDate.HasValue) config.MinDate = options.MinDate.Value;
        if (options.MaxDate.HasValue) config.MaxDate = options.MaxDate.Value;
        if (!string.IsNullOrEmpty(options.GeonamesPath)) config.GeonamesPath = options.GeonamesPath;
    }

    private static IServiceProvider BuildServiceProvider(PhotoCopyConfig config, ConfigurationDiagnostics? diagnostics = null)
    {
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            var minLevel = config.LogLevel switch
            {
                OutputLevel.Verbose => LogLevel.Trace,
                OutputLevel.Important => LogLevel.Information,
                OutputLevel.ErrorsOnly => LogLevel.Error,
                _ => LogLevel.Information
            };
            builder.SetMinimumLevel(minLevel);
        });

        // Register PhotoCopyConfig
        services.AddSingleton<IOptions<PhotoCopyConfig>>(Microsoft.Extensions.Options.Options.Create(config));

        // Register configuration diagnostics if provided
        if (diagnostics is not null)
        {
            services.AddSingleton<ConfigurationDiagnostics>(diagnostics);
        }
        else
        {
            services.AddSingleton<ConfigurationDiagnostics>();
        }

        // Register core services
        services.AddSingleton<IReverseGeocodingService, ReverseGeocodingService>();
        services.AddSingleton<IChecksumCalculator, Sha256ChecksumCalculator>();
        services.AddTransient<IFileMetadataExtractor, FileMetadataExtractor>();
        services.AddTransient<IMetadataEnricher, MetadataEnricher>();
        services.AddTransient<IMetadataEnrichmentStep, DateTimeMetadataEnrichmentStep>();
        services.AddTransient<IMetadataEnrichmentStep, LocationMetadataEnrichmentStep>();
        services.AddTransient<IMetadataEnrichmentStep, ChecksumMetadataEnrichmentStep>();
        services.AddTransient<IFileFactory, FileFactory>();
        services.AddTransient<IFileSystem, FileSystem>();
        services.AddTransient<IDirectoryScanner, DirectoryScanner>();
        services.AddTransient<IValidatorFactory, ValidatorFactory>();

        // Register copiers
        services.AddTransient<IDirectoryCopier, DirectoryCopier>();
        services.AddTransient<IDirectoryCopierAsync, DirectoryCopierAsync>();

        // Register duplicate detection
        services.AddSingleton<IDuplicateDetector, DuplicateDetector>();

        // Register rollback services
        services.AddSingleton<ITransactionLogger, TransactionLogger>();
        services.AddSingleton<IRollbackService, RollbackService>();

        // Register progress reporter
        services.AddSingleton<IProgressReporter>(sp =>
        {
            if (config.ShowProgress)
            {
                var logger = sp.GetRequiredService<ILogger<ConsoleProgressReporter>>();
                return new ConsoleProgressReporter(logger, config.LogLevel == OutputLevel.Verbose);
            }
            return NullProgressReporter.Instance;
        });

        // Register commands
        services.AddTransient<CopyCommand>();
        services.AddTransient<ScanCommand>();
        services.AddTransient<ValidateCommand>();

        return services.BuildServiceProvider();
    }

    static bool ValidateInput(PhotoCopyConfig config, ILogger logger)
    {
        if (string.IsNullOrEmpty(config.Source))
        {
            logger.LogError("Source path is required");
            return false;
        }
        if (string.IsNullOrEmpty(config.Destination))
        {
            logger.LogError("Destination path is required");
            return false;
        }

        var sourceFile = new FileInfo(config.Source);
        var isValid = true;
        if (!sourceFile.Exists)
        {
            logger.LogError("Source {SourcePath} does not exist", sourceFile.FullName);
            isValid = false;
        }
        if (!sourceFile.Attributes.HasFlag(FileAttributes.Directory))
        {
            logger.LogError("Source is not a directory");
            isValid = false;
        }
        if (!config.DuplicatesFormat.Contains(DestinationVariables.Number))
        {
            logger.LogError("Duplicates format does not contain {{number}}");
            isValid = false;
        }
        if (!config.Destination.Contains(DestinationVariables.Name) && !config.Destination.Contains(DestinationVariables.NameNoExtension))
        {
            Console.WriteLine("Your destination path does not contain name or name without extension. This will result in files losing their original name and is generally undesirable. Are you absolutely sure about this? Write yes to confirm, anything else to abort.");
            var response = Console.ReadLine();
            if (response != "yes") isValid = false;
        }
        return isValid;
    }

    static void PrintConfiguration(PhotoCopyConfig config, ILogger logger)
    {
        logger.LogInformation("Source: {Source}", config.Source);
        logger.LogInformation("Destination: {Destination}", config.Destination);
        logger.LogInformation("Dry Run: {DryRun}", config.DryRun);
        logger.LogInformation("Mode: {Mode}", config.Mode);
        logger.LogInformation("Log Level: {LogLevel}", config.LogLevel);
        if (config.UseAsync)
        {
            logger.LogInformation("Async Mode: Enabled (Parallelism: {Parallelism})", config.Parallelism);
        }
    }
}