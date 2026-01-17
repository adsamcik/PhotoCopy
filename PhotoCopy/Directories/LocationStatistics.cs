using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PhotoCopy.Files;

namespace PhotoCopy.Directories;

/// <summary>
/// Collects and provides statistics about location values across a set of files.
/// Used for conditional variable evaluation (e.g., {city?min=10}).
/// Thread-safe for concurrent access during scanning.
/// </summary>
public sealed class LocationStatistics
{
    private readonly ConcurrentDictionary<string, int> _districtCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _cityCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _countyCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _stateCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _countryCounts = new(StringComparer.OrdinalIgnoreCase);
    private int _totalFiles;
    private int _filesWithLocation;

    /// <summary>
    /// Total number of files processed.
    /// </summary>
    public int TotalFiles => _totalFiles;

    /// <summary>
    /// Number of files that have location data.
    /// </summary>
    public int FilesWithLocation => _filesWithLocation;

    /// <summary>
    /// Number of files without location data.
    /// </summary>
    public int FilesWithoutLocation => _totalFiles - _filesWithLocation;

    /// <summary>
    /// Records location data from a file. Thread-safe.
    /// </summary>
    public void RecordFile(IFile file)
    {
        Interlocked.Increment(ref _totalFiles);

        if (file.Location == null)
            return;

        Interlocked.Increment(ref _filesWithLocation);
        RecordLocationData(file.Location);
    }

    /// <summary>
    /// Records location data directly. Thread-safe.
    /// Useful for testing or when processing location data without an IFile.
    /// </summary>
    public void RecordFile(LocationData? location)
    {
        Interlocked.Increment(ref _totalFiles);

        if (location == null)
            return;

        Interlocked.Increment(ref _filesWithLocation);
        RecordLocationData(location);
    }
    
    private void RecordLocationData(LocationData location)
    {
        IncrementIfNotEmpty(_districtCounts, location.District);
        IncrementIfNotEmpty(_cityCounts, location.City);
        IncrementIfNotEmpty(_countyCounts, location.County);
        IncrementIfNotEmpty(_stateCounts, location.State);
        IncrementIfNotEmpty(_countryCounts, location.Country);
    }

    /// <summary>
    /// Gets the count of files for a specific district value.
    /// </summary>
    public int GetDistrictCount(string? district) => GetCount(_districtCounts, district);

    /// <summary>
    /// Gets the count of files for a specific city value.
    /// </summary>
    public int GetCityCount(string? city) => GetCount(_cityCounts, city);

    /// <summary>
    /// Gets the count of files for a specific county value.
    /// </summary>
    public int GetCountyCount(string? county) => GetCount(_countyCounts, county);

    /// <summary>
    /// Gets the count of files for a specific state value.
    /// </summary>
    public int GetStateCount(string? state) => GetCount(_stateCounts, state);

    /// <summary>
    /// Gets the count of files for a specific country value.
    /// </summary>
    public int GetCountryCount(string? country) => GetCount(_countryCounts, country);

    /// <summary>
    /// Gets the photo count for a location variable by name.
    /// </summary>
    /// <param name="variableName">Variable name (district, city, county, state, country)</param>
    /// <param name="value">The location value to look up</param>
    /// <returns>Number of files with that location value, or 0 if not found</returns>
    public int GetCount(string variableName, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        return variableName.ToLowerInvariant() switch
        {
            "district" => GetDistrictCount(value),
            "city" => GetCityCount(value),
            "county" => GetCountyCount(value),
            "state" => GetStateCount(value),
            "country" => GetCountryCount(value),
            _ => 0
        };
    }

    /// <summary>
    /// Gets all unique values for a location variable with their counts.
    /// </summary>
    public IReadOnlyDictionary<string, int> GetAllCounts(string variableName)
    {
        return variableName.ToLowerInvariant() switch
        {
            "district" => _districtCounts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            "city" => _cityCounts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            "county" => _countyCounts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            "state" => _stateCounts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            "country" => _countryCounts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            _ => new Dictionary<string, int>()
        };
    }

    /// <summary>
    /// Checks if a location variable value meets a minimum threshold.
    /// </summary>
    public bool MeetsMinimumThreshold(string variableName, string? value, int minCount)
    {
        return GetCount(variableName, value) >= minCount;
    }

    /// <summary>
    /// Checks if a location variable value is below a maximum threshold.
    /// </summary>
    public bool MeetsMaximumThreshold(string variableName, string? value, int maxCount)
    {
        return GetCount(variableName, value) <= maxCount;
    }

    private static void IncrementIfNotEmpty(ConcurrentDictionary<string, int> dict, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            dict.AddOrUpdate(value, 1, (_, count) => count + 1);
        }
    }

    private static int GetCount(ConcurrentDictionary<string, int> dict, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        return dict.TryGetValue(value, out var count) ? count : 0;
    }
}
