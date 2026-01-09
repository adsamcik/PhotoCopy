namespace PhotoCopy.Configuration;

/// <summary>
/// Specifies the granularity level for location-based path generation.
/// When a granularity level is set, only location data up to that level
/// will be used in path generation. More specific levels include all less specific levels.
/// </summary>
public enum LocationGranularity
{
    /// <summary>
    /// Most detailed level - includes city, county, state, and country.
    /// This is the default behavior.
    /// </summary>
    City,
    
    /// <summary>
    /// County/district level - includes county, state, and country.
    /// City information will be set to "Unknown".
    /// </summary>
    County,
    
    /// <summary>
    /// State/province level - includes state and country.
    /// City and county information will be set to "Unknown".
    /// </summary>
    State,
    
    /// <summary>
    /// Country level only.
    /// City, county, and state information will be set to "Unknown".
    /// </summary>
    Country
}
