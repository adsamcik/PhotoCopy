using System;

namespace PhotoCopy.Files.Geo.Boundaries;

/// <summary>
/// Provides efficient point-in-polygon testing using the ray-casting algorithm.
/// This algorithm counts how many times a ray from the point intersects the polygon boundary.
/// If odd, the point is inside; if even, it's outside.
/// </summary>
public static class PointInPolygon
{
    /// <summary>
    /// Tests if a point is inside a polygon ring using the ray-casting algorithm.
    /// </summary>
    /// <param name="latitude">Latitude of the test point.</param>
    /// <param name="longitude">Longitude of the test point.</param>
    /// <param name="ring">The polygon ring to test against.</param>
    /// <returns>True if the point is inside the ring.</returns>
    public static bool IsPointInRing(double latitude, double longitude, PolygonRing ring)
    {
        // Quick bounding box rejection
        if (!ring.BoundingBox.Contains(latitude, longitude))
            return false;

        return IsPointInRingCore(latitude, longitude, ring.Points);
    }

    /// <summary>
    /// Core ray-casting algorithm without bounding box check.
    /// </summary>
    private static bool IsPointInRingCore(double latitude, double longitude, GeoPoint[] points)
    {
        int n = points.Length;
        if (n < 3)
            return false;

        bool inside = false;

        // Ray-casting algorithm: count edge crossings
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double yi = points[i].Latitude;
            double yj = points[j].Latitude;
            double xi = points[i].Longitude;
            double xj = points[j].Longitude;

            // Check if the ray from the point going right crosses this edge
            if (((yi > latitude) != (yj > latitude)) &&
                (longitude < (xj - xi) * (latitude - yi) / (yj - yi) + xi))
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// Tests if a point is inside a polygon, accounting for holes.
    /// A point is inside if it's in the exterior ring but not in any hole.
    /// </summary>
    /// <param name="latitude">Latitude of the test point.</param>
    /// <param name="longitude">Longitude of the test point.</param>
    /// <param name="polygon">The polygon to test against.</param>
    /// <returns>True if the point is inside the polygon (exterior but not in holes).</returns>
    public static bool IsPointInPolygon(double latitude, double longitude, Polygon polygon)
    {
        // Quick bounding box rejection
        if (!polygon.BoundingBox.Contains(latitude, longitude))
            return false;

        // Must be inside exterior ring
        if (!IsPointInRingCore(latitude, longitude, polygon.ExteriorRing.Points))
            return false;

        // Must not be inside any hole
        foreach (var hole in polygon.Holes)
        {
            if (hole.BoundingBox.Contains(latitude, longitude) &&
                IsPointInRingCore(latitude, longitude, hole.Points))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Tests if a point is inside a multi-polygon (country boundary).
    /// Returns true if the point is inside any of the constituent polygons.
    /// </summary>
    /// <param name="latitude">Latitude of the test point.</param>
    /// <param name="longitude">Longitude of the test point.</param>
    /// <param name="boundary">The country boundary to test against.</param>
    /// <returns>True if the point is inside the country boundary.</returns>
    public static bool IsPointInCountry(double latitude, double longitude, CountryBoundary boundary)
    {
        // Quick bounding box rejection
        if (!boundary.BoundingBox.Contains(latitude, longitude))
            return false;

        // Check each polygon
        foreach (var polygon in boundary.Polygons)
        {
            if (IsPointInPolygon(latitude, longitude, polygon))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Tests if a point is on or very close to a polygon edge.
    /// Uses a small epsilon for floating-point comparison.
    /// </summary>
    /// <param name="latitude">Latitude of the test point.</param>
    /// <param name="longitude">Longitude of the test point.</param>
    /// <param name="ring">The polygon ring to test against.</param>
    /// <param name="epsilon">Tolerance for edge detection (in degrees).</param>
    /// <returns>True if the point is on or near an edge.</returns>
    public static bool IsPointOnEdge(double latitude, double longitude, PolygonRing ring, double epsilon = 0.0001)
    {
        var points = ring.Points;
        int n = points.Length;

        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            if (DistanceToSegment(latitude, longitude, 
                points[j].Latitude, points[j].Longitude,
                points[i].Latitude, points[i].Longitude) < epsilon)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Calculates the perpendicular distance from a point to a line segment.
    /// </summary>
    private static double DistanceToSegment(
        double px, double py,
        double ax, double ay,
        double bx, double by)
    {
        double dx = bx - ax;
        double dy = by - ay;
        double lengthSq = dx * dx + dy * dy;

        if (lengthSq == 0)
        {
            // Segment is a point
            return Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
        }

        // Parameter t of the closest point on the segment
        double t = Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / lengthSq));

        // Closest point on the segment
        double closestX = ax + t * dx;
        double closestY = ay + t * dy;

        return Math.Sqrt((px - closestX) * (px - closestX) + (py - closestY) * (py - closestY));
    }

    /// <summary>
    /// Normalizes longitude to the range [-180, 180].
    /// </summary>
    public static double NormalizeLongitude(double longitude)
    {
        while (longitude > 180) longitude -= 360;
        while (longitude < -180) longitude += 360;
        return longitude;
    }

    /// <summary>
    /// Clamps latitude to the valid range [-90, 90].
    /// </summary>
    public static double ClampLatitude(double latitude)
    {
        return Math.Max(-90, Math.Min(90, latitude));
    }
}
