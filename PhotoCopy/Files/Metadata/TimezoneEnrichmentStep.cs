using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy.Configuration;

namespace PhotoCopy.Files.Metadata;

/// <summary>
/// Enrichment step that handles timezone conversion for file timestamps.
/// Supports keeping original timezone, converting to local, or deriving from GPS coordinates.
/// </summary>
public class TimezoneEnrichmentStep : IMetadataEnrichmentStep
{
    private readonly TimezoneHandling _handling;
    private readonly ILogger<TimezoneEnrichmentStep> _logger;
    private bool _loggedMode;

    public TimezoneEnrichmentStep(IOptions<PhotoCopyConfig> config, ILogger<TimezoneEnrichmentStep> logger)
    {
        _handling = config.Value.TimezoneHandling;
        _logger = logger;
    }

    public void Enrich(FileMetadataContext context)
    {
        if (_handling == TimezoneHandling.Original)
        {
            // Original mode: no conversion needed
            return;
        }

        if (!_loggedMode)
        {
            _logger.LogInformation("Timezone handling mode: {Mode}", _handling);
            _loggedMode = true;
        }

        switch (_handling)
        {
            case TimezoneHandling.Local:
                ConvertToLocalTimezone(context);
                break;
            case TimezoneHandling.GpsDerived:
                ConvertToGpsDerivedTimezone(context);
                break;
        }
    }

    /// <summary>
    /// Converts the file timestamp to the local system timezone.
    /// Assumes the original timestamp is in UTC if no timezone information is available.
    /// </summary>
    private void ConvertToLocalTimezone(FileMetadataContext context)
    {
        var originalDateTime = context.Metadata.DateTime;
        
        // Treat the datetime as UTC and convert to local
        var utcDateTime = DateTime.SpecifyKind(originalDateTime.DateTime, DateTimeKind.Utc);
        var localDateTime = utcDateTime.ToLocalTime();

        // Only log if there's an actual change
        if (localDateTime != originalDateTime.DateTime)
        {
            _logger.LogDebug("Converting {File} from {Original} to local time {Local}",
                context.FileInfo.Name,
                originalDateTime.DateTime,
                localDateTime);
        }

        context.Metadata.DateTime = new FileDateTime(localDateTime, originalDateTime.Source);
    }

    /// <summary>
    /// Derives timezone from GPS coordinates using longitude-based approximation.
    /// Each 15 degrees of longitude equals approximately 1 hour of timezone offset.
    /// </summary>
    private void ConvertToGpsDerivedTimezone(FileMetadataContext context)
    {
        // Check if we have GPS coordinates
        if (!context.Coordinates.HasValue)
        {
            _logger.LogDebug("No GPS coordinates available for {File}, skipping timezone derivation",
                context.FileInfo.Name);
            return;
        }

        var (_, longitude) = context.Coordinates.Value;
        var offset = CalculateTimezoneOffset(longitude);
        
        var originalDateTime = context.Metadata.DateTime;
        var adjustedDateTime = originalDateTime.DateTime.Add(offset);

        _logger.LogDebug("Applying GPS-derived timezone offset of {Offset:+0.##;-0.##} hours to {File} (longitude: {Longitude:F2}°)",
            offset.TotalHours,
            context.FileInfo.Name,
            longitude);

        context.Metadata.DateTime = new FileDateTime(adjustedDateTime, originalDateTime.Source);
    }

    /// <summary>
    /// Calculates timezone offset from longitude using the simple approximation.
    /// Each 15 degrees of longitude equals 1 hour of timezone offset.
    /// </summary>
    /// <param name="longitude">Longitude in degrees (-180 to 180).</param>
    /// <returns>The calculated timezone offset.</returns>
    internal static TimeSpan CalculateTimezoneOffset(double longitude)
    {
        // Each 15 degrees of longitude equals 1 hour
        // Positive longitude (East) = positive offset (ahead of UTC)
        // Negative longitude (West) = negative offset (behind UTC)
        double hours = longitude / 15.0;
        
        // Round to nearest half hour for more realistic timezone approximation
        double roundedHours = Math.Round(hours * 2) / 2.0;
        
        return TimeSpan.FromHours(roundedHours);
    }
}
