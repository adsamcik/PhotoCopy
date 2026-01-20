using System;
using Microsoft.Extensions.Options;
using PhotoCopy.Configuration;
using PhotoCopy.Files.Sidecar;

namespace PhotoCopy.Files.Metadata;

/// <summary>
/// Enrichment step that applies sidecar metadata according to the configured priority.
/// </summary>
public class SidecarMetadataEnrichmentStep : IMetadataEnrichmentStep
{
    private readonly ISidecarMetadataService? _sidecarService;
    private readonly PhotoCopyConfig _config;

    public SidecarMetadataEnrichmentStep(
        IOptions<PhotoCopyConfig> config,
        ISidecarMetadataService? sidecarService = null)
    {
        _config = config.Value;
        _sidecarService = sidecarService;
    }

    public void Enrich(FileMetadataContext context)
    {
        // Skip if sidecar service is not available or fallback is disabled
        if (_sidecarService == null || !_config.SidecarMetadataFallback)
        {
            return;
        }

        var sidecarMetadata = _sidecarService.GetSidecarMetadata(context.FileInfo);
        if (sidecarMetadata == null)
        {
            return;
        }

        ApplyMetadataWithPriority(context, sidecarMetadata);
    }

    private void ApplyMetadataWithPriority(FileMetadataContext context, SidecarMetadata sidecar)
    {
        switch (_config.SidecarPriority)
        {
            case SidecarMetadataPriority.EmbeddedFirst:
                // Use sidecar only if embedded is missing
                ApplyAsFallback(context, sidecar);
                break;

            case SidecarMetadataPriority.SidecarFirst:
                // Use sidecar if available, override embedded
                ApplyAsOverride(context, sidecar);
                break;

            case SidecarMetadataPriority.MergePreferEmbedded:
                // Use embedded values, fill missing fields from sidecar
                ApplyAsFallback(context, sidecar);
                break;
        }
    }

    /// <summary>
    /// Applies sidecar metadata only for fields that are missing in embedded metadata.
    /// Used for EmbeddedFirst and MergePreferEmbedded priority modes.
    /// </summary>
    private void ApplyAsFallback(FileMetadataContext context, SidecarMetadata sidecar)
    {
        // Apply date if embedded date is missing (default value)
        if (context.Metadata.DateTime.DateTime == default && sidecar.HasDateTaken)
        {
            context.Metadata.DateTime = new FileDateTime(sidecar.DateTaken!.Value, DateTimeSource.Sidecar);
        }

        // Apply coordinates if not already set
        if (!context.Coordinates.HasValue && sidecar.HasGpsData)
        {
            context.Coordinates = (sidecar.Latitude!.Value, sidecar.Longitude!.Value);
        }
    }

    /// <summary>
    /// Applies sidecar metadata as override, replacing embedded metadata.
    /// Used for SidecarFirst priority mode.
    /// </summary>
    private void ApplyAsOverride(FileMetadataContext context, SidecarMetadata sidecar)
    {
        // Override date if sidecar has it
        if (sidecar.HasDateTaken)
        {
            context.Metadata.DateTime = new FileDateTime(sidecar.DateTaken!.Value, DateTimeSource.Sidecar);
        }

        // Override coordinates if sidecar has them
        if (sidecar.HasGpsData)
        {
            context.Coordinates = (sidecar.Latitude!.Value, sidecar.Longitude!.Value);
        }
    }
}
