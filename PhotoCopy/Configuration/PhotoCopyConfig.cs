using System;
using System.Collections.Generic;
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
    /// Whether to enable transaction logging for rollback support.
    /// </summary>
    public bool EnableRollback { get; set; } = false;
    
    public HashSet<string> AllowedExtensions { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".heic", ".mov", ".mp4", ".avi", ".cr2", ".raf", ".nef", ".arw", ".dng"
    };
}
