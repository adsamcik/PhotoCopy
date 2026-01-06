using System.Collections.Generic;
using PhotoCopy.Files;

namespace PhotoCopy.Duplicates;

/// <summary>
/// Represents a group of files that have the same content (checksum).
/// </summary>
public sealed record DuplicateGroup(string Checksum, IReadOnlyList<IFile> Files);

/// <summary>
/// Result of duplicate detection scan.
/// </summary>
public sealed record DuplicateScanResult(
    IReadOnlyList<DuplicateGroup> DuplicateGroups,
    IReadOnlyDictionary<string, IFile> UniqueFiles,
    int TotalFilesScanned,
    int DuplicateFilesFound);

/// <summary>
/// How to handle detected duplicates.
/// </summary>
public enum DuplicateHandling
{
    /// <summary>
    /// Don't detect duplicates, process all files.
    /// </summary>
    None,
    
    /// <summary>
    /// Skip duplicate files (keep first occurrence only).
    /// </summary>
    SkipDuplicates,
    
    /// <summary>
    /// Prompt user for each duplicate group.
    /// </summary>
    Prompt,
    
    /// <summary>
    /// Copy all files but report duplicates.
    /// </summary>
    ReportOnly
}
