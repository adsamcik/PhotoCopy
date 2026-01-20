using System;
using System.Text.RegularExpressions;

namespace PhotoCopy.Configuration;

/// <summary>
/// Parses time offset strings into TimeSpan values.
/// Supports formats: +2:00, -1:30, +1d, -2d, +1d2:30
/// </summary>
public static class TimeOffsetParser
{
    // Pattern breakdown:
    // ^               - Start of string
    // ([+-])         - Required sign (+ or -)
    // (?:(\d+)d)?    - Optional days portion: digits followed by 'd'
    // (?:(\d{1,2}):(\d{2}))?  - Optional hours:minutes portion
    // $              - End of string
    private static readonly Regex OffsetPattern = new(
        @"^([+-])(?:(\d+)d)?(?:(\d{1,2}):(\d{2}))?$",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses a time offset string into a TimeSpan.
    /// </summary>
    /// <param name="offsetString">The offset string to parse.</param>
    /// <returns>The parsed TimeSpan.</returns>
    /// <exception cref="FormatException">Thrown when the format is invalid.</exception>
    public static TimeSpan Parse(string offsetString)
    {
        if (!TryParse(offsetString, out var result, out var errorMessage))
        {
            throw new FormatException(errorMessage);
        }
        return result;
    }

    /// <summary>
    /// Attempts to parse a time offset string into a TimeSpan.
    /// </summary>
    /// <param name="offsetString">The offset string to parse.</param>
    /// <param name="result">The parsed TimeSpan if successful.</param>
    /// <param name="errorMessage">Error message if parsing fails.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    public static bool TryParse(string? offsetString, out TimeSpan result, out string? errorMessage)
    {
        result = TimeSpan.Zero;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(offsetString))
        {
            errorMessage = "Time offset string cannot be null or empty.";
            return false;
        }

        var match = OffsetPattern.Match(offsetString.Trim());
        if (!match.Success)
        {
            errorMessage = $"Invalid time offset format: '{offsetString}'. " +
                           "Expected formats: +2:00, -1:30, +1d, -2d, +1d2:30";
            return false;
        }

        var sign = match.Groups[1].Value;
        var daysStr = match.Groups[2].Value;
        var hoursStr = match.Groups[3].Value;
        var minutesStr = match.Groups[4].Value;

        // At least one component must be present
        if (string.IsNullOrEmpty(daysStr) && string.IsNullOrEmpty(hoursStr))
        {
            errorMessage = $"Invalid time offset format: '{offsetString}'. " +
                           "Must specify at least days (e.g., +1d) or time (e.g., +2:00).";
            return false;
        }

        int days = 0;
        int hours = 0;
        int minutes = 0;

        if (!string.IsNullOrEmpty(daysStr))
        {
            if (!int.TryParse(daysStr, out days) || days < 0)
            {
                errorMessage = $"Invalid days value: '{daysStr}'. Must be a non-negative integer.";
                return false;
            }
        }

        if (!string.IsNullOrEmpty(hoursStr))
        {
            if (!int.TryParse(hoursStr, out hours) || hours < 0 || hours > 23)
            {
                errorMessage = $"Invalid hours value: '{hoursStr}'. Must be between 0 and 23.";
                return false;
            }

            if (!int.TryParse(minutesStr, out minutes) || minutes < 0 || minutes > 59)
            {
                errorMessage = $"Invalid minutes value: '{minutesStr}'. Must be between 0 and 59.";
                return false;
            }
        }

        result = new TimeSpan(days, hours, minutes, 0);
        
        if (sign == "-")
        {
            result = result.Negate();
        }

        return true;
    }

    /// <summary>
    /// Formats a TimeSpan as a time offset string.
    /// </summary>
    /// <param name="offset">The TimeSpan to format.</param>
    /// <returns>A formatted offset string like +2:00, -1d, or +1d2:30.</returns>
    public static string Format(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var absOffset = offset.Duration();

        var days = absOffset.Days;
        var hours = absOffset.Hours;
        var minutes = absOffset.Minutes;

        if (days > 0 && (hours > 0 || minutes > 0))
        {
            return $"{sign}{days}d{hours}:{minutes:D2}";
        }
        else if (days > 0)
        {
            return $"{sign}{days}d";
        }
        else
        {
            return $"{sign}{hours}:{minutes:D2}";
        }
    }
}
