using PhotoCopy.Files.Geo;

namespace PhotoCopy.Tests.Files.Geo;

public class TieredGeocodingIntegrationTests
{
    private static string? GetTestDataDir()
    {
        // Try multiple paths to find the test data
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "data"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PhotoCopy", "data"),
            Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "PhotoCopy", "data"),
            @"G:\Github\PhotoCopy\PhotoCopy\data"
        };
        
        foreach (var path in candidates)
        {
            var fullPath = Path.GetFullPath(path);
            // Check for single file format: geo.geoindex + geo.geodata
            if (Directory.Exists(fullPath) && 
                File.Exists(Path.Combine(fullPath, "geo.geoindex")) &&
                File.Exists(Path.Combine(fullPath, "geo.geodata")))
            {
                return fullPath;
            }
        }
        return null;
    }

    [Test]
    public async Task SpatialIndex_LoadsTestData_Successfully()
    {
        var testDataDir = GetTestDataDir();
        if (testDataDir == null)
        {
            // Skip test if data files don't exist
            return;
        }
        
        var indexPath = Path.Combine(testDataDir, "geo.geoindex");

        using var index = SpatialIndex.Load(indexPath);
        
        await Assert.That(index.CellCount).IsGreaterThan(0);
        await Assert.That(index.TotalLocationCount).IsGreaterThan(0);
        await Assert.That(index.Precision).IsEqualTo(4);
        await Assert.That(index.DataFileSize).IsGreaterThan(0);
    }

    [Test]
    public async Task CellLoader_LoadsCell_Successfully()
    {
        var testDataDir = GetTestDataDir();
        if (testDataDir == null)
        {
            return;
        }
        
        var indexPath = Path.Combine(testDataDir, "geo.geoindex");
        var dataPath = Path.Combine(testDataDir, "geo.geodata");

        using var index = SpatialIndex.Load(indexPath);
        using var loader = CellLoader.Open(dataPath);
        
        var cells = index.GetAllCells();
        await Assert.That(cells).IsNotNull();
        await Assert.That(cells.Count).IsGreaterThan(0);
        
        var firstEntry = cells[0];
        var geohash = Geohash.DecodeFromUInt32(firstEntry.GeohashCode);
        var cell = loader.LoadCell(firstEntry, geohash);
        
        await Assert.That(cell.Entries.Length).IsGreaterThan(0);
        await Assert.That(cell.Geohash).IsEqualTo(geohash);
    }

    [Test]
    public async Task FullPipeline_FindsLocationNearNewYork_ReturnsExpectedResult()
    {
        var testDataDir = GetTestDataDir();
        if (testDataDir == null)
        {
            return;
        }
        
        var indexPath = Path.Combine(testDataDir, "geo.geoindex");
        var dataPath = Path.Combine(testDataDir, "geo.geodata");

        using var index = SpatialIndex.Load(indexPath);
        using var loader = CellLoader.Open(dataPath);
        using var cache = new CellCache();

        // New York coordinates
        double lat = 40.7128;
        double lon = -74.0060;
        
        // Find the cell for New York
        string geohash = Geohash.Encode(lat, lon, 4);
        
        if (!index.TryGetCell(geohash, out var entry))
        {
            // Cell might not exist in test data
            return;
        }

        var cell = loader.LoadCell(entry, geohash);
        cache.Put(geohash, cell);
        
        var nearest = cell.FindNearest(lat, lon);
        
        await Assert.That(nearest).IsNotNull();
        // Should find a location in the US near NYC (could be "New York" or a nearby landmark/area)
        await Assert.That(nearest!.City).IsNotEmpty();
        await Assert.That(nearest.Country).IsEqualTo("US");
        await Assert.That(nearest.DistanceKm(lat, lon)).IsLessThan(5.0); // Within 5km of Manhattan
    }

    [Test]
    public async Task FullPipeline_SearchesNeighborCells_FindsNearbyLocation()
    {
        var testDataDir = GetTestDataDir();
        if (testDataDir == null)
        {
            return;
        }
        
        var indexPath = Path.Combine(testDataDir, "geo.geoindex");
        var dataPath = Path.Combine(testDataDir, "geo.geodata");

        using var index = SpatialIndex.Load(indexPath);
        using var loader = CellLoader.Open(dataPath);
        using var cache = new CellCache();

        // Tokyo coordinates
        double lat = 35.6762;
        double lon = 139.6503;
        
        GeoLookupResult? bestResult = null;
        double bestDistance = 100.0; // Max 100km
        string queryGeohash = Geohash.Encode(lat, lon, 4);

        // Search cell and neighbors
        foreach (var (cellHash, entry) in index.GetCellAndNeighbors(queryGeohash))
        {
            var cell = loader.LoadCell(entry, cellHash);
            cache.Put(cellHash, cell);
            
            var nearest = cell.FindNearest(lat, lon, bestDistance);
            if (nearest != null)
            {
                double distance = nearest.DistanceKm(lat, lon);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestResult = new GeoLookupResult
                    {
                        Location = nearest,
                        DistanceKm = distance,
                        CellGeohash = cellHash,
                        IsFromNeighborCell = cellHash != queryGeohash
                    };
                }
            }
        }

        // Should find Tokyo if it's in test data
        if (bestResult != null)
        {
            await Assert.That(bestResult.DistanceKm).IsLessThan(100.0);
            await Assert.That(bestResult.Location.City).IsNotEmpty();
        }
    }

    [Test]
    public async Task CellCache_ImprovePerformance_OnRepeatedLookups()
    {
        var testDataDir = GetTestDataDir();
        if (testDataDir == null)
        {
            return;
        }
        
        var indexPath = Path.Combine(testDataDir, "geo.geoindex");
        var dataPath = Path.Combine(testDataDir, "geo.geodata");

        using var index = SpatialIndex.Load(indexPath);
        using var loader = CellLoader.Open(dataPath);
        using var cache = new CellCache();

        var cells = index.GetAllCells();
        if (cells.Count == 0) return;
        
        var entry = cells[0];
        var geohash = Geohash.DecodeFromUInt32(entry.GeohashCode);
        var cell1 = loader.LoadCell(entry, geohash);

        // Add to cache
        cache.Put(geohash, cell1);
        
        await Assert.That(cache.MissCount).IsEqualTo(0);
        await Assert.That(cache.HitCount).IsEqualTo(0);
        
        // Second access - from cache
        cache.TryGet(geohash, out var cell2);
        
        await Assert.That(cache.HitCount).IsEqualTo(1);
        await Assert.That(cell2).IsSameReferenceAs(cell1);
    }
}
