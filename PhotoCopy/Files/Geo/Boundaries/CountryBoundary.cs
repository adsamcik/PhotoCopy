using System;
using System.Collections.Generic;

namespace PhotoCopy.Files.Geo.Boundaries;

/// <summary>
/// Represents a geographic point with latitude and longitude.
/// Uses double precision for accurate calculations.
/// </summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    /// <summary>
    /// Creates a GeoPoint from quantized int16 coordinates.
    /// </summary>
    public static GeoPoint FromQuantized(short latQ, short lonQ)
    {
        // Quantized coordinates are stored as int16 with 0.01 degree precision
        return new GeoPoint(latQ / 100.0, lonQ / 100.0);
    }

    /// <summary>
    /// Converts to quantized int16 coordinates for compact storage.
    /// </summary>
    public (short LatQ, short LonQ) ToQuantized()
    {
        return ((short)(Latitude * 100), (short)(Longitude * 100));
    }
}

/// <summary>
/// Represents a bounding box for fast rejection tests.
/// </summary>
public readonly record struct BoundingBox(double MinLat, double MaxLat, double MinLon, double MaxLon)
{
    /// <summary>
    /// Checks if a point is within this bounding box.
    /// </summary>
    public bool Contains(double latitude, double longitude)
    {
        return latitude >= MinLat && latitude <= MaxLat &&
               longitude >= MinLon && longitude <= MaxLon;
    }

    /// <summary>
    /// Checks if this bounding box intersects with another.
    /// </summary>
    public bool Intersects(BoundingBox other)
    {
        return MinLat <= other.MaxLat && MaxLat >= other.MinLat &&
               MinLon <= other.MaxLon && MaxLon >= other.MinLon;
    }

    /// <summary>
    /// Creates a bounding box from a collection of points.
    /// </summary>
    public static BoundingBox FromPoints(IEnumerable<GeoPoint> points)
    {
        double minLat = double.MaxValue, maxLat = double.MinValue;
        double minLon = double.MaxValue, maxLon = double.MinValue;

        foreach (var point in points)
        {
            if (point.Latitude < minLat) minLat = point.Latitude;
            if (point.Latitude > maxLat) maxLat = point.Latitude;
            if (point.Longitude < minLon) minLon = point.Longitude;
            if (point.Longitude > maxLon) maxLon = point.Longitude;
        }

        return new BoundingBox(minLat, maxLat, minLon, maxLon);
    }
}

/// <summary>
/// Represents a polygon ring (closed loop of points).
/// Can be an exterior ring (counter-clockwise) or a hole (clockwise).
/// </summary>
public sealed class PolygonRing
{
    /// <summary>
    /// The points forming this ring. First and last point should be the same.
    /// </summary>
    public GeoPoint[] Points { get; }

    /// <summary>
    /// Pre-computed bounding box for fast rejection.
    /// </summary>
    public BoundingBox BoundingBox { get; }

    /// <summary>
    /// Whether this ring represents a hole in a polygon.
    /// </summary>
    public bool IsHole { get; }

    public PolygonRing(GeoPoint[] points, bool isHole = false)
    {
        Points = points ?? throw new ArgumentNullException(nameof(points));
        IsHole = isHole;
        BoundingBox = BoundingBox.FromPoints(points);
    }

    /// <summary>
    /// Number of vertices in this ring.
    /// </summary>
    public int VertexCount => Points.Length;
}

/// <summary>
/// Represents a polygon with an exterior ring and optional holes.
/// </summary>
public sealed class Polygon
{
    /// <summary>
    /// The exterior boundary of the polygon.
    /// </summary>
    public PolygonRing ExteriorRing { get; }

    /// <summary>
    /// Interior rings representing holes in the polygon (e.g., enclaves).
    /// </summary>
    public PolygonRing[] Holes { get; }

    /// <summary>
    /// Pre-computed bounding box for fast rejection.
    /// </summary>
    public BoundingBox BoundingBox => ExteriorRing.BoundingBox;

    public Polygon(PolygonRing exteriorRing, PolygonRing[]? holes = null)
    {
        ExteriorRing = exteriorRing ?? throw new ArgumentNullException(nameof(exteriorRing));
        Holes = holes ?? Array.Empty<PolygonRing>();
    }

    /// <summary>
    /// Total number of vertices across all rings.
    /// </summary>
    public int TotalVertexCount => ExteriorRing.VertexCount + 
        Array.ConvertAll(Holes, h => h.VertexCount).Sum();
}

/// <summary>
/// Represents a country boundary which may consist of multiple polygons
/// (e.g., for countries with islands or non-contiguous territories).
/// </summary>
public sealed class CountryBoundary
{
    /// <summary>
    /// ISO 3166-1 alpha-2 country code (e.g., "US", "SK", "AT").
    /// </summary>
    public string CountryCode { get; }

    /// <summary>
    /// ISO 3166-1 alpha-3 country code (e.g., "USA", "SVK", "AUT").
    /// </summary>
    public string? CountryCode3 { get; }

    /// <summary>
    /// Full country name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// All polygons forming this country's boundary.
    /// </summary>
    public Polygon[] Polygons { get; }

    /// <summary>
    /// Combined bounding box of all polygons.
    /// </summary>
    public BoundingBox BoundingBox { get; }

    /// <summary>
    /// Total vertex count across all polygons.
    /// </summary>
    public int TotalVertexCount { get; }

    public CountryBoundary(string countryCode, string name, Polygon[] polygons, string? countryCode3 = null)
    {
        CountryCode = countryCode ?? throw new ArgumentNullException(nameof(countryCode));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Polygons = polygons ?? throw new ArgumentNullException(nameof(polygons));
        CountryCode3 = countryCode3;

        // Compute combined bounding box
        if (polygons.Length > 0)
        {
            double minLat = double.MaxValue, maxLat = double.MinValue;
            double minLon = double.MaxValue, maxLon = double.MinValue;

            foreach (var polygon in polygons)
            {
                var bbox = polygon.BoundingBox;
                if (bbox.MinLat < minLat) minLat = bbox.MinLat;
                if (bbox.MaxLat > maxLat) maxLat = bbox.MaxLat;
                if (bbox.MinLon < minLon) minLon = bbox.MinLon;
                if (bbox.MaxLon > maxLon) maxLon = bbox.MaxLon;
            }

            BoundingBox = new BoundingBox(minLat, maxLat, minLon, maxLon);
        }

        TotalVertexCount = Array.ConvertAll(polygons, p => p.TotalVertexCount).Sum();
    }

    public override string ToString() => $"{Name} ({CountryCode})";
}

/// <summary>
/// Static helper methods for array summation (to avoid LINQ overhead in hot paths).
/// </summary>
internal static class ArrayExtensions
{
    public static int Sum(this int[] array)
    {
        int sum = 0;
        foreach (var item in array)
            sum += item;
        return sum;
    }
}
