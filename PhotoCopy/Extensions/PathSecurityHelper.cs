using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PhotoCopy.Extensions;

/// <summary>
/// Provides security helpers for detecting and preventing operations through
/// symbolic links, junctions, and other reparse points.
/// </summary>
public static class PathSecurityHelper
{
    /// <summary>
    /// Gets the real/canonical path by resolving all symlinks.
    /// On macOS, this resolves paths like /var -> /private/var.
    /// </summary>
    /// <param name="path">The path to resolve.</param>
    /// <returns>The resolved path, or the original path if resolution fails.</returns>
    private static string GetRealPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        try
        {
            // First normalize with GetFullPath
            var fullPath = Path.GetFullPath(path);
            
            // On Unix-like systems (macOS, Linux), resolve symlinks in the path
            // This handles cases like /var -> /private/var on macOS
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Walk up the path to find the deepest existing directory
                var current = fullPath;
                var nonExistentParts = new System.Collections.Generic.Stack<string>();
                
                while (!string.IsNullOrEmpty(current) && !Directory.Exists(current) && !File.Exists(current))
                {
                    nonExistentParts.Push(Path.GetFileName(current));
                    current = Path.GetDirectoryName(current) ?? string.Empty;
                }
                
                // Resolve the existing part using ResolveLinkTarget or by reading the link
                if (!string.IsNullOrEmpty(current))
                {
                    var resolvedCurrent = ResolveUnixPath(current);
                    
                    // Reconstruct the full path with the non-existent parts
                    while (nonExistentParts.Count > 0)
                    {
                        resolvedCurrent = Path.Combine(resolvedCurrent, nonExistentParts.Pop());
                    }
                    
                    return resolvedCurrent;
                }
            }
            
