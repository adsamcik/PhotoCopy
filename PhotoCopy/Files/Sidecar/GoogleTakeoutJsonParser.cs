using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PhotoCopy.Files.Sidecar;

/// <summary>
/// Parses Google Takeout JSON sidecar files to extract photo/video metadata.
/// </summary>
/// <remarks>
/// Google Takeout exports photos with companion JSON files containing metadata.
/// The JSON files can be named either "photo.jpg.json" or "photo.json" for "photo.jpg".
/// </remarks>
public class GoogleTakeoutJsonParser : ISidecarParser
{
    private readonly ILogger<GoogleTakeoutJsonParser> _logger;

    public GoogleTakeoutJsonParser(ILogger<GoogleTakeoutJsonParser> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool CanParse(string extension)
    {
        return string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public SidecarMetadata? Parse(string jsonFilePath)
    {
        if (string.IsNullOrWhiteSpace(jsonFilePath))
        {
            _logger.LogWarning("JSON file path is null or empty");
            return null;
        }

        if (!File.Exists(jsonFilePath))
        {
            _logger.LogWarning("JSON sidecar file not found: {FilePath}", jsonFilePath);
            return null;
        }

        try
        {
            var jsonContent = File.ReadAllText(jsonFilePath);
            
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                _logger.LogWarning("JSON sidecar file is empty: {FilePath}", jsonFilePath);
                return null;
            }

            return ParseJsonContent(jsonContent, jsonFilePath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning("Error reading JSON sidecar file {FilePath}: {Message}", jsonFilePath, ex.Message);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Error parsing JSON sidecar file {FilePath}: {Message}", jsonFilePath, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Unexpected error parsing JSON sidecar file {FilePath}: {Message}", jsonFilePath, ex.Message);
            return null;
        }
    }

    private SidecarMetadata? ParseJsonContent(string jsonContent, string filePath)
    {
        using var document = JsonDocument.Parse(jsonContent);
        var root = document.RootElement;

        // Extract date taken from photoTakenTime.timestamp (Unix timestamp in seconds)
        DateTime? dateTaken = ExtractPhotoTakenTime(root);

        // Extract GPS data - prefer geoData, fall back to geoDataExif
        var (latitude, longitude, altitude) = ExtractGpsData(root);

        // Extract title and description
        string? title = ExtractStringProperty(root, "title");
        string? description = ExtractStringProperty(root, "description");

        // If we couldn't extract any meaningful data, return null
        if (!dateTaken.HasValue && !latitude.HasValue && string.IsNullOrEmpty(title))
        {
            _logger.LogDebug("No meaningful metadata found in JSON sidecar file: {FilePath}", filePath);
            return null;
        }

        return new SidecarMetadata
        {
            DateTaken = dateTaken,
            Latitude = latitude,
            Longitude = longitude,
            Altitude = altitude,
            Title = string.IsNullOrWhiteSpace(title) ? null : title,
            Description = string.IsNullOrWhiteSpace(description) ? null : description
        };
    }

    private DateTime? ExtractPhotoTakenTime(JsonElement root)
    {
        try
        {
            if (root.TryGetProperty("photoTakenTime", out var photoTakenTime))
            {
                if (photoTakenTime.TryGetProperty("timestamp", out var timestamp))
                {
                    var timestampValue = GetTimestampValue(timestamp);
                    if (timestampValue.HasValue)
                    {
                        return DateTimeOffset.FromUnixTimeSeconds(timestampValue.Value).UtcDateTime;
                    }
                }
            }

            // Fallback to creationTime if photoTakenTime is not available
            if (root.TryGetProperty("creationTime", out var creationTime))
            {
                if (creationTime.TryGetProperty("timestamp", out var timestamp))
                {
                    var timestampValue = GetTimestampValue(timestamp);
                    if (timestampValue.HasValue)
                    {
                        return DateTimeOffset.FromUnixTimeSeconds(timestampValue.Value).UtcDateTime;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Silently ignore timestamp parsing errors
        }

        return null;
    }

    private static long? GetTimestampValue(JsonElement timestamp)
    {
        if (timestamp.ValueKind == JsonValueKind.String)
        {
            var timestampStr = timestamp.GetString();
            if (long.TryParse(timestampStr, out var value))
            {
                return value;
            }
        }
        else if (timestamp.ValueKind == JsonValueKind.Number)
        {
            return timestamp.GetInt64();
        }

        return null;
    }

    private (double? Latitude, double? Longitude, double? Altitude) ExtractGpsData(JsonElement root)
    {
        // Try geoData first (preferred)
        if (root.TryGetProperty("geoData", out var geoData))
        {
            var result = ParseGeoDataElement(geoData);
            if (result.Latitude.HasValue && result.Longitude.HasValue)
            {
                return result;
            }
        }

        // Fall back to geoDataExif
        if (root.TryGetProperty("geoDataExif", out var geoDataExif))
        {
            return ParseGeoDataElement(geoDataExif);
        }

        return (null, null, null);
    }

    private static (double? Latitude, double? Longitude, double? Altitude) ParseGeoDataElement(JsonElement geoData)
    {
        double? latitude = null;
        double? longitude = null;
        double? altitude = null;

        if (geoData.TryGetProperty("latitude", out var latElement))
        {
            latitude = GetDoubleValue(latElement);
        }

        if (geoData.TryGetProperty("longitude", out var lonElement))
        {
            longitude = GetDoubleValue(lonElement);
        }

        if (geoData.TryGetProperty("altitude", out var altElement))
        {
            altitude = GetDoubleValue(altElement);
        }

        // Treat (0, 0) as no GPS data - this is a common placeholder for missing data
        if (latitude.HasValue && longitude.HasValue && 
            Math.Abs(latitude.Value) < 0.0001 && Math.Abs(longitude.Value) < 0.0001)
        {
            return (null, null, null);
        }

        return (latitude, longitude, altitude);
    }

    private static double? GetDoubleValue(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.GetDouble();
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            var str = element.GetString();
            if (double.TryParse(str, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? ExtractStringProperty(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var property) && 
            property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        return null;
    }
}
