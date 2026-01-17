namespace PhotoCopy.Directories;

/// <summary>
/// Provides context for conditional path generation.
/// This interface allows GeneratePath to access collected statistics
/// and evaluate conditional expressions during the second pass.
/// </summary>
public interface IPathGeneratorContext
{
    /// <summary>
    /// Gets the location statistics collected during the first pass (scan phase).
    /// </summary>
    LocationStatistics Statistics { get; }
    
    /// <summary>
    /// Gets the total number of files being processed.
    /// </summary>
    int TotalFileCount { get; }
    
    /// <summary>
    /// Checks if a location value meets the minimum photo threshold.
    /// </summary>
    /// <param name="variableName">The variable name (district, city, county, state, country)</param>
    /// <param name="value">The value to check</param>
    /// <param name="minimumCount">The minimum required count</param>
    /// <returns>True if the value has at least the minimum count of photos</returns>
    bool MeetsMinimum(string variableName, string value, int minimumCount);
    
    /// <summary>
    /// Checks if a location value meets the maximum photo threshold.
    /// </summary>
    /// <param name="variableName">The variable name (district, city, county, state, country)</param>
    /// <param name="value">The value to check</param>
    /// <param name="maximumCount">The maximum allowed count</param>
    /// <returns>True if the value has at most the maximum count of photos</returns>
    bool MeetsMaximum(string variableName, string value, int maximumCount);
}

/// <summary>
/// Default implementation of IPathGeneratorContext.
/// </summary>
public class PathGeneratorContext : IPathGeneratorContext
{
    public LocationStatistics Statistics { get; }
    public int TotalFileCount { get; }
    
    public PathGeneratorContext(LocationStatistics statistics, int totalFileCount)
    {
        Statistics = statistics;
        TotalFileCount = totalFileCount;
    }
    
    public bool MeetsMinimum(string variableName, string value, int minimumCount)
    {
        if (string.IsNullOrEmpty(value))
            return false;
            
        return Statistics.MeetsMinimumThreshold(variableName, value, minimumCount);
    }
    
    public bool MeetsMaximum(string variableName, string value, int maximumCount)
    {
        if (string.IsNullOrEmpty(value))
            return true; // Empty values are within any max
            
        return Statistics.MeetsMaximumThreshold(variableName, value, maximumCount);
    }
}
