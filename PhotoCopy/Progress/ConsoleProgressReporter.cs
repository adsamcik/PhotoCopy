using System;
using Microsoft.Extensions.Logging;
using PhotoCopy.Extensions;

namespace PhotoCopy.Progress;

/// <summary>
/// A progress reporter that logs progress to the console.
/// </summary>
public class ConsoleProgressReporter : IProgressReporter
{
    private readonly ILogger _logger;
    private readonly bool _verbose;
    private int _lastReportedPercent = -1;

    public ConsoleProgressReporter(ILogger logger, bool verbose = false)
    {
        _logger = logger;
        _verbose = verbose;
    }

    public void Report(CopyProgress progress)
    {
        var currentPercent = (int)progress.PercentComplete;

        // In non-verbose mode, only report every 5%
        if (!_verbose && currentPercent == _lastReportedPercent)
        {
            return;
        }

        if (!_verbose && currentPercent % 5 != 0 && currentPercent != 100)
        {
            return;
        }

        _lastReportedPercent = currentPercent;

        var eta = progress.EstimatedTimeRemaining;
        var etaString = eta.HasValue ? FormatTimeSpan(eta.Value) : "calculating...";
        var rate = FormatBytes((long)progress.BytesPerSecond) + "/s";

        _logger.LogInformation(
            "Progress: {Percent}% ({Current}/{Total}) - {FileName} - ETA: {ETA} - {Rate}",
            currentPercent,
            progress.CurrentFile,
            progress.TotalFiles,
            TruncateFileName(progress.CurrentFileName, 30),
            etaString,
            rate);
    }

    public void Complete(CopyProgress finalProgress)
    {
        _logger.LogInformation(
            "Completed: {Total} files ({Bytes}) in {Time}",
            finalProgress.TotalFiles,
            FormatBytes(finalProgress.BytesProcessed),
            FormatTimeSpan(finalProgress.Elapsed));
    }

    public void ReportError(string fileName, Exception exception)
    {
        _logger.LogError(exception, "Error processing {FileName}", fileName);
    }

    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalHours >= 1)
        {
            return $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m";
        }

        if (timeSpan.TotalMinutes >= 1)
        {
            return $"{(int)timeSpan.TotalMinutes}m {timeSpan.Seconds}s";
        }

        return $"{timeSpan.Seconds}s";
    }

    private static string FormatBytes(long bytes) => ByteFormatter.FormatBytes(bytes);

    private static string TruncateFileName(string fileName, int maxLength)
    {
        if (string.IsNullOrEmpty(fileName) || fileName.Length <= maxLength)
        {
            return fileName;
        }

        return "..." + fileName.Substring(fileName.Length - maxLength + 3);
    }
}
