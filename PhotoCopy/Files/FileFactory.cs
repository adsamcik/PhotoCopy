using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Files.Metadata;

namespace PhotoCopy.Files;

public class FileFactory : IFileFactory
{
    private readonly IMetadataEnricher _metadataEnricher;
    private readonly ILogger<FileWithMetadata> _fileLogger;
    private readonly PhotoCopyConfig _config;

    public FileFactory(
        IMetadataEnricher metadataEnricher,
        ILogger<FileWithMetadata> fileLogger,
        IOptions<PhotoCopyConfig> config)
    {
        _metadataEnricher = metadataEnricher;
        _fileLogger = fileLogger;
        _config = config.Value;
    }

    public IFile Create(FileInfo fileInfo)
    {
        var metadata = _metadataEnricher.Enrich(fileInfo);
        var extension = fileInfo.Extension;
        var isSupported = _config.AllowedExtensions.Contains(extension);

        if (isSupported)
        {
            var file = new FileWithMetadata(fileInfo, metadata.DateTime, _fileLogger)
            {
                Location = metadata.Location,
                UnknownReason = metadata.UnknownReason,
                Camera = metadata.Camera,
                Album = metadata.Album
            };

            if (!string.IsNullOrWhiteSpace(metadata.Checksum))
            {
                file.SetChecksum(metadata.Checksum!);
            }

            return file;
        }
        else
        {
            return new GenericFile(fileInfo, metadata.DateTime, metadata.Checksum)
            {
                UnknownReason = metadata.UnknownReason
            };
        }
    }
}
