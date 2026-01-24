using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Files;
using PhotoCopy.Rollback;
using PhotoCopy.Statistics;
using PhotoCopy.Validators;

namespace PhotoCopy.Directories;

public class DirectoryCopier : DirectoryCopierBase, IDirectoryCopier
{
    private readonly ILogger<DirectoryCopier> _logger;

    protected override ILogger Logger => _logger;

    public DirectoryCopier(
        ILogger<DirectoryCopier> logger,
        IFileSystem fileSystem,
        IOptions<PhotoCopyConfig> options,
        ITransactionLogger transactionLogger,
        IFileValidationService fileValidationService)
        : base(fileSystem, options, transactionLogger, fileValidationService)
    {
        _logger = logger;
    }

    public CopyResult Copy(IReadOnlyCollection<IValidator> validators)
    {
        ClearUnknownFilesReport();
        var statistics = new CopyStatistics();
        var files = FileSystem.EnumerateFiles(Config.Source);
        var plan = BuildCopyPlan(files, validators, statistics);

        if (Config.DryRun)
        {
            LogDryRunSummary(plan);
            return new CopyResult(
                plan.Operations.Count,
                0,
                plan.SkippedFiles.Count,
                plan.TotalBytes,
                Array.Empty<CopyError>(),
                GetUnknownFilesReport(),
                statistics.CreateSnapshot());
        }

        // Begin transaction logging if rollback is enabled
        if (Config.EnableRollback)
        {
            TransactionLogger.BeginTransaction(Config.Source, Config.Destination, Config.DryRun);
        }

        try
        {
            var (processed, failed, bytesProcessed, errors) = ExecutePlan(plan, statistics);
            
            if (Config.EnableRollback)
            {
                if (failed == 0)
                {
                    TransactionLogger.CompleteTransaction();
                }
                else
                {
                    TransactionLogger.FailTransaction($"{failed} file(s) failed to process");
                }
                
                TransactionLogger.Save();
            }
            
            // Record errors in statistics
            statistics.RecordErrors(failed);
            
            return new CopyResult(
                processed,
                failed,
                plan.SkippedFiles.Count,
                bytesProcessed,
                errors,
                GetUnknownFilesReport(),
                statistics.CreateSnapshot());
        }
        catch (Exception ex)
        {
            if (Config.EnableRollback)
            {
                TransactionLogger.FailTransaction(ex.Message);
                TransactionLogger.Save();
            }
            throw;
        }
    }

    private CopyPlan BuildCopyPlan(IEnumerable<IFile> files, IReadOnlyCollection<IValidator> validators, CopyStatistics statistics)
    {
        var operations = new List<FileCopyPlan>();
        var skipped = new List<ValidationFailure>();
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        var filesProcessed = 0;
        var lastLogTime = DateTime.UtcNow;

        _logger.LogInformation("Building copy plan...");
        
        foreach (var file in files)
        {
            filesProcessed++;
            
            // Track files that will go to Unknown folder
            TrackUnknownFile(file);
            
            // Log progress every 5 seconds
            var now = DateTime.UtcNow;
            if ((now - lastLogTime).TotalSeconds >= 5)
            {
                _logger.LogInformation("Plan building progress: {FilesProcessed} files scanned, {Operations} operations planned, {Skipped} skipped...", 
                    filesProcessed, operations.Count, skipped.Count);
                lastLogTime = now;
            }
            
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
                // Track as existing skip for statistics
                statistics.RecordExistingSkipped();
                continue;
            }

            RegisterDirectory(resolvedPath, directories);
            totalBytes += SafeFileLength(file);

            // Record file stats for statistics tracking
            statistics.RecordFileStats(file);

            var relatedPlans = BuildRelatedPlans(file, resolvedPath, directories, ref totalBytes);
            operations.Add(new FileCopyPlan(file, resolvedPath, relatedPlans));
        }

        _logger.LogInformation("Copy plan complete: {FilesProcessed} files scanned, {Operations} operations planned, {Skipped} skipped.", 
            filesProcessed, operations.Count, skipped.Count);
            
