using PhotoCopy.Files.Geo;

namespace PhotoCopy.Tests.Files.Geo;

public class CellCacheTests
{
    [Test]
    public async Task TryGet_EmptyCache_ReturnsFalse()
    {
        using var cache = new CellCache();
        await Assert.That(cache.TryGet("dr5r", out _)).IsFalse();
    }

    [Test]
    public async Task Put_AndTryGet_ReturnsCell()
    {
        using var cache = new CellCache();
        var cell = CreateTestCell("dr5r", 100);
        
        cache.Put("dr5r", cell);
        
        await Assert.That(cache.TryGet("dr5r", out var retrieved)).IsTrue();
        await Assert.That(retrieved).IsSameReferenceAs(cell);
    }

    [Test]
    public async Task Put_ExceedsMemoryLimit_EvictsOldEntries()
    {
        // Cache with 1KB limit
        using var cache = new CellCache(1024);
        
        // Add cells that each use ~500 bytes estimated
        // Using valid geohash characters (no a, i, l, o)
        var cell1 = CreateTestCell("dr5r", 500);
        var cell2 = CreateTestCell("dr5s", 500);
        var cell3 = CreateTestCell("dr5t", 500);
        
        cache.Put("dr5r", cell1);
        cache.Put("dr5s", cell2);
        
        await Assert.That(cache.Count).IsEqualTo(2);
        
        // Adding third should evict the first (LRU)
        cache.Put("dr5t", cell3);
        
        await Assert.That(cache.TryGet("dr5r", out _)).IsFalse();
        await Assert.That(cache.TryGet("dr5s", out _)).IsTrue();
        await Assert.That(cache.TryGet("dr5t", out _)).IsTrue();
    }

    [Test]
    public async Task TryGet_UpdatesLRUOrder()
    {
        using var cache = new CellCache(1024);
        
        // Using valid geohash characters
        var cell1 = CreateTestCell("dr5r", 400);
        var cell2 = CreateTestCell("dr5s", 400);
        var cell3 = CreateTestCell("dr5t", 400);
        
        cache.Put("dr5r", cell1);
        cache.Put("dr5s", cell2);
        
        // Access cell1 to make it recently used
        cache.TryGet("dr5r", out _);
        
        // Add cell3, should evict cell2 (now least recently used)
        cache.Put("dr5t", cell3);
        
        await Assert.That(cache.TryGet("dr5r", out _)).IsTrue();
        await Assert.That(cache.TryGet("dr5s", out _)).IsFalse();
        await Assert.That(cache.TryGet("dr5t", out _)).IsTrue();
    }

    [Test]
    public async Task Remove_ExistingEntry_ReturnsTrue()
    {
        using var cache = new CellCache();
        cache.Put("dr5r", CreateTestCell("dr5r", 100));
        
        await Assert.That(cache.Remove("dr5r")).IsTrue();
        await Assert.That(cache.TryGet("dr5r", out _)).IsFalse();
    }

    [Test]
    public async Task Remove_NonExistingEntry_ReturnsFalse()
    {
        using var cache = new CellCache();
        await Assert.That(cache.Remove("nonexistent")).IsFalse();
    }

    [Test]
    public async Task Clear_RemovesAllEntries()
    {
        using var cache = new CellCache();
        cache.Put("dr5r", CreateTestCell("dr5r", 100));
        cache.Put("dr5s", CreateTestCell("dr5s", 100));
        
        cache.Clear();
        
        await Assert.That(cache.Count).IsEqualTo(0);
        await Assert.That(cache.CurrentMemoryBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Statistics_TracksHitsAndMisses()
    {
        using var cache = new CellCache();
        cache.Put("dr5r", CreateTestCell("dr5r", 100));
        
        cache.TryGet("dr5r", out _); // Hit
        cache.TryGet("dr5r", out _); // Hit
        cache.TryGet("xxxx", out _); // Miss
        
        await Assert.That(cache.HitCount).IsEqualTo(2);
        await Assert.That(cache.MissCount).IsEqualTo(1);
    }

    private static GeoCell CreateTestCell(string geohash, int estimatedBytes)
    {
        return new GeoCell
        {
            Geohash = geohash,
            Entries = Array.Empty<LocationEntry>(),
            Bounds = Geohash.DecodeBounds(geohash),
            EstimatedMemoryBytes = estimatedBytes
        };
    }
}
