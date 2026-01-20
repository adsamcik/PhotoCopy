using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Extensions;
using PhotoCopy.Files;
using PhotoCopy.Progress;
using PhotoCopy.Rollback;
using PhotoCopy.Validators;
// IPathGeneratorContext is in the same namespace, no extra using needed

namespace PhotoCopy.Directories;

/// <summary>
/// Base class containing shared logic for directory copy operations.
/// </summary>
public abstract class DirectoryCopierBase
{
    // Cache for compiled regex patterns by variable name
    private static readonly ConcurrentDictionary<string, Regex> VariableRegexCache = new(StringComparer.OrdinalIgnoreCase);

    protected readonly IFileSystem FileSystem;
    protected readonly PhotoCopyConfig Config;
    protected readonly ITransactionLogger TransactionLogger;
    protected readonly IFileValidationService FileValidationService;
    
    /// <summary>
    /// Report tracking files that went to Unknown folders.
    /// </summary>
    protected readonly UnknownFilesReport UnknownFilesReport = new();

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
    /// <param name="file">The file to generate a destination path for.</param>
    /// <param name="context">Optional context with statistics for conditional path generation.</param>
    public string GeneratePath(IFile file, IPathGeneratorContext? context = null)
    {
        var destPath = Config.Destination ?? string.Empty;
        var casing = Config.PathCasing;

        destPath = destPath.Replace(DestinationVariables.Year, file.FileDateTime.DateTime.Year.ToString());
        destPath = destPath.Replace(DestinationVariables.Month, file.FileDateTime.DateTime.Month.ToString("00"));
        destPath = destPath.Replace(DestinationVariables.Day, file.FileDateTime.DateTime.Day.ToString("00"));

        var globalFallback = CasingFormatter.ApplyCasing(Config.UnknownLocationFallback, casing);
        
        // Get location values
        string? district = null, city = null, county = null, state = null, country = null;
        if (file.Location != null)
        {
            var locationValues = GetLocationValuesWithGranularity(file.Location, string.Empty);
            district = locationValues.District;
            city = locationValues.City;
            county = locationValues.County;
            state = locationValues.State;
            country = locationValues.Country;
        }
        
        // Build a dictionary of location values for conditional expression evaluation
        var locationValues2 = new Dictionary<string, string?>
        {
            { "district", district },
            { "city", city },
            { "county", county },
            { "state", state },
            { "country", country }
        };
        
        // Replace location variables with inline fallback support and conditional expressions
        destPath = ReplaceVariableWithFallback(destPath, "district", district, globalFallback, casing, context, locationValues2);
        destPath = ReplaceVariableWithFallback(destPath, "city", city, globalFallback, casing, context, locationValues2);
        destPath = ReplaceVariableWithFallback(destPath, "county", county, globalFallback, casing, context, locationValues2);
        destPath = ReplaceVariableWithFallback(destPath, "state", state, globalFallback, casing, context, locationValues2);
        destPath = ReplaceVariableWithFallback(destPath, "country", country, globalFallback, casing, context, locationValues2);

        string directory;
        try
        {
            directory = !string.IsNullOrEmpty(Config.Source)
                ? Path.GetRelativePath(Config.Source, Path.GetDirectoryName(file.File.FullName) ?? string.Empty)
                : string.Empty;
        }
        catch (ArgumentException)
        {
            // Fallback when paths are on different drives (Windows) or incompatible
            directory = file.File.DirectoryName ?? Path.GetDirectoryName(file.File.FullName) ?? string.Empty;
        }

        destPath = destPath.Replace(DestinationVariables.Directory, directory);

        var name = Path.GetFileNameWithoutExtension(file.File.Name);
        var ext = Path.GetExtension(file.File.Name);

        destPath = destPath.Replace(DestinationVariables.Name, name);
        destPath = destPath.Replace(DestinationVariables.NameNoExtension, name);
        destPath = destPath.Replace(DestinationVariables.Extension, ext);
        destPath = destPath.Replace("{filename}", file.File.Name);
        
        // Handle {camera} variable - uses camera make/model from EXIF, falls back to "Unknown"
        var cameraFallback = CasingFormatter.ApplyCasing(Config.UnknownCameraFallback ?? Config.UnknownLocationFallback, casing);
        destPath = ReplaceVariableWithFallback(destPath, "camera", file.Camera, cameraFallback, casing, context, locationValues2);
        
        // Handle {album} variable - uses album name from EXIF/XMP/IPTC, falls back to "Unknown"
        var albumFallback = CasingFormatter.ApplyCasing(Config.UnknownAlbumFallback ?? Config.UnknownLocationFallback, casing);
        destPath = ReplaceVariableWithFallback(destPath, "album", file.Album, albumFallback, casing, context, locationValues2);

        // Normalize path to clean up orphaned separators from empty variables
        destPath = NormalizeDestinationPath(destPath);

        return destPath;
    }
    
