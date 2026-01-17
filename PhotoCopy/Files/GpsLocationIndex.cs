using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoCopy.Files;

/// <summary>
/// Implementation of GPS location index that stores timestamp→GPS mappings
/// and finds the nearest matching GPS location within a time window.
/// </summary>
public class GpsLocationIndex : IGpsLocationIndex
{
    private readonly List<(DateTime Timestamp, double Latitude, double Longitude)> _locations = new();
    private bool _isSorted = true;

    /// <inheritdoc />
    public int Count => _locations.Count;

    /// <inheritdoc />
    public void AddLocation(DateTime timestamp, double latitude, double longitude)
    {
        _locations.Add((timestamp, latitude, longitude));
        _isSorted = false;
    }

    /// <inheritdoc />
    public (double Latitude, double Longitude)? FindNearest(DateTime timestamp, TimeSpan maxWindow)
    {
        if (_locations.Count == 0)
        {
            return null;
        }

        EnsureSorted();

        // Binary search to find the closest timestamp
        var index = BinarySearchNearest(timestamp);
        
        if (index < 0 || index >= _locations.Count)
        {
            return null;
        }

        // Check the found index and its neighbors to find the closest match
        (DateTime Timestamp, double Latitude, double Longitude)? bestMatch = null;
        TimeSpan bestDiff = TimeSpan.MaxValue;

        // Check current index
        CheckAndUpdateBest(index, timestamp, maxWindow, ref bestMatch, ref bestDiff);
        
        // Check previous index
        if (index > 0)
        {
            CheckAndUpdateBest(index - 1, timestamp, maxWindow, ref bestMatch, ref bestDiff);
        }
        
        // Check next index
        if (index < _locations.Count - 1)
        {
            CheckAndUpdateBest(index + 1, timestamp, maxWindow, ref bestMatch, ref bestDiff);
        }

        if (bestMatch.HasValue)
        {
            return (bestMatch.Value.Latitude, bestMatch.Value.Longitude);
        }

        return null;
    }

    /// <inheritdoc />
    public void Clear()
    {
        _locations.Clear();
        _isSorted = true;
    }

    private void EnsureSorted()
    {
        if (!_isSorted)
        {
            _locations.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            _isSorted = true;
        }
    }

    private int BinarySearchNearest(DateTime timestamp)
    {
        if (_locations.Count == 0)
        {
            return -1;
        }

        int left = 0;
        int right = _locations.Count - 1;

        while (left < right)
        {
            int mid = left + (right - left) / 2;
            
            if (_locations[mid].Timestamp < timestamp)
            {
                left = mid + 1;
            }
            else
            {
                right = mid;
            }
        }

        return left;
    }

    private void CheckAndUpdateBest(
        int index,
        DateTime timestamp,
        TimeSpan maxWindow,
        ref (DateTime Timestamp, double Latitude, double Longitude)? bestMatch,
        ref TimeSpan bestDiff)
    {
        var location = _locations[index];
        var diff = (timestamp - location.Timestamp).Duration();
        
        if (diff <= maxWindow && diff < bestDiff)
        {
            bestMatch = location;
            bestDiff = diff;
        }
    }
}
