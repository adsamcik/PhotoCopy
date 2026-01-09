using System;
using System.Runtime.InteropServices;

namespace PhotoCopy.Files.Geo;

/// <summary>
/// Defines binary format structures for the tiered geo-index.
/// 
/// FILE STRUCTURE:
/// ================
/// 1. .geoindex file (Index File):
///    - GeoIndexHeader (48 bytes)
///    - CellIndexEntry[] - sorted by GeohashCode for binary search
///    
/// 2. .geodata file (Data File):
///    - For each cell: LZ4-compressed block containing:
///      - CellBlockHeader (12 bytes)
///      - LocationEntryDisk[] (24 bytes each)
///      - String pool (UTF-8 encoded strings)
/// </summary>
public static class GeoIndexFormat
{
    /// <summary>
    /// Magic number for index file validation ("GIDX").
    /// </summary>
    public const uint IndexMagic = 0x58444947; // "GIDX" in little-endian

    /// <summary>
    /// Magic number for data file validation ("GDAT").
    /// </summary>
    public const uint DataMagic = 0x54414447; // "GDAT" in little-endian

    /// <summary>
    /// Current format version (chunked).
    /// </summary>
    public const ushort FormatVersion = 1;

    /// <summary>
    /// Default geohash precision level (4 = ~20-40km cells).
    /// </summary>
    public const int DefaultPrecision = 4;
}

/// <summary>
/// Header for the .geoindex file. Fixed 48 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GeoIndexHeader
{
    /// <summary>Magic number (GIDX = 0x58444947).</summary>
    public uint Magic;

    /// <summary>Format version number.</summary>
    public ushort Version;

    /// <summary>Geohash precision level (typically 4).</summary>
    public byte Precision;

    /// <summary>Reserved for future flags.</summary>
    public byte Flags;

    /// <summary>Total number of cells in the index.</summary>
    public uint CellCount;

    /// <summary>Total number of location entries across all cells.</summary>
    public uint TotalLocationCount;

    /// <summary>Unix timestamp when index was generated.</summary>
    public long BuildTimestamp;

    /// <summary>Size of the data file in bytes.</summary>
    public long DataFileSize;

    /// <summary>Reserved for future use.</summary>
    public long Reserved1;

    /// <summary>Reserved for future use.</summary>
    public long Reserved2;

    /// <summary>Header size: 48 bytes.</summary>
    public const int Size = 48;

    /// <summary>Whether header is valid.</summary>
    public bool IsValid => Magic == GeoIndexFormat.IndexMagic && Version == GeoIndexFormat.FormatVersion;
}

/// <summary>
/// Entry in the cell index. Maps geohash to data file offset. Fixed 20 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CellIndexEntry
{
    /// <summary>Encoded geohash as uint32 (see Geohash.EncodeToUInt32).</summary>
    public uint GeohashCode;

    /// <summary>Offset within the data file where compressed cell block starts.</summary>
    public long DataOffset;

    /// <summary>Compressed size of the cell block in bytes.</summary>
    public int CompressedSize;

    /// <summary>Uncompressed size of the cell block in bytes.</summary>
    public int UncompressedSize;

    /// <summary>Entry size: 20 bytes.</summary>
    public const int Size = 20;
}

/// <summary>
/// Header for a cell block in the data file. 12 bytes.
/// Stored at the start of each uncompressed cell block.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CellBlockHeader
{
    /// <summary>Number of location entries in this cell.</summary>
    public ushort EntryCount;

    /// <summary>Offset to string pool from start of block.</summary>
    public ushort StringPoolOffset;

    /// <summary>Size of string pool in bytes.</summary>
    public ushort StringPoolSize;

    /// <summary>Reserved for future use.</summary>
    public ushort Reserved1;

    /// <summary>Reserved for future use.</summary>
    public uint Reserved2;

    /// <summary>Header size: 12 bytes.</summary>
    public const int Size = 12;
}

/// <summary>
/// Disk format for location entries. Compact 22-byte structure.
/// Strings are stored as offsets into the cell's string pool.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct LocationEntryDisk
{
    /// <summary>Latitude in micro-degrees (degrees * 1,000,000).</summary>
    public int LatitudeMicro;

    /// <summary>Longitude in micro-degrees (degrees * 1,000,000).</summary>
    public int LongitudeMicro;

    /// <summary>GeoNames ID (for debugging/lookup).</summary>
    public int GeoNameId;

    /// <summary>Offset to city/place name in string pool.</summary>
    public ushort CityOffset;

    /// <summary>Offset to state/admin1 name in string pool.</summary>
    public ushort StateOffset;

    /// <summary>Offset to country name in string pool.</summary>
    public ushort CountryOffset;

    /// <summary>Population (capped at 65535, 0 if unknown).</summary>
    public ushort PopulationK;

    /// <summary>Feature class code (P=populated, A=admin, etc).</summary>
    public byte FeatureClass;

    /// <summary>Reserved padding byte.</summary>
    public byte Reserved;

    /// <summary>Entry size: 22 bytes (4+4+4+2+2+2+2+1+1).</summary>
    public const int Size = 22;

    /// <summary>
    /// Converts micro-degrees to degrees.
    /// </summary>
    public double Latitude => LatitudeMicro / 1_000_000.0;

    /// <summary>
    /// Converts micro-degrees to degrees.
    /// </summary>
    public double Longitude => LongitudeMicro / 1_000_000.0;

    /// <summary>
    /// Converts degrees to micro-degrees.
    /// </summary>
    public static int ToMicroDegrees(double degrees) => (int)(degrees * 1_000_000);
}

