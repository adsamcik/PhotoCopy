using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Files;
using PhotoCopy.Configuration;
using PhotoCopy.Tests.TestingImplementation;

namespace PhotoCopy.Tests.Files;

public class CachedFileMetadataExtractorTests : TestBase
{
    private readonly PhotoCopyConfig _config = new()
    {
        AllowedExtensions = [".jpg", ".jpeg", ".png"],
        LogLevel = OutputLevel.Verbose
    };

    private CachedFileMetadataExtractor CreateExtractor(int cacheSize = 8)
    {
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(_config);
        var logger = Substitute.For<ILogger<CachedFileMetadataExtractor>>();
        return new CachedFileMetadataExtractor(logger, options, cacheSize);
    }

    #region Caching Behavior Tests

    [Test]
    public async Task GetDateTime_MultipleCallsSameFile_CachesResult()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "test.jpg");
        var dateTaken = new DateTime(2024, 6, 15, 14, 30, 0);
        
        var jpegBytes = MockImageGenerator.CreateJpeg(dateTaken: dateTaken);
        File.WriteAllBytes(tempFile, jpegBytes);
        
        var fileInfo = new FileInfo(tempFile);
        var extractor = CreateExtractor();

        try
        {
            // Act - call multiple times
            var result1 = extractor.GetDateTime(fileInfo);
            var result2 = extractor.GetDateTime(fileInfo);
            var result3 = extractor.GetDateTime(fileInfo);

            // Assert - cache should only contain one entry (file read once)
            await Assert.That(extractor.CacheCount).IsEqualTo(1);
            await Assert.That(result1.Taken).IsEqualTo(dateTaken);
            await Assert.That(result2.Taken).IsEqualTo(dateTaken);
            await Assert.That(result3.Taken).IsEqualTo(dateTaken);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task AllExtractionMethods_SameFile_SharesCachedMetadata()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "test.jpg");
        var dateTaken = new DateTime(2024, 6, 15, 14, 30, 0);
        var expectedLat = 40.7128;
        var expectedLon = -74.0060;
        
        // Create image with date and GPS
        var jpegBytes = MockImageGenerator.CreateJpeg(
            dateTaken: dateTaken, 
            gps: (expectedLat, expectedLon));
        File.WriteAllBytes(tempFile, jpegBytes);
        
        var fileInfo = new FileInfo(tempFile);
        var extractor = CreateExtractor();

        try
        {
            // Act - call all extraction methods
            var dateTime = extractor.GetDateTime(fileInfo);
            var coords = extractor.GetCoordinates(fileInfo);
            var camera = extractor.GetCamera(fileInfo);
            var album = extractor.GetAlbum(fileInfo);

            // Assert - cache should only contain one entry (file read once)
            await Assert.That(extractor.CacheCount).IsEqualTo(1);
            await Assert.That(dateTime.Taken).IsEqualTo(dateTaken);
            await Assert.That(coords).IsNotNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task CacheEviction_WhenCapacityExceeded_EvictsLeastRecentlyUsed()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        
        // Use cache size of 2
        var extractor = CreateExtractor(cacheSize: 2);
        
        // Create 3 files
        var files = new List<FileInfo>();
        for (int i = 0; i < 3; i++)
        {
            var tempFile = Path.Combine(tempDir, $"test{i}.jpg");
            var jpegBytes = MockImageGenerator.CreateJpeg(dateTaken: new DateTime(2024, 1, i + 1));
            File.WriteAllBytes(tempFile, jpegBytes);
            files.Add(new FileInfo(tempFile));
        }

        try
        {
            // Act - process all 3 files
            extractor.GetDateTime(files[0]);
            extractor.GetDateTime(files[1]);
            
            // Cache is at capacity (2)
            await Assert.That(extractor.CacheCount).IsEqualTo(2);
            
            // Add third file - should evict first (LRU)
            extractor.GetDateTime(files[2]);
            
            // Assert - cache should still be at capacity
            await Assert.That(extractor.CacheCount).IsEqualTo(2);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ClearCache_RemovesAllEntries()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "test.jpg");
        
        var jpegBytes = MockImageGenerator.CreateJpeg();
        File.WriteAllBytes(tempFile, jpegBytes);
        
        var fileInfo = new FileInfo(tempFile);
        var extractor = CreateExtractor();

        try
        {
            // Add to cache
            extractor.GetDateTime(fileInfo);
            await Assert.That(extractor.CacheCount).IsEqualTo(1);
            
            // Act
            extractor.ClearCache();
            
            // Assert
            await Assert.That(extractor.CacheCount).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region Extraction Tests (Same as FileMetadataExtractor)

    [Test]
    public async Task GetDateTime_WithValidExifDate_ReturnsDateTaken()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "test.jpg");
        var expectedDate = new DateTime(2024, 6, 15, 14, 30, 0);
        
        var jpegBytes = MockImageGenerator.CreateJpeg(dateTaken: expectedDate);
        File.WriteAllBytes(tempFile, jpegBytes);
        
        var fileInfo = new FileInfo(tempFile);
        var extractor = CreateExtractor();

        try
        {
            // Act
            var result = extractor.GetDateTime(fileInfo);

            // Assert
            await Assert.That(result.Taken).IsEqualTo(expectedDate);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task GetCoordinates_WithValidGpsData_ReturnsCoordinates()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "gps_image.jpg");
        var expectedLat = 40.7128;
        var expectedLon = -74.0060;
        
        var jpegBytes = MockImageGenerator.CreateJpeg(gps: (expectedLat, expectedLon));
        File.WriteAllBytes(tempFile, jpegBytes);
        
        var fileInfo = new FileInfo(tempFile);
        var extractor = CreateExtractor();

        try
        {
            // Act
            var result = extractor.GetCoordinates(fileInfo);

            // Assert
            await Assert.That(result).IsNotNull();
            await Assert.That(result!.Value.Latitude).IsEqualTo(expectedLat).Within(0.0001);
            await Assert.That(result!.Value.Longitude).IsEqualTo(expectedLon).Within(0.0001);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task GetCoordinates_WithNoGpsData_ReturnsNull()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "no_gps.jpg");
        
        var jpegBytes = MockImageGenerator.CreateJpeg(dateTaken: DateTime.Now);
        File.WriteAllBytes(tempFile, jpegBytes);
        
        var fileInfo = new FileInfo(tempFile);
        var extractor = CreateExtractor();

        try
        {
            // Act
            var result = extractor.GetCoordinates(fileInfo);

            // Assert
            await Assert.That(result).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task GetCamera_WithValidImage_ReturnsNullWhenNoCameraData()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "camera_image.jpg");
        
        // MockImageGenerator creates JPEG with date/GPS but not camera metadata
        var jpegBytes = MockImageGenerator.CreateJpeg(dateTaken: DateTime.Now);
        File.WriteAllBytes(tempFile, jpegBytes);
        
        var fileInfo = new FileInfo(tempFile);
        var extractor = CreateExtractor();

        try
        {
            // Act
            var result = extractor.GetCamera(fileInfo);

            // Assert - no camera data in mock image
            await Assert.That(result).IsNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task GetCamera_NonImageFile_ReturnsNull()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "Test content");
        var fileInfo = new FileInfo(tempFile);
        var extractor = CreateExtractor();

        try
        {
            // Act
            var result = extractor.GetCamera(fileInfo);

            // Assert
            await Assert.That(result).IsNull();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion

    #region ParseIso6709 Tests

    [Test]
    public async Task ParseIso6709_ValidCoordinates_ReturnsLatLon()
    {
        // Arrange
        var input = "+48.8584+002.2945/";

        // Act
        var result = CachedFileMetadataExtractor.ParseIso6709(input);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value.Latitude).IsEqualTo(48.8584).Within(0.0001);
        await Assert.That(result!.Value.Longitude).IsEqualTo(2.2945).Within(0.0001);
    }

    [Test]
    public async Task ParseIso6709_WithAltitude_ReturnsLatLon()
    {
        // Arrange
        var input = "+48.8584+002.2945+100.00/";

        // Act
        var result = CachedFileMetadataExtractor.ParseIso6709(input);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value.Latitude).IsEqualTo(48.8584).Within(0.0001);
        await Assert.That(result!.Value.Longitude).IsEqualTo(2.2945).Within(0.0001);
    }

    [Test]
    public async Task ParseIso6709_NegativeCoordinates_ReturnsCorrectValues()
    {
        // Arrange
        var input = "-33.8688+151.2093/";

        // Act
        var result = CachedFileMetadataExtractor.ParseIso6709(input);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value.Latitude).IsEqualTo(-33.8688).Within(0.0001);
        await Assert.That(result!.Value.Longitude).IsEqualTo(151.2093).Within(0.0001);
    }

    [Test]
    public async Task ParseIso6709_NullOrEmpty_ReturnsNull()
    {
        await Assert.That(CachedFileMetadataExtractor.ParseIso6709(null!)).IsNull();
        await Assert.That(CachedFileMetadataExtractor.ParseIso6709("")).IsNull();
        await Assert.That(CachedFileMetadataExtractor.ParseIso6709("   ")).IsNull();
    }

    [Test]
    public async Task ParseIso6709_ZeroCoordinates_ReturnsNull()
    {
        // Arrange
        var input = "+0.0000+0.0000/";

        // Act
        var result = CachedFileMetadataExtractor.ParseIso6709(input);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region LRU Cache Tests

    [Test]
    public async Task LruCache_AccessingEntry_MovesToFront()
    {
        // Arrange
        var cache = new LruCache<string, int>(2);
        
        // Add two entries
        cache.Add("a", 1);
        cache.Add("b", 2);
        
        // Access "a" to move it to front
        cache.TryGet("a", out _);
        
        // Add third entry - should evict "b" (least recently used)
        cache.Add("c", 3);
        
        // Assert - "a" should still be in cache, "b" should be evicted
        await Assert.That(cache.TryGet("a", out var aValue)).IsTrue();
        await Assert.That(aValue).IsEqualTo(1);
        await Assert.That(cache.TryGet("b", out _)).IsFalse();
        await Assert.That(cache.TryGet("c", out var cValue)).IsTrue();
        await Assert.That(cValue).IsEqualTo(3);
    }

    [Test]
    public async Task LruCache_UpdateExistingEntry_UpdatesValue()
    {
        // Arrange
        var cache = new LruCache<string, int>(2);
        
        cache.Add("a", 1);
        cache.Add("a", 2);
        
        // Assert
        await Assert.That(cache.TryGet("a", out var value)).IsTrue();
        await Assert.That(value).IsEqualTo(2);
        await Assert.That(cache.Count).IsEqualTo(1);
    }

    [Test]
    public void LruCache_ZeroCapacity_ThrowsException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<string, int>(0));
    }

    [Test]
    public void LruCache_NegativeCapacity_ThrowsException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<string, int>(-1));
    }

    [Test]
    public async Task LruCache_Clear_RemovesAllEntries()
    {
        // Arrange
        var cache = new LruCache<string, int>(3);
        cache.Add("a", 1);
        cache.Add("b", 2);
        cache.Add("c", 3);
        
        // Act
        cache.Clear();
        
        // Assert
        await Assert.That(cache.Count).IsEqualTo(0);
        await Assert.That(cache.TryGet("a", out _)).IsFalse();
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public async Task GetDateTime_InvalidFile_ReturnsFileCreationTime()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "Not a valid image");
        var fileInfo = new FileInfo(tempFile);
        var extractor = CreateExtractor();

        try
        {
            // Act
            var result = extractor.GetDateTime(fileInfo);

            // Assert - should fall back to file creation time
            await Assert.That(result.Created).IsEqualTo(fileInfo.CreationTime);
            await Assert.That(result.Taken).IsEqualTo(default(DateTime));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetCoordinates_InvalidFile_ReturnsNull()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "Not a valid image");
        var fileInfo = new FileInfo(tempFile);
        var extractor = CreateExtractor();

        try
        {
            // Act
            var result = extractor.GetCoordinates(fileInfo);

            // Assert
            await Assert.That(result).IsNull();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion
}
