using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhotoCopy.Rollback;

/// <summary>
/// Represents a single file operation that can be rolled back.
/// </summary>
public sealed record FileOperationEntry
{
    /// <summary>
    /// Original source path of the file.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// Destination path where the file was copied/moved to.
    /// </summary>
    public required string DestinationPath { get; init; }

    /// <summary>
    /// Type of operation performed.
    /// </summary>
    public required OperationType Operation { get; init; }

    /// <summary>
    /// Timestamp when the operation was performed.
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// SHA256 checksum of the file (if calculated).
    /// </summary>
    public string? Checksum { get; init; }
}

/// <summary>
/// Type of file operation.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OperationType
{
    Copy,
    Move
}

/// <summary>
/// Represents a complete transaction log for a copy/move operation.
/// </summary>
public sealed class TransactionLog
{
    /// <summary>
    /// Unique identifier for this transaction.
    /// </summary>
    public required string TransactionId { get; init; }

    /// <summary>
    /// When the transaction started.
    /// </summary>
    public required DateTime StartTime { get; init; }

    /// <summary>
    /// When the transaction completed (null if still in progress or failed).
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Source directory for the operation.
    /// </summary>
    public required string SourceDirectory { get; init; }

    /// <summary>
    /// Destination pattern used.
    /// </summary>
    public required string DestinationPattern { get; init; }

    /// <summary>
    /// Whether this was a dry run (no actual files moved/copied).
    /// </summary>
    public bool IsDryRun { get; init; }

    /// <summary>
    /// Status of the transaction.
    /// </summary>
    public TransactionStatus Status { get; set; } = TransactionStatus.InProgress;

    /// <summary>
    /// Error message if the transaction failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// List of file operations performed.
    /// </summary>
    public List<FileOperationEntry> Operations { get; init; } = new();

    /// <summary>
    /// Directories created during this transaction.
    /// </summary>
    public List<string> CreatedDirectories { get; init; } = new();
}

/// <summary>
/// Status of a transaction.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TransactionStatus
{
    InProgress,
    Completed,
    Failed,
    RolledBack
}
