using System;

namespace PhotoCopy.Progress;

/// <summary>
/// Represents progress information for file operations.
/// </summary>
public readonly record struct CopyProgress(
    int CurrentFile,
    int TotalFiles,
    long BytesProcessed,
    long TotalBytes,
    string CurrentFileName,
    TimeSpan Elapsed)
{
    /// <summary>
    /// Gets the percentage complete (0-100).
    /// </summary>
    public double PercentComplete => TotalFiles > 0 ? (double)CurrentFile / TotalFiles * 100 : 0;

    /// <summary>
    /// Gets the estimated time remaining based on current progress.
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining
    {
        get
        {
            if (CurrentFile == 0 || Elapsed == TimeSpan.Zero)
            {
                return null;
            }

            var averageTimePerFile = Elapsed / CurrentFile;
            var remainingFiles = TotalFiles - CurrentFile;
            return averageTimePerFile * remainingFiles;
        }
    }

    /// <summary>
    /// Gets the processing rate in bytes per second.
    /// </summary>
    public double BytesPerSecond => Elapsed.TotalSeconds > 0 ? BytesProcessed / Elapsed.TotalSeconds : 0;
}

/// <summary>
/// Interface for reporting progress during operations.
/// </summary>
public interface IProgressReporter
{
    /// <summary>
    /// Reports progress update.
    /// </summary>
    void Report(CopyProgress progress);

    /// <summary>
    /// Reports that the operation has completed.
    /// </summary>
    void Complete(CopyProgress finalProgress);

    /// <summary>
    /// Reports an error during processing.
    /// </summary>
    void ReportError(string fileName, Exception exception);
}
