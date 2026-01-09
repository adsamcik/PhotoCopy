using System.Threading;
using System.Threading.Tasks;
using PhotoCopy.Files;

namespace PhotoCopy.Rollback;

/// <summary>
/// Interface for managing transaction logs for file operations.
/// </summary>
public interface ITransactionLogger
{
    /// <summary>
    /// Starts a new transaction and returns its ID.
    /// </summary>
    /// <param name="sourceDirectory">Source directory for the operation.</param>
    /// <param name="destinationPattern">Destination pattern used.</param>
    /// <param name="isDryRun">Whether this is a dry run.</param>
    /// <returns>Transaction ID.</returns>
    string BeginTransaction(string sourceDirectory, string destinationPattern, bool isDryRun);

    /// <summary>
    /// Logs a file operation.
    /// </summary>
    /// <param name="sourcePath">Original source path.</param>
    /// <param name="destinationPath">Destination path.</param>
    /// <param name="operation">Type of operation.</param>
    /// <param name="fileSize">File size in bytes.</param>
    /// <param name="checksum">Optional checksum.</param>
    void LogOperation(string sourcePath, string destinationPath, OperationType operation, long fileSize, string? checksum = null);

    /// <summary>
    /// Logs a directory creation.
    /// </summary>
    /// <param name="directoryPath">Path of the created directory.</param>
    void LogDirectoryCreated(string directoryPath);

    /// <summary>
    /// Marks the transaction as completed successfully.
    /// </summary>
    void CompleteTransaction();

    /// <summary>
    /// Marks the transaction as failed.
    /// </summary>
    /// <param name="errorMessage">Error message.</param>
    void FailTransaction(string errorMessage);

    /// <summary>
    /// Saves the transaction log to disk.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the transaction log to disk synchronously.
    /// </summary>
    void Save();

    /// <summary>
    /// Gets the current transaction log.
    /// </summary>
    TransactionLog? CurrentTransaction { get; }

    /// <summary>
    /// Gets the path where the transaction log is saved.
    /// </summary>
    string? TransactionLogPath { get; }

    /// <summary>
    /// Gets or sets the number of operations after which the log is automatically saved.
    /// Set to 0 to disable incremental saves. Default is 100.
    /// </summary>
    int IncrementalSaveThreshold { get; set; }
}