    /// <summary>
    /// Replaces a variable in the path, supporting conditional syntax like {variable?min=N|fallback}.
    /// If the value is empty or conditions fail, uses the inline fallback if specified, otherwise uses the global fallback.
    /// </summary>
    private static string ReplaceVariableWithFallback(
        string path, 
        string variableName, 
        string? value, 
        string globalFallback, 
        PathCasing casing,
        IPathGeneratorContext? context = null,
        Dictionary<string, string?>? allLocationValues = null)
    {
        // Get or create cached regex for this variable name
        var regex = VariableRegexCache.GetOrAdd(variableName, name =>
        {
            var pattern = $@"\{{{name}(?:\?([^|}}\s]+))?(?:\|([^}}]*))?\}}";
            return new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        });
        
        return regex.Replace(path, match =>
        {
            var conditionsStr = match.Groups[1].Success ? match.Groups[1].Value : null;
            var inlineFallback = match.Groups[2].Success ? match.Groups[2].Value : null;
            var sanitizedValue = PathSanitizer.SanitizeOrFallback(CasingFormatter.ApplyCasing(value, casing), string.Empty);
            
            // Parse and evaluate conditions if present
            bool conditionsPassed = true;
            if (!string.IsNullOrEmpty(conditionsStr) && context != null && !string.IsNullOrEmpty(sanitizedValue))
            {
                // Parse the conditions (min=N, max=N)
                var expression = VariableExpressionParser.Parse(match.Value);
                if (expression != null && expression.HasConditions)
                {
                    conditionsPassed = VariableExpressionParser.EvaluateConditions(expression, sanitizedValue, context);
                }
            }
            
            // If value is not empty and conditions pass, use the value
            if (!string.IsNullOrEmpty(sanitizedValue) && conditionsPassed)
            {
                return sanitizedValue;
            }
            
            // Conditions failed or value is empty - check if fallback is another variable
            if (inlineFallback != null)
            {
                // Check if the fallback is a variable name (without braces)
                if (allLocationValues != null && allLocationValues.TryGetValue(inlineFallback.ToLowerInvariant(), out var fallbackValue))
                {
                    var sanitizedFallback = PathSanitizer.SanitizeOrFallback(CasingFormatter.ApplyCasing(fallbackValue, casing), string.Empty);
                    if (!string.IsNullOrEmpty(sanitizedFallback))
                    {
                        return sanitizedFallback;
                    }
                }
                // Otherwise use the inline fallback as a literal value
                return CasingFormatter.ApplyCasing(inlineFallback, casing);
            }
            
            return globalFallback;
        });
    }
    
    /// <summary>
    /// Normalizes a destination path by cleaning up orphaned separators from empty variables.
    /// Only cleans up patterns that result from empty variable replacements, preserving valid path segments.
    /// </summary>
    private static string NormalizeDestinationPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        // Replace multiple consecutive separators with a single separator
        path = Regex.Replace(path, @"[\\/]{2,}", Path.DirectorySeparatorChar.ToString());
        
        // Remove path segments that are ONLY separators (e.g., "/-/" -> "/", "/--/" -> "/", "/_/" -> "/")
        // This handles the case of {country}-{city} when both are empty, resulting in "-" as a segment
        path = Regex.Replace(path, @"[\\/][-_]+[\\/]", Path.DirectorySeparatorChar.ToString());
        
        // Handle consecutive hyphens/underscores within a segment (e.g., "country--city" -> "country-city")
        // This handles the case where one variable is empty: {country}-{city} with empty country becomes "-city"
        path = Regex.Replace(path, @"(?<=[\\/])[-_]+(?=[^-_\\/])", string.Empty);
        
        // Remove trailing separator-only segments at the end before filename
        // (e.g., "2024/01/-/photo.jpg" should become "2024/01/photo.jpg")
        // Already handled by the segment removal above

