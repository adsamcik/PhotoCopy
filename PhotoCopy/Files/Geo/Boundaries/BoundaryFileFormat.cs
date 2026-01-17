using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PhotoCopy.Files.Geo.Boundaries;

/// <summary>
/// Binary file format for country boundary data (.geobounds).
/// 
/// Format specification (v1):
/// 
/// Header (48 bytes):
///   - Magic: "PGB1" (4 bytes)
///   - Version: uint16 (2 bytes)
///   - Flags: uint16 (2 bytes)
///   - CountryCount: uint16 (2 bytes)
///   - Reserved: uint16 (2 bytes)
///   - TotalPolygons: uint32 (4 bytes)
///   - TotalVertices: uint32 (4 bytes)
///   - GeohashCacheCount: uint32 (4 bytes)
///   - BorderCellCount: uint32 (4 bytes)
///   - CountryTableOffset: uint64 (8 bytes)
///   - PolygonDataOffset: uint64 (8 bytes)
///   - GeohashCacheOffset: uint64 (8 bytes)
/// 
/// Country Table (variable, at CountryTableOffset):
///   For each country:
///     - CountryCode: 2 bytes (ISO alpha-2)
///     - CountryCode3: 3 bytes (ISO alpha-3)
///     - NameLength: uint8 (1 byte)
///     - Name: variable bytes (UTF-8)
///     - BoundingBox: 4 x float32 (16 bytes) - MinLat, MaxLat, MinLon, MaxLon
///     - PolygonCount: uint16 (2 bytes)
///     - FirstPolygonIndex: uint32 (4 bytes)
/// 
/// Polygon Data (variable, at PolygonDataOffset):
///   For each polygon:
///     - ExteriorRingVertexCount: uint16 (2 bytes)
///     - HoleCount: uint8 (1 byte)
///     - Reserved: uint8 (1 byte)
///     - ExteriorRing: vertices as int16 pairs (lat*100, lon*100)
///     - For each hole:
///       - HoleVertexCount: uint16 (2 bytes)
///       - HoleVertices: vertices as int16 pairs
/// 
/// Geohash Cache (variable, at GeohashCacheOffset):
///   For each cached cell:
///     - GeohashCode: 4 bytes (base32 encoded, null-padded)
///     - CountryIndex: uint16 (2 bytes), 0xFFFF = border cell
///     - Reserved: uint16 (2 bytes)
/// 
/// Border Cells (follows geohash cache):
///   For each border cell:
///     - GeohashCode: 4 bytes
///     - CandidateCount: uint8 (1 byte)
///     - CandidateIndices: uint16 array
/// </summary>
public static class BoundaryFileFormat
{
    /// <summary>
    /// Magic bytes identifying a boundary file: "PGB1" (PhotoCopy Geo Bounds v1).
    /// </summary>
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("PGB1");

    /// <summary>
    /// Current format version.
    /// </summary>
    public const ushort CurrentVersion = 1;

    /// <summary>
    /// Header size in bytes.
    /// </summary>
    public const int HeaderSize = 48;

