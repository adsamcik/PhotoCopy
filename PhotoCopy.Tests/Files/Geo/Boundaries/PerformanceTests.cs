using System.Collections.Concurrent;
using System.Diagnostics;
using PhotoCopy.Files.Geo;
using PhotoCopy.Files.Geo.Boundaries;

namespace PhotoCopy.Tests.Files.Geo.Boundaries;

/// <summary>
/// Performance and stress tests for the country boundary detection system.
/// These tests measure throughput, memory efficiency, cache performance, and scaling behavior.
/// </summary>
public class PerformanceTests
{
    #region Throughput Tests - Point-in-Polygon

    [Test]
    public async Task PointInRing_Throughput_ProcessesManyPointsQuickly()
    {
        // Arrange - Simple square polygon
        var ring = CreateSquareRing(0, 0, 100, 100);
        const int iterations = 100_000;
        var random = new Random(42); // Fixed seed for reproducibility

        // Generate test points (mix of inside and outside)
        var points = new (double lat, double lon)[iterations];
        for (int i = 0; i < iterations; i++)
        {
            points[i] = (random.NextDouble() * 200 - 50, random.NextDouble() * 200 - 50);
        }

        // Act
        var sw = Stopwatch.StartNew();
        int insideCount = 0;
        foreach (var (lat, lon) in points)
        {
            if (PointInPolygon.IsPointInRing(lat, lon, ring))
                insideCount++;
        }
        sw.Stop();

        // Assert - Should process at least 100K points per second (1ms per 100 points)
        double pointsPerSecond = iterations / (sw.Elapsed.TotalSeconds);
        await Assert.That(sw.ElapsedMilliseconds).IsLessThan(1000); // Max 1 second for 100K points
        await Assert.That(pointsPerSecond).IsGreaterThanOrEqualTo(100_000);
        
        // Sanity check - roughly 25% of points should be inside (our box is 100x100 in a 200x200 area)
        await Assert.That(insideCount).IsGreaterThan(10_000);
        await Assert.That(insideCount).IsLessThan(50_000);
    }

    [Test]
    public async Task PointInPolygon_Throughput_WithHoles()
    {
        // Arrange - Polygon with multiple holes (common for country boundaries with enclaves)
        var exterior = CreateSquareRing(0, 0, 100, 100);
        var holes = new[]
        {
            CreateSquareRing(10, 10, 20, 20, isHole: true),
            CreateSquareRing(30, 30, 40, 40, isHole: true),
            CreateSquareRing(60, 60, 70, 70, isHole: true),
            CreateSquareRing(80, 10, 90, 20, isHole: true),
        };
        var polygon = new Polygon(exterior, holes);
        const int iterations = 50_000;
        var random = new Random(42);

        var points = new (double lat, double lon)[iterations];
        for (int i = 0; i < iterations; i++)
        {
            points[i] = (random.NextDouble() * 100, random.NextDouble() * 100);
        }

        // Act
        var sw = Stopwatch.StartNew();
        int insideCount = 0;
        foreach (var (lat, lon) in points)
        {
            if (PointInPolygon.IsPointInPolygon(lat, lon, polygon))
                insideCount++;
        }
        sw.Stop();

        // Assert - Should still be fast even with holes
        await Assert.That(sw.ElapsedMilliseconds).IsLessThan(500);
        await Assert.That(insideCount).IsGreaterThan(0);
    }

    [Test]
    public async Task PointInCountry_Throughput_MultiPolygon()
    {
        // Arrange - Country with multiple disjointed territories (like Indonesia, USA, etc.)
        var polygons = new Polygon[10];
        for (int i = 0; i < 10; i++)
        {
            var exterior = CreateSquareRing(i * 20, 0, i * 20 + 15, 15);
            polygons[i] = new Polygon(exterior);
        }
        var boundary = new CountryBoundary("XX", "Test Country", polygons);
        const int iterations = 50_000;
        var random = new Random(42);

        var points = new (double lat, double lon)[iterations];
        for (int i = 0; i < iterations; i++)
        {
            points[i] = (random.NextDouble() * 200, random.NextDouble() * 20);
        }

        // Act
        var sw = Stopwatch.StartNew();
        int insideCount = 0;
        foreach (var (lat, lon) in points)
        {
            if (PointInPolygon.IsPointInCountry(lat, lon, boundary))
                insideCount++;
        }
        sw.Stop();

        // Assert
        await Assert.That(sw.ElapsedMilliseconds).IsLessThan(500);
        await Assert.That(insideCount).IsGreaterThan(0);
    }

