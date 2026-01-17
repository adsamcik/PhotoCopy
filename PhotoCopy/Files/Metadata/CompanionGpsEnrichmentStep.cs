using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;

namespace PhotoCopy.Files.Metadata;

/// <summary>
/// An enrichment step that provides GPS fallback from nearby photos/videos.
/// This step runs AFTER LocationMetadataEnrichmentStep and uses the GPS index
/// to find nearby GPS coordinates for files that lack their own GPS data.
/// </summary>
public class CompanionGpsEnrichmentStep : IMetadataEnrichmentStep
{
    private readonly IGpsLocationIndex _gpsLocationIndex;
    private readonly IReverseGeocodingService _reverseGeocodingService;
    private readonly PhotoCopyConfig _config;
    private readonly ILogger<CompanionGpsEnrichmentStep> _logger;

    public CompanionGpsEnrichmentStep(
        IGpsLocationIndex gpsLocationIndex,
        IReverseGeocodingService reverseGeocodingService,
        IOptions<PhotoCopyConfig> config,
        ILogger<CompanionGpsEnrichmentStep> logger)
    {
        _gpsLocationIndex = gpsLocationIndex;
        _reverseGeocodingService = reverseGeocodingService;
        _config = config.Value;
        _logger = logger;
    }

    public void Enrich(FileMetadataContext context)
    {
        // Skip if companion GPS fallback is not enabled
        if (!_config.GpsProximityWindowMinutes.HasValue || _config.GpsProximityWindowMinutes.Value <= 0)
        {
            return;
        }

        // Skip if the file already has location data
        if (context.Metadata.Location != null)
        {
            return;
        }

        // Skip if the file already has coordinates (just failed geocoding)
        if (context.Coordinates.HasValue)
        {
            return;
        }

        // Only process files without GPS data
        if (context.Metadata.UnknownReason != UnknownFileReason.NoGpsData)
        {
            return;
        }

        var maxWindow = TimeSpan.FromMinutes(_config.GpsProximityWindowMinutes.Value);
        var timestamp = context.Metadata.DateTime.DateTime;

        var nearbyLocation = _gpsLocationIndex.FindNearest(timestamp, maxWindow);

        if (!nearbyLocation.HasValue)
        {
            _logger.LogTrace(
                "No companion GPS found for {File} within {Minutes} minute window",
                context.FileInfo.Name,
                _config.GpsProximityWindowMinutes.Value);
            return;
        }

        _logger.LogDebug(
            "Found companion GPS for {File}: ({Lat:F6}, {Lon:F6})",
            context.FileInfo.Name,
            nearbyLocation.Value.Latitude,
            nearbyLocation.Value.Longitude);

        // Perform reverse geocoding with the fallback coordinates
        var location = _reverseGeocodingService.ReverseGeocode(
            nearbyLocation.Value.Latitude,
            nearbyLocation.Value.Longitude);

        if (location == null)
        {
            _logger.LogWarning(
                "Companion GPS geocoding failed for {File}",
                context.FileInfo.Name);
            context.Metadata.UnknownReason = UnknownFileReason.GeocodingFailed;
            return;
        }

        // Set the location from companion GPS
        context.Metadata.Location = location;
        context.Coordinates = nearbyLocation;
        context.Metadata.UnknownReason = UnknownFileReason.None;

        _logger.LogInformation(
            "Used companion GPS for {File}: {City}, {Country}",
            context.FileInfo.Name,
            location.City ?? location.District,
            location.Country);
    }
}
