using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Files;
using PhotoCopy.Progress;
using PhotoCopy.Validators;

namespace PhotoCopy.Directories;

/// <summary>
/// Async implementation of directory copier with parallel processing support.
/// </summary>
public class DirectoryCopierAsync : IDirectoryCopierAsync
{
    private readonly ILogger<DirectoryCopierAsync> _logger;
    private readonly IFileSystem _fileSystem;
    private readonly PhotoCopyConfig _config;

    public DirectoryCopierAsync(
        ILogger<DirectoryCopierAsync> logger,
        IFileSystem fileSystem,
        IOptions<PhotoCopyConfig> options)
    {
        _logger = logger;
        _fileSystem = fileSystem;
        _config = options.Value;
    }

    public async Task<CopyPlan> BuildPlanAsync(
        IReadOnlyCollection<IValidator> validators,
        CancellationToken cancellationToken = default)
    {
        var files = _fileSystem.EnumerateFiles(_config.Source).ToList();
        return await Task.Run(() => BuildCopyPlan(files, validators), cancellationToken);
    }

    public async Task<CopyResult> CopyAsync(
        IReadOnlyCollection<IValidator> validators,
        IProgressReporter progressReporter,
        CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(validators, cancellationToken);

        if (_config.DryRun)
        {
            LogDryRunSummary(plan);
            return new CopyResult(
                FilesProcessed: plan.Operations.Count,
                FilesFailed: 0,
                FilesSkipped: plan.SkippedFiles.Count,
                BytesProcessed: plan.TotalBytes,
                Errors: Array.Empty<CopyError>());
        }

        return await ExecutePlanAsync(plan, progressReporter, cancellationToken);
    }

    private CopyPlan BuildCopyPlan(IReadOnlyList<IFile> files, IReadOnlyCollection<IValidator> validators)
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
                _logger.LogDebug("File {File} skipped by validator {Validator}: {Reason}",
                    file.File.Name, failure.ValidatorName, failure.Reason);
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

    private async Task<CopyResult> ExecutePlanAsync(
        CopyPlan plan,
        IProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        var errors = new ConcurrentBag<CopyError>();
        var stopwatch = Stopwatch.StartNew();
        var processedCount = 0;
        var processedBytes = 0L;
        var totalOperations = plan.Operations.Count + plan.Operations.Sum(o => o.RelatedFiles.Count);

        // Pre-create directories
        foreach (var directory in plan.DirectoriesToCreate)
        {
            if (!_fileSystem.DirectoryExists(directory))
            {
                _fileSystem.CreateDirectory(directory);
            }
        }

        var parallelism = _config.Parallelism > 0
            ? _config.Parallelism
            : Environment.ProcessorCount;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = parallelism,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(
            plan.Operations,
            parallelOptions,
            async (operation, ct) =>
            {
                try
                {
                    await ProcessOperationAsync(operation.File, operation.DestinationPath, ct);
                    var fileSize = SafeFileLength(operation.File);
                    Interlocked.Add(ref processedBytes, fileSize);
                    var current = Interlocked.Increment(ref processedCount);

                    progressReporter.Report(new CopyProgress(
                        current,
                        totalOperations,
                        Interlocked.Read(ref processedBytes),
                        plan.TotalBytes,
                        operation.File.File.Name,
                        stopwatch.Elapsed));

                    // Process related files sequentially within this operation
                    foreach (var related in operation.RelatedFiles)
                    {
                        await ProcessOperationAsync(related.File, related.DestinationPath, ct);
                        var relatedSize = SafeFileLength(related.File);
                        Interlocked.Add(ref processedBytes, relatedSize);
                        current = Interlocked.Increment(ref processedCount);

                        progressReporter.Report(new CopyProgress(
                            current,
                            totalOperations,
                            Interlocked.Read(ref processedBytes),
                            plan.TotalBytes,
                            related.File.File.Name,
                            stopwatch.Elapsed));
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errors.Add(new CopyError(operation.File, operation.DestinationPath, ex.Message));
                    progressReporter.ReportError(operation.File.File.Name, ex);
                }
            });

        stopwatch.Stop();

        var finalProgress = new CopyProgress(
            processedCount,
            totalOperations,
            processedBytes,
            plan.TotalBytes,
            string.Empty,
            stopwatch.Elapsed);

        progressReporter.Complete(finalProgress);

        // Log skipped files
        foreach (var failure in plan.SkippedFiles)
        {
            _logger.LogInformation(
                "Skipped {File} due to validator {Validator}: {Reason}",
                failure.File.File.Name,
                failure.ValidatorName,
                failure.Reason ?? "No reason provided");
        }

        return new CopyResult(
            FilesProcessed: processedCount,
            FilesFailed: errors.Count,
            FilesSkipped: plan.SkippedFiles.Count,
            BytesProcessed: processedBytes,
            Errors: errors.ToList());
    }

    private Task ProcessOperationAsync(IFile file, string destinationPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Ensure directory exists
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory) && !_fileSystem.DirectoryExists(directory))
        {
            _fileSystem.CreateDirectory(directory);
        }

