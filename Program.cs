using CommandLine;
using Humanizer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PhotoCopy.Abstractions;
using PhotoCopy.Directories;
using PhotoCopy.Files;
using PhotoCopy.Validators;
using System;
using System.IO;

namespace PhotoCopy;

public static class ApplicationState
{
    public static Options Options { get; set; }
}
public class Program
{
    static int Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<IFileOperation, FileOperation>();
        services.AddTransient<IDirectoryCopier, DirectoryCopier>();
        services.AddTransient<IValidatorFactory, ValidatorFactory>();
        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<Program>>();

        return Parser.Default.ParseArguments<Options>(args).MapResult(options =>
        {
            if (!ValidateInput(options, logger)) return 1;
            PrintParsedOptions(options, logger);
            var validators = provider.GetRequiredService<IValidatorFactory>().Create(options);
            provider.GetRequiredService<IDirectoryCopier>().Copy(options, validators);
            return 0;
        }, _ => 1);
    }

    static bool ValidateInput(Options options, ILogger logger)
    {
        var sourceFile = new FileInfo(options.Source);
        var isValid = true;
        if (sourceFile.Exists)
        {
            logger.LogError($"Source {sourceFile.FullName} does not exist");
            isValid = false;
        }
        if (!sourceFile.Attributes.HasFlag(FileAttributes.Directory))
        {
            logger.LogError("Source is not a directory");
            isValid = false;
        }
        if (!options.DuplicatesFormat.Contains("{number}"))
        {
            logger.LogError("Duplicates format does not contain {number}");
            isValid = false;
        }
        if (!options.Destination.Contains(Options.DestinationVariables.Name) && !options.Destination.Contains(Options.DestinationVariables.NameNoExtension))
        {
            Console.WriteLine("Your destination path does not contain name or name without extension. This will result in files losing their original name and is generally undesirable. Are you absolutely sure about this? Write yes to confirm, anything else to abort.");
            var response = Console.ReadLine();
            if (response != "yes") isValid = false;
        }
        return isValid;
    }

    static void PrintParsedOptions(Options options, ILogger logger)
    {
        logger.LogInformation($"Input: {options.Source}");
        logger.LogInformation($"Output: {options.Destination}");
        logger.LogInformation($"Dry run: {options.DryRun}");
        logger.LogInformation($"Mode: {options.Mode}");
        if (options.MaxDate.HasValue)
            logger.LogInformation($"Max date: {options.MaxDate.Value.ToLongDateString()} {options.MaxDate.Value.ToLongTimeString()} ({options.MaxDate.Value.Humanize()})");
        if (options.MinDate.HasValue)
            logger.LogInformation($"Min date: {options.MinDate.Value.ToLongDateString()} {options.MinDate.Value.ToLongTimeString()} ({options.MinDate.Value.Humanize()})");
        logger.LogInformation("--------------");
    }
}