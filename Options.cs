using CommandLine;
using System;

namespace PhotoCopy
{
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

        public enum RelatedFileLookup
        {
            none,
            strict,
            loose
        }

        public static class DestinationVariables
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

        [Option('i', "input", Required = true, HelpText = "Path to a source directory, which will be scanned for files.")]
        public string Source { get; set; }

        [Option('o', "output", Required = true,
            HelpText =
                "Destination path for the operation. Determines the final path files have. Supported variables (case-sensitive): " + DestinationVariables.Year + ", " +
                DestinationVariables.Month + ", " + DestinationVariables.Day +
                ", " + DestinationVariables.DayOfYear + ", " + DestinationVariables.Directory + ", " +
                DestinationVariables.Name + ", " + DestinationVariables.NameNoExtension + ", " + DestinationVariables.Extension)]
        public string Destination { get; set; }

        [Option('d', "dry", Required = false,
            HelpText = "Only prints what will happen without actually doing it. It is recommended to combine it with log level verbose.")]
        public bool DryRun { get; set; }

        [Option('m', "mode", Required = false, Default = OperationMode.copy,
            HelpText = "Operation mode. Available modes: copy, move")]
        public OperationMode Mode { get; set; }

        [Option('l', "logLevel", Required = false, Default = LogLevel.important,
            HelpText = "Determines how much information is printed on the screen. Options: verbose, important, errorsOnly")]
        public LogLevel Log { get; set; }

        [Option("no-skip-duplicate", Required = false,
            HelpText = "Disables duplicate skipping.")]
        public bool NoDuplicateSkip { get; set; }

        [Option("duplicate-format", Required = false, Default = "_{number}",
            HelpText = "Format used for differentiating files with the same name. Use {number} for number placeholder.")]
        public string DuplicatesFormat { get; set; }

        [Option("skip-existing", Required = false, HelpText = "Skips file if it already exists in the output.")]
        public bool SkipExisting { get; set; }

        [Option("require-exif", Required = false, HelpText = "Will ignore images where exif date was not found.")]
        public bool RequireExif { get; set; }

        [Option("related-file-mode", Required = false, HelpText = "Mode used for related file lookups. Options: none, strict, loose.")]
        public RelatedFileLookup RelatedFileMode { get; set; }

        [Option("max-date", Required = false, HelpText = "Ignores all files newer than this.")]
        public DateTime? MaxDate { get; set; }

        [Option("min-date", Required = false, HelpText = "Ignores all files older than this.")]
        public DateTime? MinDate { get; set; }
    }
}
