using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PhotoCopy.Files;

namespace PhotoCopy.Statistics;

/// <summary>
/// Thread-safe statistics collector for copy operations.
/// Tracks file counts, types, locations, and other metrics during copy.
/// </summary>
public class CopyStatistics
{
    // File type tracking
    private int _totalFiles;
    private int _photosCount;
    private int _videosCount;
    private readonly ConcurrentDictionary<string, int> _extensionCounts = new(StringComparer.OrdinalIgnoreCase);

    // Location tracking
    private int _filesWithLocation;
    private readonly ConcurrentDictionary<string, byte> _uniqueCountries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _uniqueCities = new(StringComparer.OrdinalIgnoreCase);

    // Bytes tracking
    private long _totalBytesProcessed;

    // Date range tracking
    private DateTime? _earliestDate;
    private DateTime? _latestDate;
    private readonly object _dateLock = new();

    // Skip/error tracking
    private int _duplicatesSkipped;
    private int _existingSkipped;
    private int _errorCount;

    // Known photo extensions (case-insensitive check)
    private static readonly HashSet<string> PhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".heic", ".gif", ".bmp", ".tiff", ".tif",
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".rw2", ".raf", ".pef", ".srw"
    };

    // Known video extensions
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".m4v", ".webm", ".flv", ".3gp", ".mts", ".m2ts"
    };

    #region Properties

    /// <summary>
    /// Total number of files processed.
    /// </summary>
    public int TotalFiles => _totalFiles;

    /// <summary>
    /// Number of photo files processed.
    /// </summary>
    public int PhotosCount => _photosCount;

    /// <summary>
    /// Number of video files processed.
    /// </summary>
    public int VideosCount => _videosCount;

    /// <summary>
    /// Number of files with location data.
    /// </summary>
    public int FilesWithLocation => _filesWithLocation;

    /// <summary>
    /// Number of unique countries found.
    /// </summary>
    public int UniqueCountriesCount => _uniqueCountries.Count;

    /// <summary>
    /// Number of unique cities found.
    /// </summary>
    public int UniqueCitiesCount => _uniqueCities.Count;

    /// <summary>
    /// Total bytes processed.
    /// </summary>
    public long TotalBytesProcessed => Interlocked.Read(ref _totalBytesProcessed);

    /// <summary>
    /// Earliest file date encountered.
    /// </summary>
    public DateTime? EarliestDate
    {
        get
        {
            lock (_dateLock)
            {
                return _earliestDate;
            }
        }
    }

    /// <summary>
    /// Latest file date encountered.
    /// </summary>
    public DateTime? LatestDate
    {
        get
        {
            lock (_dateLock)
            {
                return _latestDate;
            }
        }
    }

    /// <summary>
    /// Number of duplicates skipped.
    /// </summary>
    public int DuplicatesSkipped => _duplicatesSkipped;

    /// <summary>
    /// Number of files skipped because they already exist.
    /// </summary>
    public int ExistingSkipped => _existingSkipped;

    /// <summary>
    /// Number of errors encountered.
    /// </summary>
    public int ErrorCount => _errorCount;

    /// <summary>
    /// Gets the unique countries found.
    /// </summary>
    public IReadOnlyCollection<string> UniqueCountries => _uniqueCountries.Keys.ToList();

    /// <summary>
    /// Gets the unique cities found.
    /// </summary>
    public IReadOnlyCollection<string> UniqueCities => _uniqueCities.Keys.ToList();

    /// <summary>
    /// Gets file type breakdown by extension.
    /// </summary>
    public IReadOnlyDictionary<string, int> ExtensionBreakdown => 
        new Dictionary<string, int>(_extensionCounts, StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Recording Methods

    /// <summary>
    /// Records a successfully processed file. Thread-safe.
    /// </summary>
    /// <param name="file">The file that was processed.</param>
    /// <param name="fileSize">The size of the file in bytes.</param>
    public void RecordFileProcessed(IFile file, long fileSize)
    {
        Interlocked.Increment(ref _totalFiles);
        Interlocked.Add(ref _totalBytesProcessed, fileSize);

        // Track file type
        var extension = Path.GetExtension(file.File.Name).ToLowerInvariant();
        if (!string.IsNullOrEmpty(extension))
        {
            _extensionCounts.AddOrUpdate(extension, 1, (_, count) => count + 1);
            
            if (PhotoExtensions.Contains(extension))
            {
                Interlocked.Increment(ref _photosCount);
            }
            else if (VideoExtensions.Contains(extension))
            {
                Interlocked.Increment(ref _videosCount);
            }
        }

        // Track location
        if (file.Location != null)
        {
            Interlocked.Increment(ref _filesWithLocation);
            
            if (!string.IsNullOrEmpty(file.Location.Country))
            {
                _uniqueCountries.TryAdd(file.Location.Country, 0);
            }

            var city = file.Location.City ?? file.Location.District;
            if (!string.IsNullOrEmpty(city))
            {
                _uniqueCities.TryAdd(city, 0);
            }
        }

        // Track date range
        UpdateDateRange(file.FileDateTime.DateTime);
    }

    /// <summary>
    /// Records a file that was skipped due to being a duplicate. Thread-safe.
    /// </summary>
    public void RecordDuplicateSkipped()
    {
        Interlocked.Increment(ref _duplicatesSkipped);
    }

    /// <summary>
    /// Records a file that was skipped because it already exists. Thread-safe.
    /// </summary>
    public void RecordExistingSkipped()
    {
        Interlocked.Increment(ref _existingSkipped);
    }

    /// <summary>
    /// Records an error that occurred during processing. Thread-safe.
    /// </summary>
    public void RecordError()
    {
        Interlocked.Increment(ref _errorCount);
    }

    /// <summary>
    /// Records multiple errors at once. Thread-safe.
    /// </summary>
    /// <param name="count">Number of errors to record.</param>
    public void RecordErrors(int count)
    {
        Interlocked.Add(ref _errorCount, count);
    }

    /// <summary>
    /// Records statistics from a file without marking it as processed.
    /// Useful for tracking planned operations before execution.
    /// </summary>
    /// <param name="file">The file to record stats from.</param>
    public void RecordFileStats(IFile file)
    {
        // Track file type
        var extension = Path.GetExtension(file.File.Name).ToLowerInvariant();
        if (!string.IsNullOrEmpty(extension))
        {
            _extensionCounts.AddOrUpdate(extension, 1, (_, count) => count + 1);
            
            if (PhotoExtensions.Contains(extension))
            {
                Interlocked.Increment(ref _photosCount);
            }
            else if (VideoExtensions.Contains(extension))
            {
                Interlocked.Increment(ref _videosCount);
            }
        }

        // Track location
        if (file.Location != null)
        {
            Interlocked.Increment(ref _filesWithLocation);
            
            if (!string.IsNullOrEmpty(file.Location.Country))
            {
                _uniqueCountries.TryAdd(file.Location.Country, 0);
            }

            var city = file.Location.City ?? file.Location.District;
            if (!string.IsNullOrEmpty(city))
            {
                _uniqueCities.TryAdd(city, 0);
            }
        }

        // Track date range
        UpdateDateRange(file.FileDateTime.DateTime);
    }

    private void UpdateDateRange(DateTime fileDate)
    {
        lock (_dateLock)
        {
            if (_earliestDate == null || fileDate < _earliestDate)
            {
                _earliestDate = fileDate;
            }

            if (_latestDate == null || fileDate > _latestDate)
            {
                _latestDate = fileDate;
            }
        }
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Creates a snapshot of the current statistics.
    /// </summary>
    public CopyStatisticsSnapshot CreateSnapshot()
    {
        return new CopyStatisticsSnapshot(
            TotalFiles: _totalFiles,
            PhotosCount: _photosCount,
            VideosCount: _videosCount,
            FilesWithLocation: _filesWithLocation,
            UniqueCountriesCount: _uniqueCountries.Count,
            UniqueCitiesCount: _uniqueCities.Count,
            TotalBytesProcessed: Interlocked.Read(ref _totalBytesProcessed),
            EarliestDate: EarliestDate,
            LatestDate: LatestDate,
            DuplicatesSkipped: _duplicatesSkipped,
            ExistingSkipped: _existingSkipped,
            ErrorCount: _errorCount,
            ExtensionBreakdown: new Dictionary<string, int>(_extensionCounts, StringComparer.OrdinalIgnoreCase),
            UniqueCountries: _uniqueCountries.Keys.ToList(),
            UniqueCities: _uniqueCities.Keys.ToList());
    }

    /// <summary>
    /// Resets all statistics to initial values. Thread-safe.
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _totalFiles, 0);
        Interlocked.Exchange(ref _photosCount, 0);
        Interlocked.Exchange(ref _videosCount, 0);
        Interlocked.Exchange(ref _filesWithLocation, 0);
        Interlocked.Exchange(ref _totalBytesProcessed, 0);
        Interlocked.Exchange(ref _duplicatesSkipped, 0);
        Interlocked.Exchange(ref _existingSkipped, 0);
        Interlocked.Exchange(ref _errorCount, 0);

        _extensionCounts.Clear();
        _uniqueCountries.Clear();
        _uniqueCities.Clear();

        lock (_dateLock)
        {
            _earliestDate = null;
            _latestDate = null;
        }
    }

    #endregion
}

/// <summary>
/// Immutable snapshot of copy statistics at a point in time.
/// </summary>
public sealed record CopyStatisticsSnapshot(
    int TotalFiles,
    int PhotosCount,
    int VideosCount,
    int FilesWithLocation,
    int UniqueCountriesCount,
    int UniqueCitiesCount,
    long TotalBytesProcessed,
    DateTime? EarliestDate,
    DateTime? LatestDate,
    int DuplicatesSkipped,
    int ExistingSkipped,
    int ErrorCount,
    IReadOnlyDictionary<string, int> ExtensionBreakdown,
    IReadOnlyList<string> UniqueCountries,
    IReadOnlyList<string> UniqueCities);
