using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy.Abstractions;
using PhotoCopy.Files;
using PhotoCopy.Validators;
using PhotoCopy.Configuration;
using PhotoCopy; // For Enums

namespace PhotoCopy.Directories;

public class DirectoryCopier : IDirectoryCopier
{
    private readonly ILogger<DirectoryCopier> _logger;
    private readonly IFileSystem _fileSystem;
    private readonly PhotoCopyConfig _config;

    public DirectoryCopier(ILogger<DirectoryCopier> logger, IFileSystem fileSystem, IOptions<PhotoCopyConfig> options)
    {
        _logger = logger;
        _fileSystem = fileSystem;
        _config = options.Value;
    }

    public void Copy(IReadOnlyCollection<IValidator> validators)
    {
        var files = _fileSystem.EnumerateFiles(_config.Source);
        var plan = BuildCopyPlan(files, validators);

        if (_config.DryRun)
        {
            LogDryRunSummary(plan);
        }

        ExecutePlan(plan);
    }

    private CopyPlan BuildCopyPlan(IEnumerable<IFile> files, IReadOnlyCollection<IValidator> validators)
    {
        var operations = new List<FileCopyPlan>();
        var skipped = new List<ValidationFailure>();
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;

        foreach (var file in files)
        {
            var failure = EvaluateValidators(file, validators);
            if (failure is not null)
            {
                skipped.Add(failure);
                _logger.LogDebug("File {File} skipped by validator {Validator}: {Reason}", file.File.Name, failure.ValidatorName, failure.Reason);
                continue;
            }

            var destinationPath = GeneratePath(file);
            var resolvedPath = ResolveDuplicate(destinationPath);
            if (resolvedPath is null)
            {
                continue;
            }

            RegisterDirectory(resolvedPath, directories);
            totalBytes += SafeFileLength(file);

            var relatedPlans = BuildRelatedPlans(file, resolvedPath, directories, ref totalBytes);
            operations.Add(new FileCopyPlan(file, resolvedPath, relatedPlans));
        }

        return new CopyPlan(operations, skipped, directories, totalBytes);
    }

    private ValidationFailure? EvaluateValidators(IFile file, IReadOnlyCollection<IValidator> validators)
    {
        foreach (var validator in validators)
        {
            var result = validator.Validate(file);
            if (!result.IsValid)
            {
                return new ValidationFailure(file, result.ValidatorName, result.Reason);
            }
        }

        return null;
    }

    private IReadOnlyCollection<RelatedFilePlan> BuildRelatedPlans(
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

    private void ExecutePlan(CopyPlan plan)
    {
        foreach (var operation in plan.Operations)
        {
            EnsureDestinationDirectory(operation.DestinationPath);
            PerformOperation(operation.File, operation.DestinationPath);

            foreach (var related in operation.RelatedFiles)
            {
                EnsureDestinationDirectory(related.DestinationPath);
                PerformOperation(related.File, related.DestinationPath);
            }
        }

        if (plan.SkippedFiles.Count > 0)
        {
            foreach (var failure in plan.SkippedFiles)
            {
                _logger.LogInformation(
                    "Skipped {File} due to validator {Validator}: {Reason}",
                    failure.File.File.Name,
                    failure.ValidatorName,
                    failure.Reason ?? "No reason provided");
            }
        }
    }

    private void PerformOperation(IFile file, string destinationPath)
    {
        if (_config.Mode == OperationMode.Move)
        {
            if (_config.DryRun)
            {
                _logger.LogInformation("DryRun: Move {Source} to {Destination}", file.File.FullName, destinationPath);
            }
            else
            {
                _fileSystem.MoveFile(file.File.FullName, destinationPath);
                _logger.LogInformation("Moved {Source} to {Destination}", file.File.FullName, destinationPath);
            }
        }
        else
        {
            if (_config.DryRun)
            {
                _logger.LogInformation("DryRun: Copy {Source} to {Destination}", file.File.FullName, destinationPath);
            }
            else
            {
                _fileSystem.CopyFile(file.File.FullName, destinationPath, true);
                _logger.LogInformation("Copied {Source} to {Destination}", file.File.FullName, destinationPath);
            }
        }
    }

    private void EnsureDestinationDirectory(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(directory) || _fileSystem.DirectoryExists(directory))
        {
            return;
        }

        if (_config.DryRun)
        {
            _logger.LogInformation("DryRun: Create directory {Directory}", directory);
        }
        else
        {
            _fileSystem.CreateDirectory(directory);
        }
    }