        return new CopyPlan(operations, skipped, directories, totalBytes);
    }

    private (int Processed, int Failed, long BytesProcessed, List<CopyError> Errors) ExecutePlan(CopyPlan plan, CopyStatistics statistics)
    {
        var processed = 0;
        var failed = 0;
        long bytesProcessed = 0;
        var errors = new List<CopyError>();
        
        foreach (var operation in plan.Operations)
        {
            try
            {
                EnsureDestinationDirectory(operation.DestinationPath);
                PerformOperation(operation.File, operation.DestinationPath);
                var fileSize = SafeFileLength(operation.File);
                processed++;
                bytesProcessed += fileSize;
                
                // Record successful file processing in statistics
                statistics.RecordFileProcessed(operation.File, fileSize);

                foreach (var related in operation.RelatedFiles)
                {
                    try
                    {
                        EnsureDestinationDirectory(related.DestinationPath);
                        PerformOperation(related.File, related.DestinationPath);
                        var relatedSize = SafeFileLength(related.File);
                        processed++;
                        bytesProcessed += relatedSize;
                        
                        // Record related file in statistics
                        statistics.RecordFileProcessed(related.File, relatedSize);
                    }
                    catch (Exception ex) when (IsExpectedFileOperationException(ex))
                    {
                        failed++;
                        errors.Add(new CopyError(related.File, related.DestinationPath, ex.Message));
                        _logger.LogError(ex, "Failed to process related file {File}", related.File.File.FullName);
                    }
                }
            }
            catch (Exception ex) when (IsExpectedFileOperationException(ex))
            {
                failed++;
                errors.Add(new CopyError(operation.File, operation.DestinationPath, ex.Message));
                _logger.LogError(ex, "Failed to process file {File}", operation.File.File.FullName);
            }
        }

        LogSkippedFiles(plan.SkippedFiles);
        
        return (processed, failed, bytesProcessed, errors);
    }

    private void PerformOperation(IFile file, string destinationPath)
    {
        var operationType = GetOperationType();
        var fileSize = SafeFileLength(file);

        if (Config.Mode == OperationMode.Move)
        {
            if (Config.DryRun)
            {
                _logger.LogInformation("DryRun: Move {Source} to {Destination}", file.File.FullName, destinationPath);
            }
            else
            {
                FileSystem.MoveFile(file.File.FullName, destinationPath);
                _logger.LogInformation("Moved {Source} to {Destination}", file.File.FullName, destinationPath);
                if (Config.EnableRollback)
                {
                    TransactionLogger.LogOperation(file.File.FullName, destinationPath, operationType, fileSize);
                }
            }
        }
        else
        {
            if (Config.DryRun)
            {
                _logger.LogInformation("DryRun: Copy {Source} to {Destination}", file.File.FullName, destinationPath);
            }
            else
            {
                FileSystem.CopyFile(file.File.FullName, destinationPath, true);
                _logger.LogInformation("Copied {Source} to {Destination}", file.File.FullName, destinationPath);
                if (Config.EnableRollback)
                {
                    TransactionLogger.LogOperation(file.File.FullName, destinationPath, operationType, fileSize);
                }
            }
        }
    }

    private void EnsureDestinationDirectory(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(directory) || FileSystem.DirectoryExists(directory))
        {
            return;
        }

        if (Config.DryRun)
        {
            _logger.LogInformation("DryRun: Create directory {Directory}", directory);
        }
        else
        {
            FileSystem.CreateDirectory(directory);
            if (Config.EnableRollback)
            {
                TransactionLogger.LogDirectoryCreated(directory);
            }
        }
    }

    public override string? ResolveDuplicate(string destinationPath)
    {
        if (!FileSystem.FileExists(destinationPath))
        {
            return destinationPath;
        }

        if (Config.SkipExisting)
        {
            _logger.LogInformation("Skipping existing file: {DestinationPath}", destinationPath);
            return null;
        }

        if (Config.Overwrite)
        {
            _logger.LogInformation("Overwriting existing file: {DestinationPath}", destinationPath);
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
        } while (FileSystem.FileExists(newPath));
        
        _logger.LogInformation("Using new path for duplicate: {NewPath}", newPath);
        return newPath;
    }

    /// <summary>
    /// Determines if an exception is an expected file operation exception that should be caught
    /// and recorded as a file failure, rather than allowed to propagate as a fatal error.
    /// </summary>
    private static bool IsExpectedFileOperationException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or PathTooLongException
            or DirectoryNotFoundException
            or System.Security.SecurityException;
    }
}
