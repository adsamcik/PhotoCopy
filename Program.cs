using CommandLine;
using PhotoCopy.Files;
using System;
using System.IO;

namespace PhotoCopySort
{
    public static class ApplicationState
    {
        public static Options Options { get; set; }
    }

    /// <summary>
    /// Command line options
    /// </summary>
    public class Options
    {
        public enum OperationMode
        {
            move,
            copy,
        }


        public enum LogLevel
        {
            verbose,
            important,
            errorsOnly
        }

        public static class DestinationEnum
        {
            public const string Year = "{year}";
            public const string Month = "{month}";
            public const string Day = "{day}";
            public const string DayOfYear = "{dayOfYear}";
            public const string Name = "{name}";
            public const string NameNoExtension = "{nameNoExtension}";
            public const string Directory = "{directory}";
            public const string Extension = "{extension}";
        }

        [Option('i', "input", Required = true, HelpText = "Set source folder")]
        public string Source { get; set; }

        [Option('o', "output", Required = true,
            HelpText =
                "Set output folder. Supported variables (case-sensitive): " + DestinationEnum.Year + ", " +
                DestinationEnum.Month + ", " + DestinationEnum.Day +
                ", " + DestinationEnum.DayOfYear + ", " + DestinationEnum.Directory + ", " +
                DestinationEnum.Name + ", " + DestinationEnum.NameNoExtension + ", " + DestinationEnum.Extension)]
        public string Destination { get; set; }

        [Option('d', "dry", Required = false,
            HelpText = "True if no files should be moved and only printed to the command line.")]
        public bool DryRun { get; set; }

        [Option('m', "mode", Required = false, Default = OperationMode.copy,
            HelpText = "Operation mode. Available modes: copy, move")]
        public OperationMode Mode { get; set; }

        [Option("skip-existing", Required = false, HelpText = "Skips file if it already exists in the output.")]
        public bool SkipExisting { get; set; }

        [Option('l', "logLevel", Required = false, Default = LogLevel.important,
            HelpText = "Determines what is printed on screen. Options: verbose, important, errorsOnly")]
        public LogLevel Log { get; set; }

        [Option("no-skip-duplicate", Required = false,
            HelpText = "Disables duplicate skipping.")]
        public bool NoDuplicateSkip { get; set; }

        [Option("duplicate-format", Required = false, Default = "_{number}",
            HelpText = "Format used for differentiating duplicates. Use {number} for number placeholder.")]
        public string DuplicatesFormat { get; set; }
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

            if (!options.Destination.Contains(Options.DestinationEnum.Name) &&
                !options.Destination.Contains(Options.DestinationEnum.NameNoExtension))
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


        static void Main(string[] args)
        {
            Parser
                .Default
                .ParseArguments<Options>(args)
                .WithParsed(options =>
                {
                    if (!ValidateInput(options)) return;
                    ApplicationState.Options = options;
                    DirectoryCopier.Copy(options);
                });
        }
    }
}