using System;
using CommandLine;
using PhotoCopy.Configuration;

namespace PhotoCopy.Commands;

/// <summary>
/// Base options shared by all commands.
/// </summary>
public abstract class CommonOptions
{
    [Option("config", Required = false, HelpText = "Path to configuration file (yaml/json)")]
    public string? ConfigPath { get; set; }

    [Option('l', "log-level", Required = false, HelpText = "Log level (verbose/important/errorsOnly)")]
    public OutputLevel? LogLevel { get; set; }
}

/// <summary>
/// Options for the copy command.
/// </summary>
[Verb("copy", isDefault: true, HelpText = "Copy or move files from source to destination")]
public class CopyOptions : CommonOptions
{
    [Option('s', "source", Required = false, HelpText = "Source folder to scan for files")]
    public string? Source { get; set; }

    [Option('d', "destination", Required = false, HelpText = "Destination path pattern")]
    public string? Destination { get; set; }

    [Option('n', "dry-run", Required = false, HelpText = "Don't actually copy/move files")]
    public bool DryRun { get; set; }

    [Option('e', "skip-existing", Required = false, HelpText = "Skip files that already exist")]
    public bool SkipExisting { get; set; }

    [Option('o', "overwrite", Required = false, HelpText = "Overwrite existing files")]
    public bool Overwrite { get; set; }

    [Option('k', "no-duplicate-skip", Required = false, HelpText = "Don't skip duplicate files")]
    public bool NoDuplicateSkip { get; set; }

    [Option('m', "mode", Required = false, HelpText = "Operation mode (copy/move)")]
    public OperationMode? Mode { get; set; }

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

    [Option("calculate-checksums", Required = false, HelpText = "Enable or disable checksum calculation during metadata enrichment")]
    public bool CalculateChecksums { get; set; }

    [Option('p', "parallel", Required = false, HelpText = "Number of parallel operations (default: processor count)")]
    public int? Parallelism { get; set; }

    [Option("detect-duplicates", Required = false, HelpText = "Duplicate detection mode: none/skip/prompt/report")]
    public DuplicateHandlingOption? DuplicateHandling { get; set; }

    [Option("enable-rollback", Required = false, HelpText = "Enable transaction logging for rollback support")]
    public bool EnableRollback { get; set; }

    [Option("max-depth", Required = false, HelpText = "Maximum directory recursion depth (0 or omit for unlimited, 1 = root only)")]
    public int? MaxDepth { get; set; }

    [Option("path-casing", Required = false, HelpText = "Casing style for path variables (original/lowercase/uppercase/titlecase/pascalcase/camelcase/snakecase/kebabcase/screamingsnakecase)")]
    public PathCasing? PathCasing { get; set; }

    [Option("unknown-report", Required = false, HelpText = "Show a report of files that went to Unknown folder (none/summary/detailed)")]
    public UnknownReportLevel? UnknownReport { get; set; }
}

/// <summary>
/// Level of detail for the unknown files report.
/// </summary>
public enum UnknownReportLevel
{
    /// <summary>
    /// No report is shown.
    /// </summary>
    None,

    /// <summary>
    /// Show summary statistics only.
    /// </summary>
    Summary,

    /// <summary>
    /// Show detailed report including file list.
    /// </summary>
    Detailed
}

/// <summary>
/// Duplicate handling options for CLI.
/// </summary>
public enum DuplicateHandlingOption
{
    None,
    Skip,
    Prompt,
    Report
}

/// <summary>
/// Options for the scan command.
/// </summary>
[Verb("scan", HelpText = "Scan source directory and display file information without copying")]
public class ScanOptions : CommonOptions
{
    [Option('s', "source", Required = false, HelpText = "Source folder to scan for files")]
    public string? Source { get; set; }

    [Option("min-date", Required = false, HelpText = "Minimum date for files to process")]
    public DateTime? MinDate { get; set; }

    [Option("max-date", Required = false, HelpText = "Maximum date for files to process")]
    public DateTime? MaxDate { get; set; }

    [Option("geonames-path", Required = false, HelpText = "Path to the GeoNames cities500.txt or cities1000.txt file")]
    public string? GeonamesPath { get; set; }

    [Option("calculate-checksums", Required = false, HelpText = "Enable or disable checksum calculation during metadata enrichment")]
    public bool CalculateChecksums { get; set; }

    [Option("max-depth", Required = false, HelpText = "Maximum directory recursion depth (0 or omit for unlimited, 1 = root only)")]
    public int? MaxDepth { get; set; }

    [Option("json", Required = false, HelpText = "Output results as JSON")]
    public bool OutputJson { get; set; }
}

/// <summary>
/// Options for the validate command.
/// </summary>
[Verb("validate", HelpText = "Validate files against configured rules without copying")]
public class ValidateOptions : CommonOptions
{
    [Option('s', "source", Required = false, HelpText = "Source folder to scan for files")]
    public string? Source { get; set; }

    [Option("min-date", Required = false, HelpText = "Minimum date for files to process")]
    public DateTime? MinDate { get; set; }

    [Option("max-date", Required = false, HelpText = "Maximum date for files to process")]
    public DateTime? MaxDate { get; set; }

    [Option("geonames-path", Required = false, HelpText = "Path to the GeoNames cities500.txt or cities1000.txt file")]
    public string? GeonamesPath { get; set; }

    [Option("max-depth", Required = false, HelpText = "Maximum directory recursion depth (0 or omit for unlimited, 1 = root only)")]
    public int? MaxDepth { get; set; }
}

/// <summary>
/// Options for the config command.
/// </summary>
[Verb("config", HelpText = "Show resolved configuration and diagnostics")]
public class ConfigOptions : CommonOptions
{
    [Option("show", Required = false, Default = true, HelpText = "Show the fully resolved configuration")]
    public bool Show { get; set; }

    [Option("json", Required = false, HelpText = "Output configuration as JSON")]
    public bool OutputJson { get; set; }

    [Option('s', "source", Required = false, HelpText = "Source folder (for testing config resolution)")]
    public string? Source { get; set; }

    [Option('d', "destination", Required = false, HelpText = "Destination pattern (for testing config resolution)")]
    public string? Destination { get; set; }
}

/// <summary>
/// Options for the rollback command.
/// </summary>
[Verb("rollback", HelpText = "Rollback a previous copy/move operation using its transaction log")]
public class RollbackOptions : CommonOptions
{
    [Option('f', "file", Required = false, HelpText = "Path to the transaction log file to rollback")]
    public string? TransactionLogPath { get; set; }

    [Option("list", Required = false, HelpText = "List available transaction logs")]
    public bool ListLogs { get; set; }

    [Option('d', "directory", Required = false, HelpText = "Directory to search for transaction logs")]
    public string? LogDirectory { get; set; }

    [Option('y', "yes", Required = false, HelpText = "Skip confirmation prompt")]
    public bool SkipConfirmation { get; set; }
}
