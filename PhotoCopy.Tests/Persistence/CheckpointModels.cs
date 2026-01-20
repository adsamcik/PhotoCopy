using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoCopy.Tests.Persistence;

/// <summary>
/// Proposed checkpoint data structure for progress persistence.
/// This record captures all state needed to resume a copy operation.
/// </summary>
public record Checkpoint
{
    /// <summary>
    /// Current version of the checkpoint format.
    /// Increment when making breaking changes to enable migration.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Version of the checkpoint format for forward/backward compatibility.
    /// </summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>
    /// When this checkpoint was first created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// When this checkpoint was last updated.
    /// </summary>
    public DateTime LastUpdatedAt { get; init; }

    /// <summary>
    /// Source directory being processed.
    /// </summary>
    public string SourceDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Destination pattern for the copy operation.
    /// </summary>
    public string DestinationPattern { get; init; } = string.Empty;

    /// <summary>
    /// Current status of the operation.
    /// </summary>
    public CheckpointStatus Status { get; init; }

    /// <summary>
    /// If status is Failed, the reason for failure.
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// Snapshot of relevant configuration to detect incompatible changes.
    /// </summary>
    public ConfigurationSnapshot Config { get; init; } = new();

    /// <summary>
    /// List of files that have been successfully processed.
    /// </summary>
    public List<ProcessedFileEntry> ProcessedFiles { get; init; } = new();

    /// <summary>
    /// Aggregate statistics for progress reporting.
    /// </summary>
    public CheckpointStatistics Statistics { get; init; } = new();
}

/// <summary>
/// Status of a checkpointed operation.
/// </summary>
public enum CheckpointStatus
{
    /// <summary>Operation is in progress and can be resumed.</summary>
    InProgress,
    
    /// <summary>Operation completed successfully.</summary>
    Completed,
    
    /// <summary>Operation failed with an error.</summary>
    Failed,
    
    /// <summary>Operation was explicitly cancelled by user.</summary>
    Cancelled
}

/// <summary>
/// Record of a file that was successfully processed.
/// Contains enough information to verify file identity on resume.
/// </summary>
public record ProcessedFileEntry(
    string SourcePath,
    string DestinationPath,
    long FileSize,
    DateTime SourceLastModified,
    string? Checksum);

/// <summary>
/// Snapshot of configuration values that affect resume compatibility.
/// </summary>
public record ConfigurationSnapshot
{
    public string Mode { get; init; } = "Copy";
    public bool CalculateChecksums { get; init; }
    public string DuplicateHandling { get; init; } = "None";
    public string? DuplicatesFormat { get; init; }
}

/// <summary>
/// Aggregate statistics for checkpoint progress.
/// </summary>
public record CheckpointStatistics
{
    public int TotalFilesDiscovered { get; init; }
    public int FilesProcessed { get; init; }
    public int FilesSkipped { get; init; }
    public int FilesFailed { get; init; }
    public long BytesProcessed { get; init; }
}

/// <summary>
/// Builder for creating checkpoint test data with sensible defaults.
/// </summary>
public class CheckpointBuilder
{
    private Checkpoint _checkpoint;

    public CheckpointBuilder()
    {
        _checkpoint = new Checkpoint
        {
            Version = Checkpoint.CurrentVersion,
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
            SourceDirectory = @"C:\Source",
            DestinationPattern = @"C:\Dest\{year}\{name}{ext}",
            Status = CheckpointStatus.InProgress,
            Config = new ConfigurationSnapshot()
        };
    }

    private CheckpointBuilder(Checkpoint checkpoint)
    {
        _checkpoint = checkpoint;
    }

    public CheckpointBuilder WithVersion(int version)
    {
        return new CheckpointBuilder(_checkpoint with { Version = version });
    }

    public CheckpointBuilder WithSourceDirectory(string path)
    {
        return new CheckpointBuilder(_checkpoint with { SourceDirectory = path });
    }

    public CheckpointBuilder WithDestinationPattern(string pattern)
    {
        return new CheckpointBuilder(_checkpoint with { DestinationPattern = pattern });
    }

