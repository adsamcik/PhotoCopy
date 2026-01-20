using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhotoCopy.Checkpoint.Models;
using PhotoCopy.Configuration;

namespace PhotoCopy.Checkpoint;

/// <summary>
/// Interface for validating checkpoints before resume.
/// </summary>
public interface ICheckpointValidator
{
    /// <summary>
    /// Validate a checkpoint is suitable for resuming.
    /// </summary>
    /// <param name="checkpoint">Checkpoint state to validate.</param>
    /// <param name="config">Current configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Validation result.</returns>
    Task<ResumeValidation> ValidateAsync(
        CheckpointState checkpoint,
        PhotoCopyConfig config,
        CancellationToken ct = default);

    /// <summary>
    /// Compute a hash of configuration settings that affect destination paths.
    /// Used to detect incompatible configuration changes.
    /// </summary>
    /// <param name="config">Configuration to hash.</param>
    /// <returns>SHA256 hash of significant config values.</returns>
    byte[] ComputeConfigHash(PhotoCopyConfig config);

    /// <summary>
    /// Compute a hash of the file plan for determinism validation.
    /// </summary>
    /// <param name="files">Files in the plan (must be in deterministic order).</param>
    /// <returns>SHA256 hash of the file list.</returns>
    byte[] ComputePlanHash(IReadOnlyList<Files.IFile> files);
}
