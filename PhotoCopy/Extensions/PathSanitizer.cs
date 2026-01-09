using System;
using System.Text.RegularExpressions;

namespace PhotoCopy.Extensions;

/// <summary>
/// Provides methods for sanitizing path segments by removing or replacing
/// characters that are invalid in file system paths.
/// </summary>
public static partial class PathSanitizer
{
    /// <summary>
    /// Windows reserved characters that are invalid in file/folder names.
    /// Characters: &lt; &gt; : " / \ | ? *
    /// </summary>
    private static readonly char[] InvalidPathChars = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

    /// <summary>
    /// Pattern to match control characters (ASCII 0-31) and invalid path characters.
    /// </summary>
#if NET7_0_OR_GREATER
    [GeneratedRegex(@"[\x00-\x1F<>:""/\\|?*]", RegexOptions.Compiled)]
    private static partial Regex InvalidCharsRegex();
#else
    private static readonly Regex InvalidCharsRegexInstance = new(@"[\x00-\x1F<>:""/\\|?*]", RegexOptions.Compiled);
    private static Regex InvalidCharsRegex() => InvalidCharsRegexInstance;
#endif

    /// <summary>
    /// Sanitizes a path segment (folder or file name) by replacing invalid characters.
    /// </summary>
    /// <param name="pathSegment">The path segment to sanitize.</param>
    /// <param name="replacement">The replacement string for invalid characters. Defaults to underscore.</param>
    /// <returns>A sanitized path segment safe for use in file system paths.</returns>
    public static string SanitizePathSegment(string? pathSegment, string replacement = "_")
    {
        if (string.IsNullOrWhiteSpace(pathSegment))
        {
            return string.Empty;
        }

        // Replace invalid characters with the replacement string
        var sanitized = InvalidCharsRegex().Replace(pathSegment, replacement);

        // Trim leading/trailing whitespace and dots (Windows restriction)
        sanitized = sanitized.Trim().TrimEnd('.');

        // Handle Windows reserved names (CON, PRN, AUX, NUL, COM1-9, LPT1-9)
        sanitized = HandleReservedNames(sanitized);

        return sanitized;
    }

    /// <summary>
    /// Sanitizes a value intended for use as a path segment, with a fallback value if empty.
    /// </summary>
    /// <param name="value">The value to sanitize.</param>
    /// <param name="fallback">The fallback value if sanitized result is empty.</param>
    /// <param name="replacement">The replacement string for invalid characters.</param>
    /// <returns>A sanitized path segment, or the fallback if empty.</returns>
    public static string SanitizeOrFallback(string? value, string fallback = "Unknown", string replacement = "_")
    {
        var sanitized = SanitizePathSegment(value, replacement);
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    /// <summary>
    /// Checks if a string contains any characters that are invalid in file paths.
    /// </summary>
    /// <param name="pathSegment">The path segment to check.</param>
    /// <returns>True if the path segment contains invalid characters.</returns>
    public static bool ContainsInvalidChars(string? pathSegment)
    {
        if (string.IsNullOrEmpty(pathSegment))
        {
            return false;
        }

        return InvalidCharsRegex().IsMatch(pathSegment);
    }

    /// <summary>
    /// Handles Windows reserved file names by appending an underscore.
    /// Reserved names: CON, PRN, AUX, NUL, COM1-9, LPT1-9
    /// </summary>
    private static string HandleReservedNames(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        // Check against reserved names (case-insensitive)
        var upperName = name.ToUpperInvariant();
        
        // Handle names with extensions (e.g., "CON.txt" is also reserved)
        var baseName = upperName.Contains('.') 
            ? upperName[..upperName.IndexOf('.')] 
            : upperName;

        return baseName switch
        {
            "CON" or "PRN" or "AUX" or "NUL" or
            "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6" or "COM7" or "COM8" or "COM9" or
            "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9"
                => $"{name}_",
            _ => name
        };
    }
}