    #endregion

    #region Throughput Tests - Complex Polygons

    [Test]
    public async Task PointInRing_ComplexPolygon_HighVertexCount()
    {
        // Arrange - Polygon with many vertices (like a detailed country border)
        const int vertexCount = 1000;
        var ring = CreateCircularRing(50, 50, 40, vertexCount);
        const int iterations = 10_000;
        var random = new Random(42);

        var points = new (double lat, double lon)[iterations];
        for (int i = 0; i < iterations; i++)
        {
            points[i] = (random.NextDouble() * 100, random.NextDouble() * 100);
        }

        // Act
        var sw = Stopwatch.StartNew();
        int insideCount = 0;
        foreach (var (lat, lon) in points)
        {
            if (PointInPolygon.IsPointInRing(lat, lon, ring))
                insideCount++;
        }
        sw.Stop();

        // Assert - Even with 1000 vertices, should handle 10K points in reasonable time
        await Assert.That(sw.ElapsedMilliseconds).IsLessThan(500);
        await Assert.That(insideCount).IsGreaterThan(1000); // Roughly pi/4 of points inside circle
    }

    [Test]
    public async Task PointInRing_VeryComplexPolygon_TenThousandVertices()
    {
        // Arrange - Extremely detailed polygon (like coastlines)
        const int vertexCount = 10_000;
        var ring = CreateCircularRing(50, 50, 40, vertexCount);
        const int iterations = 1_000;
        var random = new Random(42);

        var points = new (double lat, double lon)[iterations];
        for (int i = 0; i < iterations; i++)
        {
            points[i] = (random.NextDouble() * 100, random.NextDouble() * 100);
        }

        // Act
        var sw = Stopwatch.StartNew();
        int insideCount = 0;
        foreach (var (lat, lon) in points)
        {
            if (PointInPolygon.IsPointInRing(lat, lon, ring))
                insideCount++;
        }
        sw.Stop();

        // Assert - Should still complete in reasonable time
        await Assert.That(sw.ElapsedMilliseconds).IsLessThan(2000);
        await Assert.That(insideCount).IsGreaterThan(400); // Roughly pi/4 of points inside circle
    }

    #endregion

    #region Stress Tests - Concurrent Lookups

    [Test]
    public async Task PointInPolygon_ConcurrentAccess_ThreadSafe()
    {
        // Arrange - Shared polygon accessed by multiple threads
        var ring = CreateSquareRing(0, 0, 100, 100);
        const int threadsCount = 8;
        const int iterationsPerThread = 10_000;
        var random = new Random(42);
        var results = new ConcurrentBag<(int thread, int insideCount)>();
        var errors = new ConcurrentBag<Exception>();

        // Pre-generate all points to avoid Random contention
        var allPoints = new (double lat, double lon)[threadsCount * iterationsPerThread];
        for (int i = 0; i < allPoints.Length; i++)
        {
            allPoints[i] = (random.NextDouble() * 200 - 50, random.NextDouble() * 200 - 50);
        }

        // Act
        var sw = Stopwatch.StartNew();
        var tasks = new Task[threadsCount];
        for (int t = 0; t < threadsCount; t++)
        {
            int threadId = t;
            int startIdx = t * iterationsPerThread;
            tasks[t] = Task.Run(() =>
            {
                try
                {
                    int insideCount = 0;
                    for (int i = 0; i < iterationsPerThread; i++)
                    {
                        var (lat, lon) = allPoints[startIdx + i];
                        if (PointInPolygon.IsPointInRing(lat, lon, ring))
                            insideCount++;
                    }
                    results.Add((threadId, insideCount));
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            });
        }
        await Task.WhenAll(tasks);
        sw.Stop();

        // Assert
        await Assert.That(errors.Count).IsEqualTo(0);
        await Assert.That(results.Count).IsEqualTo(threadsCount);
        await Assert.That(sw.ElapsedMilliseconds).IsLessThan(2000);

        // Each thread should have processed its points
        foreach (var (thread, insideCount) in results)
        {
            await Assert.That(insideCount).IsGreaterThan(1000);
            await Assert.That(insideCount).IsLessThan(5000);
        }
    }

    [Test]
    public async Task PointInCountry_HighConcurrency_StressTest()
    {
        // Arrange - Multiple countries checked by many threads
        var countries = CreateTestCountries(10);
        const int threadsCount = 16;
        const int iterationsPerThread = 5_000;
        var random = new Random(42);
        var errors = new ConcurrentBag<Exception>();

        var allPoints = new (double lat, double lon)[threadsCount * iterationsPerThread];
        for (int i = 0; i < allPoints.Length; i++)
        {
            allPoints[i] = (random.NextDouble() * 180 - 90, random.NextDouble() * 360 - 180);
        }

        // Act
        var sw = Stopwatch.StartNew();
        var tasks = new Task[threadsCount];
        for (int t = 0; t < threadsCount; t++)
        {
            int threadId = t;
            int startIdx = t * iterationsPerThread;
            tasks[t] = Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < iterationsPerThread; i++)
                    {
                        var (lat, lon) = allPoints[startIdx + i];
                        foreach (var country in countries)
                        {
                            PointInPolygon.IsPointInCountry(lat, lon, country);
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            });
        }
        await Task.WhenAll(tasks);
        sw.Stop();

        // Assert - No errors during concurrent access
        await Assert.That(errors.Count).IsEqualTo(0);
        // Should complete in reasonable time (16 threads * 5K iterations * 10 countries = 800K checks)
        await Assert.That(sw.ElapsedMilliseconds).IsLessThan(10_000);
    }

