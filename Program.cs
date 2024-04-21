using CommandLine;
using Humanizer;
using PhotoCopy.Directory;
using PhotoCopy.Files;
using PhotoCopy.Validators;
using System;
using System.IO;

namespace PhotoCopy;

public static class ApplicationState
{
    public static Options Options { get; set; }
}

class Program
{
    /// <summary>
    /// Validates whether input is valid.
    /// </summary>
    /// <param name="options">Options</param>
    /// <returns>True if input is valid</returns>
    private static bool ValidateInput(Options options)
    {
        var sourceFile = new FileInfo(options.Source);
        var isValid = true;
        if (sourceFile.Exists)
        {
            Log.Print($"Source {sourceFile.FullName} does not exist", Options.LogLevel.errorsOnly);
            isValid = false;
        }

        if (!sourceFile.Attributes.HasFlag(FileAttributes.Directory))
        {
            Log.Print("Source is not a directory", Options.LogLevel.errorsOnly);
            isValid = false;
        }

        if (!options.DuplicatesFormat.Contains("{number}"))
        {
            Log.Print("Duplicates format does not contain {number}", Options.LogLevel.errorsOnly);
            isValid = false;
        }

        if (!options.Destination.Contains(Options.DestinationVariables.Name) &&
            !options.Destination.Contains(Options.DestinationVariables.NameNoExtension))
        {
            Console.WriteLine(
                "Your destination path does not contain name or name without extension. This will result in files losing their original name and is generally undesirable. Are you absolutely sure about this? Write yes to confirm, anything else to abort.");
            var response = Console.ReadLine();
            if (response != "yes")
            {
                isValid = false;
            }
        }

        return isValid;
    }

    private static void PrintParsedOptions(Options options)
    {
        Log.Print($"Input: {options.Source}", Options.LogLevel.verbose);
        Log.Print($"Output: {options.Destination}", Options.LogLevel.verbose);
        Log.Print($"Dry run: {options.DryRun}", Options.LogLevel.verbose);
        Log.Print($"Mode: {options.Mode}", Options.LogLevel.verbose);
        if (options.MaxDate.HasValue)
        {
            Log.Print($"Max date: {options.MaxDate.Value.ToLongDateString()} {options.MaxDate.Value.ToLongTimeString()} ({options.MaxDate.Value.Humanize()})", Options.LogLevel.verbose);
        }
        if (options.MinDate.HasValue)
        {
            Log.Print($"Min date: {options.MinDate.Value.ToLongDateString()} {options.MinDate.Value.ToLongTimeString()} ({options.MinDate.Value.Humanize()})", Options.LogLevel.verbose);
        }
        Log.Print("--------------", Options.LogLevel.verbose);
    }


    static void Main(string[] args)
    {
        Parser
            .Default
            .ParseArguments<Options>(args)
            .WithParsed(options =>
            {
                if (!ValidateInput(options)) return;
                ApplicationState.Options = options;

                PrintParsedOptions(options);

                var validators = ValidatorFactory.Create(options);
                DirectoryCopier.Copy(options, validators);
            });
    }
}