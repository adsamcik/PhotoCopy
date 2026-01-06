using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PhotoCopy.Rollback;

namespace PhotoCopy.Commands;

/// <summary>
/// Command for rolling back previous copy/move operations.
/// </summary>
public class RollbackCommand : ICommand
{
    private readonly ILogger<RollbackCommand> _logger;
    private readonly IRollbackService _rollbackService;
    private readonly string? _transactionLogPath;
    private readonly string? _logDirectory;
    private readonly bool _listLogs;

    public RollbackCommand(
        ILogger<RollbackCommand> logger,
        IRollbackService rollbackService,
        string? transactionLogPath = null,
        string? logDirectory = null,
        bool listLogs = false)
    {
        _logger = logger;
        _rollbackService = rollbackService;
        _transactionLogPath = transactionLogPath;
        _logDirectory = logDirectory;
        _listLogs = listLogs;
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
                return 1;
            }

            if (!File.Exists(_transactionLogPath))
            {
                _logger.LogError("Transaction log not found: {Path}", _transactionLogPath);
                return 1;
            }

            Console.WriteLine($"About to rollback transaction from: {_transactionLogPath}");
            Console.WriteLine("This will undo the file operations recorded in this log.");
            Console.Write("Are you sure? (yes/no): ");

            var response = Console.ReadLine();
            if (!string.Equals(response, "yes", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Rollback cancelled by user.");
                return 0;
            }

            var result = await _rollbackService.RollbackAsync(_transactionLogPath, cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation(
                    "Rollback completed successfully: {Files} files restored, {Dirs} directories removed",
                    result.FilesRestored, result.DirectoriesRemoved);
                return 0;
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

                return 1;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Rollback cancelled.");
            return 2;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback failed");
            return 1;
        }
    }

    private int ListTransactionLogs()
    {
        var directory = _logDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), ".photocopy-logs");

        if (!Directory.Exists(directory))
        {
            _logger.LogInformation("No transaction logs found in {Directory}", directory);
            return 0;
        }

        var logs = _rollbackService.ListTransactionLogs(directory).ToList();

        if (logs.Count == 0)
        {
            _logger.LogInformation("No transaction logs found in {Directory}", directory);
            return 0;
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

        return 0;
    }
}
