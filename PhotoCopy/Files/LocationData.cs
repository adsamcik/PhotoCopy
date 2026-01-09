namespace PhotoCopy.Files;

/// <summary>
/// Contains reverse-geocoded location data for a file based on GPS coordinates.
/// </summary>
/// <param name="City">The city/place name.</param>
/// <param name="County">The county/district (admin2 level). Null if unavailable.</param>
/// <param name="State">The state/province (admin1 level). Null if unavailable.</param>
/// <param name="Country">The country code (2-letter ISO code) or full country name.</param>
/// <param name="Population">The population of the location. Null if unavailable.</param>
public sealed record LocationData(
    string City,
    string? County,
    string? State,
    string Country,
    long? Population = null
);
