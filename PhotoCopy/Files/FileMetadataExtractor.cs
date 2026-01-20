using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;

namespace PhotoCopy.Files;

public class FileMetadataExtractor : IFileMetadataExtractor
{
    private readonly ILogger<FileMetadataExtractor> _logger;
    private readonly PhotoCopyConfig _config;

    public FileMetadataExtractor(ILogger<FileMetadataExtractor> logger, IOptions<PhotoCopyConfig> config)
    {
        _logger = logger;
        _config = config.Value;
    }
    
    public FileDateTime GetDateTime(FileInfo file)
    {
        DateTime created = file.CreationTime;
        DateTime modified = file.LastWriteTime;
        DateTime taken = default;
        
        try
        {
            taken = GetDateTakenFromExif(file);
        }
        catch (Exception ex)
        {
            if (_config.LogLevel == OutputLevel.Verbose)
            {
                _logger.LogWarning("Error extracting metadata from {FileName}: {Message}", file.Name, ex.Message);
            }
        }
        
        return new FileDateTime(created, modified, taken);
    }
    
    public (double Latitude, double Longitude)? GetCoordinates(FileInfo file)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(file.FullName);
            
            // Try GPS directory first (for images)
            var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();
            
            if (gpsDirectory != null && gpsDirectory.TryGetGeoLocation(out var location) && !location.IsZero)
            {
                return (location.Latitude, location.Longitude);
            }
            
            // Fallback to QuickTime metadata (for videos like MOV/MP4)
            var quickTimeDirectory = directories.OfType<QuickTimeMetadataHeaderDirectory>().FirstOrDefault();
            if (quickTimeDirectory != null)
            {
                var gpsString = quickTimeDirectory.GetString(QuickTimeMetadataHeaderDirectory.TagGpsLocation);
                if (!string.IsNullOrEmpty(gpsString))
                {
                    var coords = ParseIso6709(gpsString);
                    if (coords.HasValue)
                    {
                        return coords;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (_config.LogLevel == OutputLevel.Verbose)
            {
                _logger.LogWarning("Error extracting coordinates from {FileName}: {Message}", file.Name, ex.Message);
            }
        }

        return null;
    }
    
    /// <summary>
    /// Parses ISO 6709 formatted GPS string (e.g., "+48.8584+002.2945/" or "+48.8584+002.2945+100.00/").
    /// Used for QuickTime/MP4 video GPS metadata.
    /// </summary>
    /// <param name="iso6709">The ISO 6709 string to parse.</param>
    /// <returns>Latitude and longitude tuple, or null if parsing fails.</returns>
    internal static (double Latitude, double Longitude)? ParseIso6709(string iso6709)
    {
        if (string.IsNullOrWhiteSpace(iso6709))
        {
            return null;
        }
        
        // Remove trailing slash if present
        var value = iso6709.TrimEnd('/');
        
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        
        // Find all sign positions (+ or -) which indicate the start of each component
        var signPositions = new List<int>();
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '+' || value[i] == '-')
            {
                signPositions.Add(i);
            }
        }
        
        // We need at least 2 components (latitude and longitude)
        if (signPositions.Count < 2)
        {
            return null;
        }
        
        try
        {
            // Extract latitude (first component)
            var latEnd = signPositions[1];
            var latString = value.Substring(0, latEnd);
            
            // Extract longitude (second component)
            var lonEnd = signPositions.Count > 2 ? signPositions[2] : value.Length;
            var lonString = value.Substring(signPositions[1], lonEnd - signPositions[1]);
            
            if (!double.TryParse(latString, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
                !double.TryParse(lonString, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
            {
                return null;
            }
            
            // Validate coordinate ranges
            if (latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180)
            {
                return null;
            }
            
            // Skip zero coordinates (same as GpsDirectory behavior)
            if (latitude == 0 && longitude == 0)
            {
                return null;
            }
            
            return (latitude, longitude);
        }
        catch
        {
            return null;
        }
    }

    private DateTime GetDateTakenFromExif(FileInfo file)
    {
        if (!IsImage(file.Extension))
        {
            return default;
        }
        
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(file.FullName);
            var subIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            
            if (subIfdDirectory != null)
            {
                // Try DateTimeOriginal first (when photo was taken)
                if (subIfdDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTaken))
                {
                    return dateTaken;
                }
                
                // Fallback to DateTimeDigitized (when photo was digitized/captured by sensor)
                if (subIfdDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeDigitized, out var dateDigitized))
                {
                    return dateDigitized;
                }
            }
            
            return default;
        }
        catch (Exception ex)
        {
            if (_config.LogLevel == OutputLevel.Verbose)
            {
                _logger.LogWarning("Error extracting date from {FileName}: {Message}", file.Name, ex.Message);
            }
            return default;
        }
    }
    
    private bool IsImage(string extension)
    {
        return _config.AllowedExtensions.Contains(extension);
    }
    
    /// <summary>
    /// Extracts the camera make and model from EXIF metadata.
    /// </summary>
    /// <param name="file">The file to extract camera info from.</param>
    /// <returns>Camera make and model combined (e.g., "Apple iPhone 15 Pro"), or null if not available.</returns>
    public string? GetCamera(FileInfo file)
    {
        if (!IsImage(file.Extension))
        {
            return null;
        }
        
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(file.FullName);
            var ifd0Directory = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            
            if (ifd0Directory == null)
            {
                return null;
            }
            
            var make = ifd0Directory.GetString(ExifDirectoryBase.TagMake)?.Trim();
            var model = ifd0Directory.GetString(ExifDirectoryBase.TagModel)?.Trim();
            
            if (string.IsNullOrWhiteSpace(make) && string.IsNullOrWhiteSpace(model))
            {
                return null;
            }
            
            // Combine make and model, avoiding duplication if model already contains make
            string camera;
            if (!string.IsNullOrWhiteSpace(make) && !string.IsNullOrWhiteSpace(model))
            {
                // Check if model already starts with make (some cameras do this)
                if (model.StartsWith(make, StringComparison.OrdinalIgnoreCase))
                {
                    camera = model;
                }
                else
                {
                    camera = $"{make} {model}";
                }
            }
            else if (!string.IsNullOrWhiteSpace(model))
            {
                camera = model;
            }
            else
            {
                camera = make!;
            }
            
            // Sanitize for filesystem (remove invalid chars)
            return SanitizeCameraName(camera);
        }
        catch (Exception ex)
        {
            if (_config.LogLevel == OutputLevel.Verbose)
            {
                _logger.LogWarning("Error extracting camera from {FileName}: {Message}", file.Name, ex.Message);
            }
            return null;
        }
    }
    
    /// <summary>
    /// Sanitizes a camera name for use in file paths by removing or replacing invalid characters.
    /// </summary>
    private static string SanitizeCameraName(string camera)
    {
        if (string.IsNullOrEmpty(camera))
        {
            return camera;
        }
        
        // Remove invalid path characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var result = new System.Text.StringBuilder(camera.Length);
        
        foreach (var c in camera)
        {
            if (Array.IndexOf(invalidChars, c) < 0)
            {
                result.Append(c);
            }
            else
            {
                // Replace with space (will be normalized later)
                result.Append(' ');
            }
        }
        
        // Normalize multiple spaces to single space and trim
        var sanitized = result.ToString();
        while (sanitized.Contains("  "))
        {
            sanitized = sanitized.Replace("  ", " ");
        }
        
        return sanitized.Trim();
    }
}