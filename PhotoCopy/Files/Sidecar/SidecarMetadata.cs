using System;

namespace PhotoCopy.Files.Sidecar;

/// <summary>
/// Metadata extracted from a sidecar file (XMP or JSON).
/// </summary>
public class SidecarMetadata
{
    /// <summary>
    /// The date/time the photo was taken, extracted from sidecar.
    /// </summary>
    public DateTime? DateTaken { get; init; }

    /// <summary>
    /// GPS latitude from sidecar.
    /// </summary>
    public double? Latitude { get; init; }

    /// <summary>
    /// GPS longitude from sidecar.
    /// </summary>
    public double? Longitude { get; init; }

    /// <summary>
    /// GPS altitude from sidecar.
    /// </summary>
    public double? Altitude { get; init; }

    /// <summary>
    /// Title/description from sidecar.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Description from sidecar.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether this metadata contains any GPS data.
    /// </summary>
    public bool HasGpsData => Latitude.HasValue && Longitude.HasValue;

    /// <summary>
    /// Whether this metadata contains date information.
    /// </summary>
    public bool HasDateTaken => DateTaken.HasValue;
}
