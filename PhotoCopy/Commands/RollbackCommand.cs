using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PhotoCopy;
using PhotoCopy.Abstractions;
using PhotoCopy.Rollback;

namespace PhotoCopy.Commands;

/// <summary>
/// Command for rolling back previous copy/move operations.
/// </summary>
public class RollbackCommand : ICommand
{
    private readonly ILogger<RollbackCommand> _logger;
    private readonly IRollbackService _rollbackService;
    private readonly IFileSystem _fileSystem;
    private readonly string? _transactionLogPath;
    private readonly string? _logDirectory;
    private readonly bool _listLogs;
    private readonly bool _skipConfirmation;

    public RollbackCommand(
        ILogger<RollbackCommand> logger,
        IRollbackService rollbackService,
        IFileSystem fileSystem,
        string? transactionLogPath = null,
        string? logDirectory = null,
        bool listLogs = false,
        bool skipConfirmation = false)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(rollbackService);
        ArgumentNullException.ThrowIfNull(fileSystem);

        _logger = logger;
        _rollbackService = rollbackService;
        _fileSystem = fileSystem;
        _transactionLogPath = transactionLogPath;
        _logDirectory = logDirectory;
        _listLogs = listLogs;
        _skipConfirmation = skipConfirmation;
    }

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_listLogs)
            {
                return ListTransactionLogs();
            }

            if (string.IsNullOrEmpty(_transactionLogPath))
            {
                _logger.LogError("Transaction log path is required. Use --file to specify, or --list to see available logs.");
                return (int)ExitCode.InvalidArguments;
            }

            if (!_fileSystem.FileExists(_transactionLogPath))
            {
                _logger.LogError("Transaction log not found: {Path}", _transactionLogPath);
                return (int)ExitCode.IOError;
            }

            Console.WriteLine($"About to rollback transaction from: {_transactionLogPath}");
            Console.WriteLine("This will undo the file operations recorded in this log.");

            if (!_skipConfirmation)
            {
                Console.Write("Are you sure? (yes/no): ");

                var response = Console.ReadLine();
                if (!string.Equals(response, "yes", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(response) && Console.IsInputRedirected)
                    {
                        _logger.LogInformation("Rollback cancelled: no input provided in non-interactive mode. Use --yes to skip confirmation.");
                    }
                    else
                    {
                        _logger.LogInformation("Rollback cancelled by user.");
                    }
                    return (int)ExitCode.Success;
                }
            }

            var result = await _rollbackService.RollbackAsync(_transactionLogPath, cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation(
                    "Rollback completed successfully: {Files} files restored, {Dirs} directories removed",
                    result.FilesRestored, result.DirectoriesRemoved);
                return (int)ExitCode.Success;
            }
            else
            {
                _logger.LogWarning(
                    "Rollback completed with errors: {Restored} files restored, {Failed} failed",
                    result.FilesRestored, result.FilesFailed);

                foreach (var error in result.Errors)
                {
                    _logger.LogError("  {Error}", error);
                }

                // Some files were restored, some failed = partial success
                if (result.FilesRestored > 0)
                {
                    return (int)ExitCode.PartialSuccess;
                }
                
                return (int)ExitCode.Error;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Rollback cancelled.");
            return (int)ExitCode.Cancelled;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Rollback failed due to permission error");
            return (int)ExitCode.IOError;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Rollback failed due to I/O error");
            return (int)ExitCode.IOError;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback failed");
            return (int)ExitCode.Error;
        }
    }

    private int ListTransactionLogs()
    {
        var directory = _logDirectory ?? Path.Combine(_fileSystem.GetCurrentDirectory(), ".photocopy-logs");

        if (!_fileSystem.DirectoryExists(directory))
        {
            _logger.LogInformation("No transaction logs found in {Directory}", directory);
            return (int)ExitCode.Success;
        }

        var logs = _rollbackService.ListTransactionLogs(directory).ToList();

        if (logs.Count == 0)
        {
            _logger.LogInformation("No transaction logs found in {Directory}", directory);
            return (int)ExitCode.Success;
        }

        Console.WriteLine();
        Console.WriteLine($"Transaction logs in {directory}:");
        Console.WriteLine();
        Console.WriteLine($"{"Transaction ID",-30} {"Date",-20} {"Status",-12} {"Operations"}");
        Console.WriteLine(new string('-', 80));

        foreach (var log in logs.OrderByDescending(l => l.StartTime))
        {
            Console.WriteLine($"{log.TransactionId,-30} {log.StartTime:yyyy-MM-dd HH:mm,-20} {log.Status,-12} {log.OperationCount}");
        }

        Console.WriteLine();
        Console.WriteLine("Use 'photocopy rollback --file <path>' to rollback a transaction.");
        Console.WriteLine();

        return (int)ExitCode.Success;
    }
}