        return path;
    }
    
    /// <summary>
    /// Gets location values adjusted for the configured granularity level.
    /// </summary>
    private (string District, string City, string County, string State, string Country) GetLocationValuesWithGranularity(
        LocationData location, string fallback)
    {
        var granularity = Config.LocationGranularity;
        
        // Get country value, optionally converting to full name
        var country = Config.UseFullCountryNames 
            ? CountryCodeLookup.GetCountryName(location.Country) 
            : location.Country;
        
        // Apply granularity masking
        // District is the nearest place (neighborhood/district within a city)
        var district = granularity == LocationGranularity.City 
            ? location.District 
            : fallback;
        
        // City is the nearest city-level place (excludes districts/neighborhoods)
        var city = granularity == LocationGranularity.City 
            ? location.City ?? location.District  // Fall back to district if city is null
            : fallback;
            
        var county = granularity <= LocationGranularity.County 
            ? location.County ?? fallback 
            : fallback;
            
        var state = granularity <= LocationGranularity.State 
            ? location.State ?? fallback 
            : fallback;
        
        return (district, city, county, state, country);
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

        const int maxIterations = 10000;
        var counter = 1;
        string newPath;

        do
        {
            if (counter > maxIterations)
            {
                throw new InvalidOperationException(
                    $"Unable to find available path for '{destinationPath}' after {maxIterations} attempts. " +
                    "This may indicate a naming pattern issue or an extremely high number of duplicates.");
            }

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
        var operationType = Config.Mode == OperationMode.Move ? "Move" : "Copy";

        Logger.LogInformation("DryRun Summary: {Primary} primary files, {Related} related files", primaryCount, relatedCount);
        Logger.LogInformation("DryRun Summary: {Directories} directories to create", directories);
        
        foreach (var dir in plan.DirectoriesToCreate.OrderBy(d => d))
        {
            Logger.LogInformation("DryRun: Will create directory {Directory}", dir);
        }
        
        Logger.LogInformation("DryRun Summary: Total bytes {Bytes} ({Readable})", plan.TotalBytes, FormatBytes(plan.TotalBytes));

        if (plan.SkippedFiles.Count > 0)
        {
            Logger.LogInformation("DryRun Summary: {Skipped} files skipped by validators", plan.SkippedFiles.Count);
        }
        else
        {
            Logger.LogInformation("DryRun Summary: No files skipped by validators");
        }
        
        // Log each planned operation with full details
        Logger.LogInformation("DryRun: Planned operations:");
        foreach (var op in plan.Operations)
        {
            var size = SafeFileLength(op.File);
            Logger.LogInformation(
                "DryRun: Will {Operation} {Source} -> {Destination} ({Size})",
                operationType, op.File.File.FullName, op.DestinationPath, FormatBytes(size));
            
            // Log related files (sidecar files like XMP, JSON, etc.)
            foreach (var related in op.RelatedFiles)
            {
                var relatedSize = SafeFileLength(related.File);
                Logger.LogInformation(
                    "DryRun:   -> {Source} -> {Destination} ({Size}) [related]",
                    related.File.File.Name, related.DestinationPath, FormatBytes(relatedSize));
            }
        }
        
        // Log skipped files in dry-run output too
        if (plan.SkippedFiles.Count > 0)
        {
            Logger.LogInformation("DryRun: Skipped files:");
            foreach (var skipped in plan.SkippedFiles)
            {
                Logger.LogInformation(
                    "DryRun: Skipped {File} ({Validator}: {Reason})",
                    skipped.File.File.Name,
                    skipped.ValidatorName,
                    skipped.Reason ?? "No reason provided");
            }
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

    /// <summary>
    /// Tracks a file if it has no location data (goes to Unknown folder).
    /// </summary>
    /// <param name="file">The file to check.</param>
    protected void TrackUnknownFile(IFile file)
    {
        if (file.Location == null && file.UnknownReason != UnknownFileReason.None)
        {
            UnknownFilesReport.AddEntry(file, file.UnknownReason);
        }
    }

    /// <summary>
    /// Clears the unknown files report. Called at the start of a copy operation.
    /// </summary>
    protected void ClearUnknownFilesReport()
    {
        UnknownFilesReport.Clear();
    }

    /// <summary>
    /// Gets a clone of the current unknown files report.
    /// </summary>
    protected UnknownFilesReport GetUnknownFilesReport()
    {
        return UnknownFilesReport;
    }
}