    /// <summary>
    /// Writes country boundary data to a binary file.
    /// </summary>
    public static void Write(
        string path,
        IReadOnlyList<CountryBoundary> countries,
        IReadOnlyDictionary<string, string> geohashCache,
        IReadOnlyDictionary<string, string[]> borderCells)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream);

        // Build country index
        var countryIndex = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < countries.Count; i++)
            countryIndex[countries[i].CountryCode] = (ushort)i;

        // Count totals
        uint totalPolygons = 0;
        uint totalVertices = 0;
        foreach (var country in countries)
        {
            totalPolygons += (uint)country.Polygons.Length;
            totalVertices += (uint)country.TotalVertexCount;
        }

        // Reserve space for header (we'll write it at the end with correct offsets)
        writer.Write(new byte[HeaderSize]);

        // Write country table
        long countryTableOffset = stream.Position;
        foreach (var country in countries)
        {
            WriteCountryEntry(writer, country);
        }

        // Write polygon data
        long polygonDataOffset = stream.Position;
        foreach (var country in countries)
        {
            foreach (var polygon in country.Polygons)
            {
                WritePolygon(writer, polygon);
            }
        }

        // Write geohash cache
        long geohashCacheOffset = stream.Position;
        foreach (var (geohash, countryCode) in geohashCache)
        {
            WriteGeohashCacheEntry(writer, geohash, countryIndex.GetValueOrDefault(countryCode, ushort.MaxValue));
        }

        // Write border cells
        foreach (var (geohash, candidates) in borderCells)
        {
            WriteBorderCell(writer, geohash, candidates, countryIndex);
        }

        // Go back and write header
        stream.Seek(0, SeekOrigin.Begin);
        WriteHeader(writer, new BoundaryFileHeader
        {
            Version = CurrentVersion,
            CountryCount = (ushort)countries.Count,
            TotalPolygons = totalPolygons,
            TotalVertices = totalVertices,
            GeohashCacheCount = (uint)geohashCache.Count,
            BorderCellCount = (uint)borderCells.Count,
            CountryTableOffset = (ulong)countryTableOffset,
            PolygonDataOffset = (ulong)polygonDataOffset,
            GeohashCacheOffset = (ulong)geohashCacheOffset,
        });
    }

    /// <summary>
    /// Reads country boundary data from a binary file.
    /// </summary>
    public static BoundaryFileData Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);

        // Read and validate header
        var header = ReadHeader(reader);

        // Read country table
        stream.Seek((long)header.CountryTableOffset, SeekOrigin.Begin);
        var countries = new List<CountryBoundary>((int)header.CountryCount);
        var countryPolygonInfo = new List<(ushort PolygonCount, uint FirstPolygonIndex)>();

        for (int i = 0; i < header.CountryCount; i++)
        {
            var (country, polygonCount, firstPolygonIndex) = ReadCountryEntry(reader);
            countries.Add(country);
            countryPolygonInfo.Add((polygonCount, firstPolygonIndex));
        }

        // Read polygon data
        stream.Seek((long)header.PolygonDataOffset, SeekOrigin.Begin);
        var allPolygons = new List<Polygon>();
        for (int i = 0; i < header.TotalPolygons; i++)
        {
            allPolygons.Add(ReadPolygon(reader));
        }

        // Assign polygons to countries
        for (int i = 0; i < countries.Count; i++)
        {
            var (polygonCount, firstIndex) = countryPolygonInfo[i];
            var countryPolygons = new Polygon[polygonCount];
            for (int j = 0; j < polygonCount; j++)
            {
                countryPolygons[j] = allPolygons[(int)firstIndex + j];
            }

            // Rebuild country with polygons
            var oldCountry = countries[i];
            countries[i] = new CountryBoundary(
                oldCountry.CountryCode,
                oldCountry.Name,
                countryPolygons,
                oldCountry.CountryCode3);
        }

        // Read geohash cache
        stream.Seek((long)header.GeohashCacheOffset, SeekOrigin.Begin);
        var geohashCache = new Dictionary<string, string>();
        for (int i = 0; i < header.GeohashCacheCount; i++)
        {
            var (geohash, countryIndex) = ReadGeohashCacheEntry(reader);
            if (countryIndex < countries.Count)
            {
                geohashCache[geohash] = countries[countryIndex].CountryCode;
            }
        }

        // Read border cells
        var borderCells = new Dictionary<string, string[]>();
        for (int i = 0; i < header.BorderCellCount; i++)
        {
            var (geohash, candidateIndices) = ReadBorderCell(reader);
            var candidateCodes = new string[candidateIndices.Length];
            for (int j = 0; j < candidateIndices.Length; j++)
            {
                candidateCodes[j] = countries[candidateIndices[j]].CountryCode;
            }
            borderCells[geohash] = candidateCodes;
        }

        return new BoundaryFileData(countries, geohashCache, borderCells);
    }

    private static void WriteHeader(BinaryWriter writer, BoundaryFileHeader header)
    {
        writer.Write(Magic);
        writer.Write(header.Version);
        writer.Write(header.Flags);
        writer.Write(header.CountryCount);
        writer.Write((ushort)0); // Reserved
        writer.Write(header.TotalPolygons);
        writer.Write(header.TotalVertices);
        writer.Write(header.GeohashCacheCount);
        writer.Write(header.BorderCellCount);
        writer.Write(header.CountryTableOffset);
        writer.Write(header.PolygonDataOffset);
        writer.Write(header.GeohashCacheOffset);
    }

    private static BoundaryFileHeader ReadHeader(BinaryReader reader)
    {
        var magic = reader.ReadBytes(4);
        if (!magic.AsSpan().SequenceEqual(Magic))
            throw new InvalidDataException("Invalid boundary file magic bytes");

        return new BoundaryFileHeader
        {
            Version = reader.ReadUInt16(),
            Flags = reader.ReadUInt16(),
            CountryCount = reader.ReadUInt16(),
            Reserved = reader.ReadUInt16(),
            TotalPolygons = reader.ReadUInt32(),
            TotalVertices = reader.ReadUInt32(),
            GeohashCacheCount = reader.ReadUInt32(),
            BorderCellCount = reader.ReadUInt32(),
            CountryTableOffset = reader.ReadUInt64(),
            PolygonDataOffset = reader.ReadUInt64(),
            GeohashCacheOffset = reader.ReadUInt64(),
        };
    }

    private static void WriteCountryEntry(BinaryWriter writer, CountryBoundary country)
    {
        // Country codes (padded)
        var code2 = Encoding.ASCII.GetBytes(country.CountryCode.PadRight(2));
        var code3 = Encoding.ASCII.GetBytes((country.CountryCode3 ?? "").PadRight(3));
        writer.Write(code2, 0, 2);
        writer.Write(code3, 0, 3);

        // Name
        var nameBytes = Encoding.UTF8.GetBytes(country.Name);
        writer.Write((byte)Math.Min(nameBytes.Length, 255));
        writer.Write(nameBytes, 0, Math.Min(nameBytes.Length, 255));

        // Bounding box
        writer.Write((float)country.BoundingBox.MinLat);
        writer.Write((float)country.BoundingBox.MaxLat);
        writer.Write((float)country.BoundingBox.MinLon);
        writer.Write((float)country.BoundingBox.MaxLon);

        // Polygon info (will be set correctly in second pass if needed)
        writer.Write((ushort)country.Polygons.Length);
        writer.Write((uint)0); // FirstPolygonIndex - placeholder
    }

    private static (CountryBoundary Country, ushort PolygonCount, uint FirstPolygonIndex) ReadCountryEntry(BinaryReader reader)
    {
        var code2 = Encoding.ASCII.GetString(reader.ReadBytes(2)).Trim();
        var code3 = Encoding.ASCII.GetString(reader.ReadBytes(3)).Trim();

        var nameLength = reader.ReadByte();
        var name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));

        var minLat = reader.ReadSingle();
        var maxLat = reader.ReadSingle();
        var minLon = reader.ReadSingle();
        var maxLon = reader.ReadSingle();

        var polygonCount = reader.ReadUInt16();
        var firstPolygonIndex = reader.ReadUInt32();

        // Create placeholder country (polygons will be assigned later)
        var country = new CountryBoundary(
            code2,
            name,
            Array.Empty<Polygon>(),
            string.IsNullOrEmpty(code3) ? null : code3);

        return (country, polygonCount, firstPolygonIndex);
    }

    private static void WritePolygon(BinaryWriter writer, Polygon polygon)
    {
        // Exterior ring
        writer.Write((ushort)polygon.ExteriorRing.VertexCount);
        writer.Write((byte)polygon.Holes.Length);
        writer.Write((byte)0); // Reserved

        foreach (var point in polygon.ExteriorRing.Points)
        {
            var (latQ, lonQ) = point.ToQuantized();
            writer.Write(latQ);
            writer.Write(lonQ);
        }

        // Holes
        foreach (var hole in polygon.Holes)
        {
            writer.Write((ushort)hole.VertexCount);
            foreach (var point in hole.Points)
            {
                var (latQ, lonQ) = point.ToQuantized();
                writer.Write(latQ);
                writer.Write(lonQ);
            }
        }
    }

    private static Polygon ReadPolygon(BinaryReader reader)
    {
        var exteriorVertexCount = reader.ReadUInt16();
        var holeCount = reader.ReadByte();
        reader.ReadByte(); // Reserved

        // Read exterior ring
        var exteriorPoints = new GeoPoint[exteriorVertexCount];
        for (int i = 0; i < exteriorVertexCount; i++)
        {
            var latQ = reader.ReadInt16();
            var lonQ = reader.ReadInt16();
            exteriorPoints[i] = GeoPoint.FromQuantized(latQ, lonQ);
        }
        var exteriorRing = new PolygonRing(exteriorPoints, isHole: false);

        // Read holes
        var holes = new PolygonRing[holeCount];
        for (int h = 0; h < holeCount; h++)
        {
            var holeVertexCount = reader.ReadUInt16();
            var holePoints = new GeoPoint[holeVertexCount];
            for (int i = 0; i < holeVertexCount; i++)
            {
                var latQ = reader.ReadInt16();
                var lonQ = reader.ReadInt16();
                holePoints[i] = GeoPoint.FromQuantized(latQ, lonQ);
            }
            holes[h] = new PolygonRing(holePoints, isHole: true);
        }

        return new Polygon(exteriorRing, holes);
    }

    private static void WriteGeohashCacheEntry(BinaryWriter writer, string geohash, ushort countryIndex)
    {
        var geohashBytes = Encoding.ASCII.GetBytes(geohash.PadRight(4));
        writer.Write(geohashBytes, 0, 4);
        writer.Write(countryIndex);
        writer.Write((ushort)0); // Reserved
    }

    private static (string Geohash, ushort CountryIndex) ReadGeohashCacheEntry(BinaryReader reader)
    {
        var geohash = Encoding.ASCII.GetString(reader.ReadBytes(4)).Trim();
        var countryIndex = reader.ReadUInt16();
        reader.ReadUInt16(); // Reserved
        return (geohash, countryIndex);
    }

    private static void WriteBorderCell(BinaryWriter writer, string geohash, string[] candidates, 
        Dictionary<string, ushort> countryIndex)
    {
        var geohashBytes = Encoding.ASCII.GetBytes(geohash.PadRight(4));
        writer.Write(geohashBytes, 0, 4);
        writer.Write((byte)candidates.Length);

        foreach (var code in candidates)
        {
            writer.Write(countryIndex.GetValueOrDefault(code, ushort.MaxValue));
        }
    }

    private static (string Geohash, ushort[] CandidateIndices) ReadBorderCell(BinaryReader reader)
    {
        var geohash = Encoding.ASCII.GetString(reader.ReadBytes(4)).Trim();
        var count = reader.ReadByte();
        var indices = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            indices[i] = reader.ReadUInt16();
        }
        return (geohash, indices);
    }
}

/// <summary>
/// Header structure for boundary file.
/// </summary>
public struct BoundaryFileHeader
{
    public ushort Version;
    public ushort Flags;
    public ushort CountryCount;
    public ushort Reserved;
    public uint TotalPolygons;
    public uint TotalVertices;
    public uint GeohashCacheCount;
    public uint BorderCellCount;
    public ulong CountryTableOffset;
    public ulong PolygonDataOffset;
    public ulong GeohashCacheOffset;
}

/// <summary>
/// Container for data loaded from a boundary file.
/// </summary>
public sealed record BoundaryFileData(
    IReadOnlyList<CountryBoundary> Countries,
    IReadOnlyDictionary<string, string> GeohashCache,
    IReadOnlyDictionary<string, string[]> BorderCells);
