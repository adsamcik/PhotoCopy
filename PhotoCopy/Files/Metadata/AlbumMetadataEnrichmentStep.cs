using PhotoCopy.Abstractions;

namespace PhotoCopy.Files.Metadata;

/// <summary>
/// Enriches file metadata with album name information from EXIF/XMP/IPTC metadata.
/// </summary>
public class AlbumMetadataEnrichmentStep : IMetadataEnrichmentStep
{
    private readonly IFileMetadataExtractor _metadataExtractor;

    public AlbumMetadataEnrichmentStep(IFileMetadataExtractor metadataExtractor)
    {
        _metadataExtractor = metadataExtractor;
    }

    public void Enrich(FileMetadataContext context)
    {
        var album = _metadataExtractor.GetAlbum(context.FileInfo);
        context.Metadata.Album = album;
    }
}