/// <summary>
/// In-memory format for location entries. 32-byte aligned for cache efficiency.
/// String references point directly to managed strings.
/// </summary>
public sealed class LocationEntryMemory
{
    /// <summary>Latitude in degrees.</summary>
    public double Latitude { get; init; }

    /// <summary>Longitude in degrees.</summary>
    public double Longitude { get; init; }

    /// <summary>GeoNames ID.</summary>
    public int GeoNameId { get; init; }

    /// <summary>City/place name.</summary>
    public string City { get; init; } = string.Empty;

    /// <summary>State/admin1 name.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Country name.</summary>
    public string Country { get; init; } = string.Empty;

    /// <summary>Approximate population (in thousands).</summary>
    public int PopulationK { get; init; }

    /// <summary>Feature class (P=populated, A=admin, etc).</summary>
    public char FeatureClass { get; init; }

    /// <summary>
    /// Calculates distance to specified coordinates.
    /// </summary>
    public double DistanceKm(double lat, double lon) => Geohash.HaversineDistance(Latitude, Longitude, lat, lon);

    public override string ToString() => $"{City}, {State}, {Country} ({Latitude:F4}, {Longitude:F4})";
}

/// <summary>
/// Represents a loaded cell with all its location entries.
/// </summary>
public sealed class GeoCell
{
    /// <summary>Geohash string for this cell.</summary>
    public string Geohash { get; init; } = string.Empty;

    /// <summary>All location entries in this cell.</summary>
    public LocationEntryMemory[] Entries { get; init; } = Array.Empty<LocationEntryMemory>();

    /// <summary>Cell bounds (minLat, maxLat, minLon, maxLon).</summary>
    public (double MinLat, double MaxLat, double MinLon, double MaxLon) Bounds { get; init; }

    /// <summary>Approximate memory size in bytes.</summary>
    public int EstimatedMemoryBytes { get; init; }

    /// <summary>
    /// Finds the nearest location to the specified coordinates.
    /// </summary>
    /// <param name="latitude">Query latitude.</param>
    /// <param name="longitude">Query longitude.</param>
    /// <param name="maxDistanceKm">Maximum search distance in km.</param>
    /// <param name="priorityThresholdKm">
    /// Within this distance, prefer higher-priority feature classes (P > A > L).
    /// Beyond this distance, just use closest location. Default 15km.
    /// </param>
    public LocationEntryMemory? FindNearest(
        double latitude, 
        double longitude, 
        double maxDistanceKm = double.MaxValue,
        double priorityThresholdKm = 15.0)
    {
        LocationEntryMemory? best = null;
        double bestDistance = maxDistanceKm;
        int bestPriority = int.MaxValue;

        foreach (var entry in Entries)
        {
            double distance = entry.DistanceKm(latitude, longitude);
            if (distance >= maxDistanceKm)
                continue;

            int priority = GeoFeatureClass.GetPriority(entry.FeatureClass);

            // Within priority threshold: prefer better feature class, then closer
            // Beyond threshold: just prefer closer (ignore feature class)
            bool isBetter;
            if (distance <= priorityThresholdKm && bestDistance <= priorityThresholdKm)
            {
                // Both within threshold: prefer better priority, then closer
                isBetter = priority < bestPriority || 
                           (priority == bestPriority && distance < bestDistance);
            }
            else if (distance <= priorityThresholdKm)
            {
                // New one is within threshold, old one isn't: prefer new
                isBetter = true;
            }
            else if (bestDistance <= priorityThresholdKm)
            {
                // Old one is within threshold, new one isn't: keep old
                isBetter = false;
            }
            else
            {
                // Both outside threshold: just prefer closer
                isBetter = distance < bestDistance;
            }

            if (isBetter)
            {
                best = entry;
                bestDistance = distance;
                bestPriority = priority;
            }
        }

        return best;
    }
}

/// <summary>
/// Result of a reverse geocoding lookup.
/// </summary>
public sealed class GeoLookupResult
{
    /// <summary>The matched location.</summary>
    public required LocationEntryMemory Location { get; init; }

    /// <summary>Distance from query point to matched location in kilometers.</summary>
    public required double DistanceKm { get; init; }

    /// <summary>Geohash cell where the match was found.</summary>
    public required string CellGeohash { get; init; }

    /// <summary>Whether the match was found in a neighboring cell.</summary>
    public bool IsFromNeighborCell { get; init; }

    public override string ToString() => $"{Location} @ {DistanceKm:F2}km";
}

/// <summary>
/// Feature class codes from GeoNames.
/// </summary>
public static class GeoFeatureClass
{
    public const char AdminBoundary = 'A';
    public const char HydroGraphic = 'H';
    public const char Area = 'L';
    public const char PopulatedPlace = 'P';
    public const char Road = 'R';
    public const char Spot = 'S';
    public const char HypsoGraphic = 'T';
    public const char Undersea = 'U';
    public const char Vegetation = 'V';

    /// <summary>
    /// Priority order for selecting best match (lower = better).
    /// </summary>
    public static int GetPriority(char featureClass) => featureClass switch
    {
        PopulatedPlace => 0,    // Cities/towns are best
        AdminBoundary => 1,     // Admin regions second
        Spot => 2,              // Buildings, facilities
        Area => 3,              // Parks, regions
        _ => 4                  // Everything else
    };
}
