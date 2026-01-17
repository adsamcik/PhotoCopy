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
using PhotoCopy.Rollback;
using PhotoCopy.Validators;

namespace PhotoCopy.Directories;

/// <summary>
/// Async implementation of directory copier with parallel processing support.
/// </summary>
public class DirectoryCopierAsync : DirectoryCopierBase, IDirectoryCopierAsync
{
    private readonly ILogger<DirectoryCopierAsync> _logger;
    private readonly ConcurrentDictionary<string, byte> _reservedPaths = new(StringComparer.OrdinalIgnoreCase);

    protected override ILogger Logger => _logger;

    public DirectoryCopierAsync(
        ILogger<DirectoryCopierAsync> logger,
        IFileSystem fileSystem,
        IOptions<PhotoCopyConfig> options,
        ITransactionLogger transactionLogger,
        IFileValidationService fileValidationService)
        : base(fileSystem, options, transactionLogger, fileValidationService)
    {
        _logger = logger;
    }

    public async Task<CopyPlan> BuildPlanAsync(
        IReadOnlyCollection<IValidator> validators,
        CancellationToken cancellationToken = default)
    {
        ClearUnknownFilesReport();
        var files = FileSystem.EnumerateFiles(Config.Source).ToList();
        return await Task.Run(() => BuildCopyPlan(files, validators), cancellationToken);
    }

    public async Task<CopyResult> CopyAsync(
        IReadOnlyCollection<IValidator> validators,
        IProgressReporter progressReporter,
        CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(validators, cancellationToken);

        if (Config.DryRun)
        {
            LogDryRunSummary(plan);
            return new CopyResult(
                FilesProcessed: plan.Operations.Count,
                FilesFailed: 0,
                FilesSkipped: plan.SkippedFiles.Count,
                BytesProcessed: plan.TotalBytes,
                Errors: Array.Empty<CopyError>(),
                UnknownFilesReport: GetUnknownFilesReport());
        }

        // Begin transaction logging if rollback is enabled
        if (Config.EnableRollback)
        {
            TransactionLogger.BeginTransaction(Config.Source, Config.Destination, Config.DryRun);
        }

        try
        {
            var result = await ExecutePlanAsync(plan, progressReporter, cancellationToken);
            
            if (Config.EnableRollback)
            {
                if (result.FilesFailed == 0)
                {
                    TransactionLogger.CompleteTransaction();
                }
                else
                {
                    TransactionLogger.FailTransaction($"{result.FilesFailed} file(s) failed to process");
                }
                
                await TransactionLogger.SaveAsync(cancellationToken);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            if (Config.EnableRollback)
            {
                TransactionLogger.FailTransaction(ex.Message);
                await TransactionLogger.SaveAsync(cancellationToken);
            }
            throw;
        }
    }

    private CopyPlan BuildCopyPlan(IReadOnlyList<IFile> files, IReadOnlyCollection<IValidator> validators)
    {
        // Clear reserved paths from any previous operations
        _reservedPaths.Clear();
        
        var operations = new List<FileCopyPlan>();
        var skipped = new List<ValidationFailure>();
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;

        foreach (var file in files)
        {
            // Track files that will go to Unknown folder
            TrackUnknownFile(file);
            
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
            if (!FileSystem.DirectoryExists(directory))
            {
                FileSystem.CreateDirectory(directory);
                if (Config.EnableRollback)
                {
                    TransactionLogger.LogDirectoryCreated(directory);
                }
            }
        }

        var parallelism = Config.Parallelism > 0
            ? Config.Parallelism
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
                        try
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
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            // Attribute error to the related file, not the primary file
                            errors.Add(new CopyError(related.File, related.DestinationPath, ex.Message));
                            progressReporter.ReportError(related.File.File.Name, ex);
                        }
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

        LogSkippedFiles(plan.SkippedFiles);

        return new CopyResult(
            FilesProcessed: processedCount,
            FilesFailed: errors.Count,
            FilesSkipped: plan.SkippedFiles.Count,
            BytesProcessed: processedBytes,
            Errors: errors.ToList(),
            UnknownFilesReport: GetUnknownFilesReport());
    }

    private Task ProcessOperationAsync(IFile file, string destinationPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Ensure directory exists
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory) && !FileSystem.DirectoryExists(directory))
        {
            FileSystem.CreateDirectory(directory);
            if (Config.EnableRollback)
            {
                TransactionLogger.LogDirectoryCreated(directory);
            }
        }

        var operationType = GetOperationType();
        var fileSize = SafeFileLength(file);

        if (Config.Mode == OperationMode.Move)
        {
            FileSystem.MoveFile(file.File.FullName, destinationPath);
            _logger.LogDebug("Moved {Source} to {Destination}", file.File.FullName, destinationPath);
        }
        else
        {
            FileSystem.CopyFile(file.File.FullName, destinationPath, true);
            _logger.LogDebug("Copied {Source} to {Destination}", file.File.FullName, destinationPath);
        }

        if (Config.EnableRollback)
        {
            TransactionLogger.LogOperation(file.File.FullName, destinationPath, operationType, fileSize);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Thread-safe duplicate resolution with path reservation for parallel processing.
    /// </summary>
    public override string? ResolveDuplicate(string destinationPath)
    {
        // Check if path is available (not on disk and not reserved by another thread)
        if (!FileSystem.FileExists(destinationPath) && _reservedPaths.TryAdd(destinationPath, 0))
        {
            return destinationPath;
        }

        if (Config.SkipExisting)
        {
            _logger.LogDebug("Skipping existing file: {DestinationPath}", destinationPath);
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
        } while (FileSystem.FileExists(newPath) || !_reservedPaths.TryAdd(newPath, 0));

        return newPath;
    }
}

