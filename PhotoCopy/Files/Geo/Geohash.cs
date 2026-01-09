using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace PhotoCopy.Files.Geo;

/// <summary>
/// Geohash encoding/decoding utilities for spatial indexing.
/// Geohash encodes latitude/longitude into a hierarchical base-32 string
/// where each character refines precision.
/// </summary>
public static class Geohash
{
    /// <summary>
    /// Base32 alphabet used by geohash (excludes a, i, l, o to avoid confusion).
    /// </summary>
    private const string Base32Chars = "0123456789bcdefghjkmnpqrstuvwxyz";

    /// <summary>
    /// Lookup table for decoding base32 characters to 5-bit values.
    /// </summary>
    private static readonly int[] Base32Decode = CreateDecodeLookup();

    /// <summary>
    /// Approximate cell dimensions at each precision level.
    /// </summary>
    public static readonly (double LatHeight, double LonWidth)[] CellDimensions = new[]
    {
        (180.0, 360.0),           // Precision 0: entire world
        (180.0, 45.0),            // Precision 1: ~5000km x 5000km
        (22.5, 45.0),             // Precision 2: ~1250km x 625km  
        (22.5, 5.625),            // Precision 3: ~156km x 156km
        (2.8125, 5.625),          // Precision 4: ~39km x 20km (OUR PRIMARY LEVEL)
        (2.8125, 0.703125),       // Precision 5: ~5km x 5km
        (0.3515625, 0.703125),    // Precision 6: ~1.2km x 0.6km
        (0.3515625, 0.0878906),   // Precision 7: ~150m x 150m
        (0.0439453, 0.0878906),   // Precision 8: ~40m x 20m
    };

    private static int[] CreateDecodeLookup()
    {
        var lookup = new int[128];
        Array.Fill(lookup, -1);
        for (int i = 0; i < Base32Chars.Length; i++)
        {
            lookup[Base32Chars[i]] = i;
            // Also handle uppercase
            if (char.IsLetter(Base32Chars[i]))
            {
                lookup[char.ToUpperInvariant(Base32Chars[i])] = i;
            }
        }
        return lookup;
    }