    public CheckpointBuilder WithStatus(CheckpointStatus status)
    {
        return new CheckpointBuilder(_checkpoint with { Status = status });
    }

    public CheckpointBuilder WithFailureReason(string reason)
    {
        return new CheckpointBuilder(_checkpoint with { FailureReason = reason, Status = CheckpointStatus.Failed });
    }

    public CheckpointBuilder WithProcessedFiles(int count)
    {
        var files = Enumerable.Range(0, count)
            .Select(i => new ProcessedFileEntry(
                $@"C:\Source\file{i}.jpg",
                $@"C:\Dest\2025\file{i}.jpg",
                1024 * (i + 1),
                DateTime.UtcNow.AddHours(-i),
                null))
            .ToList();

        return new CheckpointBuilder(_checkpoint with { ProcessedFiles = files });
    }

    public CheckpointBuilder WithProcessedFiles(IEnumerable<ProcessedFileEntry> files)
    {
        return new CheckpointBuilder(_checkpoint with { ProcessedFiles = files.ToList() });
    }

    public CheckpointBuilder WithStatistics(int total, int processed, int skipped, int failed, long bytes)
    {
        return new CheckpointBuilder(_checkpoint with
        {
            Statistics = new CheckpointStatistics
            {
                TotalFilesDiscovered = total,
                FilesProcessed = processed,
                FilesSkipped = skipped,
                FilesFailed = failed,
                BytesProcessed = bytes
            }
        });
    }

    public CheckpointBuilder WithCreatedAt(DateTime createdAt)
    {
        return new CheckpointBuilder(_checkpoint with { CreatedAt = createdAt });
    }

    public CheckpointBuilder WithLastUpdatedAt(DateTime lastUpdatedAt)
    {
        return new CheckpointBuilder(_checkpoint with { LastUpdatedAt = lastUpdatedAt });
    }

    public CheckpointBuilder Corrupted()
    {
        return new CheckpointBuilder(_checkpoint with { Version = -999 });
    }

    public CheckpointBuilder Stale(TimeSpan age)
    {
        var staleTime = DateTime.UtcNow.Subtract(age);
        return new CheckpointBuilder(_checkpoint with { CreatedAt = staleTime, LastUpdatedAt = staleTime });
    }

    public Checkpoint Build() => _checkpoint;
}

/// <summary>
/// Assertion extensions for checkpoint testing.
/// </summary>
public static class CheckpointAssertionExtensions
{
    public static void ShouldBeValidCheckpoint(this Checkpoint checkpoint)
    {
        if (checkpoint.Version <= 0)
            throw new Exception($"Checkpoint version should be positive, was {checkpoint.Version}");
        
        if (string.IsNullOrEmpty(checkpoint.SourceDirectory))
            throw new Exception("Checkpoint source directory should not be empty");
        
        if (checkpoint.CreatedAt > DateTime.UtcNow.AddMinutes(1))
            throw new Exception("Checkpoint creation time should not be in the future");
    }

    public static void ShouldBeResumableFrom(this Checkpoint checkpoint, int expectedProcessedCount)
    {
        if (checkpoint.Status != CheckpointStatus.InProgress)
            throw new Exception($"Checkpoint should be InProgress to resume, was {checkpoint.Status}");
        
        if (checkpoint.ProcessedFiles.Count != expectedProcessedCount)
            throw new Exception($"Expected {expectedProcessedCount} processed files, found {checkpoint.ProcessedFiles.Count}");
    }

    public static void ShouldHaveProcessedExactly(this Checkpoint checkpoint, IEnumerable<string> expectedSourcePaths)
    {
        var actualPaths = checkpoint.ProcessedFiles.Select(f => f.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedSet = expectedSourcePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = expectedSet.Except(actualPaths).ToList();
        var extra = actualPaths.Except(expectedSet).ToList();

        if (missing.Any() || extra.Any())
        {
            throw new Exception(
                $"Processed files mismatch. Missing: [{string.Join(", ", missing)}], Extra: [{string.Join(", ", extra)}]");
        }
    }
}
