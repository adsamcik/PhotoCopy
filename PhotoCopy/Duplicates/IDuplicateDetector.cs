using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhotoCopy.Files;

namespace PhotoCopy.Duplicates;

/// <summary>
/// Service for detecting duplicate files based on content checksums.
/// </summary>
public interface IDuplicateDetector
{
    /// <summary>
    /// Scans files and builds an index of duplicates based on checksums.
    /// </summary>
    /// <param name="files">Files to scan for duplicates.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing duplicate groups and unique files.</returns>
    Task<DuplicateScanResult> ScanForDuplicatesAsync(
        IEnumerable<IFile> files,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a file is a duplicate of any previously seen file.
    /// </summary>
    /// <param name="file">File to check.</param>
    /// <returns>The original file if this is a duplicate, null otherwise.</returns>
    IFile? FindDuplicateOf(IFile file);

    /// <summary>
    /// Registers a file in the duplicate index.
    /// </summary>
    /// <param name="file">File to register.</param>
    void RegisterFile(IFile file);

    /// <summary>
    /// Clears the duplicate index.
    /// </summary>
    void Clear();
}
