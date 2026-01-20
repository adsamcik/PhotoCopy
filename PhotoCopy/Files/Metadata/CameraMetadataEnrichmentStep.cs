using PhotoCopy.Abstractions;

namespace PhotoCopy.Files.Metadata;

/// <summary>
/// Enriches file metadata with camera make and model information from EXIF data.
/// </summary>
public class CameraMetadataEnrichmentStep : IMetadataEnrichmentStep
{
    private readonly IFileMetadataExtractor _metadataExtractor;

    public CameraMetadataEnrichmentStep(IFileMetadataExtractor metadataExtractor)
    {
        _metadataExtractor = metadataExtractor;
    }

    public void Enrich(FileMetadataContext context)
    {
        var camera = _metadataExtractor.GetCamera(context.FileInfo);
        context.Metadata.Camera = camera;
    }
}
