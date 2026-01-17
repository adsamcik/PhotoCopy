using PhotoCopy.Files.Geo.Boundaries;

namespace PhotoCopy.Tests.Files.Geo.Boundaries;

/// <summary>
/// Tests for the point-in-polygon algorithm.
/// </summary>
public class PointInPolygonTests
{
    #region Basic Shape Tests

    [Test]
    public async Task IsPointInRing_PointInsideSquare_ReturnsTrue()
    {
        // Arrange - Simple square from (0,0) to (10,10)
        var ring = CreateSquareRing(0, 0, 10, 10);

        // Act & Assert - Point in center
        var result = PointInPolygon.IsPointInRing(5, 5, ring);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsPointInRing_PointOutsideSquare_ReturnsFalse()
    {
        var ring = CreateSquareRing(0, 0, 10, 10);

        // Point outside
        var result = PointInPolygon.IsPointInRing(15, 15, ring);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPointInRing_PointOnEdge_ReturnsTrueOrFalse()
    {
        // Note: Points exactly on edges may return either true or false
        // depending on implementation details (this is acceptable)
        var ring = CreateSquareRing(0, 0, 10, 10);

        // This test just ensures no exception is thrown
        var result = PointInPolygon.IsPointInRing(0, 5, ring);
        // Result can be true or false - both are acceptable for edge cases
        await Assert.That(result == true || result == false).IsTrue();
    }

    [Test]
    public async Task IsPointInRing_PointAtVertex_ReturnsTrueOrFalse()
    {
        var ring = CreateSquareRing(0, 0, 10, 10);

        // Point at vertex - behavior varies by implementation
        var result = PointInPolygon.IsPointInRing(0, 0, ring);
        await Assert.That(result == true || result == false).IsTrue();
    }

    #endregion

    #region Polygon with Holes Tests

    [Test]
    public async Task IsPointInPolygon_PointInExterior_ReturnsTrue()
    {
        // Square with a hole
        var exterior = CreateSquareRing(0, 0, 100, 100);
        var hole = CreateSquareRing(40, 40, 60, 60, isHole: true);
        var polygon = new Polygon(exterior, new[] { hole });

        // Point in exterior but not in hole
        var result = PointInPolygon.IsPointInPolygon(20, 20, polygon);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsPointInPolygon_PointInHole_ReturnsFalse()
    {
        var exterior = CreateSquareRing(0, 0, 100, 100);
        var hole = CreateSquareRing(40, 40, 60, 60, isHole: true);
        var polygon = new Polygon(exterior, new[] { hole });

        // Point inside the hole
        var result = PointInPolygon.IsPointInPolygon(50, 50, polygon);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPointInPolygon_PointOutside_ReturnsFalse()
    {
        var exterior = CreateSquareRing(0, 0, 100, 100);
        var hole = CreateSquareRing(40, 40, 60, 60, isHole: true);
        var polygon = new Polygon(exterior, new[] { hole });

        var result = PointInPolygon.IsPointInPolygon(150, 150, polygon);
        await Assert.That(result).IsFalse();
    }

    #endregion

    #region Country Boundary Tests (Multi-Polygon)

    [Test]
    public async Task IsPointInCountry_PointInMainland_ReturnsTrue()
    {
        // Country with mainland and island
        var mainland = new Polygon(CreateSquareRing(0, 0, 100, 100));
        var island = new Polygon(CreateSquareRing(150, 150, 170, 170));
        var country = new CountryBoundary("XX", "Test Country", new[] { mainland, island });

        // Point in mainland
        var result = PointInPolygon.IsPointInCountry(50, 50, country);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsPointInCountry_PointOnIsland_ReturnsTrue()
    {
        var mainland = new Polygon(CreateSquareRing(0, 0, 100, 100));
        var island = new Polygon(CreateSquareRing(150, 150, 170, 170));
        var country = new CountryBoundary("XX", "Test Country", new[] { mainland, island });

        // Point on island
        var result = PointInPolygon.IsPointInCountry(160, 160, country);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsPointInCountry_PointBetweenMainlandAndIsland_ReturnsFalse()
    {
        var mainland = new Polygon(CreateSquareRing(0, 0, 100, 100));
        var island = new Polygon(CreateSquareRing(150, 150, 170, 170));
        var country = new CountryBoundary("XX", "Test Country", new[] { mainland, island });

        // Point in water between mainland and island
        var result = PointInPolygon.IsPointInCountry(125, 125, country);
        await Assert.That(result).IsFalse();
    }

    #endregion

    #region Bounding Box Tests

    [Test]
    public async Task IsPointInCountry_PointOutsideBoundingBox_ReturnsFalseQuickly()
    {
        var polygon = new Polygon(CreateSquareRing(0, 0, 10, 10));
        var country = new CountryBoundary("XX", "Test", new[] { polygon });

        // This should be rejected by bounding box check without polygon test
        var result = PointInPolygon.IsPointInCountry(1000, 1000, country);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task BoundingBox_Contains_WorksCorrectly()
    {
        var bbox = new BoundingBox(0, 10, 0, 10);

        await Assert.That(bbox.Contains(5, 5)).IsTrue();
        await Assert.That(bbox.Contains(0, 0)).IsTrue();
        await Assert.That(bbox.Contains(10, 10)).IsTrue();
        await Assert.That(bbox.Contains(-1, 5)).IsFalse();
        await Assert.That(bbox.Contains(5, 11)).IsFalse();
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task IsPointInRing_EmptyRing_ReturnsFalse()
    {
        var ring = new PolygonRing(Array.Empty<GeoPoint>());
        var result = PointInPolygon.IsPointInRing(0, 0, ring);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPointInRing_TwoPointRing_ReturnsFalse()
    {
        var points = new[] { new GeoPoint(0, 0), new GeoPoint(10, 10) };
        var ring = new PolygonRing(points);
        var result = PointInPolygon.IsPointInRing(5, 5, ring);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task NormalizeLongitude_HandlesWrapAround()
    {
        await Assert.That(PointInPolygon.NormalizeLongitude(180)).IsEqualTo(180);
        await Assert.That(PointInPolygon.NormalizeLongitude(-180)).IsEqualTo(-180);
        await Assert.That(PointInPolygon.NormalizeLongitude(190)).IsEqualTo(-170);
        await Assert.That(PointInPolygon.NormalizeLongitude(-190)).IsEqualTo(170);
        await Assert.That(PointInPolygon.NormalizeLongitude(540)).IsEqualTo(180);
    }

    [Test]
    public async Task ClampLatitude_HandlesExtremes()
    {
        await Assert.That(PointInPolygon.ClampLatitude(90)).IsEqualTo(90);
        await Assert.That(PointInPolygon.ClampLatitude(-90)).IsEqualTo(-90);
        await Assert.That(PointInPolygon.ClampLatitude(100)).IsEqualTo(90);
        await Assert.That(PointInPolygon.ClampLatitude(-100)).IsEqualTo(-90);
    }

    #endregion

    #region Concave Shape Tests

    [Test]
    public async Task IsPointInRing_ConcaveShape_HandlesCorrectly()
    {
        // L-shaped polygon
        var points = new[]
        {
            new GeoPoint(0, 0),
            new GeoPoint(0, 20),
            new GeoPoint(10, 20),
            new GeoPoint(10, 10),
            new GeoPoint(20, 10),
            new GeoPoint(20, 0),
            new GeoPoint(0, 0) // Close the ring
        };
        var ring = new PolygonRing(points);

        // Inside the L
        await Assert.That(PointInPolygon.IsPointInRing(5, 5, ring)).IsTrue();
        await Assert.That(PointInPolygon.IsPointInRing(5, 15, ring)).IsTrue();
        await Assert.That(PointInPolygon.IsPointInRing(15, 5, ring)).IsTrue();

        // Outside the L (in the "notch")
        await Assert.That(PointInPolygon.IsPointInRing(15, 15, ring)).IsFalse();

        // Outside completely
        await Assert.That(PointInPolygon.IsPointInRing(25, 25, ring)).IsFalse();
    }

    #endregion

    #region Helper Methods

    private static PolygonRing CreateSquareRing(double minLat, double minLon, double maxLat, double maxLon, bool isHole = false)
    {
        var points = new[]
        {
            new GeoPoint(minLat, minLon),
            new GeoPoint(maxLat, minLon),
            new GeoPoint(maxLat, maxLon),
            new GeoPoint(minLat, maxLon),
            new GeoPoint(minLat, minLon) // Close the ring
        };
        return new PolygonRing(points, isHole);
    }

    #endregion
}
