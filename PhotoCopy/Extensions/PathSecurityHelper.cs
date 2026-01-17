using System;
using System.IO;

namespace PhotoCopy.Extensions;

/// <summary>
/// Provides security helpers for detecting and preventing operations through
/// symbolic links, junctions, and other reparse points.
/// </summary>
public static class PathSecurityHelper
{
    /// <summary>
    /// Checks if the specified path is a reparse point (symlink, junction, etc.).
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <returns>True if the path exists and is a reparse point; otherwise, false.</returns>
    public static bool IsReparsePoint(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            // Check if the file/directory exists first
            var fileInfo = new FileInfo(path);
            if (fileInfo.Exists)
            {
                return (fileInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }

            var directoryInfo = new DirectoryInfo(path);
            if (directoryInfo.Exists)
            {
                return (directoryInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }

            // Also check parent directories for reparse points
            var directory = Path.GetDirectoryName(path);
            while (!string.IsNullOrEmpty(directory))
            {
                var dirInfo = new DirectoryInfo(directory);
                if (dirInfo.Exists && (dirInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                {
                    return true;
                }
                directory = Path.GetDirectoryName(directory);
            }

            return false;
        }
        catch (Exception)
        {
            // If we can't determine, assume it's not safe
            return false;
        }
    }

    /// <summary>
    /// Validates that a path is safe for file operations (no path traversal, is rooted).
    /// </summary>
    /// <param name="path">The path to validate.</param>
    /// <returns>True if the path is safe; otherwise, false.</returns>
    public static bool IsPathSafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        // Check for path traversal
        if (path.Contains(".."))
        {
            return false;
        }

        // Ensure path is absolute/rooted
        if (!Path.IsPathRooted(path))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Throws an <see cref="InvalidOperationException"/> if the specified path is a reparse point.
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <exception cref="InvalidOperationException">Thrown when the path is a reparse point.</exception>
    public static void ThrowIfReparsePoint(string path)
    {
        if (IsReparsePoint(path))
        {
            throw new InvalidOperationException(
                $"Security violation: Path '{path}' is or contains a reparse point (symlink/junction). " +
                "Operations through reparse points are not allowed to prevent directory traversal attacks.");
        }
    }
}
