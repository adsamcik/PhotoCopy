using System;
using System.Collections.Generic;
using PhotoCopy.Commands;
using PhotoCopy.Duplicates;

namespace PhotoCopy.Configuration;

public class PhotoCopyConfig
{
    public string Source { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public bool DryRun { get; set; }
    public bool SkipExisting { get; set; }
    public bool Overwrite { get; set; }
    public bool NoDuplicateSkip { get; set; }
    public OperationMode Mode { get; set; } = OperationMode.Copy;
    public OutputLevel LogLevel { get; set; } = OutputLevel.Important;
    public RelatedFileLookup RelatedFileMode { get; set; } = RelatedFileLookup.None;
    public string DuplicatesFormat { get; set; } = "-{number}";
    public DateTime? MinDate { get; set; }
    public DateTime? MaxDate { get; set; }
    public string? GeonamesPath { get; set; }
    public bool CalculateChecksums { get; set; } = true;
    
    /// <summary>
    /// Maximum degree of parallelism for async file operations.
    /// Defaults to the number of processors on the machine.
    /// </summary>
    public int Parallelism { get; set; } = Environment.ProcessorCount;
    
    /// <summary>
    /// Whether to show progress reporting during copy operations.
    /// </summary>
    public bool ShowProgress { get; set; } = true;
    
    /// <summary>
    /// Whether to use asynchronous parallel processing.
    /// </summary>
    public bool UseAsync { get; set; } = false;
    
    /// <summary>
    /// How to handle duplicate files (same content, different names).
    /// </summary>
    public DuplicateHandling DuplicateHandling { get; set; } = DuplicateHandling.None;
    
    /// <summary>
    /// Maximum directory recursion depth for scanning.
    /// Null or 0 = unlimited (default), 1 = root only, 2 = root + 1 level, etc.
    /// Negative values are treated as unlimited.
    /// </summary>
    public int? MaxDepth { get; set; }
    
    /// <summary>
    /// Whether to enable transaction logging for rollback support.
    /// </summary>
    public bool EnableRollback { get; set; } = false;
    
    /// <summary>
    /// Minimum population threshold for locations in reverse geocoding.
    /// Locations with population below this value will be filtered out.
    /// Null or 0 means no filtering (default).
    /// </summary>
    public int? MinimumPopulation { get; set; }
    
    /// <summary>
    /// The granularity level for location-based path generation.
    /// Controls which location variables are populated vs set to "Unknown".
    /// </summary>
    public LocationGranularity LocationGranularity { get; set; } = LocationGranularity.City;
    
    /// <summary>
    /// Whether to use full country names instead of 2-letter ISO codes.
    /// </summary>
    public bool UseFullCountryNames { get; set; } = false;
    
    /// <summary>
    /// Fallback text used when location data is unavailable.
    /// </summary>
    public string UnknownLocationFallback { get; set; } = "Unknown";
    
    /// <summary>
    /// The casing style to apply to destination path variable values.
    /// Affects location-based variables like {city}, {country}, {state}, etc.
    /// </summary>
    public PathCasing PathCasing { get; set; } = PathCasing.Original;
    
    /// <summary>
    /// Level of detail for the unknown files report after copy operations.
    /// </summary>
    public UnknownReportLevel UnknownReport { get; set; } = UnknownReportLevel.None;
    
    /// <summary>
    /// Time window in minutes for companion GPS fallback.
    /// When a video or photo lacks GPS data, the system will look for nearby photos
    /// (within this time window) that have GPS data and use their location.
    /// Null or 0 disables this feature (default).
    /// </summary>
    public int? GpsProximityWindowMinutes { get; set; }
    
    public HashSet<string> AllowedExtensions { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".heic", ".mov", ".mp4", ".avi", ".cr2", ".raf", ".nef", ".arw", ".dng"
    };
}
