using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PhotoCopy.Statistics;

/// <summary>
/// Formats and generates statistics reports for copy operations.
/// </summary>
public class StatisticsReporter
{
    private const char BoxHorizontal = '═';
    private const int DefaultWidth = 60;

    /// <summary>
    /// Generates a formatted summary report from copy statistics.
    /// </summary>
    /// <param name="statistics">The statistics to report on.</param>
    /// <returns>Formatted report string.</returns>
    public string GenerateReport(CopyStatistics statistics)
    {
        return GenerateReport(statistics.CreateSnapshot());
    }

    /// <summary>
    /// Generates a formatted summary report from a statistics snapshot.
    /// </summary>
    /// <param name="snapshot">The statistics snapshot to report on.</param>
    /// <returns>Formatted report string.</returns>
    public string GenerateReport(CopyStatisticsSnapshot snapshot)
    {
        var sb = new StringBuilder();

        // Top border
        sb.AppendLine(new string(BoxHorizontal, DefaultWidth));
        sb.AppendLine(CenterText("Copy Operation Summary", DefaultWidth));
        sb.AppendLine(new string(BoxHorizontal, DefaultWidth));

        // Files processed section
        sb.AppendLine();
        sb.AppendLine(FormatLine("Files processed:", FormatNumber(snapshot.TotalFiles)));

        if (snapshot.TotalFiles > 0)
        {
            var photoPercent = (double)snapshot.PhotosCount / snapshot.TotalFiles * 100;
            var videoPercent = (double)snapshot.VideosCount / snapshot.TotalFiles * 100;

            sb.AppendLine(FormatLine("  Photos:", $"{FormatNumber(snapshot.PhotosCount)} ({photoPercent:F1}%)"));
            sb.AppendLine(FormatLine("  Videos:", $"{FormatNumber(snapshot.VideosCount)} ({videoPercent:F1}%)"));
        }

        // Location data section
        sb.AppendLine();
        if (snapshot.TotalFiles > 0)
        {
            var locationPercent = (double)snapshot.FilesWithLocation / snapshot.TotalFiles * 100;
            sb.AppendLine(FormatLine("Location data:", $"{FormatNumber(snapshot.FilesWithLocation)} files ({locationPercent:F1}%)"));
        }
        else
        {
            sb.AppendLine(FormatLine("Location data:", "0 files"));
        }

        sb.AppendLine(FormatLine("  Countries:", FormatNumber(snapshot.UniqueCountriesCount)));
        sb.AppendLine(FormatLine("  Cities:", FormatNumber(snapshot.UniqueCitiesCount)));

        // Date range section
        sb.AppendLine();
        if (snapshot.EarliestDate.HasValue && snapshot.LatestDate.HasValue)
        {
            var dateRange = $"{snapshot.EarliestDate.Value:yyyy-MM-dd} to {snapshot.LatestDate.Value:yyyy-MM-dd}";
            sb.AppendLine(FormatLine("Date range:", dateRange));
        }
        else
        {
            sb.AppendLine(FormatLine("Date range:", "N/A"));
        }

        // Size section
        sb.AppendLine(FormatLine("Total size:", FormatBytes(snapshot.TotalBytesProcessed)));

        // Skipped/errors section
        sb.AppendLine();
        sb.AppendLine(FormatLine("Duplicates skipped:", FormatNumber(snapshot.DuplicatesSkipped)));
        sb.AppendLine(FormatLine("Already existing:", FormatNumber(snapshot.ExistingSkipped)));
        sb.AppendLine(FormatLine("Errors:", FormatNumber(snapshot.ErrorCount)));

        // Bottom border
        sb.AppendLine(new string(BoxHorizontal, DefaultWidth));

        return sb.ToString();
    }

    /// <summary>
    /// Generates a compact single-line summary suitable for logging.
    /// </summary>
    /// <param name="snapshot">The statistics snapshot to summarize.</param>
    /// <returns>Compact summary string.</returns>
    public string GenerateCompactSummary(CopyStatisticsSnapshot snapshot)
    {
        return $"Processed: {FormatNumber(snapshot.TotalFiles)} files ({FormatBytes(snapshot.TotalBytesProcessed)}), " +
               $"Photos: {FormatNumber(snapshot.PhotosCount)}, Videos: {FormatNumber(snapshot.VideosCount)}, " +
               $"Locations: {FormatNumber(snapshot.FilesWithLocation)}, " +
               $"Skipped: {FormatNumber(snapshot.DuplicatesSkipped + snapshot.ExistingSkipped)}, " +
               $"Errors: {FormatNumber(snapshot.ErrorCount)}";
    }

    /// <summary>
    /// Generates a detailed breakdown of file types.
    /// </summary>
    /// <param name="snapshot">The statistics snapshot.</param>
    /// <param name="maxTypes">Maximum number of types to show before summarizing.</param>
    /// <returns>File type breakdown string.</returns>
    public string GenerateFileTypeBreakdown(CopyStatisticsSnapshot snapshot, int maxTypes = 10)
    {
        if (snapshot.ExtensionBreakdown.Count == 0)
        {
            return "No files processed.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("File Type Breakdown:");
        sb.AppendLine(new string('-', 30));

        var sorted = snapshot.ExtensionBreakdown
            .OrderByDescending(kvp => kvp.Value)
            .ToList();

        var shown = sorted.Take(maxTypes);
        var remaining = sorted.Skip(maxTypes).ToList();

        foreach (var (ext, count) in shown)
        {
            var percent = (double)count / snapshot.TotalFiles * 100;
            sb.AppendLine($"  {ext,-10} {count,8:N0} ({percent,5:F1}%)");
        }

        if (remaining.Count > 0)
        {
            var otherCount = remaining.Sum(x => x.Value);
            var otherPercent = (double)otherCount / snapshot.TotalFiles * 100;
            sb.AppendLine($"  {"(other)",-10} {otherCount,8:N0} ({otherPercent,5:F1}%)");
        }

        return sb.ToString();
    }

    #region Formatting Helpers

    private static string FormatLine(string label, string value)
    {
        const int labelWidth = 20;
        return $"{label,-labelWidth} {value}";
    }

    private static string CenterText(string text, int width)
    {
        if (text.Length >= width)
        {
            return text;
        }

        var padding = (width - text.Length) / 2;
        return new string(' ', padding) + text;
    }

    /// <summary>
    /// Formats a number with thousand separators.
    /// </summary>
    public static string FormatNumber(int number)
    {
        return number.ToString("N0");
    }

    /// <summary>
    /// Formats a number with thousand separators.
    /// </summary>
    public static string FormatNumber(long number)
    {
        return number.ToString("N0");
    }

    /// <summary>
    /// Formats bytes into a human-readable string (KB, MB, GB, etc.).
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 0)
        {
            return "0 B";
        }

        string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };
        var order = 0;
        double size = bytes;

        while (size >= 1024 && order < suffixes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        // Format with appropriate decimal places
        if (order == 0)
        {
            return $"{size:N0} {suffixes[order]}";
        }
        
        return $"{size:N1} {suffixes[order]}";
    }

    #endregion
}