    private static void RegisterDirectory(string path, ISet<string> directories)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            directories.Add(directory);
        }
    }

    private static long SafeFileLength(IFile file)
    {
        try
        {
            return file.File.Length;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private void LogDryRunSummary(CopyPlan plan)
    {
        var primaryCount = plan.Operations.Count;
        var relatedCount = plan.Operations.Sum(o => o.RelatedFiles.Count);
        var directories = plan.DirectoriesToCreate.Count;
        var totalFiles = primaryCount + relatedCount;

        _logger.LogInformation("DryRun Summary: {Primary} primary files, {Related} related files", primaryCount, relatedCount);
        _logger.LogInformation("DryRun Summary: {Directories} directories to create", directories);
        _logger.LogInformation("DryRun Summary: Total bytes {Bytes} ({Readable})", plan.TotalBytes, FormatBytes(plan.TotalBytes));

        if (plan.SkippedFiles.Count > 0)
        {
            _logger.LogInformation("DryRun Summary: {Skipped} files skipped by validators", plan.SkippedFiles.Count);
        }
        else
        {
            _logger.LogInformation("DryRun Summary: No files skipped by validators");
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var unitIndex = (int)Math.Floor(Math.Log(bytes, 1024));
        unitIndex = Math.Clamp(unitIndex, 0, units.Length - 1);
        var adjusted = bytes / Math.Pow(1024, unitIndex);
        return $"{adjusted:0.##} {units[unitIndex]}";
    }

    public string? ResolveDuplicate(string destinationPath)
    {
        if (!_fileSystem.FileExists(destinationPath))
        {
            return destinationPath;
        }

        if (_config.SkipExisting)
        {
            _logger.LogInformation("Skipping existing file: {DestinationPath}", destinationPath);
            return null;
        }

        if (_config.Overwrite)
        {
            _logger.LogInformation("Overwriting existing file: {DestinationPath}", destinationPath);
            return destinationPath;
        }

        string? directory = Path.GetDirectoryName(destinationPath);
        string filenameWithoutExt = Path.GetFileNameWithoutExtension(destinationPath);
        string extension = Path.GetExtension(destinationPath);
        
        int counter = 1;
        string newPath;
        
        do
        {
            string suffixFormat = _config.DuplicatesFormat.Replace("{number}", counter.ToString());
            string newFilename = $"{filenameWithoutExt}{suffixFormat}{extension}";
            newPath = Path.Combine(directory ?? string.Empty, newFilename);
            counter++;
        } while (_fileSystem.FileExists(newPath));
        
        _logger.LogInformation("Using new path for duplicate: {NewPath}", newPath);
        return newPath;
    }

    public string GeneratePath(IFile file)
    {
        var destPath = _config.Destination ?? string.Empty;

        destPath = destPath.Replace(PhotoCopy.Options.DestinationVariables.Year, file.FileDateTime.DateTime.Year.ToString());
        destPath = destPath.Replace(PhotoCopy.Options.DestinationVariables.Month, file.FileDateTime.DateTime.Month.ToString("00"));
        destPath = destPath.Replace(PhotoCopy.Options.DestinationVariables.Day, file.FileDateTime.DateTime.Day.ToString("00"));

        if (file.Location != null)
        {
            destPath = destPath.Replace(PhotoCopy.Options.DestinationVariables.City, file.Location.City ?? "Unknown");
            destPath = destPath.Replace(PhotoCopy.Options.DestinationVariables.State, file.Location.State ?? "Unknown");
            destPath = destPath.Replace(PhotoCopy.Options.DestinationVariables.Country, file.Location.Country ?? "Unknown");
        }
        else
        {
            destPath = destPath.Replace(PhotoCopy.Options.DestinationVariables.City, "Unknown");
            destPath = destPath.Replace(PhotoCopy.Options.DestinationVariables.State, "Unknown");
            destPath = destPath.Replace(PhotoCopy.Options.DestinationVariables.Country, "Unknown");
        }

        string directory;
        try 
        {
            directory = !string.IsNullOrEmpty(_config.Source) 
                ? Path.GetRelativePath(_config.Source, Path.GetDirectoryName(file.File.FullName) ?? string.Empty) 
                : string.Empty;
        }
        catch (ArgumentException)
        {
            directory = Path.GetDirectoryName(file.File.Name) ?? string.Empty;
        }
        
        destPath = destPath.Replace(PhotoCopy.Options.DestinationVariables.Directory, directory);

        var name = Path.GetFileNameWithoutExtension(file.File.Name);
        var ext = Path.GetExtension(file.File.Name);

        destPath = destPath.Replace(PhotoCopy.Options.DestinationVariables.Name, name);
        destPath = destPath.Replace(PhotoCopy.Options.DestinationVariables.NameNoExtension, name);
        destPath = destPath.Replace(PhotoCopy.Options.DestinationVariables.Extension, ext);
        destPath = destPath.Replace("{filename}", file.File.Name);

        return destPath;
    }
}
