namespace PhotoCopy.Configuration;

public static class DestinationVariables
{
    public const string Year = "{year}";
    public const string Month = "{month}";
    public const string Day = "{day}";
    public const string Name = "{name}";
    public const string NameNoExtension = "{namenoext}";
    public const string Extension = "{ext}";
    public const string Directory = "{directory}";
    public const string Number = "{number}";
    
    /// <summary>
    /// The nearest populated place name (district, neighborhood, or small town).
    /// For locations within larger cities, this typically returns the district/neighborhood name.
    /// </summary>
    public const string District = "{district}";
    
    /// <summary>
    /// The nearest city-level place (excludes districts/neighborhoods within larger cities).
    /// For example, in Prague's Troja district, {district} returns "Troja" while {city} returns "Praha".
    /// </summary>
    public const string City = "{city}";
    
    public const string County = "{county}";
    public const string State = "{state}";
    public const string Country = "{country}";
}
