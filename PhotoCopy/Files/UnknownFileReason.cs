namespace PhotoCopy.Files;

/// <summary>
/// Reason why a file was placed in the Unknown folder.
/// </summary>
public enum UnknownFileReason
{
    /// <summary>
    /// File has valid location data (not unknown).
    /// </summary>
    None = 0,

    /// <summary>
    /// File does not contain GPS metadata.
    /// </summary>
    NoGpsData = 1,

    /// <summary>
    /// GPS metadata extraction failed due to an error.
    /// </summary>
    GpsExtractionError = 2,

    /// <summary>
    /// GPS coordinates were found but reverse geocoding failed.
    /// </summary>
    GeocodingFailed = 3
}
