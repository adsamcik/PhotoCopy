using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PhotoCopy.Files;

namespace PhotoCopy.Progress;

/// <summary>
/// Represents an entry for a file that went to the Unknown folder.
/// </summary>
public sealed record UnknownFileEntry(
    string FilePath,
    string FileName,
    string Extension,
    UnknownFileReason Reason,
    string? AdditionalInfo = null);

/// <summary>
/// Summary of files that were placed in the Unknown folder.
/// </summary>
public sealed record UnknownFilesSummary(
    int TotalCount,
    IReadOnlyDictionary<UnknownFileReason, int> ByReason,
    IReadOnlyDictionary<string, int> ByExtension,
    IReadOnlyList<UnknownFileEntry> Files);

/// <summary>
/// Tracks and reports on files that were placed in the Unknown folder.
/// </summary>
public class UnknownFilesReport
{
    private readonly ConcurrentBag<UnknownFileEntry> _entries = new();

    /// <summary>
    /// Gets the number of tracked unknown files.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Adds a file entry to the report.
    /// </summary>
    /// <param name="filePath">The full path to the file.</param>
    /// <param name="reason">The reason the file went to Unknown.</param>
    /// <param name="additionalInfo">Optional additional information about the reason.</param>
    public void AddEntry(string filePath, UnknownFileReason reason, string? additionalInfo = null)
    {
        if (reason == UnknownFileReason.None)
        {
            return; // Don't track files that have location
        }

        var fileName = Path.GetFileName(filePath);
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension))
        {
            extension = "(no extension)";
        }

        _entries.Add(new UnknownFileEntry(filePath, fileName, extension, reason, additionalInfo));
    }

    /// <summary>
    /// Adds a file entry to the report using FileInfo.
    /// </summary>
    /// <param name="file">The file info.</param>
    /// <param name="reason">The reason the file went to Unknown.</param>
    /// <param name="additionalInfo">Optional additional information about the reason.</param>
    public void AddEntry(FileInfo file, UnknownFileReason reason, string? additionalInfo = null)
    {
        AddEntry(file.FullName, reason, additionalInfo);
    }

    /// <summary>
    /// Adds a file entry to the report using IFile.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="reason">The reason the file went to Unknown.</param>
    /// <param name="additionalInfo">Optional additional information about the reason.</param>
    public void AddEntry(IFile file, UnknownFileReason reason, string? additionalInfo = null)
    {
        AddEntry(file.File.FullName, reason, additionalInfo);
    }

    /// <summary>
    /// Clears all entries from the report.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
    }

    /// <summary>
    /// Gets all tracked entries.
    /// </summary>
    public IReadOnlyList<UnknownFileEntry> GetEntries()
    {
        return _entries.ToList();
    }

    /// <summary>
    /// Generates a summary of the unknown files.
    /// </summary>
    /// <param name="includeFiles">Whether to include the detailed file list in the summary.</param>
    /// <returns>A summary of the unknown files report.</returns>
    public UnknownFilesSummary GenerateSummary(bool includeFiles = false)
    {
        var entries = _entries.ToList();
        
        var byReason = entries
            .GroupBy(e => e.Reason)
            .ToDictionary(g => g.Key, g => g.Count());
        
        var byExtension = entries
            .GroupBy(e => e.Extension)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());

        return new UnknownFilesSummary(
            entries.Count,
            byReason,
            byExtension,
            includeFiles ? entries : Array.Empty<UnknownFileEntry>());
    }

    /// <summary>
    /// Generates a formatted report string.
    /// </summary>
    /// <param name="includeDetailedFileList">Whether to include individual file paths.</param>
    /// <param name="maxFilesToList">Maximum number of files to list in detail (default 100).</param>
    /// <returns>Formatted report string.</returns>
    public string GenerateReport(bool includeDetailedFileList = false, int maxFilesToList = 100)
    {
        var summary = GenerateSummary(includeDetailedFileList);
        
        if (summary.TotalCount == 0)
        {
            return "No files were placed in the Unknown folder.";
        }

        var sb = new StringBuilder();
        
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine("                    UNKNOWN FILES REPORT                        ");
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine($"Total files without location data: {summary.TotalCount}");
        sb.AppendLine();
        
        // Breakdown by reason
        sb.AppendLine("Breakdown by Reason:");
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        
        foreach (var reason in Enum.GetValues<UnknownFileReason>())
        {
            if (reason == UnknownFileReason.None) continue;
            
            if (summary.ByReason.TryGetValue(reason, out var count) && count > 0)
            {
                var reasonDescription = GetReasonDescription(reason);
                var percentage = (double)count / summary.TotalCount * 100;
                sb.AppendLine($"  {reasonDescription,-35} {count,6} ({percentage:F1}%)");
            }
        }
        sb.AppendLine();
        
        // Breakdown by extension
        sb.AppendLine("Breakdown by File Extension:");
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        
        foreach (var (extension, count) in summary.ByExtension.Take(15))
        {
            var percentage = (double)count / summary.TotalCount * 100;
            sb.AppendLine($"  {extension,-15} {count,6} ({percentage:F1}%)");
        }
        
        if (summary.ByExtension.Count > 15)
        {
            var remaining = summary.ByExtension.Skip(15).Sum(x => x.Value);
            sb.AppendLine($"  {"(other)",-15} {remaining,6}");
        }
        sb.AppendLine();
        
        // Detailed file list
        if (includeDetailedFileList && summary.Files.Count > 0)
        {
            sb.AppendLine("Detailed File List:");
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            
            var filesToShow = summary.Files.Take(maxFilesToList).ToList();
            foreach (var entry in filesToShow)
            {
                var reasonCode = GetReasonShortCode(entry.Reason);
                sb.AppendLine($"  [{reasonCode}] {entry.FileName}");
                if (!string.IsNullOrEmpty(entry.AdditionalInfo))
                {
                    sb.AppendLine($"        └── {entry.AdditionalInfo}");
                }
            }
            
            if (summary.Files.Count > maxFilesToList)
            {
                sb.AppendLine($"  ... and {summary.Files.Count - maxFilesToList} more files");
            }
            sb.AppendLine();
        }
        
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        
        return sb.ToString();
    }

    private static string GetReasonDescription(UnknownFileReason reason) => reason switch
    {
        UnknownFileReason.NoGpsData => "No GPS data in file",
        UnknownFileReason.GpsExtractionError => "GPS extraction failed",
        UnknownFileReason.GeocodingFailed => "Geocoding failed",
        _ => reason.ToString()
    };

    private static string GetReasonShortCode(UnknownFileReason reason) => reason switch
    {
        UnknownFileReason.NoGpsData => "NO-GPS",
        UnknownFileReason.GpsExtractionError => "GPS-ERR",
        UnknownFileReason.GeocodingFailed => "GEO-FAIL",
        _ => "UNKNOWN"
    };
}
