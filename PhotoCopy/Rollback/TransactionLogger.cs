using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy.Configuration;
using PhotoCopy.Files;

namespace PhotoCopy.Rollback;

/// <summary>
/// Manages transaction logging for file operations.
/// </summary>
public class TransactionLogger : ITransactionLogger
{
    private readonly ILogger<TransactionLogger> _logger;
    private readonly PhotoCopyConfig _config;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _lock = new();
    private TransactionLog? _currentTransaction;
    private string? _transactionLogPath;
    private int _operationsSinceLastSave;
    
    /// <summary>
    /// Maximum number of operations per transaction log to prevent memory exhaustion.
    /// </summary>
    public const int MaxOperationsPerLog = 100000;
    
    /// <summary>
    /// Number of operations after which the log is automatically saved.
    /// Set to 0 to disable incremental saves.
    /// </summary>
    public int IncrementalSaveThreshold { get; set; } = 100;
    
    /// <summary>
    /// Gets whether the current transaction log is at capacity.
    /// </summary>
    public bool IsLogFull => _currentTransaction?.Operations.Count >= MaxOperationsPerLog;

    public TransactionLogger(ILogger<TransactionLogger> logger, IOptions<PhotoCopyConfig> config)
    {
        _logger = logger;
        _config = config.Value;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public TransactionLog? CurrentTransaction => _currentTransaction;
    public string? TransactionLogPath => _transactionLogPath;

    public string BeginTransaction(string sourceDirectory, string destinationPattern, bool isDryRun)
    {
        lock (_lock)
        {
            var transactionId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
            
            _currentTransaction = new TransactionLog
            {
                TransactionId = transactionId,
                StartTime = DateTime.UtcNow,
                SourceDirectory = sourceDirectory,
                DestinationPattern = destinationPattern,
                IsDryRun = isDryRun,
                Status = TransactionStatus.InProgress
            };

            // Reset operation counter
            _operationsSinceLastSave = 0;

            // Determine log file path - use destination directory if available, otherwise source
            var logDirectory = GetLogDirectory();
            RetryHelper.ExecuteWithRetry(
                () => Directory.CreateDirectory(logDirectory),
                _logger,
                $"CreateDirectory {logDirectory}");
            _transactionLogPath = Path.Combine(logDirectory, $"photocopy-{transactionId}.json");

            _logger.LogDebug("Started transaction {TransactionId}, log at {LogPath}", 
                transactionId, _transactionLogPath);

            // Save initial transaction state immediately (crash recovery)
            SaveInternal();

            return transactionId;
        }
    }

    public void LogOperation(string sourcePath, string destinationPath, OperationType operation, long fileSize, string? checksum = null)
    {
        lock (_lock)
        {
            if (_currentTransaction is null)
            {
                throw new InvalidOperationException("No transaction in progress. Call BeginTransaction first.");
            }

            // Check if log is at capacity
            if (_currentTransaction.Operations.Count >= MaxOperationsPerLog)
            {
                _logger.LogWarning(
                    "Transaction log at capacity ({MaxOperations} operations). Operation for {Source} -> {Destination} will not be logged.",
                    MaxOperationsPerLog, sourcePath, destinationPath);
                return;
            }

            _currentTransaction.Operations.Add(new FileOperationEntry
            {
                SourcePath = sourcePath,
                DestinationPath = destinationPath,
                Operation = operation,
                Timestamp = DateTime.UtcNow,
                FileSize = fileSize,
                Checksum = checksum
            });

            _operationsSinceLastSave++;

            // Incremental save for crash recovery
            if (IncrementalSaveThreshold > 0 && _operationsSinceLastSave >= IncrementalSaveThreshold)
            {
                SaveInternal();
                _operationsSinceLastSave = 0;
            }
        }
    }

    public void LogDirectoryCreated(string directoryPath)
    {
        lock (_lock)
        {
            if (_currentTransaction is null)
            {
                throw new InvalidOperationException("No transaction in progress. Call BeginTransaction first.");
            }

            _currentTransaction.CreatedDirectories.Add(directoryPath);
        }
    }

    public void CompleteTransaction()
    {
        lock (_lock)
        {
            if (_currentTransaction is null)
            {
                throw new InvalidOperationException("No transaction in progress.");
            }

            _currentTransaction.EndTime = DateTime.UtcNow;
            _currentTransaction.Status = TransactionStatus.Completed;

            _logger.LogInformation("Transaction {TransactionId} completed: {Count} operations",
                _currentTransaction.TransactionId, _currentTransaction.Operations.Count);
        }
    }

    public void FailTransaction(string errorMessage)
    {
        lock (_lock)
        {
            if (_currentTransaction is null)
            {
                throw new InvalidOperationException("No transaction in progress.");
            }

            _currentTransaction.EndTime = DateTime.UtcNow;
            _currentTransaction.Status = TransactionStatus.Failed;
            _currentTransaction.ErrorMessage = errorMessage;

            _logger.LogWarning("Transaction {TransactionId} failed: {Error}",
                _currentTransaction.TransactionId, errorMessage);
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        string? json;
        string? logPath;
        
        lock (_lock)
        {
            if (_currentTransaction is null || _transactionLogPath is null)
            {
                return;
            }

            json = JsonSerializer.Serialize(_currentTransaction, _jsonOptions);
            logPath = _transactionLogPath;
            _operationsSinceLastSave = 0;
        }
        
        // Use atomic write pattern: write to temp file, then move
        var tempPath = logPath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, json, cancellationToken);
            await RetryHelper.ExecuteWithRetryAsync(
                () => { File.Move(tempPath, logPath, overwrite: true); return Task.CompletedTask; },
                _logger,
                $"MoveFile {Path.GetFileName(logPath)}",
                cancellationToken: cancellationToken);
        }
        finally
        {
            // Clean up temp file if it exists
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* Ignore cleanup errors */ }
            }
        }

        _logger.LogDebug("Transaction log saved to {LogPath}", logPath);
    }

    public void Save()
    {
        string? json;
        string? logPath;
        
        lock (_lock)
        {
            if (_currentTransaction is null || _transactionLogPath is null)
            {
                return;
            }

            json = JsonSerializer.Serialize(_currentTransaction, _jsonOptions);
            logPath = _transactionLogPath;
            _operationsSinceLastSave = 0;
        }
        
        // Use atomic write pattern: write to temp file, then move
        var tempPath = logPath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, json);
            RetryHelper.ExecuteWithRetry(
                () => File.Move(tempPath, logPath, overwrite: true),
                _logger,
                $"MoveFile {Path.GetFileName(logPath)}");
        }
        finally
        {
            // Clean up temp file if it exists
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* Ignore cleanup errors */ }
            }
        }

        _logger.LogDebug("Transaction log saved to {LogPath}", logPath);
    }

    /// <summary>
    /// Internal save method for incremental saves. Does not acquire lock (caller must hold lock).
    /// </summary>
    private void SaveInternal()
    {
        if (_currentTransaction is null || _transactionLogPath is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(_currentTransaction, _jsonOptions);
        var logPath = _transactionLogPath;
        
        // Use atomic write pattern: write to temp file, then move
        var tempPath = logPath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, json);
            RetryHelper.ExecuteWithRetry(
                () => File.Move(tempPath, logPath, overwrite: true),
                _logger,
                $"MoveFile {Path.GetFileName(logPath)}");
        }
        catch (Exception ex)
        {
            // Log but don't throw - incremental save failures shouldn't stop the operation
            _logger.LogWarning(ex, "Failed to save incremental transaction log to {LogPath}", logPath);
            return;
        }
        finally
        {
            // Clean up temp file if it exists
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* Ignore cleanup errors */ }
            }
        }

        _logger.LogDebug("Transaction log incrementally saved to {LogPath}", logPath);
    }

    private string GetLogDirectory()
    {
        // Try to extract base destination directory from pattern
        var destPattern = _config.Destination;
        if (!string.IsNullOrEmpty(destPattern))
        {
            // Find the first variable in the pattern
            var variableIndex = destPattern.IndexOf('{');
            if (variableIndex > 0)
            {
                var basePath = destPattern[..variableIndex].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (Directory.Exists(basePath) || !string.IsNullOrEmpty(basePath))
                {
                    return Path.Combine(basePath, ".photocopy-logs");
                }
            }
        }

        // Fall back to source directory
        if (!string.IsNullOrEmpty(_config.Source))
        {
            return Path.Combine(_config.Source, ".photocopy-logs");
        }

        // Last resort: current directory
        return Path.Combine(Directory.GetCurrentDirectory(), ".photocopy-logs");
    }
}