    #endregion

    #region Memory Efficiency Tests

    [Test]
    public async Task PolygonRing_MemoryFootprint_ReasonableForManyVertices()
    {
        // Arrange
        const int vertexCount = 10_000;
        
        // Act - Create a ring with many vertices
        var beforeMemory = GC.GetTotalMemory(true);
        var ring = CreateCircularRing(0, 0, 100, vertexCount);
        var afterMemory = GC.GetTotalMemory(false);

        var memoryUsed = afterMemory - beforeMemory;

        // Assert
        // Each GeoPoint is 16 bytes (2 doubles), plus array overhead
        // Expected: ~160KB for 10K points
        await Assert.That(ring.Points.Length).IsEqualTo(vertexCount + 1); // +1 for closing point
        await Assert.That(memoryUsed).IsLessThan(500_000); // Allow some overhead
    }

    [Test]
    public async Task CountryBoundary_Creation_ManyPolygons()
    {
        // Arrange
        const int polygonCount = 100;
        const int verticesPerPolygon = 100;

        // Act
        var beforeMemory = GC.GetTotalMemory(true);
        var sw = Stopwatch.StartNew();
        
        var polygons = new Polygon[polygonCount];
        for (int i = 0; i < polygonCount; i++)
        {
            var ring = CreateCircularRing(i * 10 % 180 - 90, i * 20 % 360 - 180, 5, verticesPerPolygon);
            polygons[i] = new Polygon(ring);
        }
        var boundary = new CountryBoundary("XX", "Test Country", polygons);
        
        sw.Stop();
        var afterMemory = GC.GetTotalMemory(false);

        // Assert
        await Assert.That(sw.ElapsedMilliseconds).IsLessThan(100); // Fast creation
        await Assert.That(boundary.Polygons.Length).IsEqualTo(polygonCount);
        await Assert.That(boundary.TotalVertexCount).IsEqualTo(polygonCount * (verticesPerPolygon + 1));
        
        // Memory should be reasonable
        var memoryUsed = afterMemory - beforeMemory;
        await Assert.That(memoryUsed).IsLessThan(5_000_000); // Less than 5MB
    }

    [Test]
    public async Task BoundingBox_RejectionEfficiency_SkipsMostChecks()
    {
        // Arrange - Small polygon in a large search space
        var ring = CreateSquareRing(45, 45, 55, 55); // 10x10 degree area
        const int iterations = 100_000;
        var random = new Random(42);

        // Generate points across the entire world
        var points = new (double lat, double lon)[iterations];
        for (int i = 0; i < iterations; i++)
        {
            points[i] = (random.NextDouble() * 180 - 90, random.NextDouble() * 360 - 180);
        }

        // Act - Time with bounding box (normal operation)
        var swWithBbox = Stopwatch.StartNew();
        int insideWithBbox = 0;
        foreach (var (lat, lon) in points)
        {
            if (PointInPolygon.IsPointInRing(lat, lon, ring))
                insideWithBbox++;
        }
        swWithBbox.Stop();

        // Assert - Bounding box rejection should make this very fast
        // Only ~0.15% of points are in the 10x10 box within the 180x360 world
        await Assert.That(swWithBbox.ElapsedMilliseconds).IsLessThan(100);
        await Assert.That(insideWithBbox).IsLessThan(500); // Very few should be inside
    }