    /// <summary>
    /// Encodes latitude/longitude to a geohash string at specified precision.
    /// </summary>
    /// <param name="latitude">Latitude in degrees (-90 to 90).</param>
    /// <param name="longitude">Longitude in degrees (-180 to 180).</param>
    /// <param name="precision">Number of characters (1-12, default 4 for ~5km cells).</param>
    /// <returns>Geohash string of specified length.</returns>
    public static string Encode(double latitude, double longitude, int precision = 4)
    {
        if (precision < 1 || precision > 12)
            throw new ArgumentOutOfRangeException(nameof(precision), "Precision must be between 1 and 12");

        double minLat = -90.0, maxLat = 90.0;
        double minLon = -180.0, maxLon = 180.0;

        var result = new StringBuilder(precision);
        bool isLon = true;
        int bits = 0;
        int charValue = 0;

        while (result.Length < precision)
        {
            if (isLon)
            {
                double mid = (minLon + maxLon) / 2;
                if (longitude >= mid)
                {
                    charValue = (charValue << 1) | 1;
                    minLon = mid;
                }
                else
                {
                    charValue <<= 1;
                    maxLon = mid;
                }
            }
            else
            {
                double mid = (minLat + maxLat) / 2;
                if (latitude >= mid)
                {
                    charValue = (charValue << 1) | 1;
                    minLat = mid;
                }
                else
                {
                    charValue <<= 1;
                    maxLat = mid;
                }
            }

            isLon = !isLon;
            bits++;

            if (bits == 5)
            {
                result.Append(Base32Chars[charValue]);
                bits = 0;
                charValue = 0;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Decodes a geohash string to its bounding box.
    /// </summary>
    /// <param name="geohash">Geohash string.</param>
    /// <returns>Bounding box (minLat, maxLat, minLon, maxLon).</returns>
    public static (double MinLat, double MaxLat, double MinLon, double MaxLon) DecodeBounds(string geohash)
    {
        if (string.IsNullOrEmpty(geohash))
            throw new ArgumentException("Geohash cannot be null or empty", nameof(geohash));

        double minLat = -90.0, maxLat = 90.0;
        double minLon = -180.0, maxLon = 180.0;
        bool isLon = true;

        foreach (char c in geohash)
        {
            int charValue = c < 128 ? Base32Decode[c] : -1;
            if (charValue < 0)
                throw new ArgumentException($"Invalid geohash character: {c}", nameof(geohash));

            for (int bit = 4; bit >= 0; bit--)
            {
                int bitValue = (charValue >> bit) & 1;

                if (isLon)
                {
                    double mid = (minLon + maxLon) / 2;
                    if (bitValue == 1)
                        minLon = mid;
                    else
                        maxLon = mid;
                }
                else
                {
                    double mid = (minLat + maxLat) / 2;
                    if (bitValue == 1)
                        minLat = mid;
                    else
                        maxLat = mid;
                }

                isLon = !isLon;
            }
        }

        return (minLat, maxLat, minLon, maxLon);
    }

    /// <summary>
    /// Decodes a geohash string to its center point.
    /// </summary>
    public static (double Latitude, double Longitude) DecodeCenter(string geohash)
    {
        var (minLat, maxLat, minLon, maxLon) = DecodeBounds(geohash);
        return ((minLat + maxLat) / 2, (minLon + maxLon) / 2);
    }

    /// <summary>
    /// Encodes a geohash string to a 32-bit integer code for efficient storage.
    /// Supports up to 6 characters (30 bits).
    /// Format: [3-bit length][29-bit left-aligned geohash bits]
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint EncodeToUInt32(string geohash)
    {
        if (string.IsNullOrEmpty(geohash) || geohash.Length > 6)
            throw new ArgumentException("Geohash must be 1-6 characters for uint32 encoding", nameof(geohash));

        uint result = 0;
        foreach (char c in geohash)
        {
            int charValue = c < 128 ? Base32Decode[c] : -1;
            if (charValue < 0)
                throw new ArgumentException($"Invalid geohash character: {c}", nameof(geohash));
            result = (result << 5) | (uint)charValue;
        }

        // Left-align the geohash bits (shift to fill 30 bits for 6 chars max)
        int bitsUsed = geohash.Length * 5;
        result <<= (30 - bitsUsed);
        
        // Store: [3-bit length in upper bits][29-bit geohash data]
        // Length goes in bits 31-29, geohash data in bits 28-0
        return ((uint)geohash.Length << 29) | (result >> 1);
    }

    /// <summary>
    /// Decodes a 32-bit integer code back to a geohash string.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string DecodeFromUInt32(uint code)
    {
        // Extract length from upper 3 bits
        int length = (int)(code >> 29);
        if (length == 0 || length > 6)
            throw new ArgumentException("Invalid encoded geohash", nameof(code));

        // Extract geohash data (shift back to original position)
        uint value = (code & 0x1FFFFFFF) << 1;
        
        var chars = new char[length];
        int startBit = 30 - length * 5;

        for (int i = 0; i < length; i++)
        {
            int charIndex = (int)((value >> (25 - i * 5)) & 0x1F);
            chars[i] = Base32Chars[charIndex];
        }

        return new string(chars);
    }

    /// <summary>
    /// Gets the 8 neighboring geohash cells (N, NE, E, SE, S, SW, W, NW).
    /// </summary>
    public static IEnumerable<string> GetNeighbors(string geohash)
    {
        var (minLat, maxLat, minLon, maxLon) = DecodeBounds(geohash);
        double latStep = maxLat - minLat;
        double lonStep = maxLon - minLon;
        double centerLat = (minLat + maxLat) / 2;
        double centerLon = (minLon + maxLon) / 2;

        // 8 directions: N, NE, E, SE, S, SW, W, NW
        var offsets = new (double dLat, double dLon)[]
        {
            (latStep, 0),           // N
            (latStep, lonStep),     // NE
            (0, lonStep),           // E
            (-latStep, lonStep),    // SE
            (-latStep, 0),          // S
            (-latStep, -lonStep),   // SW
            (0, -lonStep),          // W
            (latStep, -lonStep),    // NW
        };

        foreach (var (dLat, dLon) in offsets)
        {
            double newLat = centerLat + dLat;
            double newLon = centerLon + dLon;

            // Handle latitude bounds
            if (newLat > 90 || newLat < -90)
                continue;

            // Handle longitude wrapping
            if (newLon > 180) newLon -= 360;
            if (newLon < -180) newLon += 360;

            yield return Encode(newLat, newLon, geohash.Length);
        }
    }

    /// <summary>
    /// Gets geohash and all its neighbors (9 cells total for boundary-safe queries).
    /// </summary>
    public static IEnumerable<string> GetCellAndNeighbors(string geohash)
    {
        yield return geohash;
        foreach (var neighbor in GetNeighbors(geohash))
        {
            yield return neighbor;
        }
    }

    /// <summary>
    /// Gets all ancestor geohashes (less precise containing cells).
    /// </summary>
    public static IEnumerable<string> GetAncestors(string geohash)
    {
        for (int i = 1; i < geohash.Length; i++)
        {
            yield return geohash[..i];
        }
    }

    /// <summary>
    /// Calculates Haversine distance between two points in kilometers.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double EarthRadiusKm = 6371.0;

        double dLat = ToRadians(lat2 - lat1);
        double dLon = ToRadians(lon2 - lon1);

        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
