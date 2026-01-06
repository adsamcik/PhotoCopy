using System.Collections.Generic;
using System.IO;

namespace PhotoCopy.Files.Metadata;

public class MetadataEnricher : IMetadataEnricher
{
    private readonly IEnumerable<IMetadataEnrichmentStep> _steps;

    public MetadataEnricher(IEnumerable<IMetadataEnrichmentStep> steps)
    {
        _steps = steps;
    }

    public FileMetadata Enrich(FileInfo fileInfo)
    {
        var context = new FileMetadataContext(fileInfo);

        foreach (var step in _steps)
        {
            step.Enrich(context);
        }

        return context.Metadata;
    }
}