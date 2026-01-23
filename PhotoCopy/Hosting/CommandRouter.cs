using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PhotoCopy.Abstractions;
using PhotoCopy.Commands;
using PhotoCopy.Configuration;
using PhotoCopy.Rollback;
using PhotoCopy.Validators;
using System;
using System.Threading.Tasks;

namespace PhotoCopy.Hosting;

/// <summary>
/// Routes parsed command-line arguments to appropriate command handlers.
/// </summary>
public class CommandRouter
{
    /// <summary>
    /// Parses command-line arguments and routes to the appropriate command handler.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Exit code from the executed command.</returns>
    public async Task<int> RouteAsync(string[] args)
    {
        var parseResult = Parser.Default.ParseArguments<
            CopyOptions, 
            ScanOptions, 
            ValidateOptions, 
            ConfigOptions, 
            RollbackOptions, 
            ValidateConfigOptions>(args);

        return await parseResult.MapResult<
            CopyOptions, 
            ScanOptions, 
            ValidateOptions, 
            ConfigOptions, 
            RollbackOptions, 
            ValidateConfigOptions, 
            Task<int>>(
            RunCopyCommandAsync,
            RunScanCommandAsync,
            RunValidateCommandAsync,
            RunConfigCommandAsync,
            RunRollbackCommandAsync,
            RunValidateConfigCommandAsync,
            _ => Task.FromResult((int)ExitCode.InvalidArguments));
    }

    private async Task<int> RunCopyCommandAsync(CopyOptions options)
    {
        var config = ConfigurationLoader.Load(options);
        await using var serviceProvider = ServiceProviderFactory.Build(config);
        
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        var validator = serviceProvider.GetRequiredService<IInputValidator>();

        // Initialize geocoding service
        var geocodingService = serviceProvider.GetRequiredService<IReverseGeocodingService>();
        await geocodingService.InitializeAsync();

        if (!validator.ValidateCopyConfiguration(config))
        {
            return (int)ExitCode.ConfigurationError;
        }

        PrintConfiguration(config, logger);

        var command = serviceProvider.GetRequiredService<CopyCommand>();
        using var cancellation = new CancellationHandler(logger);

        return await command.ExecuteAsync(cancellation.Token);
    }

    private async Task<int> RunScanCommandAsync(ScanOptions options)
    {
        var config = ConfigurationLoader.Load(options);
        await using var serviceProvider = ServiceProviderFactory.Build(config);
        
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        var validator = serviceProvider.GetRequiredService<IInputValidator>();

        // Initialize geocoding service
        var geocodingService = serviceProvider.GetRequiredService<IReverseGeocodingService>();
        await geocodingService.InitializeAsync();

        if (!validator.ValidateSourceRequired(config))
        {
            return (int)ExitCode.ConfigurationError;
        }

        var command = ActivatorUtilities.CreateInstance<ScanCommand>(serviceProvider, options.OutputJson);
        using var cancellation = new CancellationHandler();

        return await command.ExecuteAsync(cancellation.Token);
    }

    private async Task<int> RunValidateCommandAsync(ValidateOptions options)
    {
        var config = ConfigurationLoader.Load(options);
        await using var serviceProvider = ServiceProviderFactory.Build(config);
        
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        var validator = serviceProvider.GetRequiredService<IInputValidator>();

        // Initialize geocoding service
        var geocodingService = serviceProvider.GetRequiredService<IReverseGeocodingService>();
        await geocodingService.InitializeAsync();

        if (!validator.ValidateSourceRequired(config))
        {
            return (int)ExitCode.ConfigurationError;
        }

        var command = serviceProvider.GetRequiredService<ValidateCommand>();
        using var cancellation = new CancellationHandler();

        return await command.ExecuteAsync(cancellation.Token);
    }

    private async Task<int> RunConfigCommandAsync(ConfigOptions options)
    {
        var (config, diagnostics) = ConfigurationLoader.LoadWithDiagnostics(options);
        ConfigurationLoader.ApplyOverrides(config, options, diagnostics);

        await using var serviceProvider = ServiceProviderFactory.Build(config, diagnostics);

        var command = ActivatorUtilities.CreateInstance<ConfigCommand>(serviceProvider, options.OutputJson);

        return await command.ExecuteAsync();
    }

    private async Task<int> RunRollbackCommandAsync(RollbackOptions options)
    {
        var config = ConfigurationLoader.Load(options);
        await using var serviceProvider = ServiceProviderFactory.Build(config);
        
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

    private async Task<int> RunValidateConfigCommandAsync(ValidateConfigOptions options)
    {
        var config = ConfigurationLoader.Load(options);
        await using var serviceProvider = ServiceProviderFactory.Build(config);

        var command = serviceProvider.GetRequiredService<ValidateConfigCommand>();
        return await command.ExecuteAsync();
    }

    private static void PrintConfiguration(PhotoCopyConfig config, ILogger logger)
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
