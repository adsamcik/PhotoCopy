using System;
using PhotoCopy.Abstractions;

namespace PhotoCopy.Files.Metadata;

public class LocationMetadataEnrichmentStep : IMetadataEnrichmentStep
{
    private readonly IFileMetadataExtractor _metadataExtractor;
    private readonly IReverseGeocodingService _reverseGeocodingService;
    private readonly IGpsLocationIndex? _gpsLocationIndex;

    public LocationMetadataEnrichmentStep(
        IFileMetadataExtractor metadataExtractor,
        IReverseGeocodingService reverseGeocodingService,
        IGpsLocationIndex? gpsLocationIndex = null)
    {
        _metadataExtractor = metadataExtractor;
        _reverseGeocodingService = reverseGeocodingService;
        _gpsLocationIndex = gpsLocationIndex;
    }

    public void Enrich(FileMetadataContext context)
    {
        (double Latitude, double Longitude)? coords = null;
        
        try
        {
            coords = _metadataExtractor.GetCoordinates(context.FileInfo);
        }
        catch (Exception)
        {
            // GPS extraction failed due to an error
            context.Metadata.UnknownReason = UnknownFileReason.GpsExtractionError;
            return;
        }

        if (!coords.HasValue)
        {
            // No GPS data found in the file
            context.Metadata.UnknownReason = UnknownFileReason.NoGpsData;
            return;
        }

        // Store coordinates in context for other enrichment steps (e.g., CompanionGpsEnrichmentStep)
        context.Coordinates = coords;

        // Add coordinates to the GPS index for companion GPS fallback
        if (_gpsLocationIndex != null)
        {
            _gpsLocationIndex.AddLocation(
                context.Metadata.DateTime.DateTime,
                coords.Value.Latitude,
                coords.Value.Longitude);
        }

        var location = _reverseGeocodingService.ReverseGeocode(coords.Value.Latitude, coords.Value.Longitude);
        
        if (location == null)
        {
            // Geocoding failed - we had coordinates but couldn't resolve them
            context.Metadata.UnknownReason = UnknownFileReason.GeocodingFailed;
            return;
        }

        context.Metadata.Location = location;
        context.Metadata.UnknownReason = UnknownFileReason.None;
    }
}