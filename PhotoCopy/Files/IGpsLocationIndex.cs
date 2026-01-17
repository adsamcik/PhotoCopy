using System;

namespace PhotoCopy.Files;

/// <summary>
/// An index that stores GPS locations by timestamp for companion GPS fallback.
/// Used to find GPS data from nearby photos when a file lacks its own GPS data.
/// </summary>
public interface IGpsLocationIndex
{
    /// <summary>
    /// Adds a GPS location to the index with the associated timestamp.
    /// </summary>
    /// <param name="timestamp">The timestamp of the file with GPS data.</param>
    /// <param name="latitude">The latitude coordinate.</param>
    /// <param name="longitude">The longitude coordinate.</param>
    void AddLocation(DateTime timestamp, double latitude, double longitude);

    /// <summary>
    /// Finds the nearest GPS location to the given timestamp within the specified time window.
    /// </summary>
    /// <param name="timestamp">The timestamp to search for.</param>
    /// <param name="maxWindow">The maximum time difference to consider.</param>
    /// <returns>The nearest GPS coordinates, or null if none found within the window.</returns>
    (double Latitude, double Longitude)? FindNearest(DateTime timestamp, TimeSpan maxWindow);

    /// <summary>
    /// Gets the number of locations stored in the index.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Clears all stored locations from the index.
    /// </summary>
    void Clear();
}
