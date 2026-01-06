using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy.Configuration;

namespace PhotoCopy.Rollback;

/// <summary>
/// Manages transaction logging for file operations.
/// </summary>
public class TransactionLogger : ITransactionLogger
{
    private readonly ILogger<TransactionLogger> _logger;
    private readonly PhotoCopyConfig _config;
    private readonly JsonSerializerOptions _jsonOptions;
    private TransactionLog? _currentTransaction;
    private string? _transactionLogPath;

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

        // Determine log file path - use destination directory if available, otherwise source
        var logDirectory = GetLogDirectory();
        Directory.CreateDirectory(logDirectory);
        _transactionLogPath = Path.Combine(logDirectory, $"photocopy-{transactionId}.json");

        _logger.LogDebug("Started transaction {TransactionId}, log at {LogPath}", 
            transactionId, _transactionLogPath);

        return transactionId;
    }

    public void LogOperation(string sourcePath, string destinationPath, OperationType operation, long fileSize, string? checksum = null)
    {
        if (_currentTransaction is null)
        {
            throw new InvalidOperationException("No transaction in progress. Call BeginTransaction first.");
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
    }

    public void LogDirectoryCreated(string directoryPath)
    {
        if (_currentTransaction is null)
        {
            throw new InvalidOperationException("No transaction in progress. Call BeginTransaction first.");
        }

        _currentTransaction.CreatedDirectories.Add(directoryPath);
    }

    public void CompleteTransaction()
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

    public void FailTransaction(string errorMessage)
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

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null || _transactionLogPath is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(_currentTransaction, _jsonOptions);
        await File.WriteAllTextAsync(_transactionLogPath, json, cancellationToken);

        _logger.LogDebug("Transaction log saved to {LogPath}", _transactionLogPath);
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
