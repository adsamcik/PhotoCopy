using System.Collections.Generic;
using System.IO;

namespace PhotoCopy.Files.Metadata;

public interface IMetadataEnrichmentStep
{
    void Enrich(FileMetadataContext context);
}

public interface IMetadataEnricher
{
    FileMetadata Enrich(FileInfo fileInfo);
}