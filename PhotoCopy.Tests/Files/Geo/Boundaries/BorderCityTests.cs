using PhotoCopy.Files.Geo.Boundaries;

namespace PhotoCopy.Tests.Files.Geo.Boundaries;

/// <summary>
/// Tests for border city geocoding scenarios.
/// These tests verify that the boundary-aware geocoding correctly identifies
/// countries for locations near international borders.
/// </summary>
public class BorderCityTests
{
    #region Test Data - Known Border City Coordinates

    // Bratislava, Slovakia vs Vienna, Austria
    // Bratislava: 48.1486°N, 17.1077°E
    // Vienna: 48.2082°N, 16.3738°E
    // Border area: ~48.0833°N, 17.1333°E

    // Windsor, Canada vs Detroit, USA
    // Windsor: 42.3149°N, 83.0364°W
    // Detroit: 42.3314°N, 83.0458°W
    // Very close - separated by Detroit River

    // Strasbourg, France vs Kehl, Germany
    // Strasbourg: 48.5734°N, 7.7521°E
    // Kehl: 48.5728°N, 7.8153°E
    // Separated by Rhine River

    #endregion

    #region Mock Country Boundary Setup

    private static CountryBoundary CreateMockSlovakia()
    {
        // Simplified Slovakia boundary (approximate)
        // Real Slovakia roughly: 47.7-49.6°N, 16.8-22.6°E
        var points = new[]
        {
            new GeoPoint(47.7, 16.8),
            new GeoPoint(49.6, 16.8),
            new GeoPoint(49.6, 22.6),
            new GeoPoint(47.7, 22.6),
            new GeoPoint(47.7, 16.8)
        };
        var ring = new PolygonRing(points);
        var polygon = new Polygon(ring);
        return new CountryBoundary("SK", "Slovakia", new[] { polygon }, "SVK");
    }

    private static CountryBoundary CreateMockAustria()
    {
        // Simplified Austria boundary (approximate)
        // Real Austria roughly: 46.4-49.0°N, 9.5-17.2°E
        var points = new[]
        {
            new GeoPoint(46.4, 9.5),
            new GeoPoint(49.0, 9.5),
            new GeoPoint(49.0, 17.2),
            new GeoPoint(46.4, 17.2),
            new GeoPoint(46.4, 9.5)
        };
        var ring = new PolygonRing(points);
        var polygon = new Polygon(ring);
        return new CountryBoundary("AT", "Austria", new[] { polygon }, "AUT");
    }

    private static CountryBoundary CreateMockFrance()
    {
        // Simplified France boundary (approximate)
        // Focus on Alsace region near Strasbourg
        var points = new[]
        {
            new GeoPoint(47.5, 6.8),
            new GeoPoint(49.0, 6.8),
            new GeoPoint(49.0, 8.0),  // Border at ~7.8°E with Germany
            new GeoPoint(47.5, 8.0),
            new GeoPoint(47.5, 6.8)
        };
        var ring = new PolygonRing(points);
        var polygon = new Polygon(ring);
        return new CountryBoundary("FR", "France", new[] { polygon }, "FRA");
    }

    private static CountryBoundary CreateMockGermany()
    {
        // Simplified Germany boundary (approximate)
        // Focus on Baden-Württemberg near Kehl
        var points = new[]
        {
            new GeoPoint(47.5, 7.8),  // Border at ~7.8°E with France
            new GeoPoint(49.0, 7.8),
            new GeoPoint(49.0, 15.0),
            new GeoPoint(47.5, 15.0),
            new GeoPoint(47.5, 7.8)
        };
        var ring = new PolygonRing(points);
        var polygon = new Polygon(ring);
        return new CountryBoundary("DE", "Germany", new[] { polygon }, "DEU");
    }

    #endregion

    #region Point-in-Country Tests

