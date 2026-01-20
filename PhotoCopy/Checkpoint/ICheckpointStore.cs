using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhotoCopy.Checkpoint.Models;

namespace PhotoCopy.Checkpoint;

/// <summary>
/// Interface for checkpoint persistence operations.
/// </summary>
public interface ICheckpointStore
{
    /// <summary>
    /// Find the most recent valid checkpoint for given source/dest.
    /// </summary>
    /// <param name="sourceDirectory">Source directory path.</param>
    /// <param name="destinationPattern">Destination pattern.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The checkpoint state if found, null otherwise.</returns>
    Task<CheckpointState?> FindLatestAsync(
        string sourceDirectory,
        string destinationPattern,
        CancellationToken ct = default);

    /// <summary>
    /// Load a specific checkpoint file.
    /// </summary>
    /// <param name="path">Path to the checkpoint file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The checkpoint state if valid, null otherwise.</returns>
    Task<CheckpointState?> LoadAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Create a new checkpoint file and return a writer for it.
    /// </summary>
    /// <param name="state">Initial checkpoint state.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A writer for the checkpoint file.</returns>
    Task<ICheckpointWriter> CreateWriterAsync(CheckpointState state, CancellationToken ct = default);

    /// <summary>
    /// Create a writer that resumes from an existing checkpoint.
    /// </summary>
    /// <param name="state">Existing checkpoint state to resume.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A writer for the checkpoint file.</returns>
    Task<ICheckpointWriter> ResumeWriterAsync(CheckpointState state, CancellationToken ct = default);

    /// <summary>
    /// Get the checkpoint directory path for a destination.
    /// </summary>
    /// <param name="destinationPattern">Destination pattern.</param>
    /// <returns>The checkpoint directory path.</returns>
    string GetCheckpointDirectory(string destinationPattern);

    /// <summary>
    /// List all checkpoint files.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Enumerable of checkpoint information.</returns>
    IAsyncEnumerable<CheckpointInfo> ListCheckpointsAsync(CancellationToken ct = default);

    /// <summary>
    /// Delete old/completed checkpoints.
    /// </summary>
    /// <param name="maxAge">Maximum age of checkpoints to keep.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CleanupAsync(TimeSpan maxAge, CancellationToken ct = default);

    /// <summary>
    /// Delete a specific checkpoint file.
    /// </summary>
    /// <param name="path">Path to the checkpoint file.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(string path, CancellationToken ct = default);
}

/// <summary>
/// Information about a checkpoint file.
/// </summary>
public sealed record CheckpointInfo(
    string Path,
    string SessionId,
    string SourceDirectory,
    string DestinationPattern,
    DateTime StartedUtc,
    int TotalFiles,
    int CompletedFiles,
    CheckpointStatus Status)
{
    /// <summary>Completion percentage.</summary>
    public double CompletionPercentage => TotalFiles > 0 
        ? (double)CompletedFiles / TotalFiles * 100 
        : 0;
}
