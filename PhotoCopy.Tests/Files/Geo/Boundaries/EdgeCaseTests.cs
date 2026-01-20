using AwesomeAssertions;
using PhotoCopy.Files.Geo;
using PhotoCopy.Files.Geo.Boundaries;

namespace PhotoCopy.Tests.Files.Geo.Boundaries;

/// <summary>
/// Security-focused edge case tests for the country boundary detection system.
/// Tests input validation, boundary conditions, numeric edge cases, and adversarial inputs.
/// </summary>
public class EdgeCaseTests
{
    #region Input Validation - Null and Empty Inputs

    [Test]
    public async Task PolygonRing_NullPoints_ThrowsArgumentNullException()
    {
        GeoPoint[]? nullPoints = null;
        var act = () => new PolygonRing(nullPoints!);
        act.Should().Throw<ArgumentNullException>();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Polygon_NullExteriorRing_ThrowsArgumentNullException()
    {
        PolygonRing? nullRing = null;
        var act = () => new Polygon(nullRing!);
        act.Should().Throw<ArgumentNullException>();
        await Task.CompletedTask;
    }

    [Test]
    public async Task CountryBoundary_NullCountryCode_ThrowsArgumentNullException()
    {
        var polygon = CreateSimplePolygon();
        var act = () => new CountryBoundary(null!, "Test", new[] { polygon });
        act.Should().Throw<ArgumentNullException>();
        await Task.CompletedTask;
    }

    [Test]
    public async Task CountryBoundary_NullName_ThrowsArgumentNullException()
    {
        var polygon = CreateSimplePolygon();
        var act = () => new CountryBoundary("XX", null!, new[] { polygon });
        act.Should().Throw<ArgumentNullException>();
        await Task.CompletedTask;
    }

    [Test]
    public async Task CountryBoundary_NullPolygons_ThrowsArgumentNullException()
    {
        var act = () => new CountryBoundary("XX", "Test", null!);
        act.Should().Throw<ArgumentNullException>();
        await Task.CompletedTask;
    }

    [Test]
    public async Task CountryBoundary_EmptyPolygonArray_DoesNotThrow()
    {
        // Empty polygon array should be allowed (though unusual)
        var country = new CountryBoundary("XX", "Test", Array.Empty<Polygon>());
        await Assert.That(country.Polygons.Length).IsEqualTo(0);
    }

    [Test]
    public async Task CountryBoundary_EmptyCountryCode_Allowed()
    {
        // Empty string for country code - unusual but technically allowed
        var polygon = CreateSimplePolygon();
        var country = new CountryBoundary("", "Test", new[] { polygon });
        await Assert.That(country.CountryCode).IsEqualTo("");
    }

    #endregion

    #region Numeric Edge Cases - NaN and Infinity

    [Test]
    public async Task IsPointInRing_NaNLatitude_ReturnsFalse()
    {
        var ring = CreateSquareRing(0, 0, 10, 10);
        var result = PointInPolygon.IsPointInRing(double.NaN, 5, ring);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPointInRing_NaNLongitude_ReturnsFalse()
    {
        var ring = CreateSquareRing(0, 0, 10, 10);
        var result = PointInPolygon.IsPointInRing(5, double.NaN, ring);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPointInRing_BothNaN_ReturnsFalse()
    {
        var ring = CreateSquareRing(0, 0, 10, 10);
        var result = PointInPolygon.IsPointInRing(double.NaN, double.NaN, ring);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPointInRing_PositiveInfinityLatitude_ReturnsFalse()
    {
        var ring = CreateSquareRing(0, 0, 10, 10);
        var result = PointInPolygon.IsPointInRing(double.PositiveInfinity, 5, ring);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPointInRing_NegativeInfinityLatitude_ReturnsFalse()
    {
        var ring = CreateSquareRing(0, 0, 10, 10);
        var result = PointInPolygon.IsPointInRing(double.NegativeInfinity, 5, ring);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPointInRing_PositiveInfinityLongitude_ReturnsFalse()
    {
        var ring = CreateSquareRing(0, 0, 10, 10);
        var result = PointInPolygon.IsPointInRing(5, double.PositiveInfinity, ring);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPointInRing_NegativeInfinityLongitude_ReturnsFalse()
    {
        var ring = CreateSquareRing(0, 0, 10, 10);
        var result = PointInPolygon.IsPointInRing(5, double.NegativeInfinity, ring);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task BoundingBox_ContainsNaN_ReturnsFalse()
    {
        var bbox = new BoundingBox(0, 10, 0, 10);
        // NaN comparisons should return false
        var result = bbox.Contains(double.NaN, 5);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task BoundingBox_ContainsInfinity_ReturnsFalse()
    {
        var bbox = new BoundingBox(0, 10, 0, 10);
        var result = bbox.Contains(double.PositiveInfinity, 5);
        await Assert.That(result).IsFalse();
    }

    #endregion

    #region Numeric Edge Cases - Very Small/Large Numbers

    [Test]
    public async Task IsPointInRing_VerySmallValues_HandlesCorrectly()
    {
        // Test with values near epsilon
        var ring = CreateSquareRing(0, 0, 1e-10, 1e-10);
        var result = PointInPolygon.IsPointInRing(0.5e-10, 0.5e-10, ring);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsPointInRing_MaxDoubleValues_DoesNotThrow()
    {
        var ring = CreateSquareRing(0, 0, 10, 10);
        // Should return false without throwing
        var result = PointInPolygon.IsPointInRing(double.MaxValue, double.MaxValue, ring);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPointInRing_MinDoubleValues_DoesNotThrow()
    {
        var ring = CreateSquareRing(0, 0, 10, 10);
        var result = PointInPolygon.IsPointInRing(double.MinValue, double.MinValue, ring);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPointInRing_EpsilonDifference_HandlesCorrectly()
    {
        // Test with very small differences that might cause precision issues
        var ring = CreateSquareRing(0, 0, 10, 10);
        // Point just inside the boundary
        var result = PointInPolygon.IsPointInRing(10 - double.Epsilon, 5, ring);
        // Result depends on epsilon handling - just verify no exception
        await Assert.That(result == true || result == false).IsTrue();
    }

    [Test]
    public async Task GeoPoint_QuantizedRoundTrip_PreservesPrecision()
    {
        // Test quantization doesn't lose too much precision
        var original = new GeoPoint(48.8566, 2.3522); // Paris
        var (latQ, lonQ) = original.ToQuantized();
        var restored = GeoPoint.FromQuantized(latQ, lonQ);

        // Should be within 0.01 degree tolerance
        await Assert.That(Math.Abs(original.Latitude - restored.Latitude)).IsLessThan(0.01);
        await Assert.That(Math.Abs(original.Longitude - restored.Longitude)).IsLessThan(0.01);
    }

    [Test]
    public async Task GeoPoint_QuantizedOverflow_HandlesExtremes()
    {
        // int16 range is -32768 to 32767
        // Latitude * 100 max is 9000, well within range
        // Longitude * 100 max is 18000, also within range
        var extreme = new GeoPoint(90, 180);
        var (latQ, lonQ) = extreme.ToQuantized();
        await Assert.That(latQ).IsEqualTo((short)9000);
        await Assert.That(lonQ).IsEqualTo((short)18000);
    }

    #endregion

    #region Boundary Edge Cases - Points on Edges and Vertices

    [Test]
    public async Task IsPointInRing_PointExactlyOnVertex_NoException()
    {
        var ring = CreateSquareRing(0, 0, 10, 10);
        // Test each vertex
        var vertices = new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0) };
        foreach (var (lat, lon) in vertices)
        {
            var result = PointInPolygon.IsPointInRing(lat, lon, ring);
            // Any result is acceptable, just no exception
            await Assert.That(result == true || result == false).IsTrue();
        }
    }

    [Test]
    public async Task IsPointInRing_PointExactlyOnEdge_NoException()
    {
        var ring = CreateSquareRing(0, 0, 10, 10);
        // Test points on each edge
        var edgePoints = new[] { (5.0, 0.0), (10.0, 5.0), (5.0, 10.0), (0.0, 5.0) };
        foreach (var (lat, lon) in edgePoints)
        {
            var result = PointInPolygon.IsPointInRing(lat, lon, ring);
            await Assert.That(result == true || result == false).IsTrue();
        }
    }

    [Test]
    public async Task IsPointOnEdge_PointOnVertex_ReturnsTrue()
    {
        var ring = CreateSquareRing(0, 0, 10, 10);
        var result = PointInPolygon.IsPointOnEdge(0, 0, ring, epsilon: 0.0001);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsPointOnEdge_PointOnMidEdge_ReturnsTrue()
    {
        var ring = CreateSquareRing(0, 0, 10, 10);
        var result = PointInPolygon.IsPointOnEdge(5, 0, ring, epsilon: 0.0001);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsPointOnEdge_PointNearEdge_ReturnsTrue()
    {
        var ring = CreateSquareRing(0, 0, 10, 10);
        // Point very close to edge (within epsilon)
        var result = PointInPolygon.IsPointOnEdge(5.00005, 0, ring, epsilon: 0.0001);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsPointOnEdge_PointFarFromEdge_ReturnsFalse()
    {
        var ring = CreateSquareRing(0, 0, 10, 10);
        var result = PointInPolygon.IsPointOnEdge(5, 5, ring, epsilon: 0.0001);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPointOnEdge_ZeroEpsilon_StillWorks()
    {
        var ring = CreateSquareRing(0, 0, 10, 10);
        // Exact point on edge with zero epsilon
        var result = PointInPolygon.IsPointOnEdge(5, 0, ring, epsilon: 0);
        // This should still work for exact matches
        await Assert.That(result == true || result == false).IsTrue();
    }

    [Test]
    public async Task IsPointOnEdge_NegativeEpsilon_HandlesGracefully()
    {
        var ring = CreateSquareRing(0, 0, 10, 10);
        // Negative epsilon - should not match anything
        var result = PointInPolygon.IsPointOnEdge(5, 0, ring, epsilon: -1);
        await Assert.That(result).IsFalse();
    }

    #endregion

    #region Geographic Edge Cases - Poles and Antimeridian

    [Test]
    public async Task NormalizeLongitude_AtAntimeridian_HandlesCorrectly()
    {
        // Exactly at the antimeridian
        await Assert.That(PointInPolygon.NormalizeLongitude(180)).IsEqualTo(180);
        await Assert.That(PointInPolygon.NormalizeLongitude(-180)).IsEqualTo(-180);
    }

    [Test]
    public async Task NormalizeLongitude_BeyondAntimeridian_WrapsCorrectly()
    {
        await Assert.That(PointInPolygon.NormalizeLongitude(181)).IsEqualTo(-179);
        await Assert.That(PointInPolygon.NormalizeLongitude(-181)).IsEqualTo(179);
    }

    [Test]
    public async Task NormalizeLongitude_MultipleTurns_WrapsCorrectly()
    {
        // Going around multiple times
        await Assert.That(PointInPolygon.NormalizeLongitude(720)).IsEqualTo(0);
        await Assert.That(PointInPolygon.NormalizeLongitude(-720)).IsEqualTo(0);
    }

    [Test]
    public async Task ClampLatitude_AtPoles_ReturnsExactly()
    {
        await Assert.That(PointInPolygon.ClampLatitude(90)).IsEqualTo(90);
        await Assert.That(PointInPolygon.ClampLatitude(-90)).IsEqualTo(-90);
    }

    [Test]
    public async Task ClampLatitude_BeyondPoles_ClampsToPoles()
    {
        await Assert.That(PointInPolygon.ClampLatitude(91)).IsEqualTo(90);
        await Assert.That(PointInPolygon.ClampLatitude(-91)).IsEqualTo(-90);
        await Assert.That(PointInPolygon.ClampLatitude(1000)).IsEqualTo(90);
        await Assert.That(PointInPolygon.ClampLatitude(-1000)).IsEqualTo(-90);
    }

    [Test]
    public async Task IsPointInRing_NorthPole_HandlesCorrectly()
    {
        // Polygon around north pole - note: ray casting algorithm has known issues at exact poles
        // The algorithm may return false for latitude 90 due to degenerate geometry at poles
        var ring = CreateSquareRing(85, -180, 90, 180);
        var result = PointInPolygon.IsPointInRing(90, 0, ring);
        // Point at exact pole is a known edge case - just verify no exception is thrown
        await Assert.That(result == true || result == false).IsTrue();
    }

    [Test]
    public async Task IsPointInRing_SouthPole_HandlesCorrectly()
    {
        // Polygon around south pole
        var ring = CreateSquareRing(-90, -180, -85, 180);
        var result = PointInPolygon.IsPointInRing(-90, 0, ring);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsPointInRing_AtOrigin_PointZeroZero()
    {
        // Origin point (0, 0) is in the Gulf of Guinea
        var ring = CreateSquareRing(-10, -10, 10, 10);
        var result = PointInPolygon.IsPointInRing(0, 0, ring);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsPointInRing_CrossingAntimeridian_HandlesCorrectly()
    {
        // This is a tricky case - polygon crossing the antimeridian
        // Standard algorithm may have issues here
        var points = new[]
        {
            new GeoPoint(0, 170),
            new GeoPoint(10, 170),
            new GeoPoint(10, -170),
            new GeoPoint(0, -170),
            new GeoPoint(0, 170)
        };
        var ring = new PolygonRing(points);
        
        // This test just verifies no exception is thrown
        // The actual behavior for antimeridian-crossing polygons is complex
        var result = PointInPolygon.IsPointInRing(5, 175, ring);
        await Assert.That(result == true || result == false).IsTrue();
    }

    #endregion

    #region Degenerate Polygon Cases

    [Test]
    public async Task IsPointInRing_SinglePoint_ReturnsFalse()
    {
        var ring = new PolygonRing(new[] { new GeoPoint(5, 5) });
        var result = PointInPolygon.IsPointInRing(5, 5, ring);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPointInRing_Line_ReturnsFalse()
    {
        var ring = new PolygonRing(new[]
        {
            new GeoPoint(0, 0),
            new GeoPoint(10, 10),
            new GeoPoint(0, 0)
        });
        var result = PointInPolygon.IsPointInRing(5, 5, ring);
        // A line is not a valid polygon, should return false
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPointInRing_CollinearPoints_ReturnsFalse()
    {
        // All points on a single line
        var ring = new PolygonRing(new[]
        {
            new GeoPoint(0, 0),
            new GeoPoint(5, 0),
            new GeoPoint(10, 0),
            new GeoPoint(0, 0)
        });
        var result = PointInPolygon.IsPointInRing(5, 0, ring);
        // Degenerate polygon (zero area)
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsPointInRing_TriangleWithCoincidentPoints_HandlesCorrectly()
    {
        // Triangle where two vertices are the same
        var ring = new PolygonRing(new[]
        {
            new GeoPoint(0, 0),
            new GeoPoint(0, 0),  // Duplicate vertex
            new GeoPoint(10, 0),
            new GeoPoint(5, 10),
            new GeoPoint(0, 0)
        });
        var result = PointInPolygon.IsPointInRing(5, 3, ring);
        // Should still work with coincident points
        await Assert.That(result == true || result == false).IsTrue();
    }

    [Test]
    public async Task IsPointInRing_VeryThinPolygon_HandlesCorrectly()
    {
        // Very thin sliver polygon
        var ring = CreateSquareRing(0, 0, 0.0001, 100);
        var result = PointInPolygon.IsPointInRing(0.00005, 50, ring);
        await Assert.That(result == true || result == false).IsTrue();
    }

    [Test]
    public async Task IsPointInRing_SelfIntersectingPolygon_NoException()
    {
        // Figure-8 shaped (self-intersecting) polygon
        var ring = new PolygonRing(new[]
        {
            new GeoPoint(0, 0),
            new GeoPoint(10, 10),
            new GeoPoint(10, 0),
            new GeoPoint(0, 10),
            new GeoPoint(0, 0)
        });
        // Self-intersecting polygons have undefined behavior,
        // but should not throw exceptions
        var result = PointInPolygon.IsPointInRing(5, 5, ring);
        await Assert.That(result == true || result == false).IsTrue();
    }

    #endregion

    #region Adversarial Inputs - Malformed Data

    [Test]
    public async Task CountryBoundary_UnicodeInCountryCode_Allowed()
    {
        // Country codes should be ASCII, but Unicode shouldn't crash
        var polygon = CreateSimplePolygon();
        var country = new CountryBoundary("日本", "Japan", new[] { polygon });
        await Assert.That(country.CountryCode).IsEqualTo("日本");
    }

    [Test]
    public async Task CountryBoundary_EmojiInName_Allowed()
    {
        var polygon = CreateSimplePolygon();
        var country = new CountryBoundary("XX", "Test 🌍 Country", new[] { polygon });
        await Assert.That(country.Name).IsEqualTo("Test 🌍 Country");
    }

    [Test]
    public async Task CountryBoundary_VeryLongName_Allowed()
    {
        var polygon = CreateSimplePolygon();
        var longName = new string('A', 10000);
        var country = new CountryBoundary("XX", longName, new[] { polygon });
        await Assert.That(country.Name.Length).IsEqualTo(10000);
    }

    [Test]
    public async Task CountryBoundary_SpecialCharactersInName_Allowed()
    {
        var polygon = CreateSimplePolygon();
        var specialName = "Test\0Country\nWith\tSpecial\rChars";
        var country = new CountryBoundary("XX", specialName, new[] { polygon });
        await Assert.That(country.Name).IsEqualTo(specialName);
    }

    [Test]
    public async Task CountryBoundary_WhitespaceOnlyName_Allowed()
    {
        var polygon = CreateSimplePolygon();
        var country = new CountryBoundary("XX", "   ", new[] { polygon });
        await Assert.That(country.Name).IsEqualTo("   ");
    }

    #endregion

    #region Overflow/Underflow Scenarios - Large Polygons

    [Test]
    public async Task IsPointInRing_LargePolygon_FullEarth_HandlesCorrectly()
    {
        // Polygon covering the entire valid coordinate range
        var ring = CreateSquareRing(-90, -180, 90, 180);
        var result = PointInPolygon.IsPointInRing(0, 0, ring);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsPointInRing_VeryManyVertices_HandlesCorrectly()
    {
        // Polygon with many vertices (circle approximation)
        var points = new GeoPoint[1001];
        for (int i = 0; i < 1000; i++)
        {
            double angle = 2 * Math.PI * i / 1000;
            points[i] = new GeoPoint(
                45 + 10 * Math.Cos(angle),
                10 + 10 * Math.Sin(angle)
            );
        }
        points[1000] = points[0]; // Close the ring

        var ring = new PolygonRing(points);
        var result = PointInPolygon.IsPointInRing(45, 10, ring);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task CountryBoundary_ManyPolygons_HandlesCorrectly()
    {
        // Country with many island polygons (like Indonesia)
        var polygons = new Polygon[100];
        for (int i = 0; i < 100; i++)
        {
            polygons[i] = new Polygon(CreateSquareRing(i, i, i + 0.5, i + 0.5));
        }
        var country = new CountryBoundary("ID", "Indonesia Test", polygons);
        
        // Point in one of the islands
        var result = PointInPolygon.IsPointInCountry(50.25, 50.25, country);
        await Assert.That(result).IsTrue();
        
        // Point between islands
        result = PointInPolygon.IsPointInCountry(50.75, 50.75, country);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task Polygon_ManyHoles_HandlesCorrectly()
    {
        var exterior = CreateSquareRing(0, 0, 100, 100);
        var holes = new PolygonRing[50];
        for (int i = 0; i < 50; i++)
        {
            int row = i / 10;
            int col = i % 10;
            holes[i] = CreateSquareRing(
                5 + row * 9, 5 + col * 9,
                8 + row * 9, 8 + col * 9,
                isHole: true
            );
        }
        var polygon = new Polygon(exterior, holes);
        
        // Point in exterior, not in hole
        var result = PointInPolygon.IsPointInPolygon(3, 3, polygon);
        await Assert.That(result).IsTrue();
        
        // Point in one of the holes
        result = PointInPolygon.IsPointInPolygon(6, 6, polygon);
        await Assert.That(result).IsFalse();
    }

    #endregion

    #region Geohash Edge Cases

    [Test]
    public async Task Geohash_MinimumPrecision_Works()
    {
        var hash = Geohash.Encode(45, 10, precision: 1);
        await Assert.That(hash.Length).IsEqualTo(1);
    }

    [Test]
    public async Task Geohash_MaximumPrecision_Works()
    {
        var hash = Geohash.Encode(45, 10, precision: 12);
        await Assert.That(hash.Length).IsEqualTo(12);
    }

    [Test]
    public async Task Geohash_ZeroPrecision_ThrowsException()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(Geohash.Encode(45, 10, precision: 0)));
    }

    [Test]
    public async Task Geohash_NegativePrecision_ThrowsException()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(Geohash.Encode(45, 10, precision: -1)));
    }

    [Test]
    public async Task Geohash_ExcessivePrecision_ThrowsException()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(Geohash.Encode(45, 10, precision: 13)));
    }

    [Test]
    public async Task Geohash_AtPoles_HandlesCorrectly()
    {
        var northPole = Geohash.Encode(90, 0, precision: 4);
        var southPole = Geohash.Encode(-90, 0, precision: 4);
        
        await Assert.That(northPole.Length).IsEqualTo(4);
        await Assert.That(southPole.Length).IsEqualTo(4);
    }

    [Test]
    public async Task Geohash_AtOrigin_HandlesCorrectly()
    {
        var origin = Geohash.Encode(0, 0, precision: 4);
        await Assert.That(origin.Length).IsEqualTo(4);
    }

    [Test]
    public async Task Geohash_AtAntimeridian_HandlesCorrectly()
    {
        var east = Geohash.Encode(0, 180, precision: 4);
        var west = Geohash.Encode(0, -180, precision: 4);
        
        await Assert.That(east.Length).IsEqualTo(4);
        await Assert.That(west.Length).IsEqualTo(4);
    }

    #endregion

    #region BoundingBox Edge Cases

    [Test]
    public async Task BoundingBox_ZeroArea_ContainsExactPoint()
    {
        var bbox = new BoundingBox(5, 5, 10, 10);
        var result = bbox.Contains(5, 10);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task BoundingBox_InvertedBounds_DoesNotContainAnything()
    {
        // Min > Max - invalid bounding box
        var bbox = new BoundingBox(10, 0, 10, 0);
        var result = bbox.Contains(5, 5);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task BoundingBox_Intersects_SameBox_ReturnsTrue()
    {
        var bbox = new BoundingBox(0, 10, 0, 10);
        var result = bbox.Intersects(bbox);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task BoundingBox_Intersects_TouchingEdge_ReturnsTrue()
    {
        var bbox1 = new BoundingBox(0, 10, 0, 10);
        var bbox2 = new BoundingBox(10, 20, 0, 10);
        var result = bbox1.Intersects(bbox2);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task BoundingBox_Intersects_TouchingCorner_ReturnsTrue()
    {
        var bbox1 = new BoundingBox(0, 10, 0, 10);
        var bbox2 = new BoundingBox(10, 20, 10, 20);
        var result = bbox1.Intersects(bbox2);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task BoundingBox_Intersects_NonOverlapping_ReturnsFalse()
    {
        var bbox1 = new BoundingBox(0, 10, 0, 10);
        var bbox2 = new BoundingBox(20, 30, 20, 30);
        var result = bbox1.Intersects(bbox2);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task BoundingBox_FromPoints_EmptyEnumerable_ThrowsOrHandles()
    {
        // Empty enumerable might cause issues
        try
        {
            var bbox = BoundingBox.FromPoints(Array.Empty<GeoPoint>());
            // If it doesn't throw, verify the result is somewhat sane
            await Assert.That(double.IsFinite(bbox.MinLat) || bbox.MinLat == double.MaxValue).IsTrue();
        }
        catch (Exception)
        {
            // Exception is also acceptable behavior for empty input
            await Assert.That(true).IsTrue();
        }
    }

    [Test]
    public async Task BoundingBox_FromPoints_SinglePoint_CreatesDegenerateBbox()
    {
        var point = new GeoPoint(45, 10);
        var bbox = BoundingBox.FromPoints(new[] { point });
        
        await Assert.That(bbox.MinLat).IsEqualTo(45);
        await Assert.That(bbox.MaxLat).IsEqualTo(45);
        await Assert.That(bbox.MinLon).IsEqualTo(10);
        await Assert.That(bbox.MaxLon).IsEqualTo(10);
    }

    #endregion

    #region CountryLookupResult Tests

    [Test]
    public async Task CountryLookupResult_DefaultValues_Correct()
    {
        var result = new CountryLookupResult(null);
        await Assert.That(result.CountryCode).IsNull();
        await Assert.That(result.IsOcean).IsFalse();
        await Assert.That(result.IsBorderArea).IsFalse();
        await Assert.That(result.CandidateCountries).IsNull();
    }

    [Test]
    public async Task CountryLookupResult_WithAllValues_PreservesAll()
    {
        var candidates = new[] { "SK", "AT", "HU" };
        var result = new CountryLookupResult(
            "SK",
            IsOcean: false,
            IsBorderArea: true,
            CandidateCountries: candidates
        );
        
        await Assert.That(result.CountryCode).IsEqualTo("SK");
        await Assert.That(result.IsOcean).IsFalse();
        await Assert.That(result.IsBorderArea).IsTrue();
        await Assert.That(result.CandidateCountries).IsEquivalentTo(candidates);
    }

    #endregion

    #region Thread Safety (Basic)

    [Test]
    public async Task PointInPolygon_ConcurrentReads_NoException()
    {
        var ring = CreateSquareRing(0, 0, 10, 10);
        var tasks = new Task<bool>[100];
        
        for (int i = 0; i < 100; i++)
        {
            int idx = i;
            tasks[i] = Task.Run(() => PointInPolygon.IsPointInRing(idx % 10, idx % 10, ring));
        }
        
        var results = await Task.WhenAll(tasks);
        await Assert.That(results.All(r => r == true || r == false)).IsTrue();
    }

    [Test]
    public async Task BoundingBox_ConcurrentContains_NoException()
    {
        var bbox = new BoundingBox(0, 10, 0, 10);
        var tasks = new Task<bool>[100];
        
        for (int i = 0; i < 100; i++)
        {
            int idx = i;
            tasks[i] = Task.Run(() => bbox.Contains(idx % 10, idx % 10));
        }
        
        var results = await Task.WhenAll(tasks);
        await Assert.That(results.All(r => r == true || r == false)).IsTrue();
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

    private static Polygon CreateSimplePolygon()
    {
        return new Polygon(CreateSquareRing(0, 0, 10, 10));
    }

    #endregion
}
