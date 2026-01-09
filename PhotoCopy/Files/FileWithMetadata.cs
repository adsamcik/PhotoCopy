using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using PhotoCopy.Configuration;

namespace PhotoCopy.Files;

public class FileWithMetadata : IFile
{
    private readonly ILogger _logger;
    private readonly List<IFile> _relatedFiles = new List<IFile>();

    public FileInfo File { get; }
    public FileDateTime FileDateTime { get; }
    public LocationData? Location { get; set; }
    public string Checksum { get; private set; } = string.Empty;
    
    public IReadOnlyCollection<IFile> RelatedFiles => _relatedFiles;

    public FileWithMetadata(FileInfo file, FileDateTime fileDateTime, ILogger logger)
    {
        File = file;
        FileDateTime = fileDateTime;
        _logger = logger;
    }

    public void AddRelatedFiles(IEnumerable<IFile> files, RelatedFileLookup relatedFileLookup)
    {
        if (relatedFileLookup == RelatedFileLookup.None)
        {
            return;
        }

        var mainFileWithoutExt = Path.GetFileNameWithoutExtension(File.Name);

        foreach (var related in files)
        {
            // Skip files that are null or have null properties
            if (related?.File?.Name == null)
            {
                continue;
            }

            var relatedFile = related.File.Name;
            var relatedBaseName = Path.GetFileNameWithoutExtension(relatedFile);

            // Skip if it's the same file
            if (relatedFile.Equals(File.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool isRelated;
            if (relatedFileLookup == RelatedFileLookup.Strict)
            {
                // In strict mode, check if the related file name starts with the main file name
                // and adds either a dot or underscore - common patterns for related files
                isRelated = relatedFile.StartsWith(mainFileWithoutExt + ".", StringComparison.OrdinalIgnoreCase) ||
                            relatedFile.StartsWith(mainFileWithoutExt + "_", StringComparison.OrdinalIgnoreCase) || 
                            relatedBaseName.Equals(mainFileWithoutExt, StringComparison.OrdinalIgnoreCase);
            }
            else if (relatedFileLookup == RelatedFileLookup.Loose)
            {
                // In loose mode, check if the related file name simply starts with the main file name
                isRelated = relatedBaseName.StartsWith(mainFileWithoutExt, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                isRelated = false;
            }

            if (isRelated)
            {
                _logger.LogInformation("Found related file: {RelatedFile} for {MainFile}", relatedFile, File.Name);
                _relatedFiles.Add(related);
            }
        }
    }

    public string GetRelatedPath(string mainDestinationPath, IFile relatedFile)
    {
        // Get the destination directory
        var directory = Path.GetDirectoryName(mainDestinationPath) ?? string.Empty;
        
        // For simple case "photo.jpg.xmp" - just use the original name with the new destination directory
        if (relatedFile.File.Name == Path.GetFileNameWithoutExtension(File.Name) + File.Extension + Path.GetExtension(relatedFile.File.Name))
        {
            // For file patterns like "photo.jpg.xmp"
            return Path.Combine(directory, Path.GetFileName(mainDestinationPath) + Path.GetExtension(relatedFile.File.Name));
        }
        
        // For other patterns, just replace the base name with the new destination base name
        var mainFileNameWithoutExt = Path.GetFileNameWithoutExtension(mainDestinationPath);
        var relatedFileNameWithoutExt = Path.GetFileNameWithoutExtension(relatedFile.File.Name);
        
        if (relatedFileNameWithoutExt.StartsWith(Path.GetFileNameWithoutExtension(File.Name)))
        {
            // Get the suffix after the base name (if any)
            var suffix = relatedFileNameWithoutExt.Substring(Path.GetFileNameWithoutExtension(File.Name).Length);
            var relatedExtension = Path.GetExtension(relatedFile.File.Name);
            
            return Path.Combine(directory, mainFileNameWithoutExt + suffix + relatedExtension);
        }
        
        // Default case - just use the same extension
        return Path.Combine(directory, mainFileNameWithoutExt + Path.GetExtension(relatedFile.File.Name));
    }

    public void CopyTo(string destinationPath, bool isDryRun = false)
    {
        _logger.LogInformation("Copying {FileName} -> {DestinationPath}", File.Name, destinationPath);
        if (!isDryRun)
        {
            // Ensure destination directory exists
            string? directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            RetryHelper.ExecuteWithRetry(
                () => File.CopyTo(destinationPath, true),
                _logger,
                $"Copy {File.Name}");
        }
    }

    public void MoveTo(string destinationPath, bool isDryRun = false)
    {
        _logger.LogInformation("Moving {FileName} -> {DestinationPath}", File.Name, destinationPath);
        if (!isDryRun)
        {
            // Ensure destination directory exists
            string? directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            RetryHelper.ExecuteWithRetry(
                () => File.MoveTo(destinationPath, true),
                _logger,
                $"Move {File.Name}");
        }
    }

    public void SetChecksum(string checksum)
    {
        if (!string.IsNullOrWhiteSpace(checksum))
        {
            Checksum = checksum;
        }
    }
}