using System.Collections.Generic;

namespace PhotoCopy.Files;

/// <summary>
/// Service that performs a second-pass enrichment of files using companion GPS data.
/// This service should be called after all files have been scanned and the GPS index is populated.
/// </summary>
public interface ICompanionGpsEnricher
{
    /// <summary>
    /// Gets whether companion GPS enrichment is enabled based on configuration.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Enriches files without GPS data using nearby companion GPS coordinates.
    /// </summary>
    /// <param name="files">The collection of files to enrich.</param>
    void EnrichFiles(IEnumerable<IFile> files);
}
