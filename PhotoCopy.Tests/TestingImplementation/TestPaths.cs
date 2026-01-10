using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PhotoCopy.Tests.TestingImplementation;

/// <summary>
/// Provides cross-platform test paths and path utilities.
/// On Linux, Windows-style paths (e.g., C:\...) don't work correctly with Path.GetRelativePath(),
/// Path.IsPathRooted(), and FileInfo.FullName. This class provides platform-appropriate paths.
/// </summary>
public static class TestPaths
{
    /// <summary>
    /// Gets whether the current platform is Windows.
    /// </summary>
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// Gets a root path appropriate for the current platform.
    /// On Windows: "C:\"
    /// On Linux/macOS: "/tmp/PhotoCopyTests/"
    /// </summary>
    public static string Root => IsWindows ? @"C:\" : "/tmp/PhotoCopyTests/";

    /// <summary>
    /// Gets a source directory path appropriate for the current platform.
    /// </summary>
    public static string Source => Combine(Root, "Source");

    /// <summary>
    /// Gets a destination directory path appropriate for the current platform.
    /// </summary>
    public static string Dest => Combine(Root, "Dest");

    /// <summary>
    /// Gets a photos directory path appropriate for the current platform.
    /// </summary>
    public static string Photos => Combine(Root, "Photos");

    /// <summary>
    /// Gets an organized directory path appropriate for the current platform.
    /// </summary>
    public static string Organized => Combine(Root, "Organized");

    /// <summary>
    /// Gets a new directory path appropriate for the current platform.
    /// </summary>
    public static string New => Combine(Root, "New");

    /// <summary>
    /// Gets a backup directory path appropriate for the current platform.
    /// </summary>
    public static string Backup => Combine(Root, "Backup");

    /// <summary>
    /// Gets an "other" directory path appropriate for the current platform.
    /// </summary>
    public static string Other => Combine(Root, "Other");

    /// <summary>
    /// Gets a videos directory path appropriate for the current platform.
    /// </summary>
    public static string Videos => Combine(Root, "Videos");

    /// <summary>
    /// Gets an empty directory path appropriate for the current platform.
    /// </summary>
    public static string Empty => Combine(Root, "Empty");

    /// <summary>
    /// Combines path components in a platform-appropriate way.
    /// </summary>
    public static string Combine(params string[] paths)
    {
        return Path.Combine(paths);
    }

    /// <summary>
    /// Creates a destination pattern with the given template.
    /// </summary>
    /// <param name="template">Template like "{year}/{month}/{day}/{name}{ext}"</param>
    /// <returns>Full destination pattern path</returns>
    public static string DestPattern(string template)
    {
        return Combine(Dest, template);
    }

    /// <summary>
    /// Creates a path in the source directory.
    /// </summary>
    public static string InSource(params string[] subPaths)
    {
        var parts = new string[subPaths.Length + 1];
        parts[0] = Source;
        Array.Copy(subPaths, 0, parts, 1, subPaths.Length);
        return Combine(parts);
    }

    /// <summary>
    /// Creates a path in the destination directory.
    /// </summary>
    public static string InDest(params string[] subPaths)
    {
        var parts = new string[subPaths.Length + 1];
        parts[0] = Dest;
        Array.Copy(subPaths, 0, parts, 1, subPaths.Length);
        return Combine(parts);
    }

    /// <summary>
    /// Creates a path in the photos directory.
    /// </summary>
    public static string InPhotos(params string[] subPaths)
    {
        var parts = new string[subPaths.Length + 1];
        parts[0] = Photos;
        Array.Copy(subPaths, 0, parts, 1, subPaths.Length);
        return Combine(parts);
    }

    /// <summary>
    /// Creates a path in the backup directory.
    /// </summary>
    public static string InBackup(params string[] subPaths)
    {
        var parts = new string[subPaths.Length + 1];
        parts[0] = Backup;
        Array.Copy(subPaths, 0, parts, 1, subPaths.Length);
        return Combine(parts);
    }

    /// <summary>
    /// Creates a path in the "other" directory.
    /// </summary>
    public static string InOther(params string[] subPaths)
    {
        var parts = new string[subPaths.Length + 1];
        parts[0] = Other;
        Array.Copy(subPaths, 0, parts, 1, subPaths.Length);
        return Combine(parts);
    }

    /// <summary>
    /// Creates a path in the new directory.
    /// </summary>
    public static string InNew(params string[] subPaths)
    {
        var parts = new string[subPaths.Length + 1];
        parts[0] = New;
        Array.Copy(subPaths, 0, parts, 1, subPaths.Length);
        return Combine(parts);
    }

    /// <summary>
    /// Creates a path in the organized directory.
    /// </summary>
    public static string InOrganized(params string[] subPaths)
    {
        var parts = new string[subPaths.Length + 1];
        parts[0] = Organized;
        Array.Copy(subPaths, 0, parts, 1, subPaths.Length);
        return Combine(parts);
    }

    /// <summary>
    /// Creates a path in the videos directory.
    /// </summary>
    public static string InVideos(params string[] subPaths)
    {
        var parts = new string[subPaths.Length + 1];
        parts[0] = Videos;
        Array.Copy(subPaths, 0, parts, 1, subPaths.Length);
        return Combine(parts);
    }

    /// <summary>
    /// Creates a destination pattern with multiple path segments.
    /// </summary>
    /// <param name="segments">Path segments that may include template variables like "{year}"</param>
    /// <returns>Full destination pattern path</returns>
    public static string DestPattern(params string[] segments)
    {
        var parts = new string[segments.Length + 1];
        parts[0] = Dest;
        Array.Copy(segments, 0, parts, 1, segments.Length);
        return Combine(parts);
    }

    /// <summary>
    /// Creates a pattern path from an arbitrary base directory with template segments.
    /// </summary>
    /// <param name="baseDir">Base directory (e.g., TestPaths.Organized)</param>
    /// <param name="segments">Path segments that may include template variables like "{year}"</param>
    public static string Pattern(string baseDir, params string[] segments)
    {
        var parts = new string[segments.Length + 1];
        parts[0] = baseDir;
        Array.Copy(segments, 0, parts, 1, segments.Length);
        return Combine(parts);
    }
}
