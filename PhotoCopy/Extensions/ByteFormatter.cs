using System;
using System.IO;
using PhotoCopy.Files;

namespace PhotoCopy.Extensions;

/// <summary>
/// Utility class for formatting byte sizes and safe file operations.
/// </summary>
public static class ByteFormatter
{
    private static readonly string[] SizeSuffixes = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>
    /// Formats a byte count into a human-readable string (e.g., "1.5 GB").
    /// </summary>
    /// <param name="bytes">The byte count to format.</param>
    /// <returns>A formatted string with appropriate size suffix.</returns>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "-" + FormatBytes(-bytes);
        if (bytes == 0) return "0 B";

        int i = 0;
        double value = bytes;
        while (value >= 1024 && i < SizeSuffixes.Length - 1)
        {
            value /= 1024;
            i++;
        }
        return $"{value:0.##} {SizeSuffixes[i]}";
    }

    /// <summary>
    /// Safely gets the length of a file, returning 0 if the file doesn't exist or can't be accessed.
    /// </summary>
    /// <param name="file">The IFile to get the length from.</param>
    /// <returns>The file length in bytes, or 0 if unavailable.</returns>
    public static long SafeFileLength(IFile file)
    {
        try
        {
            return file.File.Length;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Safely gets the length of a file, returning 0 if the file doesn't exist or can't be accessed.
    /// </summary>
    /// <param name="fileInfo">The file info to get the length from.</param>
    /// <returns>The file length in bytes, or 0 if unavailable.</returns>
    public static long SafeFileLength(FileInfo fileInfo)
    {
        try
        {
            return fileInfo.Length;
        }
        catch (FileNotFoundException)
        {
            return 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Safely gets the length of a file by path, returning 0 if the file doesn't exist or can't be accessed.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    /// <returns>The file length in bytes, or 0 if unavailable.</returns>
    public static long SafeFileLength(string filePath)
    {
        return SafeFileLength(new FileInfo(filePath));
    }
}