    [Test]
    public async Task BratislavaCityCenter_IsInSlovakia()
    {
        var slovakia = CreateMockSlovakia();
        
        // Bratislava city center
        bool result = PointInPolygon.IsPointInCountry(48.1486, 17.1077, slovakia);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task ViennaCityCenter_IsNotInSlovakia()
    {
        var slovakia = CreateMockSlovakia();
        
        // Vienna city center
        bool result = PointInPolygon.IsPointInCountry(48.2082, 16.3738, slovakia);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ViennaCityCenter_IsInAustria()
    {
        var austria = CreateMockAustria();
        
        // Vienna city center
        bool result = PointInPolygon.IsPointInCountry(48.2082, 16.3738, austria);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Strasbourg_IsInFrance()
    {
        var france = CreateMockFrance();
        
        // Strasbourg city center
        bool result = PointInPolygon.IsPointInCountry(48.5734, 7.7521, france);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Kehl_IsInGermany()
    {
        var germany = CreateMockGermany();
        
        // Kehl city center
        bool result = PointInPolygon.IsPointInCountry(48.5728, 7.8153, germany);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Strasbourg_IsNotInGermany()
    {
        var germany = CreateMockGermany();
        
        // Strasbourg is in France, not Germany
        bool result = PointInPolygon.IsPointInCountry(48.5734, 7.7521, germany);
        await Assert.That(result).IsFalse();
    }

    #endregion

    #region Border Area Tests

    [Test]
    public async Task PointOnSlovakSideOfBorder_IsInSlovakiaNotAustria()
    {
        var slovakia = CreateMockSlovakia();
        var austria = CreateMockAustria();
        
        // A point that's clearly on the Slovak side of the border
        // (just east of the Austria/Slovakia border)
        double lat = 48.1;
        double lon = 17.0;  // 17.0 is east of Austria's eastern border (17.2)... wait, that's wrong
        
        // Let me recalculate: Austria goes up to 17.2°E, Slovakia starts at 16.8°E
        // So 17.0°E is in BOTH mock countries (overlapping simplified boundaries)
        // For a proper test, let's use a point clearly in Slovakia only
        lon = 17.5;  // This is east of Austria's mock boundary (17.2)
        
        bool inSlovakia = PointInPolygon.IsPointInCountry(lat, lon, slovakia);
        bool inAustria = PointInPolygon.IsPointInCountry(lat, lon, austria);
        
        await Assert.That(inSlovakia).IsTrue();
        await Assert.That(inAustria).IsFalse();
    }

    [Test]
    public async Task PointOnAustrianSideOfBorder_IsInAustriaNotSlovakia()
    {
        var slovakia = CreateMockSlovakia();
        var austria = CreateMockAustria();
        
        // Point clearly in Austria (west of Slovakia's boundary)
        double lat = 48.1;
        double lon = 16.0;  // 16.0 is west of Slovakia's western border (16.8)
        
        bool inSlovakia = PointInPolygon.IsPointInCountry(lat, lon, slovakia);
        bool inAustria = PointInPolygon.IsPointInCountry(lat, lon, austria);
        
        await Assert.That(inSlovakia).IsFalse();
        await Assert.That(inAustria).IsTrue();
    }

    #endregion

    #region FindCountry Helper Tests

    [Test]
    public async Task FindCountry_BratislavskaCoordinates_ReturnsSlovakia()
    {
        var countries = new[] { CreateMockSlovakia(), CreateMockAustria() };
        
        // Bratislava coordinates
        double lat = 48.1486;
        double lon = 17.1077;
        
        string? foundCountry = null;
        foreach (var country in countries)
        {
            if (PointInPolygon.IsPointInCountry(lat, lon, country))
            {
                foundCountry = country.CountryCode;
                break;
            }
        }
        
        await Assert.That(foundCountry).IsEqualTo("SK");
    }

    [Test]
    public async Task FindCountry_ViennaCoordinates_ReturnsAustria()
    {
        var countries = new[] { CreateMockSlovakia(), CreateMockAustria() };
        
        // Vienna coordinates
        double lat = 48.2082;
        double lon = 16.3738;
        
        string? foundCountry = null;
        foreach (var country in countries)
        {
            if (PointInPolygon.IsPointInCountry(lat, lon, country))
            {
                foundCountry = country.CountryCode;
                break;
            }
        }
        
        await Assert.That(foundCountry).IsEqualTo("AT");
    }

    #endregion

    #region Enclave/Exclave Tests

    [Test]
    public async Task VaticanCity_IsNotInItaly()
    {
        // Vatican City is an enclave within Italy
        // For this test, we create Italy with a hole for Vatican
        var italyExterior = new PolygonRing(new[]
        {
            new GeoPoint(41.5, 12.0),
            new GeoPoint(42.5, 12.0),
            new GeoPoint(42.5, 13.0),
            new GeoPoint(41.5, 13.0),
            new GeoPoint(41.5, 12.0)
        });
        
        // Hole for Vatican (very small)
        var vaticanHole = new PolygonRing(new[]
        {
            new GeoPoint(41.900, 12.450),
            new GeoPoint(41.910, 12.450),
            new GeoPoint(41.910, 12.460),
            new GeoPoint(41.900, 12.460),
            new GeoPoint(41.900, 12.450)
        }, isHole: true);
        
        var italyWithHole = new Polygon(italyExterior, new[] { vaticanHole });
        var italy = new CountryBoundary("IT", "Italy", new[] { italyWithHole });
        
        // Point inside Vatican should NOT be in Italy
        bool inItaly = PointInPolygon.IsPointInCountry(41.905, 12.455, italy);
        await Assert.That(inItaly).IsFalse();
        
        // Point outside Vatican but inside Italy should be in Italy
        bool inItalyOutsideVatican = PointInPolygon.IsPointInCountry(42.0, 12.5, italy);
        await Assert.That(inItalyOutsideVatican).IsTrue();
    }

    #endregion
}
