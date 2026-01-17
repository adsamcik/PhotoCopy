using System;

namespace PhotoCopy.Files.Geo;

/// <summary>
/// Defines binary format structures for the PhotoCopy geo-index v1.
/// 
/// Optimized for reverse geocoding photos with accurate city/district detection.
/// 
/// FILE STRUCTURE:
/// ================
/// 1. .geoindex file (Index File):
///    - GeoIndexHeader (32 bytes)
///    - CountryTable[] (4 bytes each) - country code to name mapping
///    - Country name string pool
///    - CellIndexEntry[] (16 bytes each) - sorted by GeohashCode
///    
/// 2. .geodata file (Data File):
///    - For each cell: Brotli-compressed block containing:
///      - CellBlockHeader (6 bytes)
///      - LocationEntryDisk[] (14 bytes each, sorted: districts first, cities last)
///      - String pool (UTF-8 encoded, null-terminated strings)
/// </summary>
public static class GeoIndexFormat
{
    /// <summary>
    /// Magic number for index file validation ("PGI1" - PhotoCopy Geo Index v1).
    /// </summary>
    public const uint IndexMagic = 0x31494750; // "PGI1" in little-endian

    /// <summary>
    /// Magic number for data file validation ("PGD1" - PhotoCopy Geo Data v1).
    /// </summary>
    public const uint DataMagic = 0x31444750; // "PGD1" in little-endian

    /// <summary>
    /// Current format version.
    /// </summary>
    public const ushort FormatVersion = 1;

    /// <summary>
    /// Default geohash precision level (4 = ~20-40km cells).
    /// </summary>
    public const int DefaultPrecision = 4;
}

/// <summary>
/// Place type classification based on GeoNames feature codes.
/// Determines whether a place is a city or district/neighborhood.
/// Ordered so that higher values indicate larger/more important places.
/// </summary>
public enum PlaceType : byte
{
    /// <summary>PPLX - Section of populated place (neighborhood, district within a city).</summary>
    District = 0,
    
    /// <summary>PPL with population less than 10K, or PPLL (locality).</summary>
    Village = 1,
    
    /// <summary>PPL with population 10K-100K.</summary>
    Town = 2,
    
    /// <summary>PPL with population over 100K.</summary>
    City = 3,
    
    /// <summary>PPLA, PPLA2, PPLA3, PPLA4 - Seat of administrative division.</summary>
    AdminSeat = 4,
    
    /// <summary>PPLC - Capital of a political entity (country capital).</summary>
    Capital = 5,
}

/// <summary>
/// Header for the .geoindex file. Fixed 32 bytes.
/// </summary>
public struct GeoIndexHeader
{
    /// <summary>Magic number (PGI1 = 0x31494750).</summary>
    public uint Magic;

    /// <summary>Format version number.</summary>
    public ushort Version;

    /// <summary>Geohash precision level (typically 4).</summary>
    public byte Precision;

    /// <summary>Number of countries in the country table.</summary>
    public byte CountryCount;

    /// <summary>Total number of cells in the index.</summary>
    public uint CellCount;

    /// <summary>Total number of location entries across all cells.</summary>
    public uint TotalLocationCount;

    /// <summary>Unix timestamp when index was generated.</summary>
    public long BuildTimestamp;

    /// <summary>Size of the data file in bytes.</summary>
    public long DataFileSize;

    /// <summary>Header size: 32 bytes.</summary>
    public const int Size = 32;

    /// <summary>Whether header is valid.</summary>
    public readonly bool IsValid => Magic == GeoIndexFormat.IndexMagic && Version == GeoIndexFormat.FormatVersion;
}

/// <summary>
/// Country entry in the country table. 4 bytes each.
/// The country table is stored after the header, followed by a string pool with country names.
/// </summary>
public struct CountryEntry
{
    /// <summary>2-letter ISO country code as 2 ASCII bytes.</summary>
    public ushort CountryCode;

    /// <summary>Offset to full country name in the country name string pool.</summary>
    public ushort NameOffset;

    /// <summary>Entry size: 4 bytes.</summary>
    public const int Size = 4;

    /// <summary>Gets the country code as a string.</summary>
    public readonly string GetCode() => new([(char)(CountryCode & 0xFF), (char)(CountryCode >> 8)]);

