using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace PhotoCopy.Files;

public class FileMetadataExtractor : IFileMetadataExtractor
{
    private readonly ILogger<FileMetadataExtractor> _logger;
    private readonly PhotoCopyConfig _config;

    public FileMetadataExtractor(ILogger<FileMetadataExtractor> logger, IOptions<PhotoCopyConfig> config)
    {
        _logger = logger;
        _config = config.Value;
    }
    
    public FileDateTime GetDateTime(FileInfo file)
    {
        DateTime created = file.CreationTime;
        DateTime modified = file.LastWriteTime;
        DateTime taken = default;
        
        try
        {
            taken = GetDateTakenFromExif(file);
        }
        catch (Exception ex)
        {
            if (_config.LogLevel == OutputLevel.Verbose)
            {
                _logger.LogWarning("Error extracting metadata from {FileName}: {Message}", file.Name, ex.Message);
            }
        }
        
        return new FileDateTime(created, modified, taken);
    }
    
    public (double Latitude, double Longitude)? GetCoordinates(FileInfo file)
    {
        if (!IsImage(file.Extension))
        {
            return null;
        }

        try
        {
            var directories = ImageMetadataReader.ReadMetadata(file.FullName);
            var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();
            
            if (gpsDirectory != null && gpsDirectory.TryGetGeoLocation(out var location) && !location.IsZero)
            {
                return (location.Latitude, location.Longitude);
            }
        }
        catch (Exception ex)
        {
            if (_config.LogLevel == OutputLevel.Verbose)
            {
                _logger.LogWarning("Error extracting coordinates from {FileName}: {Message}", file.Name, ex.Message);
            }
        }

        return null;
    }

    private DateTime GetDateTakenFromExif(FileInfo file)
    {
        if (!IsImage(file.Extension))
        {
            return default;
        }
        
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(file.FullName);
            var subIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            
            if (subIfdDirectory != null && subIfdDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTaken))
            {
                return dateTaken;
            }
            
            return default;
        }
        catch (Exception ex)
        {
            if (_config.LogLevel == OutputLevel.Verbose)
            {
                _logger.LogWarning("Error extracting date from {FileName}: {Message}", file.Name, ex.Message);
            }
            return default;
        }
    }
    
    private bool IsImage(string extension)
    {
        return _config.AllowedExtensions.Contains(extension);
    }
}