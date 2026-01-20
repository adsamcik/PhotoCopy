using System;
using System.Threading;
using System.Threading.Tasks;
using PhotoCopy.Checkpoint.Models;

namespace PhotoCopy.Checkpoint;

/// <summary>
/// Lock-free async writer for checkpoint progress.
/// Workers call RecordCompletion without blocking.
/// </summary>
public interface ICheckpointWriter : IAsyncDisposable
{
    /// <summary>
    /// Record a completed operation. Lock-free, non-blocking.
    /// </summary>
    /// <param name="fileIndex">Index of the file in the plan.</param>
    /// <param name="result">Result of the operation.</param>
    /// <param name="fileSize">Size of the file in bytes.</param>
    void RecordCompletion(int fileIndex, OperationResult result, long fileSize);

    /// <summary>
    /// Record a failure with error message.
    /// </summary>
    /// <param name="fileIndex">Index of the file in the plan.</param>
    /// <param name="fileSize">Size of the file in bytes.</param>
    /// <param name="errorMessage">Error message describing the failure.</param>
    void RecordFailure(int fileIndex, long fileSize, string errorMessage);

    /// <summary>
    /// Check if a file index is already completed (for resume filtering).
    /// Thread-safe.
    /// </summary>
    /// <param name="fileIndex">Index of the file in the plan.</param>
    /// <returns>True if the file has been completed.</returns>
    bool IsCompleted(int fileIndex);

    /// <summary>
    /// Get current statistics.
    /// </summary>
    /// <returns>Current checkpoint statistics.</returns>
    CheckpointStatistics GetStatistics();

    /// <summary>
    /// Flush all pending writes and update header.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task FlushAsync(CancellationToken ct = default);

    /// <summary>
    /// Mark the checkpoint as completed successfully.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task CompleteAsync(CancellationToken ct = default);

    /// <summary>
    /// Mark the checkpoint as failed.
    /// </summary>
    /// <param name="errorMessage">Error message.</param>
    /// <param name="ct">Cancellation token.</param>
    Task FailAsync(string errorMessage, CancellationToken ct = default);
}
