using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Extensions;
using PhotoCopy.Files;
using PhotoCopy.Rollback;
using PhotoCopy.Validators;

namespace PhotoCopy.Directories;

/// <summary>
/// Base class containing shared logic for directory copy operations.
/// </summary>
public abstract class DirectoryCopierBase
{
    protected readonly IFileSystem FileSystem;
    protected readonly PhotoCopyConfig Config;
    protected readonly ITransactionLogger TransactionLogger;
    protected readonly IFileValidationService FileValidationService;

    /// <summary>
    /// Gets the logger for the derived class.
    /// </summary>
    protected abstract ILogger Logger { get; }

    protected DirectoryCopierBase(
        IFileSystem fileSystem,
        IOptions<PhotoCopyConfig> options,
        ITransactionLogger transactionLogger,
        IFileValidationService fileValidationService)
    {
        FileSystem = fileSystem;
        Config = options.Value;
        TransactionLogger = transactionLogger;
        FileValidationService = fileValidationService;
    }

    /// <summary>
    /// Generates the destination path for a file using the configured pattern.
    /// </summary>
    public string GeneratePath(IFile file)
    {
        var destPath = Config.Destination ?? string.Empty;

        destPath = destPath.Replace(DestinationVariables.Year, file.FileDateTime.DateTime.Year.ToString());
        destPath = destPath.Replace(DestinationVariables.Month, file.FileDateTime.DateTime.Month.ToString("00"));
        destPath = destPath.Replace(DestinationVariables.Day, file.FileDateTime.DateTime.Day.ToString("00"));

        var fallback = Config.UnknownLocationFallback;
        
        if (file.Location != null)
        {
            var (city, county, state, country) = GetLocationValuesWithGranularity(file.Location, fallback);
            
            destPath = destPath.Replace(DestinationVariables.City, PathSanitizer.SanitizeOrFallback(city, fallback));
            destPath = destPath.Replace(DestinationVariables.County, PathSanitizer.SanitizeOrFallback(county, fallback));
            destPath = destPath.Replace(DestinationVariables.State, PathSanitizer.SanitizeOrFallback(state, fallback));
            destPath = destPath.Replace(DestinationVariables.Country, PathSanitizer.SanitizeOrFallback(country, fallback));
        }
        else
        {
            destPath = destPath.Replace(DestinationVariables.City, fallback);
            destPath = destPath.Replace(DestinationVariables.County, fallback);
            destPath = destPath.Replace(DestinationVariables.State, fallback);
            destPath = destPath.Replace(DestinationVariables.Country, fallback);
        }

        string directory;
        try
        {
            directory = !string.IsNullOrEmpty(Config.Source)
                ? Path.GetRelativePath(Config.Source, Path.GetDirectoryName(file.File.FullName) ?? string.Empty)
                : string.Empty;
        }
        catch (ArgumentException)
        {
            directory = Path.GetDirectoryName(file.File.Name) ?? string.Empty;
        }

        destPath = destPath.Replace(DestinationVariables.Directory, directory);

        var name = Path.GetFileNameWithoutExtension(file.File.Name);
        var ext = Path.GetExtension(file.File.Name);

        destPath = destPath.Replace(DestinationVariables.Name, name);
        destPath = destPath.Replace(DestinationVariables.NameNoExtension, name);
        destPath = destPath.Replace(DestinationVariables.Extension, ext);
        destPath = destPath.Replace("{filename}", file.File.Name);

        return destPath;
    }
    
    /// <summary>
    /// Gets location values adjusted for the configured granularity level.
    /// </summary>
    private (string City, string County, string State, string Country) GetLocationValuesWithGranularity(
        LocationData location, string fallback)
    {
        var granularity = Config.LocationGranularity;
        
        // Get country value, optionally converting to full name
        var country = Config.UseFullCountryNames 
            ? CountryCodeLookup.GetCountryName(location.Country) 
            : location.Country;
        
        // Apply granularity masking
        var city = granularity == LocationGranularity.City 
            ? location.City 
            : fallback;
            
        var county = granularity <= LocationGranularity.County 
            ? location.County ?? fallback 
            : fallback;
            
        var state = granularity <= LocationGranularity.State 
            ? location.State ?? fallback 
            : fallback;
        
        return (city, county, state, country);
    }

    /// <summary>
    /// Resolves duplicate file paths. Override for thread-safe behavior in async implementations.
    /// </summary>
    public abstract string? ResolveDuplicate(string destinationPath);