        if (_config.Mode == OperationMode.Move)
        {
            _fileSystem.MoveFile(file.File.FullName, destinationPath);
            _logger.LogDebug("Moved {Source} to {Destination}", file.File.FullName, destinationPath);
        }
        else
        {
            _fileSystem.CopyFile(file.File.FullName, destinationPath, true);
            _logger.LogDebug("Copied {Source} to {Destination}", file.File.FullName, destinationPath);
        }

        return Task.CompletedTask;
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

        _logger.LogInformation("DryRun Summary: {Primary} primary files, {Related} related files", primaryCount, relatedCount);
        _logger.LogInformation("DryRun Summary: {Directories} directories to create", directories);
        _logger.LogInformation("DryRun Summary: Total bytes {Bytes} ({Readable})", plan.TotalBytes, FormatBytes(plan.TotalBytes));

        if (plan.SkippedFiles.Count > 0)
        {
            _logger.LogInformation("DryRun Summary: {Skipped} files skipped by validators", plan.SkippedFiles.Count);
        }

        // Log each planned operation
        foreach (var op in plan.Operations)
        {
            _logger.LogInformation("DryRun: {Mode} {Source} to {Destination}",
                _config.Mode, op.File.File.FullName, op.DestinationPath);
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";

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
            _logger.LogDebug("Skipping existing file: {DestinationPath}", destinationPath);
            return null;
        }

        if (_config.Overwrite)
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
            var suffixFormat = _config.DuplicatesFormat.Replace("{number}", counter.ToString());
            var newFilename = $"{filenameWithoutExt}{suffixFormat}{extension}";
            newPath = Path.Combine(directory ?? string.Empty, newFilename);
            counter++;
        } while (_fileSystem.FileExists(newPath));

        return newPath;
    }

    public string GeneratePath(IFile file)
    {
        var destPath = _config.Destination ?? string.Empty;

        destPath = destPath.Replace(DestinationVariables.Year, file.FileDateTime.DateTime.Year.ToString());
        destPath = destPath.Replace(DestinationVariables.Month, file.FileDateTime.DateTime.Month.ToString("00"));
        destPath = destPath.Replace(DestinationVariables.Day, file.FileDateTime.DateTime.Day.ToString("00"));

        if (file.Location != null)
        {
            destPath = destPath.Replace(DestinationVariables.City, file.Location.City ?? "Unknown");
            destPath = destPath.Replace(DestinationVariables.State, file.Location.State ?? "Unknown");
            destPath = destPath.Replace(DestinationVariables.Country, file.Location.Country ?? "Unknown");
        }
        else
        {
            destPath = destPath.Replace(DestinationVariables.City, "Unknown");
            destPath = destPath.Replace(DestinationVariables.State, "Unknown");
            destPath = destPath.Replace(DestinationVariables.Country, "Unknown");
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

        destPath = destPath.Replace(DestinationVariables.Directory, directory);

        var name = Path.GetFileNameWithoutExtension(file.File.Name);
        var ext = Path.GetExtension(file.File.Name);

        destPath = destPath.Replace(DestinationVariables.Name, name);
        destPath = destPath.Replace(DestinationVariables.NameNoExtension, name);
        destPath = destPath.Replace(DestinationVariables.Extension, ext);
        destPath = destPath.Replace("{filename}", file.File.Name);

        return destPath;
    }
}
