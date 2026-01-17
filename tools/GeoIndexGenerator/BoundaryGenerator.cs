using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GeoIndexGenerator;

/// <summary>
/// Generates country boundary index files from Natural Earth data.
/// 
/// Downloads and processes Natural Earth Admin 0 boundaries to create a compact
/// .geobounds file for country detection during reverse geocoding.
/// 
/// Usage:
///   GeoIndexGenerator --boundaries --output ./data
/// </summary>
public static class BoundaryGenerator
{
    private const string NaturalEarthUrl = "https://naciscdn.org/naturalearth/10m/cultural/ne_10m_admin_0_countries.zip";
    private const string GeoJsonFileName = "ne_10m_admin_0_countries.geojson";
    
    // Alternative: GeoJSON from GitHub (pre-converted)
    private const string GeoJsonDirectUrl = "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_10m_admin_0_countries.geojson";
    
    /// <summary>
    /// Generates boundary data from Natural Earth.
    /// </summary>
    public static async Task GenerateAsync(DirectoryInfo outputDir, double simplifyTolerance = 0.01)
    {
        Console.WriteLine("=== Boundary Generator ===");
        var sw = Stopwatch.StartNew();

        // Download GeoJSON directly (simpler than shapefile)
        Console.WriteLine($"Downloading Natural Earth boundaries from GitHub...");
        var geoJson = await DownloadGeoJsonAsync();
        
        Console.WriteLine($"Downloaded in {sw.Elapsed.TotalSeconds:F1}s");
        sw.Restart();

        // Parse countries
        Console.WriteLine("Parsing country boundaries...");
        var countries = ParseGeoJson(geoJson);
        Console.WriteLine($"Parsed {countries.Count} countries");

        // Simplify polygons
        Console.WriteLine($"Simplifying polygons (tolerance: {simplifyTolerance}°)...");
        int verticesBefore = countries.Sum(c => c.TotalVertexCount);
        SimplifyPolygons(countries, simplifyTolerance);
        int verticesAfter = countries.Sum(c => c.TotalVertexCount);
        Console.WriteLine($"Vertices: {verticesBefore:N0} -> {verticesAfter:N0} ({100.0 * verticesAfter / verticesBefore:F1}%)");

        // Build geohash cache
        Console.WriteLine("Building geohash cache...");
        var (geohashCache, borderCells) = BuildGeohashCache(countries, precision: 4);
        Console.WriteLine($"Cached cells: {geohashCache.Count}, Border cells: {borderCells.Count}");

        // Write output file
        var outputPath = Path.Combine(outputDir.FullName, "geo.geobounds");
        Console.WriteLine($"Writing {outputPath}...");
        WriteGeoBoundsFile(outputPath, countries, geohashCache, borderCells);

        var fileInfo = new FileInfo(outputPath);
        Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:F1}s, output size: {fileInfo.Length / 1024.0:F1} KB");
    }

    private static async Task<string> DownloadGeoJsonAsync()
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        return await client.GetStringAsync(GeoJsonDirectUrl);
    }

    private static List<CountryBoundaryData> ParseGeoJson(string geoJson)
    {
        var countries = new List<CountryBoundaryData>();
        
        using var doc = JsonDocument.Parse(geoJson);
        var root = doc.RootElement;
        var features = root.GetProperty("features");

        foreach (var feature in features.EnumerateArray())
        {
            var properties = feature.GetProperty("properties");
            var geometry = feature.GetProperty("geometry");

            // Extract country code (try different property names)
            string? countryCode = GetPropertyString(properties, "ISO_A2") 
                ?? GetPropertyString(properties, "ISO_A2_EH")
                ?? GetPropertyString(properties, "ADM0_A3")?.Substring(0, 2);

            if (string.IsNullOrEmpty(countryCode) || countryCode == "-1" || countryCode == "-99")
            {
                // Skip countries without valid ISO code
                continue;
            }

            string name = GetPropertyString(properties, "NAME") ?? GetPropertyString(properties, "ADMIN") ?? countryCode;
            string? code3 = GetPropertyString(properties, "ISO_A3") ?? GetPropertyString(properties, "ADM0_A3");

            var polygons = ParseGeometry(geometry);
            if (polygons.Count > 0)
            {
                countries.Add(new CountryBoundaryData
                {
                    CountryCode = countryCode.ToUpperInvariant(),
                    CountryCode3 = code3?.ToUpperInvariant(),
                    Name = name,
                    Polygons = polygons
                });
            }
        }

        return countries;
    }

    private static string? GetPropertyString(JsonElement properties, string name)
    {
        if (properties.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var str = value.GetString();
            return string.IsNullOrEmpty(str) || str == "-99" || str == "-1" ? null : str;
        }
        return null;
    }

    private static List<PolygonData> ParseGeometry(JsonElement geometry)
    {
        var polygons = new List<PolygonData>();
        var type = geometry.GetProperty("type").GetString();
        var coordinates = geometry.GetProperty("coordinates");

        if (type == "Polygon")
        {
            var polygon = ParsePolygonCoordinates(coordinates);
            if (polygon != null)
                polygons.Add(polygon);
        }
        else if (type == "MultiPolygon")
        {
            foreach (var polygonCoords in coordinates.EnumerateArray())
            {
                var polygon = ParsePolygonCoordinates(polygonCoords);
                if (polygon != null)
                    polygons.Add(polygon);
            }
        }

        return polygons;
    }

    private static PolygonData? ParsePolygonCoordinates(JsonElement coordinates)
    {
        var rings = new List<List<GeoPointData>>();

        foreach (var ring in coordinates.EnumerateArray())
        {
            var points = new List<GeoPointData>();
            foreach (var coord in ring.EnumerateArray())
            {
                var lon = coord[0].GetDouble();
                var lat = coord[1].GetDouble();
                points.Add(new GeoPointData(lat, lon));
            }
            if (points.Count >= 3)
                rings.Add(points);
        }

        if (rings.Count == 0)
            return null;

        return new PolygonData
        {
            ExteriorRing = rings[0],
            Holes = rings.Skip(1).ToList()
        };
    }

    private static void SimplifyPolygons(List<CountryBoundaryData> countries, double tolerance)
    {
        foreach (var country in countries)
        {
            foreach (var polygon in country.Polygons)
            {
                polygon.ExteriorRing = SimplifyRing(polygon.ExteriorRing, tolerance);
                polygon.Holes = polygon.Holes.Select(h => SimplifyRing(h, tolerance)).ToList();
            }
        }
    }

    /// <summary>
    /// Douglas-Peucker line simplification algorithm.
    /// </summary>
    private static List<GeoPointData> SimplifyRing(List<GeoPointData> points, double tolerance)
    {
        if (points.Count <= 4)
            return points;

        var result = new List<GeoPointData> { points[0] };
        SimplifyDPRecursive(points, 0, points.Count - 1, tolerance, result);
        result.Add(points[^1]);

        // Ensure ring is closed
        if (result.Count >= 3 && (result[0].Lat != result[^1].Lat || result[0].Lon != result[^1].Lon))
        {
            result.Add(result[0]);
        }

        return result;
    }

    private static void SimplifyDPRecursive(List<GeoPointData> points, int start, int end, double tolerance, List<GeoPointData> result)
    {
        if (end - start <= 1)
            return;

        double maxDist = 0;
        int maxIndex = start;

        var startPt = points[start];
        var endPt = points[end];

        for (int i = start + 1; i < end; i++)
        {
            double dist = PerpendicularDistance(points[i], startPt, endPt);
            if (dist > maxDist)
            {
                maxDist = dist;
                maxIndex = i;
            }
        }

        if (maxDist > tolerance)
        {
            SimplifyDPRecursive(points, start, maxIndex, tolerance, result);
            result.Add(points[maxIndex]);
            SimplifyDPRecursive(points, maxIndex, end, tolerance, result);
        }
    }

    private static double PerpendicularDistance(GeoPointData point, GeoPointData lineStart, GeoPointData lineEnd)
    {
        double dx = lineEnd.Lon - lineStart.Lon;
        double dy = lineEnd.Lat - lineStart.Lat;

        double lengthSq = dx * dx + dy * dy;
        if (lengthSq == 0)
            return Math.Sqrt(Math.Pow(point.Lon - lineStart.Lon, 2) + Math.Pow(point.Lat - lineStart.Lat, 2));

        double t = Math.Max(0, Math.Min(1, ((point.Lon - lineStart.Lon) * dx + (point.Lat - lineStart.Lat) * dy) / lengthSq));

        double projLon = lineStart.Lon + t * dx;
        double projLat = lineStart.Lat + t * dy;

        return Math.Sqrt(Math.Pow(point.Lon - projLon, 2) + Math.Pow(point.Lat - projLat, 2));
    }

    private static (Dictionary<string, string> GeohashCache, Dictionary<string, string[]> BorderCells) BuildGeohashCache(
        List<CountryBoundaryData> countries, int precision)
    {
        var geohashCache = new Dictionary<string, string>();
        var borderCells = new Dictionary<string, string[]>();

        // Build bounding box lookup for countries
        var countryBounds = countries.ToDictionary(
            c => c.CountryCode,
            c => ComputeBoundingBox(c.Polygons));

        // Generate all possible land geohash cells and check which countries they belong to
        // This is approximate - we sample the center and corners of each cell
        
        // Instead of iterating all possible cells (expensive), we iterate cells that
        // overlap with country bounding boxes
        
        var cellsChecked = new HashSet<string>();
        
        foreach (var country in countries)
        {
            var bounds = countryBounds[country.CountryCode];
            
            // Get all geohash cells that overlap with this country's bounding box
            var cells = GetGeohashCellsInBounds(bounds, precision);
            
            foreach (var cell in cells)
            {
                if (cellsChecked.Contains(cell))
                    continue;
                cellsChecked.Add(cell);

                // Check which countries this cell might belong to
                var candidates = new List<string>();
                var (minLat, maxLat, minLon, maxLon) = DecodeGeohashBounds(cell);
                
                foreach (var c in countries)
                {
                    if (!countryBounds[c.CountryCode].Intersects(minLat, maxLat, minLon, maxLon))
                        continue;

                    // Check if cell center is in this country
                    double centerLat = (minLat + maxLat) / 2;
                    double centerLon = (minLon + maxLon) / 2;
                    
                    if (IsPointInCountry(centerLat, centerLon, c))
                    {
                        candidates.Add(c.CountryCode);
                    }
                }

                if (candidates.Count == 1)
                {
                    geohashCache[cell] = candidates[0];
                }
                else if (candidates.Count > 1)
                {
                    borderCells[cell] = candidates.ToArray();
                }
            }
        }

        return (geohashCache, borderCells);
    }

    private static bool IsPointInCountry(double lat, double lon, CountryBoundaryData country)
    {
        foreach (var polygon in country.Polygons)
        {
            if (IsPointInPolygon(lat, lon, polygon))
                return true;
        }
        return false;
    }

    private static bool IsPointInPolygon(double lat, double lon, PolygonData polygon)
    {
        // Quick bounding box check
        var bounds = ComputeRingBounds(polygon.ExteriorRing);
        if (lat < bounds.MinLat || lat > bounds.MaxLat || lon < bounds.MinLon || lon > bounds.MaxLon)
            return false;

        // Ray casting algorithm
        if (!IsPointInRing(lat, lon, polygon.ExteriorRing))
            return false;

        // Check holes
        foreach (var hole in polygon.Holes)
        {
            if (IsPointInRing(lat, lon, hole))
                return false;
        }

        return true;
    }

    private static bool IsPointInRing(double lat, double lon, List<GeoPointData> ring)
    {
        bool inside = false;
        int n = ring.Count;

        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double yi = ring[i].Lat;
            double yj = ring[j].Lat;
            double xi = ring[i].Lon;
            double xj = ring[j].Lon;

            if (((yi > lat) != (yj > lat)) &&
                (lon < (xj - xi) * (lat - yi) / (yj - yi) + xi))
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static BoundsData ComputeBoundingBox(List<PolygonData> polygons)
    {
        double minLat = double.MaxValue, maxLat = double.MinValue;
        double minLon = double.MaxValue, maxLon = double.MinValue;

        foreach (var polygon in polygons)
        {
            foreach (var point in polygon.ExteriorRing)
            {
                if (point.Lat < minLat) minLat = point.Lat;
                if (point.Lat > maxLat) maxLat = point.Lat;
                if (point.Lon < minLon) minLon = point.Lon;
                if (point.Lon > maxLon) maxLon = point.Lon;
            }
        }

        return new BoundsData(minLat, maxLat, minLon, maxLon);
    }

    private static BoundsData ComputeRingBounds(List<GeoPointData> ring)
    {
        double minLat = double.MaxValue, maxLat = double.MinValue;
        double minLon = double.MaxValue, maxLon = double.MinValue;

        foreach (var point in ring)
        {
            if (point.Lat < minLat) minLat = point.Lat;
            if (point.Lat > maxLat) maxLat = point.Lat;
            if (point.Lon < minLon) minLon = point.Lon;
            if (point.Lon > maxLon) maxLon = point.Lon;
        }

        return new BoundsData(minLat, maxLat, minLon, maxLon);
    }

    private static HashSet<string> GetGeohashCellsInBounds(BoundsData bounds, int precision)
    {
        var cells = new HashSet<string>();
        
        // Calculate step size for this precision
        double latStep = 180.0 / Math.Pow(2, (precision * 5) / 2);
        double lonStep = 360.0 / Math.Pow(2, (precision * 5 + 1) / 2);

        for (double lat = bounds.MinLat; lat <= bounds.MaxLat; lat += latStep / 2)
        {
            for (double lon = bounds.MinLon; lon <= bounds.MaxLon; lon += lonStep / 2)
            {
                cells.Add(EncodeGeohash(lat, lon, precision));
            }
        }

        return cells;
    }

    private static string EncodeGeohash(double lat, double lon, int precision)
    {
        const string base32 = "0123456789bcdefghjkmnpqrstuvwxyz";
        
        double minLat = -90, maxLat = 90;
        double minLon = -180, maxLon = 180;
        
        var result = new StringBuilder(precision);
        bool isLon = true;
        int bit = 0;
        int ch = 0;

        while (result.Length < precision)
        {
            if (isLon)
            {
                double mid = (minLon + maxLon) / 2;
                if (lon >= mid)
                {
                    ch |= 1 << (4 - bit);
                    minLon = mid;
                }
                else
                {
                    maxLon = mid;
                }
            }
            else
            {
                double mid = (minLat + maxLat) / 2;
                if (lat >= mid)
                {
                    ch |= 1 << (4 - bit);
                    minLat = mid;
                }
                else
                {
                    maxLat = mid;
                }
            }

            isLon = !isLon;
            bit++;

            if (bit == 5)
            {
                result.Append(base32[ch]);
                bit = 0;
                ch = 0;
            }
        }

        return result.ToString();
    }

    private static (double MinLat, double MaxLat, double MinLon, double MaxLon) DecodeGeohashBounds(string geohash)
    {
        const string base32 = "0123456789bcdefghjkmnpqrstuvwxyz";
        
        double minLat = -90, maxLat = 90;
        double minLon = -180, maxLon = 180;
        bool isLon = true;

        foreach (char c in geohash)
        {
            int val = base32.IndexOf(c);
            if (val < 0) continue;

            for (int bit = 4; bit >= 0; bit--)
            {
                if (isLon)
                {
                    double mid = (minLon + maxLon) / 2;
                    if ((val & (1 << bit)) != 0)
                        minLon = mid;
                    else
                        maxLon = mid;
                }
                else
                {
                    double mid = (minLat + maxLat) / 2;
                    if ((val & (1 << bit)) != 0)
                        minLat = mid;
                    else
                        maxLat = mid;
                }
                isLon = !isLon;
            }
        }

        return (minLat, maxLat, minLon, maxLon);
    }

    private static void WriteGeoBoundsFile(
        string path,
        List<CountryBoundaryData> countries,
        Dictionary<string, string> geohashCache,
        Dictionary<string, string[]> borderCells)
    {
        // Build country index
        var countryIndex = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < countries.Count; i++)
            countryIndex[countries[i].CountryCode] = (ushort)i;

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream);

        // Count totals
        uint totalPolygons = (uint)countries.Sum(c => c.Polygons.Count);
        uint totalVertices = (uint)countries.Sum(c => c.TotalVertexCount);

        // Reserve space for header
        writer.Write(new byte[48]);

        // Write country table
        long countryTableOffset = stream.Position;
        uint polygonIndex = 0;
        foreach (var country in countries)
        {
            WriteCountryEntry(writer, country, polygonIndex);
            polygonIndex += (uint)country.Polygons.Count;
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
        foreach (var (geohash, code) in geohashCache)
        {
            WriteGeohashCacheEntry(writer, geohash, countryIndex.GetValueOrDefault(code, ushort.MaxValue));
        }

        // Write border cells
        foreach (var (geohash, candidates) in borderCells)
        {
            WriteBorderCell(writer, geohash, candidates, countryIndex);
        }

        // Write header
        stream.Seek(0, SeekOrigin.Begin);
        writer.Write(Encoding.ASCII.GetBytes("PGB1")); // Magic
        writer.Write((ushort)1); // Version
        writer.Write((ushort)0); // Flags
        writer.Write((ushort)countries.Count);
        writer.Write((ushort)0); // Reserved
        writer.Write(totalPolygons);
        writer.Write(totalVertices);
        writer.Write((uint)geohashCache.Count);
        writer.Write((uint)borderCells.Count);
        writer.Write((ulong)countryTableOffset);
        writer.Write((ulong)polygonDataOffset);
        writer.Write((ulong)geohashCacheOffset);
    }

    private static void WriteCountryEntry(BinaryWriter writer, CountryBoundaryData country, uint firstPolygonIndex)
    {
        var code2 = Encoding.ASCII.GetBytes(country.CountryCode.PadRight(2));
        var code3 = Encoding.ASCII.GetBytes((country.CountryCode3 ?? "").PadRight(3));
        writer.Write(code2, 0, 2);
        writer.Write(code3, 0, 3);

        var nameBytes = Encoding.UTF8.GetBytes(country.Name);
        writer.Write((byte)Math.Min(nameBytes.Length, 255));
        writer.Write(nameBytes, 0, Math.Min(nameBytes.Length, 255));

        var bounds = ComputeBoundingBox(country.Polygons);
        writer.Write((float)bounds.MinLat);
        writer.Write((float)bounds.MaxLat);
        writer.Write((float)bounds.MinLon);
        writer.Write((float)bounds.MaxLon);

        writer.Write((ushort)country.Polygons.Count);
        writer.Write(firstPolygonIndex);
    }

    private static void WritePolygon(BinaryWriter writer, PolygonData polygon)
    {
        writer.Write((ushort)polygon.ExteriorRing.Count);
        writer.Write((byte)polygon.Holes.Count);
        writer.Write((byte)0); // Reserved

        foreach (var point in polygon.ExteriorRing)
        {
            writer.Write((short)(point.Lat * 100));
            writer.Write((short)(point.Lon * 100));
        }

        foreach (var hole in polygon.Holes)
        {
            writer.Write((ushort)hole.Count);
            foreach (var point in hole)
            {
                writer.Write((short)(point.Lat * 100));
                writer.Write((short)(point.Lon * 100));
            }
        }
    }

    private static void WriteGeohashCacheEntry(BinaryWriter writer, string geohash, ushort countryIndex)
    {
        var geohashBytes = Encoding.ASCII.GetBytes(geohash.PadRight(4));
        writer.Write(geohashBytes, 0, 4);
        writer.Write(countryIndex);
        writer.Write((ushort)0);
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

    // Internal data classes
    private class CountryBoundaryData
    {
        public required string CountryCode { get; init; }
        public string? CountryCode3 { get; init; }
        public required string Name { get; init; }
        public required List<PolygonData> Polygons { get; init; }
        
        public int TotalVertexCount => Polygons.Sum(p => 
            p.ExteriorRing.Count + p.Holes.Sum(h => h.Count));
    }

    private class PolygonData
    {
        public required List<GeoPointData> ExteriorRing { get; set; }
        public required List<List<GeoPointData>> Holes { get; set; }
    }

    private readonly record struct GeoPointData(double Lat, double Lon);

    private readonly record struct BoundsData(double MinLat, double MaxLat, double MinLon, double MaxLon)
    {
        public bool Intersects(double minLat, double maxLat, double minLon, double maxLon)
        {
            return MinLat <= maxLat && MaxLat >= minLat &&
                   MinLon <= maxLon && MaxLon >= minLon;
        }
    }
}