            return fullPath;
        }
        catch (Exception)
        {
            return path;
        }
    }
    
    /// <summary>
    /// Resolves a Unix path by following symlinks to get the real path.
    /// </summary>
    private static string ResolveUnixPath(string path)
    {
        try
        {
            var current = path;
            var visited = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            
            // Walk from root to the target, resolving symlinks at each level
            var parts = current.Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            var resolved = current.StartsWith("/") ? "/" : string.Empty;
            
            foreach (var part in parts)
            {
                resolved = Path.Combine(resolved, part);
                
                // Prevent infinite loops from circular symlinks
                if (!visited.Add(resolved))
                {
                    break;
                }
                
                // Check if this component is a symlink and resolve it
                if (Directory.Exists(resolved) || File.Exists(resolved))
                {
                    var info = new FileInfo(resolved);
                    if ((info.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                    {
                        var target = info.ResolveLinkTarget(returnFinalTarget: true);
                        if (target != null)
                        {
                            resolved = target.FullName;
                        }
                    }
                    else
                    {
                        var dirInfo = new DirectoryInfo(resolved);
                        if (dirInfo.Exists && (dirInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                        {
                            var target = dirInfo.ResolveLinkTarget(returnFinalTarget: true);
                            if (target != null)
                            {
                                resolved = target.FullName;
                            }
                        }
                    }
                }
            }
            
            return resolved;
        }
        catch (Exception)
        {
            return path;
        }
    }

    /// <summary>
    /// Checks if the specified path is a reparse point (symlink, junction, etc.).
    /// On Unix systems, this first resolves the real path to handle system symlinks
    /// like /var -> /private/var on macOS, then checks the resolved path.
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
            // Resolve the real path first to handle system-level symlinks
            // This prevents false positives from macOS's /var -> /private/var symlink
            var resolvedPath = GetRealPath(path);
            
            // Check if the file/directory exists first
            var fileInfo = new FileInfo(resolvedPath);
            if (fileInfo.Exists)
            {
                return (fileInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }

            var directoryInfo = new DirectoryInfo(resolvedPath);
            if (directoryInfo.Exists)
            {
                return (directoryInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }

            // Also check parent directories for reparse points
            // Use the resolved path to avoid flagging system symlinks
            var directory = Path.GetDirectoryName(resolvedPath);
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
    /// Validates that a generated destination path is within the expected root directory.
    /// This prevents path traversal attacks where user-controlled input (location names, 
    /// camera names, etc.) could cause files to be written outside the intended destination.
    /// </summary>
    /// <param name="generatedPath">The fully resolved destination path after variable substitution.</param>
    /// <param name="destinationRoot">The expected root directory all files should stay within.</param>
    /// <returns>True if the path is safely within bounds; otherwise, false.</returns>
    public static bool IsPathWithinBounds(string generatedPath, string destinationRoot)
    {
        if (string.IsNullOrWhiteSpace(generatedPath) || string.IsNullOrWhiteSpace(destinationRoot))
        {
            return false;
        }

        try
        {
            // Normalize both paths to canonical form to catch traversal attempts
            var normalizedPath = Path.GetFullPath(generatedPath);
            var normalizedRoot = Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // The generated path must start with the destination root
            // We add separator to prevent "C:\PhotosEvil" matching root "C:\Photos"
            return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                   || normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // Path.GetFullPath can throw on invalid paths - treat as unsafe
            return false;
        }
    }

    /// <summary>
    /// Performs comprehensive validation of a generated destination path.
    /// Checks for path traversal, ensures path is rooted, and validates it stays within bounds.
    /// </summary>
    /// <param name="generatedPath">The fully resolved destination path after variable substitution.</param>
    /// <param name="destinationRoot">The expected root directory all files should stay within.</param>
    /// <returns>A tuple indicating (isValid, errorMessage). If valid, errorMessage is null.</returns>
    public static (bool IsValid, string? ErrorMessage) ValidateGeneratedPath(string generatedPath, string destinationRoot)
    {
        if (string.IsNullOrWhiteSpace(generatedPath))
        {
            return (false, "Generated path is empty or whitespace.");
        }

        if (string.IsNullOrWhiteSpace(destinationRoot))
        {
            return (false, "Destination root is empty or whitespace.");
        }

        // Check for actual path traversal sequences (..) as complete path segments
        // We need to check each segment of the path to find ".." as a segment, not just as part of a name
        if (ContainsPathTraversalSegment(generatedPath))
        {
            return (false, $"Security violation: Path '{generatedPath}' contains path traversal segment '..'.");
        }

        // Ensure the path is rooted
        if (!Path.IsPathRooted(generatedPath))
        {
            return (false, $"Security violation: Path '{generatedPath}' is not an absolute path.");
        }

        // Validate path stays within destination bounds
        if (!IsPathWithinBounds(generatedPath, destinationRoot))
        {
            return (false, $"Security violation: Path '{generatedPath}' escapes destination root '{destinationRoot}'.");
        }

        return (true, null);
    }

    /// <summary>
    /// Checks if a path contains ".." as a complete path segment (not just within a filename).
    /// For example: "C:\Photos\..\secret" returns true, but "C:\Photos\City..Name\photo.jpg" returns false.
    /// </summary>
    private static bool ContainsPathTraversalSegment(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        // Split by path separators and check for ".." as a complete segment
        var segments = path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, 
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            if (segment == "..")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Throws an exception if the generated path is not safe.
    /// </summary>
    /// <param name="generatedPath">The fully resolved destination path after variable substitution.</param>
    /// <param name="destinationRoot">The expected root directory all files should stay within.</param>
    /// <exception cref="InvalidOperationException">Thrown when path validation fails.</exception>
    public static void ThrowIfPathUnsafe(string generatedPath, string destinationRoot)
    {
        var (isValid, errorMessage) = ValidateGeneratedPath(generatedPath, destinationRoot);
        if (!isValid)
        {
            throw new InvalidOperationException(errorMessage);
        }
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

    /// <summary>
    /// Extracts the root directory from a destination pattern (the static part before any variables).
    /// </summary>
    /// <param name="destinationPattern">The destination pattern containing variables like {year}, {city}, etc.</param>
    /// <returns>The root directory path, or the current directory if pattern starts with a variable.</returns>
    public static string ExtractDestinationRoot(string destinationPattern)
    {
        if (string.IsNullOrWhiteSpace(destinationPattern))
        {
            return Directory.GetCurrentDirectory();
        }

        // Find the first variable placeholder
        var varIndex = destinationPattern.IndexOf('{');

        if (varIndex == 0)
        {
            // Pattern starts with variable, use current directory
            return Directory.GetCurrentDirectory();
        }

        string path;
        if (varIndex > 0)
        {
            // Take everything before the first variable
            path = destinationPattern[..varIndex];
        }
        else
        {
            // No variables, use the directory part of the path
            path = destinationPattern;
        }

        // Trim trailing separators
        var trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // If the original path ended with a separator, it's a complete directory path
        // Otherwise, we have a partial file path like "C:\Dest\photo" from pattern "C:\Dest\photo{ext}"
        // In that case, extract the directory part
        if (path.Length > trimmedPath.Length || varIndex < 0)
        {
            // Path ended with separator OR no variables - use as-is
            if (Path.IsPathRooted(trimmedPath))
            {
                return Path.GetFullPath(trimmedPath);
            }
            return Path.GetFullPath(trimmedPath);
        }
        else
        {
            // Path didn't end with separator before variable - extract directory
            // Pattern like "C:\Dest\photo{ext}" should give root "C:\Dest"
            var dirPath = Path.GetDirectoryName(trimmedPath);
            if (!string.IsNullOrEmpty(dirPath))
            {
                return Path.GetFullPath(dirPath);
            }
            // Fallback: use the path itself if we can't get directory
            if (Path.IsPathRooted(trimmedPath))
            {
                return Path.GetFullPath(trimmedPath);
            }
            return Path.GetFullPath(trimmedPath);
        }
    }
}
