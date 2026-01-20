using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy.Configuration;

namespace PhotoCopy.Files.Metadata;

/// <summary>
/// Enrichment step that applies a time offset to file timestamps.
/// Useful for correcting camera clock errors.
/// </summary>
public class TimeOffsetEnrichmentStep : IMetadataEnrichmentStep
{
    private readonly TimeSpan? _timeOffset;
    private readonly ILogger<TimeOffsetEnrichmentStep> _logger;
    private bool _loggedOffset;

    public TimeOffsetEnrichmentStep(IOptions<PhotoCopyConfig> config, ILogger<TimeOffsetEnrichmentStep> logger)
    {
        _timeOffset = config.Value.TimeOffset;
        _logger = logger;
    }

    public void Enrich(FileMetadataContext context)
    {
        if (_timeOffset == null || _timeOffset == TimeSpan.Zero)
        {
            return;
        }

        var offset = _timeOffset.Value;
        var originalDateTime = context.Metadata.DateTime;
        
        // Log once when first applying offset
        if (!_loggedOffset)
        {
            _logger.LogInformation("Applying time offset of {Offset} to file timestamps", 
                TimeOffsetParser.Format(offset));
            _loggedOffset = true;
        }

        // Create a new FileDateTime with the offset applied
        var adjustedDateTime = new FileDateTime(
            originalDateTime.DateTime.Add(offset),
            originalDateTime.Source);

        context.Metadata.DateTime = adjustedDateTime;
    }
}
