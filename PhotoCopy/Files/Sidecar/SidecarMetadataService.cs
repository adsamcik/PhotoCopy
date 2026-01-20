using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy.Configuration;

namespace PhotoCopy.Files.Sidecar;

/// <summary>
/// Service that finds and parses sidecar files for a given media file.
/// </summary>
public interface ISidecarMetadataService
{
    /// <summary>
    /// Attempts to find and parse sidecar metadata for the given file.
    /// </summary>
    /// <param name="mediaFile">The main media file (photo/video).</param>
    /// <returns>Parsed sidecar metadata, or null if no sidecar found or parsing failed.</returns>
    SidecarMetadata? GetSidecarMetadata(FileInfo mediaFile);
}

/// <summary>
/// Implementation of <see cref="ISidecarMetadataService"/> that finds and parses sidecar files.
/// </summary>
public class SidecarMetadataService : ISidecarMetadataService
{
    private readonly ILogger<SidecarMetadataService> _logger;
    private readonly PhotoCopyConfig _config;
    private readonly List<ISidecarParser> _parsers;

    public SidecarMetadataService(
        ILogger<SidecarMetadataService> logger,
        IOptions<PhotoCopyConfig> config,
        IEnumerable<ISidecarParser> parsers)
    {
        _logger = logger;
        _config = config.Value;
        _parsers = new List<ISidecarParser>(parsers);
    }

    /// <inheritdoc />
    public SidecarMetadata? GetSidecarMetadata(FileInfo mediaFile)
    {
        if (!_config.SidecarMetadataFallback)
        {
            return null;
        }

        var directory = mediaFile.DirectoryName;
        if (directory == null)
        {
            return null;
        }

        // Look for sidecar files in these patterns:
        // 1. photo.jpg.xmp (full filename + sidecar extension) - preferred
        // 2. photo.xmp (base name + sidecar extension) - fallback

        foreach (var sidecarExt in _config.SidecarExtensions)
        {
            // Pattern 1: photo.jpg.xmp (preferred - more specific match)
            var fullNameSidecar = Path.Combine(directory, mediaFile.Name + sidecarExt);
            if (File.Exists(fullNameSidecar))
            {
                var metadata = TryParseSidecar(fullNameSidecar, sidecarExt);
                if (metadata != null)
                {
                    _logger.LogDebug("Found sidecar metadata in {SidecarPath}", fullNameSidecar);
                    return metadata;
                }
            }

            // Pattern 2: photo.xmp (base name without extension)
            var baseName = Path.GetFileNameWithoutExtension(mediaFile.Name);
            var baseNameSidecar = Path.Combine(directory, baseName + sidecarExt);
            
            // Only try base name pattern if it's different from the full name pattern
            if (!string.Equals(fullNameSidecar, baseNameSidecar, StringComparison.OrdinalIgnoreCase) 
                && File.Exists(baseNameSidecar))
            {
                var metadata = TryParseSidecar(baseNameSidecar, sidecarExt);
                if (metadata != null)
                {
                    _logger.LogDebug("Found sidecar metadata in {SidecarPath}", baseNameSidecar);
                    return metadata;
                }
            }
        }

        return null;
    }

    private SidecarMetadata? TryParseSidecar(string sidecarPath, string extension)
    {
        // Skip JSON if GoogleTakeoutSupport is disabled
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase) && !_config.GoogleTakeoutSupport)
        {
            _logger.LogDebug("Skipping JSON sidecar (GoogleTakeoutSupport disabled): {SidecarPath}", sidecarPath);
            return null;
        }

        var parser = _parsers.FirstOrDefault(p => p.CanParse(extension));
        if (parser == null)
        {
            _logger.LogDebug("No parser found for sidecar extension: {Extension}", extension);
            return null;
        }

        _logger.LogDebug("Parsing sidecar file: {SidecarPath}", sidecarPath);
        return parser.Parse(sidecarPath);
    }
}
