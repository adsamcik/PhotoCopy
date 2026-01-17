using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;

namespace PhotoCopy.Files;

/// <summary>
/// Implementation of companion GPS enrichment that performs a second pass
/// over files to assign GPS coordinates from nearby photos/videos.
/// </summary>
public class CompanionGpsEnricher : ICompanionGpsEnricher
{
    private readonly IGpsLocationIndex _gpsLocationIndex;
    private readonly IReverseGeocodingService _reverseGeocodingService;
    private readonly PhotoCopyConfig _config;
    private readonly ILogger<CompanionGpsEnricher> _logger;

    public CompanionGpsEnricher(
        IGpsLocationIndex gpsLocationIndex,
        IReverseGeocodingService reverseGeocodingService,
        IOptions<PhotoCopyConfig> config,
        ILogger<CompanionGpsEnricher> logger)
    {
        _gpsLocationIndex = gpsLocationIndex;
        _reverseGeocodingService = reverseGeocodingService;
        _config = config.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsEnabled => _config.GpsProximityWindowMinutes.HasValue && 
                             _config.GpsProximityWindowMinutes.Value > 0;

    /// <inheritdoc />
    public void EnrichFiles(IEnumerable<IFile> files)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (_gpsLocationIndex.Count == 0)
        {
            _logger.LogDebug("GPS index is empty, skipping companion GPS enrichment");
            return;
        }

        var maxWindow = TimeSpan.FromMinutes(_config.GpsProximityWindowMinutes!.Value);
        var enrichedCount = 0;

        foreach (var file in files)
        {
            // Only process files without location that are missing GPS data
            if (file.Location != null)
            {
                continue;
            }

            if (file.UnknownReason != UnknownFileReason.NoGpsData)
            {
                continue;
            }

            // Only process FileWithMetadata which has setters
            if (file is not FileWithMetadata fileWithMetadata)
            {
                continue;
            }

            var timestamp = file.FileDateTime.DateTime;
            var nearbyLocation = _gpsLocationIndex.FindNearest(timestamp, maxWindow);

            if (!nearbyLocation.HasValue)
            {
                _logger.LogTrace(
                    "No companion GPS found for {File} within {Minutes} minute window",
                    file.File.Name,
                    _config.GpsProximityWindowMinutes.Value);
                continue;
            }

            _logger.LogDebug(
                "Found companion GPS for {File}: ({Lat:F6}, {Lon:F6})",
                file.File.Name,
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
                    file.File.Name);
                fileWithMetadata.UnknownReason = UnknownFileReason.GeocodingFailed;
                continue;
            }

            // Set the location from companion GPS
            fileWithMetadata.Location = location;
            fileWithMetadata.UnknownReason = UnknownFileReason.None;
            enrichedCount++;

            _logger.LogInformation(
                "Used companion GPS for {File}: {City}, {Country}",
                file.File.Name,
                location.City ?? location.District,
                location.Country);
        }

        if (enrichedCount > 0)
        {
            _logger.LogInformation(
                "Companion GPS enrichment complete: {Count} files enriched",
                enrichedCount);
        }
    }
}