    /// <summary>Creates a CountryEntry from a 2-letter code.</summary>
    public static CountryEntry FromCode(string code) => new()
    {
        CountryCode = (ushort)(code[0] | (code[1] << 8))
    };
}

/// <summary>
/// Entry in the cell index. Maps geohash to data file offset. Fixed 16 bytes.
/// </summary>
public struct CellIndexEntry
{
    /// <summary>Encoded geohash as uint32 (see Geohash.EncodeToUInt32).</summary>
    public uint GeohashCode;

    /// <summary>Offset within the data file where compressed cell block starts.</summary>
    public long DataOffset;

    /// <summary>Compressed size of the cell block in bytes.</summary>
    public int CompressedSize;

    /// <summary>Entry size: 16 bytes.</summary>
    public const int Size = 16;
}

/// <summary>
/// Header for a cell block in the data file. 6 bytes.
/// Entries are sorted: districts (PlaceType 0-2) first, then cities (PlaceType 3-5).
/// </summary>
public struct CellBlockHeader
{
    /// <summary>Total number of location entries in this cell.</summary>
    public ushort EntryCount;

    /// <summary>Index where city-level entries begin (PlaceType >= City).</summary>
    public ushort CityStartIndex;

    /// <summary>Offset to string pool from start of block.</summary>
    public ushort StringPoolOffset;

    /// <summary>Header size: 6 bytes.</summary>
    public const int Size = 6;
}

/// <summary>
/// Disk format for location entries. Compact 14-byte structure.
/// Strings are stored as offsets into the cell's string pool.
/// </summary>
public struct LocationEntryDisk
{
    /// <summary>Latitude in micro-degrees (degrees * 1,000,000).</summary>
    public int LatitudeMicro;

    /// <summary>Longitude in micro-degrees (degrees * 1,000,000).</summary>
    public int LongitudeMicro;

    /// <summary>Offset to place name in string pool.</summary>
    public ushort NameOffset;

    /// <summary>Offset to state/admin1 name in string pool.</summary>
    public ushort StateOffset;

    /// <summary>Index into the country table (0-255).</summary>
    public byte CountryIndex;

    /// <summary>Place type classification.</summary>
    public PlaceType PlaceType;

    /// <summary>Entry size: 14 bytes (4+4+2+2+1+1).</summary>
    public const int Size = 14;

    /// <summary>Converts micro-degrees to degrees.</summary>
    public readonly double Latitude => LatitudeMicro / 1_000_000.0;

    /// <summary>Converts micro-degrees to degrees.</summary>
    public readonly double Longitude => LongitudeMicro / 1_000_000.0;

    /// <summary>Converts degrees to micro-degrees.</summary>
    public static int ToMicroDegrees(double degrees) => (int)(degrees * 1_000_000);
    
    /// <summary>Whether this entry is a city-level place (Town, City, AdminSeat, or Capital).</summary>
    public readonly bool IsCity => PlaceType >= PlaceType.Town;
}

/// <summary>
/// In-memory format for location entries with resolved string references.
/// </summary>
public sealed class LocationEntry
{
    /// <summary>Latitude in degrees.</summary>
    public double Latitude { get; init; }

    /// <summary>Longitude in degrees.</summary>
    public double Longitude { get; init; }

    /// <summary>Place name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>State/province name (full name, not code).</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Country name (full name).</summary>
    public string Country { get; init; } = string.Empty;

    /// <summary>Place type classification.</summary>
    public PlaceType PlaceType { get; init; }

    /// <summary>Whether this is a city-level place (Town or larger).</summary>
    public bool IsCity => PlaceType >= PlaceType.Town;

    /// <summary>Calculates distance to specified coordinates in kilometers.</summary>
    public double DistanceKm(double lat, double lon) => Geohash.HaversineDistance(Latitude, Longitude, lat, lon);

    public override string ToString() => $"{Name}, {State}, {Country} ({PlaceType})";
}

/// <summary>
/// Represents a loaded cell with all its location entries.
/// Entries are sorted: districts first (indices 0 to CityStartIndex-1), 
/// then cities (indices CityStartIndex to end).
/// </summary>
public sealed class GeoCell
{
    /// <summary>Geohash string for this cell.</summary>
    public string Geohash { get; init; } = string.Empty;

    /// <summary>All location entries in this cell (sorted: districts first, cities last).</summary>
    public LocationEntry[] Entries { get; init; } = [];