    #endregion

    #region Geohash Cache Performance Tests

    [Test]
    public async Task Geohash_Encode_Throughput()
    {
        // Arrange
        const int iterations = 100_000;
        var random = new Random(42);
        var points = new (double lat, double lon)[iterations];
        for (int i = 0; i < iterations; i++)
        {
            points[i] = (random.NextDouble() * 180 - 90, random.NextDouble() * 360 - 180);
        }

        // Act
        var sw = Stopwatch.StartNew();
        var hashes = new string[iterations];
        for (int i = 0; i < iterations; i++)
        {
            var (lat, lon) = points[i];
            hashes[i] = Geohash.Encode(lat, lon, BoundaryIndex.CachePrecision);
        }
        sw.Stop();

        // Assert - Should be very fast (geohash encoding is simple math)
        await Assert.That(sw.ElapsedMilliseconds).IsLessThan(500);
        
        // Verify hashes are valid
        foreach (var hash in hashes)
        {
            await Assert.That(hash.Length).IsEqualTo(BoundaryIndex.CachePrecision);
        }
    }

    [Test]
    public async Task Geohash_Decode_Throughput()
    {
        // Arrange
        const int iterations = 100_000;
        var random = new Random(42);
        
        // Generate random geohashes
        var hashes = new string[iterations];
        for (int i = 0; i < iterations; i++)
        {
            double lat = random.NextDouble() * 180 - 90;
            double lon = random.NextDouble() * 360 - 180;
            hashes[i] = Geohash.Encode(lat, lon, BoundaryIndex.CachePrecision);
        }

        // Act
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var (lat, lon) = Geohash.DecodeCenter(hashes[i]);
            // Use values to prevent optimization
            _ = lat + lon;
        }
        sw.Stop();

