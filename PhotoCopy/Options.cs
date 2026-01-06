using System;
using CommandLine;
using PhotoCopy.Configuration;

namespace PhotoCopy;

/// <summary>
/// Command line options
/// </summary>
public class Options
{
    [Option('s', "source", Required = false, HelpText = "Source folder to scan for files")]
    public string? Source { get; set; }

    [Option('d', "destination", Required = false, HelpText = "Destination path pattern")]
    public string? Destination { get; set; }

    [Option('n', "dry-run", Required = false, HelpText = "Don't actually copy/move files")]
    public bool? DryRun { get; set; }

    [Option('e', "skip-existing", Required = false, HelpText = "Skip files that already exist")]
    public bool? SkipExisting { get; set; }

    [Option('o', "overwrite", Required = false, HelpText = "Overwrite existing files")]
    public bool? Overwrite { get; set; }

    [Option('k', "no-duplicate-skip", Required = false, HelpText = "Don't skip duplicate files")]
    public bool? NoDuplicateSkip { get; set; }

    [Option('m', "mode", Required = false, HelpText = "Operation mode (copy/move)")]
    public OperationMode? Mode { get; set; }

    [Option('l', "log-level", Required = false, HelpText = "Log level (verbose/important/errorsOnly)")]
    public OutputLevel? LogLevel { get; set; }

    [Option('r', "related-file-mode", Required = false, HelpText = "Related file lookup mode (none/strict/loose)")]
    public RelatedFileLookup? RelatedFileMode { get; set; }

    [Option('f', "duplicates-format", Required = false, HelpText = "Format for duplicate files")]
    public string? DuplicatesFormat { get; set; }
    
    [Option("min-date", Required = false, HelpText = "Minimum date for files to process")]
    public DateTime? MinDate { get; set; }
    
    [Option("max-date", Required = false, HelpText = "Maximum date for files to process")]
    public DateTime? MaxDate { get; set; }

    [Option("geonames-path", Required = false, HelpText = "Path to the GeoNames cities500.txt or cities1000.txt file")]
    public string? GeonamesPath { get; set; }

    [Option("config", Required = false, HelpText = "Path to configuration file (yaml/json)")]
    public string? ConfigPath { get; set; }

    [Option("calculate-checksums", Required = false, HelpText = "Enable or disable checksum calculation during metadata enrichment")]
    public bool? CalculateChecksums { get; set; }

    [Option('p', "parallelism", Required = false, HelpText = "Maximum degree of parallelism for file operations (default: number of processors)")]
    public int? Parallelism { get; set; }

    [Option("show-progress", Required = false, HelpText = "Show progress reporting during operations")]
    public bool? ShowProgress { get; set; }

    [Option("async", Required = false, HelpText = "Use asynchronous parallel processing")]
    public bool? UseAsync { get; set; }

    public static class DestinationVariables
    {
        public const string Year = "{year}";
        public const string Month = "{month}";
        public const string Day = "{day}";
        public const string Name = "{name}";
        public const string NameNoExtension = "{namenoext}";
        public const string Extension = "{ext}";
        public const string Directory = "{directory}";
        public const string Number = "{number}";
        public const string City = "{city}";
        public const string State = "{state}";
        public const string Country = "{country}";
    }
}

