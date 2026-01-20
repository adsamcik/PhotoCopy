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
    /// Whether to automatically resume from the last checkpoint if one exists.
    /// When true, skips the user prompt and resumes directly.
    /// </summary>
    public bool Resume { get; set; } = false;
    
    /// <summary>
    /// Whether to start fresh and ignore any existing checkpoints.
    /// When true, any previous checkpoint is discarded and a new operation starts.
    /// </summary>
    public bool FreshStart { get; set; } = false;
    
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
    /// Fallback text used when camera make/model is unavailable.
    /// If null, uses UnknownLocationFallback value.
    /// </summary>
    public string? UnknownCameraFallback { get; set; }
    
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
    
    /// <summary>
    /// Whether to enable Live Photo metadata inheritance.
    /// When enabled, companion .mov videos from iPhone Live Photos will inherit
    /// GPS and date metadata from their paired .heic/.jpg photos with the same base name.
    /// This is particularly useful for iPhone Live Photos where the video lacks GPS data.
    /// Default is true.
    /// </summary>
    public bool EnableLivePhotoInheritance { get; set; } = true;
    
    /// <summary>
    /// Glob patterns for files to exclude from processing.
    /// Supports patterns like "*.aae", "*_thumb*", ".trashed-*".
    /// Patterns are matched against relative file paths from the source root.
    /// </summary>
    public List<string> ExcludePatterns { get; set; } = new();
    
    /// <summary>
    /// Whether to read metadata from sidecar files (XMP, JSON) when the main file lacks metadata.
    /// When enabled, PhotoCopy will look for .xmp or .json sidecar files and extract GPS/date information.
    /// </summary>
    public bool SidecarMetadataFallback { get; set; } = false;

    /// <summary>
    /// Extensions to recognize as sidecar metadata files.
    /// These files will be checked for metadata when SidecarMetadataFallback is enabled.
    /// </summary>
    public HashSet<string> SidecarExtensions { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xmp", ".json"
    };

    /// <summary>
    /// Whether to parse Google Takeout JSON format for metadata extraction.
    /// Google Photos exports metadata in separate .json files with photoTakenTime and geoData fields.
    /// </summary>
    public bool GoogleTakeoutSupport { get; set; } = true;

    /// <summary>
    /// How to prioritize metadata when both embedded file metadata and sidecar metadata exist.
    /// </summary>
    public SidecarMetadataPriority SidecarPriority { get; set; } = SidecarMetadataPriority.EmbeddedFirst;

    /// <summary>
    /// Time offset to apply to all file timestamps.
    /// Used to correct camera clock errors.
    /// Positive values move timestamps forward, negative values move them backward.
    /// </summary>
    public TimeSpan? TimeOffset { get; set; }

    /// <summary>
    /// Enable checkpoint persistence for resume support.
    /// Checkpoints are saved automatically during copy operations.
    /// </summary>
    public bool EnableCheckpoint { get; set; } = true;

    /// <summary>
    /// Custom checkpoint directory. Default: {destination}/.photocopy/
    /// </summary>
    public string? CheckpointDirectory { get; set; }

    public HashSet<string> AllowedExtensions { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".heic", ".mov", ".mp4", ".avi", ".cr2", ".raf", ".nef", ".arw", ".dng"
    };
}
