using System;
using System.Collections.Generic;

namespace PhotoCopy.Checkpoint.Models;

/// <summary>
/// Result of validating a checkpoint for resume.
/// </summary>
public sealed record ResumeValidation
{
    /// <summary>Whether the checkpoint is valid for resume.</summary>
    public required bool IsValid { get; init; }

    /// <summary>Reason the checkpoint is invalid (if IsValid is false).</summary>
    public string? InvalidReason { get; init; }

    /// <summary>Total operations in the checkpoint.</summary>
    public required int TotalOperations { get; init; }

    /// <summary>Operations already completed.</summary>
    public required int CompletedOperations { get; init; }

    /// <summary>Operations remaining to process.</summary>
    public int PendingOperations => TotalOperations - CompletedOperations;

    /// <summary>Completion percentage.</summary>
    public double CompletionPercentage => TotalOperations > 0 
        ? (double)CompletedOperations / TotalOperations * 100 
        : 0;

    /// <summary>Warnings about the checkpoint (non-fatal issues).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Files that have changed since the checkpoint was created.</summary>
    public IReadOnlyList<SourceChangeInfo> ChangedFiles { get; init; } = Array.Empty<SourceChangeInfo>();

    /// <summary>
    /// Creates a valid checkpoint validation result.
    /// </summary>
    public static ResumeValidation Valid(
        int totalOperations,
        int completedOperations,
        IReadOnlyList<string>? warnings = null,
        IReadOnlyList<SourceChangeInfo>? changedFiles = null)
    {
        return new ResumeValidation
        {
            IsValid = true,
            TotalOperations = totalOperations,
            CompletedOperations = completedOperations,
            Warnings = warnings ?? Array.Empty<string>(),
            ChangedFiles = changedFiles ?? Array.Empty<SourceChangeInfo>()
        };
    }

    /// <summary>
    /// Creates an invalid checkpoint validation result.
    /// </summary>
    public static ResumeValidation Invalid(string reason)
    {
        return new ResumeValidation
        {
            IsValid = false,
            InvalidReason = reason,
            TotalOperations = 0,
            CompletedOperations = 0
        };
    }
}

/// <summary>
/// Information about a source file that changed since checkpoint.
/// </summary>
public sealed record SourceChangeInfo(
    string SourcePath,
    SourceChangeType ChangeType,
    string Details);

/// <summary>
/// Type of change detected in a source file.
/// </summary>
public enum SourceChangeType
{
    /// <summary>File was deleted.</summary>
    Deleted,

    /// <summary>File was modified (timestamp changed).</summary>
    Modified,

    /// <summary>File size changed.</summary>
    SizeChanged
}
