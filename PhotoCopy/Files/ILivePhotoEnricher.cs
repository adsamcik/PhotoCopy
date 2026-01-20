using System.Collections.Generic;

namespace PhotoCopy.Files;

/// <summary>
/// Service that enriches Live Photo companion videos (.mov) with metadata from their paired photos (.heic).
/// 
/// iPhone Live Photos consist of a .heic image and a companion .mov video with the same base name.
/// The video typically lacks GPS and date metadata, so this enricher copies the metadata from the photo.
/// </summary>
public interface ILivePhotoEnricher
{
    /// <summary>
    /// Gets whether Live Photo enrichment is enabled based on configuration.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Enriches Live Photo companion videos with metadata from their paired photos.
    /// </summary>
    /// <param name="files">The collection of files to enrich.</param>
    void EnrichFiles(IEnumerable<IFile> files);
}
