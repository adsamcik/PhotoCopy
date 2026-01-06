using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PhotoCopy.Rollback;

/// <summary>
/// Interface for rolling back file operations.
/// </summary>
public interface IRollbackService
{
    /// <summary>
    /// Rolls back a transaction by its log file path.
    /// </summary>
    Task<RollbackResult> RollbackAsync(string transactionLogPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists available transaction logs in a directory.
    /// </summary>
    IEnumerable<TransactionLogInfo> ListTransactionLogs(string directory);
}

/// <summary>
/// Summary information about a transaction log.
/// </summary>
public sealed record TransactionLogInfo(
    string FilePath,
    string TransactionId,
    DateTime StartTime,
    TransactionStatus Status,
    int OperationCount);

/// <summary>
/// Result of a rollback operation.
/// </summary>
public sealed record RollbackResult(
    bool Success,
    int FilesRestored,
    int FilesFailed,
    int DirectoriesRemoved,
    IReadOnlyList<string> Errors);

/// <summary>
/// Service for rolling back file operations using transaction logs.
/// </summary>
public class RollbackService : IRollbackService
{
    private readonly ILogger<RollbackService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public RollbackService(ILogger<RollbackService> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<RollbackResult> RollbackAsync(string transactionLogPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(transactionLogPath))
        {
            return new RollbackResult(false, 0, 0, 0, new[] { $"Transaction log not found: {transactionLogPath}" });
        }

        var json = await File.ReadAllTextAsync(transactionLogPath, cancellationToken);
        var log = JsonSerializer.Deserialize<TransactionLog>(json, _jsonOptions);

        if (log is null)
        {
            return new RollbackResult(false, 0, 0, 0, new[] { "Failed to parse transaction log" });
        }

        if (log.IsDryRun)
        {
            return new RollbackResult(false, 0, 0, 0, new[] { "Cannot rollback a dry run transaction" });
        }

        _logger.LogInformation("Rolling back transaction {TransactionId} with {Count} operations",
            log.TransactionId, log.Operations.Count);

        var errors = new List<string>();
        var filesRestored = 0;
        var filesFailed = 0;

        // Rollback file operations in reverse order
        foreach (var operation in log.Operations.AsEnumerable().Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (operation.Operation == OperationType.Move)
                {
                    // For move operations, move the file back
                    if (File.Exists(operation.DestinationPath))
                    {
                        var sourceDir = Path.GetDirectoryName(operation.SourcePath);
                        if (!string.IsNullOrEmpty(sourceDir) && !Directory.Exists(sourceDir))
                        {
                            Directory.CreateDirectory(sourceDir);
                        }

                        File.Move(operation.DestinationPath, operation.SourcePath);
                        filesRestored++;
                        _logger.LogDebug("Restored {Destination} to {Source}",
                            operation.DestinationPath, operation.SourcePath);
                    }
                    else
                    {
                        errors.Add($"Destination file not found: {operation.DestinationPath}");
                        filesFailed++;
                    }
                }
                else
                {
                    // For copy operations, delete the copied file
                    if (File.Exists(operation.DestinationPath))
                    {
                        File.Delete(operation.DestinationPath);
                        filesRestored++;
                        _logger.LogDebug("Deleted copied file {Destination}", operation.DestinationPath);
                    }
                    else
                    {
                        _logger.LogDebug("Copied file already removed: {Destination}", operation.DestinationPath);
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to rollback {operation.DestinationPath}: {ex.Message}");
                filesFailed++;
            }
        }

        // Remove created directories in reverse order (deepest first)
        var directoriesRemoved = 0;
        var sortedDirs = log.CreatedDirectories
            .OrderByDescending(d => d.Length)
            .ToList();

        foreach (var dir in sortedDirs)
        {
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                    directoriesRemoved++;
                    _logger.LogDebug("Removed empty directory {Directory}", dir);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to remove directory {dir}: {ex.Message}");
            }
        }

        // Update the transaction log status
        log.Status = TransactionStatus.RolledBack;
        var updatedJson = JsonSerializer.Serialize(log, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await File.WriteAllTextAsync(transactionLogPath, updatedJson, cancellationToken);

        var success = filesFailed == 0;
        _logger.LogInformation(
            "Rollback {Status}: {Restored} files restored, {Failed} failed, {Dirs} directories removed",
            success ? "completed" : "completed with errors",
            filesRestored, filesFailed, directoriesRemoved);

        return new RollbackResult(success, filesRestored, filesFailed, directoriesRemoved, errors);
    }

    public IEnumerable<TransactionLogInfo> ListTransactionLogs(string directory)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "photocopy-*.json"))
        {
            TransactionLogInfo? info = null;
            try
            {
                var json = File.ReadAllText(file);
                var log = JsonSerializer.Deserialize<TransactionLog>(json, _jsonOptions);
                if (log is not null)
                {
                    info = new TransactionLogInfo(
                        file,
                        log.TransactionId,
                        log.StartTime,
                        log.Status,
                        log.Operations.Count);
                }
            }
            catch
            {
                // Skip invalid log files
            }

            if (info is not null)
            {
                yield return info;
            }
        }
    }
}
