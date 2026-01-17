using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhotoCopy.Files;
using PhotoCopy.Progress;
using PhotoCopy.Validators;

namespace PhotoCopy.Directories;

/// <summary>
/// Async interface for directory copy operations.
/// </summary>
public interface IDirectoryCopierAsync
{
    /// <summary>
    /// Copies files asynchronously with progress reporting.
    /// </summary>
    /// <param name="validators">Validators to apply to files.</param>
    /// <param name="progressReporter">Progress reporter for tracking operation status.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The copy result summary.</returns>
    Task<CopyResult> CopyAsync(
        IReadOnlyCollection<IValidator> validators,
        IProgressReporter progressReporter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a copy plan without executing it.
    /// </summary>
    /// <param name="validators">Validators to apply to files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The planned copy operations.</returns>
    Task<CopyPlan> BuildPlanAsync(
        IReadOnlyCollection<IValidator> validators,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a copy operation.
/// </summary>
public sealed record CopyResult(
    int FilesProcessed,
    int FilesFailed,
    int FilesSkipped,
    long BytesProcessed,
    IReadOnlyList<CopyError> Errors,
    UnknownFilesReport? UnknownFilesReport = null);

/// <summary>
/// Represents an error that occurred during copying.
/// </summary>
public sealed record CopyError(IFile File, string DestinationPath, string ErrorMessage);