    /// <summary>Index where city-level entries begin.</summary>
    public int CityStartIndex { get; init; }

    /// <summary>Cell bounds (minLat, maxLat, minLon, maxLon).</summary>
    public (double MinLat, double MaxLat, double MinLon, double MaxLon) Bounds { get; init; }

    /// <summary>Approximate memory size in bytes.</summary>
    public int EstimatedMemoryBytes { get; init; }

    /// <summary>Gets only district-level entries (PlaceType &lt; Town).</summary>
    public ReadOnlySpan<LocationEntry> Districts => Entries.AsSpan(0, CityStartIndex);

    /// <summary>Gets only city-level entries (PlaceType >= Town).</summary>
    public ReadOnlySpan<LocationEntry> Cities => Entries.AsSpan(CityStartIndex);

    /// <summary>
    /// Finds the nearest location to the specified coordinates.
    /// </summary>
    /// <param name="latitude">Query latitude.</param>
    /// <param name="longitude">Query longitude.</param>
    /// <param name="maxDistanceKm">Maximum search distance in km.</param>
    /// <param name="citiesOnly">If true, only search city-level entries (Town or larger).</param>
    /// <param name="countryFilter">If specified, only consider entries from this country (ISO 3166-1 alpha-2).</param>
    public LocationEntry? FindNearest(
        double latitude, 
        double longitude, 
        double maxDistanceKm = double.MaxValue,
        bool citiesOnly = false,
        string? countryFilter = null)
    {
        LocationEntry? best = null;
        double bestDistance = maxDistanceKm;

        var entries = citiesOnly ? Cities : Entries.AsSpan();
        
        foreach (var entry in entries)
        {
            // Apply country filter if specified
            if (countryFilter != null && 
                !string.Equals(entry.Country, countryFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            double distance = entry.DistanceKm(latitude, longitude);
            if (distance < bestDistance)
            {
                best = entry;
                bestDistance = distance;
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
    public required LocationEntry Location { get; init; }

    /// <summary>Distance from query point to matched location in kilometers.</summary>
    public required double DistanceKm { get; init; }

    /// <summary>Geohash cell where the match was found.</summary>
    public required string CellGeohash { get; init; }

    /// <summary>Whether the match was found in a neighboring cell.</summary>
    public bool IsFromNeighborCell { get; init; }

    public override string ToString() => $"{Location} @ {DistanceKm:F2}km";
}

/// <summary>
/// Extension methods for PlaceType classification.
/// </summary>
public static class PlaceTypeExtensions
{
    /// <summary>
    /// Determines the PlaceType from a GeoNames feature_code and population.
    /// </summary>
    public static PlaceType FromFeatureCode(string featureCode, long population)
    {
        // Country capitals
        if (featureCode == "PPLC")
            return PlaceType.Capital;

        // Administrative seats (state/region capitals)
        if (featureCode.StartsWith("PPLA"))
            return PlaceType.AdminSeat;

        // District/neighborhood within a city
        if (featureCode == "PPLX")
            return PlaceType.District;

        // Locality (very small place)
        if (featureCode == "PPLL")
            return PlaceType.Village;

        // For generic PPL, classify by population
        return population switch
        {
            >= 100_000 => PlaceType.City,
            >= 10_000 => PlaceType.Town,
            _ => PlaceType.Village
        };
    }
}

/// <summary>
/// Helper class for working with GeoNames feature classes.
/// Used by StreamedGeocodingService which reads raw GeoNames data.
/// </summary>
public static class GeoFeatureClass
{
    /// <summary>
    /// Gets a priority value for a GeoNames feature code.
    /// Higher priority = more preferred for geocoding results.
    /// </summary>
    public static int GetPriority(string featureCode)
    {
        // Capitals are highest priority
        if (featureCode == "PPLC")
            return 100;

        // Administrative seats (state capitals, etc)
        if (featureCode.StartsWith("PPLA"))
            return 80;

        // Regular populated places
        if (featureCode == "PPL")
            return 60;

        // Sections/districts of cities
        if (featureCode == "PPLX")
            return 40;

        // Very small localities
        if (featureCode == "PPLL")
            return 20;

        // Any other PPL type
        if (featureCode.StartsWith("PPL"))
            return 50;

        // Non-PPL features (shouldn't happen in our data)
        return 10;
    }
}
