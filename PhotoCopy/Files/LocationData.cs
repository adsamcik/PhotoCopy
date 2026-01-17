using System;

namespace PhotoCopy.Files;

/// <summary>
/// Contains reverse-geocoded location data for a file based on GPS coordinates.
/// </summary>
/// <param name="District">The nearest populated place name (district, neighborhood, village, or small town).</param>
/// <param name="City">The nearest city-level place (excludes districts/neighborhoods within larger cities).</param>
/// <param name="County">The county/district (admin2 level). Null if unavailable.</param>
/// <param name="State">The state/province (admin1 level). Null if unavailable.</param>
/// <param name="Country">The country code (2-letter ISO code) or full country name.</param>
/// <param name="Population">The population of the district/nearest place. Null if unavailable.</param>
public sealed record LocationData(
    string District,
    string? City,
    string? County,
    string? State,
    string Country,
    long? Population = null
);
