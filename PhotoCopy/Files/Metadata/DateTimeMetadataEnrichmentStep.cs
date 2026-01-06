using PhotoCopy.Abstractions;

namespace PhotoCopy.Files.Metadata;

public class DateTimeMetadataEnrichmentStep : IMetadataEnrichmentStep
{
    private readonly IFileMetadataExtractor _metadataExtractor;

    public DateTimeMetadataEnrichmentStep(IFileMetadataExtractor metadataExtractor)
    {
        _metadataExtractor = metadataExtractor;
    }

    public void Enrich(FileMetadataContext context)
    {
        var dateTime = _metadataExtractor.GetDateTime(context.FileInfo);
        context.Metadata.DateTime = dateTime;
    }
}