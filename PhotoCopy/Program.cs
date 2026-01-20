using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PhotoCopy.Abstractions;
using PhotoCopy.Commands;
using PhotoCopy.Configuration;
using PhotoCopy.Extensions;
using PhotoCopy.Rollback;
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
        var parseResult = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions, ValidateConfigOptions>(args);

        return await parseResult.MapResult<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions, ValidateConfigOptions, Task<int>>(
            async copy => await RunCopyCommand(copy),
            async scan => await RunScanCommand(scan),
            async validate => await RunValidateCommand(validate),
            async config => await RunConfigCommand(config),
            async rollback => await RunRollbackCommand(rollback),
            async validateConfig => await RunValidateConfigCommand(validateConfig),
            async errors => await Task.FromResult((int)ExitCode.Error));
    }

    private static async Task<int> RunCopyCommand(CopyOptions options)
    {
        var config = ConfigurationLoader.Load(options);

        await using var serviceProvider = BuildServiceProvider(config);
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        // Initialize geocoding service
        var geocodingService = serviceProvider.GetRequiredService<IReverseGeocodingService>();
        await geocodingService.InitializeAsync();

        if (!ValidateInput(config, logger)) return (int)ExitCode.Error;
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
        var config = ConfigurationLoader.Load(options);

        await using var serviceProvider = BuildServiceProvider(config);
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        // Initialize geocoding service
        var geocodingService = serviceProvider.GetRequiredService<IReverseGeocodingService>();
        await geocodingService.InitializeAsync();

        if (string.IsNullOrEmpty(config.Source))
        {
            logger.LogError("Source path is required");
            return (int)ExitCode.Error;
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
        var config = ConfigurationLoader.Load(options);

        await using var serviceProvider = BuildServiceProvider(config);
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        // Initialize geocoding service
        var geocodingService = serviceProvider.GetRequiredService<IReverseGeocodingService>();
        await geocodingService.InitializeAsync();

        if (string.IsNullOrEmpty(config.Source))
        {
            logger.LogError("Source path is required");
            return (int)ExitCode.Error;
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
        var (config, diagnostics) = ConfigurationLoader.LoadWithDiagnostics(options);
        ConfigurationLoader.ApplyOverrides(config, options, diagnostics);

        await using var serviceProvider = BuildServiceProvider(config, diagnostics);

        var command = ActivatorUtilities.CreateInstance<ConfigCommand>(serviceProvider, options.OutputJson);

        return await command.ExecuteAsync();
    }

    private static async Task<int> RunRollbackCommand(RollbackOptions options)
    {
        var config = ConfigurationLoader.Load(options);
        await using var serviceProvider = BuildServiceProvider(config);
        var logger = serviceProvider.GetRequiredService<ILogger<RollbackCommand>>();
        var rollbackService = serviceProvider.GetRequiredService<IRollbackService>();
        var fileSystem = serviceProvider.GetRequiredService<IFileSystem>();

        var command = new RollbackCommand(
            logger,
            rollbackService,
            fileSystem,
            options.TransactionLogPath,
            options.LogDirectory,
            options.ListLogs,
            options.SkipConfirmation);

        return await command.ExecuteAsync();
    }

    private static async Task<int> RunValidateConfigCommand(ValidateConfigOptions options)
    {
        var config = ConfigurationLoader.Load(options);
        await using var serviceProvider = BuildServiceProvider(config);

        var command = serviceProvider.GetRequiredService<ValidateConfigCommand>();
        return await command.ExecuteAsync();
    }

    private static ServiceProvider BuildServiceProvider(PhotoCopyConfig config, ConfigurationDiagnostics? diagnostics = null)
    {
        var services = new ServiceCollection();
        services.AddPhotoCopyServices(config, diagnostics);
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

        var sourceDir = new DirectoryInfo(config.Source);
        var isValid = true;
        if (!sourceDir.Exists)
        {
            logger.LogError("Source {SourcePath} does not exist", sourceDir.FullName);
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
            
            // In non-interactive mode (piped input, CI), default to abort
            if (Console.IsInputRedirected)
            {
                logger.LogWarning("Non-interactive mode detected, aborting due to missing filename variable in destination");
                isValid = false;
            }
            else
            {
                var response = Console.ReadLine();
                if (response != "yes") isValid = false;
            }
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
