using PhotoCopy.Abstractions;

namespace PhotoCopy.Files.Metadata;

public class LocationMetadataEnrichmentStep : IMetadataEnrichmentStep
{
    private readonly IFileMetadataExtractor _metadataExtractor;
    private readonly IReverseGeocodingService _reverseGeocodingService;

    public LocationMetadataEnrichmentStep(
        IFileMetadataExtractor metadataExtractor,
        IReverseGeocodingService reverseGeocodingService)
    {
        _metadataExtractor = metadataExtractor;
        _reverseGeocodingService = reverseGeocodingService;
    }

    public void Enrich(FileMetadataContext context)
    {
        var coords = _metadataExtractor.GetCoordinates(context.FileInfo);
        if (!coords.HasValue)
        {
            return;
        }

        context.Metadata.Location = _reverseGeocodingService.ReverseGeocode(coords.Value.Latitude, coords.Value.Longitude);
    }
}