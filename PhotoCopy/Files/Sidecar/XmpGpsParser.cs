using System;
using System.Text.RegularExpressions;

namespace PhotoCopy.Files.Sidecar;

/// <summary>
/// Parses GPS coordinates from XMP format strings.
/// </summary>
public static partial class XmpGpsParser
{
    /// <summary>
    /// Regex pattern for DMS format with direction: "40,42.768N" or "40,42,46.08N"
    /// Group 1: degrees, Group 2: minutes, Group 3: optional seconds, Group 4: direction
    /// </summary>
    [GeneratedRegex(@"^(\d+),(\d+(?:\.\d+)?)(?:,(\d+(?:\.\d+)?))?\s*([NSEW])$", RegexOptions.IgnoreCase)]
    private static partial Regex DmsWithDirectionRegex();

    /// <summary>
    /// Parses XMP GPS coordinate string to decimal degrees.
    /// Handles formats: "40,42.768N", "40.7128", "40,42,46.08N"
    /// </summary>
    /// <param name="value">The coordinate string to parse.</param>
    /// <param name="isLongitude">True if parsing longitude (affects direction interpretation).</param>
    /// <returns>The coordinate in decimal degrees, or null if parsing fails.</returns>
    public static double? ParseCoordinate(string? value, bool isLongitude)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();

        // Try DMS format with direction: "40,42.768N", "40,42,46.08N"
        var dmsMatch = DmsWithDirectionRegex().Match(value);
        if (dmsMatch.Success)
        {
            return ParseDmsFormat(dmsMatch, isLongitude);
        }

        // Try simple decimal format: "40.7128" or "-74.006"
        if (double.TryParse(value, out var decimalValue))
        {
            return ValidateCoordinate(decimalValue, isLongitude);
        }

        return null;
    }

    /// <summary>
    /// Parses XMP altitude string (may be fraction like "10/1").
    /// </summary>
    /// <param name="value">The altitude string to parse.</param>
    /// <returns>The altitude value, or null if parsing fails.</returns>
    public static double? ParseAltitude(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();

        // Check for fraction format: "10/1", "305/10"
        var fractionIndex = value.IndexOf('/');
        if (fractionIndex > 0)
        {
            var numeratorStr = value.Substring(0, fractionIndex);
            var denominatorStr = value.Substring(fractionIndex + 1);

            if (double.TryParse(numeratorStr, out var numerator) &&
                double.TryParse(denominatorStr, out var denominator) &&
                denominator != 0)
            {
                return numerator / denominator;
            }

            return null;
        }

        // Try simple decimal format
        if (double.TryParse(value, out var decimalValue))
        {
            return decimalValue;
        }

        return null;
    }

    private static double? ParseDmsFormat(Match match, bool isLongitude)
    {
        if (!double.TryParse(match.Groups[1].Value, out var degrees))
        {
            return null;
        }

        if (!double.TryParse(match.Groups[2].Value, out var minutes))
        {
            return null;
        }

        double seconds = 0;
        if (match.Groups[3].Success && !string.IsNullOrEmpty(match.Groups[3].Value))
        {
            if (!double.TryParse(match.Groups[3].Value, out seconds))
            {
                return null;
            }
        }

        var direction = match.Groups[4].Value.ToUpperInvariant();

        // Validate direction matches coordinate type
        if (isLongitude)
        {
            if (direction != "E" && direction != "W")
            {
                return null;
            }
        }
        else
        {
            if (direction != "N" && direction != "S")
            {
                return null;
            }
        }

        // Calculate decimal degrees
        var decimalDegrees = degrees + (minutes / 60.0) + (seconds / 3600.0);

        // Apply sign based on direction
        if (direction == "S" || direction == "W")
        {
            decimalDegrees = -decimalDegrees;
        }

        return ValidateCoordinate(decimalDegrees, isLongitude);
    }

    private static double? ValidateCoordinate(double value, bool isLongitude)
    {
        if (isLongitude)
        {
            // Longitude must be between -180 and 180
            if (value < -180 || value > 180)
            {
                return null;
            }
        }
        else
        {
            // Latitude must be between -90 and 90
            if (value < -90 || value > 90)
            {
                return null;
            }
        }

        return value;
    }
}
