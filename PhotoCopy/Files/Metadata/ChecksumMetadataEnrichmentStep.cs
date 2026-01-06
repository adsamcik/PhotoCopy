using Microsoft.Extensions.Options;
using PhotoCopy.Configuration;

namespace PhotoCopy.Files.Metadata;

public class ChecksumMetadataEnrichmentStep : IMetadataEnrichmentStep
{
    private readonly IChecksumCalculator _checksumCalculator;
    private readonly PhotoCopyConfig _config;

    public ChecksumMetadataEnrichmentStep(IChecksumCalculator checksumCalculator, IOptions<PhotoCopyConfig> config)
    {
        _checksumCalculator = checksumCalculator;
        _config = config.Value;
    }

    public void Enrich(FileMetadataContext context)
    {
        if (!_config.CalculateChecksums)
        {
            return;
        }

        context.Metadata.Checksum = _checksumCalculator.Calculate(context.FileInfo);
    }
}