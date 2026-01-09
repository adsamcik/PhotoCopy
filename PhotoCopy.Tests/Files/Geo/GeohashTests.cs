using PhotoCopy.Files.Geo;

namespace PhotoCopy.Tests.Files.Geo;

public class GeohashTests
{
    [Test]
    [Arguments(40.7128, -74.0060, 4, "dr5r")] // New York
    [Arguments(51.5074, -0.1278, 4, "gcpv")]  // London
    [Arguments(35.6762, 139.6503, 4, "xn76")] // Tokyo
    [Arguments(-33.8688, 151.2093, 4, "r3gx")] // Sydney
    [Arguments(0, 0, 4, "s000")]              // Null Island
    public async Task Encode_KnownLocations_ReturnsExpectedGeohash(double lat, double lon, int precision, string expected)
    {
        var result = Geohash.Encode(lat, lon, precision);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    [Arguments(90, 180, 4)]   // Edge: max lat, max lon
    [Arguments(-90, -180, 4)] // Edge: min lat, min lon
    [Arguments(0, 180, 4)]    // Edge: wrap around
    public async Task Encode_EdgeCases_DoesNotThrow(double lat, double lon, int precision)
    {
        var result = Geohash.Encode(lat, lon, precision);
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Length).IsEqualTo(precision);
    }

    [Test]
    public async Task Encode_InvalidPrecision_ThrowsArgumentOutOfRange()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(Geohash.Encode(0, 0, 0)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(Geohash.Encode(0, 0, 13)));
    }

    [Test]
    [Arguments("dr5r")] // New York area
    [Arguments("gcpv")] // London area
    [Arguments("s000")] // Null Island
    public async Task DecodeBounds_ValidGeohash_ReturnsValidBounds(string geohash)
    {
        var (minLat, maxLat, minLon, maxLon) = Geohash.DecodeBounds(geohash);
        
        await Assert.That(minLat).IsLessThan(maxLat);
        await Assert.That(minLon).IsLessThan(maxLon);
        await Assert.That(minLat).IsGreaterThanOrEqualTo(-90);
        await Assert.That(maxLat).IsLessThanOrEqualTo(90);
        await Assert.That(minLon).IsGreaterThanOrEqualTo(-180);
        await Assert.That(maxLon).IsLessThanOrEqualTo(180);
    }

    [Test]
    [Arguments("dr5r", 40.7, -74.0)] // New York (approximate center)
    [Arguments("gcpv", 51.5, -0.2)]  // London (approximate center)
    public async Task DecodeCenter_ReturnsPointInsideBounds(string geohash, double expectedLat, double expectedLon)
    {
        var (lat, lon) = Geohash.DecodeCenter(geohash);
        var (minLat, maxLat, minLon, maxLon) = Geohash.DecodeBounds(geohash);
        
        await Assert.That(lat).IsGreaterThanOrEqualTo(minLat);
        await Assert.That(lat).IsLessThanOrEqualTo(maxLat);
        await Assert.That(lon).IsGreaterThanOrEqualTo(minLon);
        await Assert.That(lon).IsLessThanOrEqualTo(maxLon);
        await Assert.That(Math.Abs(lat - expectedLat)).IsLessThan(1.0);
        await Assert.That(Math.Abs(lon - expectedLon)).IsLessThan(1.0);
    }

    [Test]
    public async Task EncodeAndDecode_RoundTrip_ContainsOriginalPoint()
    {
        double lat = 40.7128;
        double lon = -74.0060;
        
        string geohash = Geohash.Encode(lat, lon, 8);
        var (minLat, maxLat, minLon, maxLon) = Geohash.DecodeBounds(geohash);
        
        await Assert.That(lat).IsGreaterThanOrEqualTo(minLat);
        await Assert.That(lat).IsLessThanOrEqualTo(maxLat);
        await Assert.That(lon).IsGreaterThanOrEqualTo(minLon);
        await Assert.That(lon).IsLessThanOrEqualTo(maxLon);
    }

    [Test]
    [Arguments("dr5r")]
    [Arguments("gcpv")]
    [Arguments("s")]
    public async Task EncodeToUInt32_DecodeFromUInt32_RoundTrip(string geohash)
    {
        uint encoded = Geohash.EncodeToUInt32(geohash);
        string decoded = Geohash.DecodeFromUInt32(encoded);
        await Assert.That(decoded).IsEqualTo(geohash);
    }

    [Test]
    public async Task GetNeighbors_ReturnsSevenOrEightNeighbors()
    {
        string geohash = "dr5r";
        var neighbors = Geohash.GetNeighbors(geohash).ToList();
        
        // Most cells should have 8 neighbors (except at poles)
        await Assert.That(neighbors.Count).IsGreaterThanOrEqualTo(7);
        await Assert.That(neighbors.Count).IsLessThanOrEqualTo(8);
        await Assert.That(neighbors).DoesNotContain(geohash); // Should not include self
    }

    [Test]
    public async Task GetCellAndNeighbors_IncludesSelfAndNeighbors()
    {
        string geohash = "dr5r";
        var cells = Geohash.GetCellAndNeighbors(geohash).ToList();
        
        await Assert.That(cells).Contains(geohash);
        await Assert.That(cells.Count).IsGreaterThanOrEqualTo(8);
        await Assert.That(cells.Count).IsLessThanOrEqualTo(9);
    }

    [Test]
    [Arguments(40.7128, -74.0060, 40.7484, -73.9857, 4.5)] // NY: Times Square to Empire State (~4.5km)
    [Arguments(51.5074, -0.1278, 48.8566, 2.3522, 344.0)]  // London to Paris (~344km)
    [Arguments(0, 0, 0, 0, 0)]                              // Same point
    public async Task HaversineDistance_ReturnsExpectedDistance(double lat1, double lon1, double lat2, double lon2, double expectedKm)
    {
        double distance = Geohash.HaversineDistance(lat1, lon1, lat2, lon2);
        double tolerance = expectedKm * 0.05 + 0.5;
        await Assert.That(Math.Abs(distance - expectedKm)).IsLessThan(tolerance);
    }

    [Test]
    public async Task GetAncestors_ReturnsAllParentGeohashes()
    {
        string geohash = "dr5rg";
        var ancestors = Geohash.GetAncestors(geohash).ToList();
        
        await Assert.That(ancestors.Count).IsEqualTo(4); // d, dr, dr5, dr5r
        await Assert.That(ancestors[0]).IsEqualTo("d");
        await Assert.That(ancestors[1]).IsEqualTo("dr");
        await Assert.That(ancestors[2]).IsEqualTo("dr5");
        await Assert.That(ancestors[3]).IsEqualTo("dr5r");
    }
}