    /// <summary>
    /// Core duplicate resolution logic that can be used by derived classes.
    /// </summary>
    /// <param name="destinationPath">The original destination path.</param>
    /// <param name="isPathAvailable">Function to check if a path is available.</param>
    /// <returns>The resolved path, or null if the file should be skipped.</returns>
    protected string? ResolveDuplicateCore(string destinationPath, Func<string, bool> isPathAvailable)
    {
        if (isPathAvailable(destinationPath))
        {
            return destinationPath;
        }

        if (Config.SkipExisting)
        {
            Logger.LogDebug("Skipping existing file: {DestinationPath}", destinationPath);
            return null;
        }

        if (Config.Overwrite)
        {
            return destinationPath;
        }

        var directory = Path.GetDirectoryName(destinationPath);
        var filenameWithoutExt = Path.GetFileNameWithoutExtension(destinationPath);
        var extension = Path.GetExtension(destinationPath);

        var counter = 1;
        string newPath;

        do
        {
            var suffixFormat = Config.DuplicatesFormat.Replace("{number}", counter.ToString());
            var newFilename = $"{filenameWithoutExt}{suffixFormat}{extension}";
            newPath = Path.Combine(directory ?? string.Empty, newFilename);
            counter++;
        } while (!isPathAvailable(newPath));

        Logger.LogDebug("Using new path for duplicate: {NewPath}", newPath);
        return newPath;
    }

    /// <summary>
    /// Evaluates validators against a file.
    /// </summary>
    protected ValidationFailure? EvaluateValidators(IFile file, IReadOnlyCollection<IValidator> validators)
    {
        var result = FileValidationService.ValidateFirstFailure(file, validators);
        if (!result.IsValid)
        {
            return new ValidationFailure(file, result.FirstValidatorName ?? "Unknown", result.FirstRejectionReason);
        }

        return null;
    }

    /// <summary>
    /// Builds related file plans for sidecar files.
    /// </summary>
    protected IReadOnlyCollection<RelatedFilePlan> BuildRelatedPlans(
        IFile file,
        string primaryDestination,
        ISet<string> directories,
        ref long totalBytes)
    {
        if (file is not FileWithMetadata metadata || metadata.RelatedFiles.Count == 0)
        {
            return Array.Empty<RelatedFilePlan>();
        }

        var plans = new List<RelatedFilePlan>();
        foreach (var related in metadata.RelatedFiles)
        {
            var relatedDestPath = metadata.GetRelatedPath(primaryDestination, related);
            RegisterDirectory(relatedDestPath, directories);
            totalBytes += SafeFileLength(related);
            plans.Add(new RelatedFilePlan(related, relatedDestPath));
        }

        return plans;
    }

    /// <summary>
    /// Logs dry run summary. Can be overridden to add additional logging.
    /// </summary>
    protected virtual void LogDryRunSummary(CopyPlan plan)
    {
        var primaryCount = plan.Operations.Count;
        var relatedCount = plan.Operations.Sum(o => o.RelatedFiles.Count);
        var directories = plan.DirectoriesToCreate.Count;

        Logger.LogInformation("DryRun Summary: {Primary} primary files, {Related} related files", primaryCount, relatedCount);
        Logger.LogInformation("DryRun Summary: {Directories} directories to create", directories);
        Logger.LogInformation("DryRun Summary: Total bytes {Bytes} ({Readable})", plan.TotalBytes, FormatBytes(plan.TotalBytes));

        if (plan.SkippedFiles.Count > 0)
        {
            Logger.LogInformation("DryRun Summary: {Skipped} files skipped by validators", plan.SkippedFiles.Count);
        }
        else
        {
            Logger.LogInformation("DryRun Summary: No files skipped by validators");
        }
    }

    /// <summary>
    /// Logs skipped files after execution.
    /// </summary>
    protected void LogSkippedFiles(IReadOnlyList<ValidationFailure> skippedFiles)
    {
        foreach (var failure in skippedFiles)
        {
            Logger.LogInformation(
                "Skipped {File} due to validator {Validator}: {Reason}",
                failure.File.File.Name,
                failure.ValidatorName,
                failure.Reason ?? "No reason provided");
        }
    }

    /// <summary>
    /// Gets the operation type based on configuration.
    /// </summary>
    protected OperationType GetOperationType() =>
        Config.Mode == OperationMode.Move ? OperationType.Move : OperationType.Copy;

    /// <summary>
    /// Registers a directory for creation.
    /// </summary>
    protected static void RegisterDirectory(string path, ISet<string> directories)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            directories.Add(directory);
        }
    }

    /// <summary>
    /// Safely gets file length, returning 0 on error.
    /// </summary>
    protected static long SafeFileLength(IFile file) => ByteFormatter.SafeFileLength(file);

    /// <summary>
    /// Formats bytes as human-readable string.
    /// </summary>
    protected static string FormatBytes(long bytes) => ByteFormatter.FormatBytes(bytes);
}
