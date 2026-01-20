using System;
using System.Collections;
using System.Collections.Generic;

namespace PhotoCopy.Checkpoint.Models;

/// <summary>
/// In-memory checkpoint state optimized for scale.
/// Uses BitArray for O(1) lookup with O(n/8) memory.
/// </summary>
public sealed class CheckpointState
{
    /// <summary>Unique session identifier.</summary>
    public required string SessionId { get; init; }

    /// <summary>Schema version for forward compatibility.</summary>
    public int Version { get; init; } = 1;

    /// <summary>When this session started.</summary>
    public required DateTime StartedUtc { get; init; }

    /// <summary>Source directory (for validation).</summary>
    public required string SourceDirectory { get; init; }

    /// <summary>Destination pattern (for validation).</summary>
    public required string DestinationPattern { get; init; }

    /// <summary>
    /// Hash of configuration affecting paths.
    /// If mismatch on resume, checkpoint is invalid.
    /// </summary>
    public required byte[] ConfigHash { get; init; }

    /// <summary>
    /// Hash of the sorted file list.
    /// Ensures deterministic plan ordering for index-based tracking.
    /// </summary>
    public required byte[] PlanHash { get; init; }

    /// <summary>Total files in the original plan.</summary>
    public required int TotalFiles { get; init; }

    /// <summary>Total bytes planned.</summary>
    public required long TotalBytes { get; init; }

    /// <summary>
    /// Completion status per file index.
    /// Memory: 125 KB for 1M files (vs 160 MB with HashSet).
    /// </summary>
    public required BitArray Completed { get; init; }

    /// <summary>
    /// Files that completed the copy phase but not source deletion (Move mode).
    /// Sparse - only for Move operations where copy succeeded before crash.
    /// </summary>
    public HashSet<int> PendingSourceDeletion { get; init; } = new();

    /// <summary>Failed operations with error messages (typically &lt;1%).</summary>
    public Dictionary<int, string> Failed { get; init; } = new();

    /// <summary>Running statistics.</summary>
    public CheckpointStatistics Statistics { get; set; } = new();

    /// <summary>Path to the checkpoint file on disk.</summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Creates a new checkpoint state for a fresh operation.
    /// </summary>
    public static CheckpointState CreateNew(
        string sourceDirectory,
        string destinationPattern,
        int totalFiles,
        long totalBytes,
        byte[] configHash,
        byte[] planHash,
        DateTime startedUtc)
    {
        var sessionId = $"{startedUtc:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}";

        return new CheckpointState
        {
            SessionId = sessionId,
            Version = 1,
            StartedUtc = startedUtc,
            SourceDirectory = sourceDirectory,
            DestinationPattern = destinationPattern,
            ConfigHash = configHash,
            PlanHash = planHash,
            TotalFiles = totalFiles,
            TotalBytes = totalBytes,
            Completed = new BitArray(totalFiles),
            Statistics = new CheckpointStatistics { LastUpdatedUtc = startedUtc }
        };
    }

    /// <summary>
    /// Gets the count of completed files.
    /// </summary>
    public int CompletedCount
    {
        get
        {
            var count = 0;
            for (var i = 0; i < Completed.Length; i++)
            {
                if (Completed[i]) count++;
            }
            return count;
        }
    }

    /// <summary>
    /// Gets the count of remaining files to process.
    /// </summary>
    public int RemainingCount => TotalFiles - CompletedCount;

    /// <summary>
    /// Gets completion percentage.
    /// </summary>
    public double CompletionPercentage => TotalFiles > 0 ? (double)CompletedCount / TotalFiles * 100 : 0;
}

/// <summary>
/// Statistics tracked during checkpoint operation.
/// </summary>
public sealed class CheckpointStatistics
{
    public int FilesCompleted { get; set; }
    public int FilesFailed { get; set; }
    public int FilesSkipped { get; set; }
    public long BytesCompleted { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
}
