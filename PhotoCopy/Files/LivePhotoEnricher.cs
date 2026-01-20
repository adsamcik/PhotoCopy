using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy.Configuration;

namespace PhotoCopy.Files;

/// <summary>
/// Implementation of Live Photo enrichment that pairs .heic photos with their companion .mov videos
/// and transfers GPS/date metadata from the photo to the video.
/// 
/// iPhone Live Photos are stored as:
/// - IMG_1234.heic (the still photo with full EXIF metadata including GPS)
/// - IMG_1234.mov (the companion video, often without GPS metadata)
/// 
/// This enricher finds matching pairs and copies the photo's metadata to the video.
/// </summary>
public class LivePhotoEnricher : ILivePhotoEnricher
{
    /// <summary>
    /// Common photo extensions that can be paired with Live Photo videos.
    /// </summary>
    private static readonly HashSet<string> PhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".heic", ".heif", ".jpg", ".jpeg"
    };

    /// <summary>
    /// Video extensions that can be Live Photo companions.
    /// </summary>
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mov"
    };

    private readonly PhotoCopyConfig _config;
    private readonly ILogger<LivePhotoEnricher> _logger;

    public LivePhotoEnricher(
        IOptions<PhotoCopyConfig> config,
        ILogger<LivePhotoEnricher> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsEnabled => _config.EnableLivePhotoInheritance;

    /// <inheritdoc />
    public void EnrichFiles(IEnumerable<IFile> files)
    {
        if (!IsEnabled)
        {
            return;
        }

        var fileList = files.ToList();
        
        // Group files by directory for efficient matching
        var filesByDirectory = fileList
            .GroupBy(f => Path.GetDirectoryName(f.File.FullName) ?? string.Empty);

        var enrichedCount = 0;

        foreach (var directoryGroup in filesByDirectory)
        {
            var filesInDir = directoryGroup.ToList();
            
            // Build a lookup of photo files by base name
            var photoLookup = BuildPhotoLookup(filesInDir);
            
            if (photoLookup.Count == 0)
            {
                continue;
            }

            // Find and enrich companion videos
            foreach (var file in filesInDir)
            {
                if (!IsLivePhotoVideo(file))
                {
                    continue;
                }

                var baseName = Path.GetFileNameWithoutExtension(file.File.Name);
                
                if (!photoLookup.TryGetValue(baseName, out var photoFile))
                {
                    continue;
                }

                // Check if the video needs enrichment
                if (TryEnrichFromPhoto(file, photoFile))
                {
                    enrichedCount++;
                }
            }
        }

        if (enrichedCount > 0)
        {
            _logger.LogInformation(
                "Live Photo enrichment complete: {Count} companion videos enriched",
                enrichedCount);
        }
    }

    /// <summary>
    /// Builds a lookup dictionary of photo files by their base name (without extension).
    /// Only includes photos with valid location data.
    /// </summary>
    private Dictionary<string, IFile> BuildPhotoLookup(IEnumerable<IFile> files)
    {
        var lookup = new Dictionary<string, IFile>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var extension = file.File.Extension;
            
            if (!PhotoExtensions.Contains(extension))
            {
                continue;
            }

            // Only include photos that have location or date metadata worth inheriting
            if (file.Location == null && file.FileDateTime.Source == DateTimeSource.FileCreation)
            {
                continue;
            }

            var baseName = Path.GetFileNameWithoutExtension(file.File.Name);
            
            // If multiple photos with same base name, prefer ones with GPS
            if (!lookup.TryGetValue(baseName, out var existing) || 
                (existing.Location == null && file.Location != null))
            {
                lookup[baseName] = file;
            }
        }

        return lookup;
    }

    /// <summary>
    /// Checks if a file is a potential Live Photo companion video.
    /// </summary>
    private static bool IsLivePhotoVideo(IFile file)
    {
        return VideoExtensions.Contains(file.File.Extension);
    }

    /// <summary>
    /// Attempts to enrich a companion video with metadata from its paired photo.
    /// </summary>
    /// <returns>True if the video was enriched, false otherwise.</returns>
    private bool TryEnrichFromPhoto(IFile video, IFile photo)
    {
        if (video is not FileWithMetadata videoWithMetadata)
        {
            return false;
        }

        var enriched = false;

        // Transfer location data if the video lacks it
        if (video.Location == null && photo.Location != null)
        {
            videoWithMetadata.Location = photo.Location;
            videoWithMetadata.UnknownReason = UnknownFileReason.None;
            enriched = true;

            _logger.LogDebug(
                "Inherited GPS from Live Photo pair: {Video} <- {Photo}",
                video.File.Name,
                photo.File.Name);
        }

        // Transfer date/time if video only has file system date and photo has better metadata
        if (video.FileDateTime.Source == DateTimeSource.FileCreation &&
            photo.FileDateTime.Source != DateTimeSource.FileCreation)
        {
            // Note: FileDateTime is immutable, so we need to update the internal value
            // This is a design limitation - for now we prioritize GPS inheritance
            // and rely on the related file mechanism for date-based organization
            
            _logger.LogTrace(
                "Live Photo pair detected but FileDateTime is immutable: {Video} <- {Photo}",
                video.File.Name,
                photo.File.Name);
        }

        if (enriched)
        {
            _logger.LogInformation(
                "Enriched Live Photo companion: {Video} using metadata from {Photo} ({City}, {Country})",
                video.File.Name,
                photo.File.Name,
                photo.Location?.City ?? photo.Location?.District ?? "Unknown",
                photo.Location?.Country ?? "Unknown");
        }

        return enriched;
    }
}