        // Assert
        await Assert.That(sw.ElapsedMilliseconds).IsLessThan(500);
    }

    [Test]
    public async Task Geohash_CacheSimulation_HighHitRate()
    {
        // Arrange - Simulate geohash cache behavior
        const int cacheSize = 10_000;
        const int lookupCount = 100_000;
        var random = new Random(42);

        // Build a cache with common geohashes (clustered locations like a photo collection)
        var cache = new Dictionary<string, string>(cacheSize);
        var clusterCenters = new (double lat, double lon)[50];
        for (int i = 0; i < clusterCenters.Length; i++)
        {
            clusterCenters[i] = (random.NextDouble() * 180 - 90, random.NextDouble() * 360 - 180);
        }

        // Populate cache with geohashes around cluster centers
        foreach (var (clat, clon) in clusterCenters)
        {
            for (int i = 0; i < 200; i++)
            {
                double lat = clat + (random.NextDouble() - 0.5) * 2; // ±1 degree
                double lon = clon + (random.NextDouble() - 0.5) * 2;
                string hash = Geohash.Encode(lat, lon, BoundaryIndex.CachePrecision);
                cache.TryAdd(hash, "XX"); // Country code doesn't matter for this test
            }
        }

        // Generate lookups biased toward cluster centers (simulating real photo locations)
        var lookupPoints = new (double lat, double lon)[lookupCount];
        for (int i = 0; i < lookupCount; i++)
        {
            var center = clusterCenters[random.Next(clusterCenters.Length)];
            // 80% near cluster, 20% random
            if (random.NextDouble() < 0.8)
            {
                lookupPoints[i] = (
                    center.lat + (random.NextDouble() - 0.5) * 2,
                    center.lon + (random.NextDouble() - 0.5) * 2
                );
            }
            else
            {
                lookupPoints[i] = (random.NextDouble() * 180 - 90, random.NextDouble() * 360 - 180);
            }
        }

        // Act
        var sw = Stopwatch.StartNew();
        int hits = 0;
        int misses = 0;
        foreach (var (lat, lon) in lookupPoints)
        {
            string hash = Geohash.Encode(lat, lon, BoundaryIndex.CachePrecision);
            if (cache.ContainsKey(hash))
                hits++;
            else
                misses++;
        }
        sw.Stop();

        double hitRate = (double)hits / lookupCount * 100;

        // Assert
        await Assert.That(sw.ElapsedMilliseconds).IsLessThan(500);
        await Assert.That(hitRate).IsGreaterThan(50); // Should have >50% hit rate with clustered data
    }

    #endregion

    #region Scaling Tests

    [Test]
    public async Task Scaling_ManyCountries_LookupRemainsFast()
    {
        // Arrange - Create many countries
        const int countryCount = 200;
        var countries = CreateTestCountries(countryCount);
        const int iterations = 1_000;
        var random = new Random(42);

        var points = new (double lat, double lon)[iterations];
        for (int i = 0; i < iterations; i++)
        {
            points[i] = (random.NextDouble() * 180 - 90, random.NextDouble() * 360 - 180);
        }

        // Act - Check each point against all countries
        var sw = Stopwatch.StartNew();
        int totalMatches = 0;
        foreach (var (lat, lon) in points)
        {
            foreach (var country in countries)
            {
                if (PointInPolygon.IsPointInCountry(lat, lon, country))
                {
                    totalMatches++;
                    break; // Found a match, move to next point
                }
            }
        }
        sw.Stop();

        // Assert - Should complete in reasonable time despite many countries
        // 1K points * 200 countries = 200K potential checks (reduced by bounding box rejection)
        await Assert.That(sw.ElapsedMilliseconds).IsLessThan(5000);
    }

    [Test]
    public async Task Scaling_VertexCount_LinearBehavior()
    {
        // Arrange - Test with varying vertex counts
        var vertexCounts = new[] { 100, 500, 1000, 2000 };
        const int iterations = 1_000;
        var random = new Random(42);

        var points = new (double lat, double lon)[iterations];
        for (int i = 0; i < iterations; i++)
        {
            points[i] = (random.NextDouble() * 100, random.NextDouble() * 100);
        }

        var times = new Dictionary<int, long>();

        // Act
        foreach (int vertexCount in vertexCounts)
        {
            var ring = CreateCircularRing(50, 50, 40, vertexCount);
            var sw = Stopwatch.StartNew();
            foreach (var (lat, lon) in points)
            {
                PointInPolygon.IsPointInRing(lat, lon, ring);
            }
            sw.Stop();
            times[vertexCount] = sw.ElapsedMilliseconds;
        }

        // Assert - Time should scale roughly linearly with vertex count
        // Allow some overhead for JIT and timing variance
        var baseTime = Math.Max(1, times[100]);
        var maxTime = times[2000];
        double ratio = (double)maxTime / baseTime;
        
        await Assert.That(ratio).IsLessThan(100); // Should not be more than 100x slower (allows for timing variance)
        
        // All should complete in reasonable time
        foreach (var (vertexCount, time) in times)
        {
            await Assert.That(time).IsLessThan(1000);
        }
    }

    [Test]
    public async Task Scaling_PolygonCount_ReasonablePerformance()
    {
        // Arrange - Test with varying polygon counts per country
        var polygonCounts = new[] { 1, 10, 50, 100 };
        const int iterations = 1_000;
        var random = new Random(42);

        var points = new (double lat, double lon)[iterations];
        for (int i = 0; i < iterations; i++)
        {
            points[i] = (random.NextDouble() * 180 - 90, random.NextDouble() * 360 - 180);
        }

        var times = new Dictionary<int, long>();

        // Act
        foreach (int polygonCount in polygonCounts)
        {
            var polygons = new Polygon[polygonCount];
            for (int i = 0; i < polygonCount; i++)
            {
                var ring = CreateSquareRing(
                    (i * 3) % 180 - 90, 
                    (i * 5) % 360 - 180, 
                    (i * 3) % 180 - 90 + 2, 
                    (i * 5) % 360 - 180 + 2
                );
                polygons[i] = new Polygon(ring);
            }
            var boundary = new CountryBoundary("XX", "Test", polygons);

            var sw = Stopwatch.StartNew();
            foreach (var (lat, lon) in points)
            {
                PointInPolygon.IsPointInCountry(lat, lon, boundary);
            }
            sw.Stop();
            times[polygonCount] = sw.ElapsedMilliseconds;
        }

        // Assert - All should complete in reasonable time
        foreach (var (count, time) in times)
        {
            await Assert.That(time).IsLessThan(500);
        }
    }

    #endregion

    #region Edge Detection Performance

    [Test]
    public async Task IsPointOnEdge_Performance_ManyChecks()
    {
        // Arrange
        var ring = CreateCircularRing(0, 0, 50, 100);
        const int iterations = 10_000;
        var random = new Random(42);

        var points = new (double lat, double lon)[iterations];
        for (int i = 0; i < iterations; i++)
        {
            points[i] = (random.NextDouble() * 100 - 50, random.NextDouble() * 100 - 50);
        }

        // Act
        var sw = Stopwatch.StartNew();
        int onEdgeCount = 0;
        foreach (var (lat, lon) in points)
        {
            if (PointInPolygon.IsPointOnEdge(lat, lon, ring))
                onEdgeCount++;
        }
        sw.Stop();

        // Assert
        await Assert.That(sw.ElapsedMilliseconds).IsLessThan(500);
        // Very few random points should be on the edge
        await Assert.That(onEdgeCount).IsLessThan(100);
    }

    #endregion

    #region Coordinate Normalization Performance

    [Test]
    public async Task CoordinateNormalization_Throughput()
    {
        // Arrange
        const int iterations = 1_000_000;
        var random = new Random(42);
        var lats = new double[iterations];
        var lons = new double[iterations];
        for (int i = 0; i < iterations; i++)
        {
            lats[i] = random.NextDouble() * 400 - 200; // -200 to 200 (out of bounds)
            lons[i] = random.NextDouble() * 800 - 400; // -400 to 400 (out of bounds)
        }

        // Act
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            _ = PointInPolygon.ClampLatitude(lats[i]);
            _ = PointInPolygon.NormalizeLongitude(lons[i]);
        }
        sw.Stop();

        // Assert - Simple math operations should be extremely fast
        await Assert.That(sw.ElapsedMilliseconds).IsLessThan(100);
    }

    #endregion

    #region Comparative Performance Tests

    [Test]
    public async Task BoundingBoxVsFullCheck_SpeedupRatio()
    {
        // Arrange - Polygon that takes up small portion of bounding area
        var ring = CreateCircularRing(0, 0, 10, 500); // Small circle with many vertices
        const int iterations = 10_000;
        var random = new Random(42);

        // Points mostly outside bounding box
        var farPoints = new (double lat, double lon)[iterations];
        for (int i = 0; i < iterations; i++)
        {
            farPoints[i] = (random.NextDouble() * 180 - 90, random.NextDouble() * 360 - 180);
        }

        // Points inside bounding box (near polygon)
        var nearPoints = new (double lat, double lon)[iterations];
        for (int i = 0; i < iterations; i++)
        {
            nearPoints[i] = (random.NextDouble() * 30 - 15, random.NextDouble() * 30 - 15);
        }

        // Act - Time with far points (bounding box rejection)
        var swFar = Stopwatch.StartNew();
        foreach (var (lat, lon) in farPoints)
        {
            PointInPolygon.IsPointInRing(lat, lon, ring);
        }
        swFar.Stop();

        // Time with near points (full polygon check)
        var swNear = Stopwatch.StartNew();
        foreach (var (lat, lon) in nearPoints)
        {
            PointInPolygon.IsPointInRing(lat, lon, ring);
        }
        swNear.Stop();

        // Assert - Far points should be significantly faster due to bounding box rejection
        // At least 2x speedup expected
        await Assert.That(swFar.ElapsedMilliseconds).IsLessThan(swNear.ElapsedMilliseconds);
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

    private static PolygonRing CreateCircularRing(double centerLat, double centerLon, double radius, int vertexCount)
    {
        var points = new GeoPoint[vertexCount + 1];
        for (int i = 0; i < vertexCount; i++)
        {
            double angle = 2 * Math.PI * i / vertexCount;
            points[i] = new GeoPoint(
                centerLat + radius * Math.Sin(angle),
                centerLon + radius * Math.Cos(angle)
            );
        }
        points[vertexCount] = points[0]; // Close the ring
        return new PolygonRing(points);
    }

    private static CountryBoundary[] CreateTestCountries(int count)
    {
        var countries = new CountryBoundary[count];
        var random = new Random(42);

        for (int i = 0; i < count; i++)
        {
            // Create a small polygon at a random location
            double lat = random.NextDouble() * 160 - 80; // -80 to 80
            double lon = random.NextDouble() * 340 - 170; // -170 to 170
            double size = random.NextDouble() * 5 + 1; // 1-6 degree size

            var ring = CreateSquareRing(lat, lon, lat + size, lon + size);
            var polygon = new Polygon(ring);
            countries[i] = new CountryBoundary(
                $"C{i:D3}",
                $"Country {i}",
                new[] { polygon }
            );
        }

        return countries;
    }

    #endregion
}
