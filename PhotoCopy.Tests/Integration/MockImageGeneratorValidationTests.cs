using System;
using System.IO;
using System.Linq;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using PhotoCopy.Tests.TestingImplementation;
using Directory = System.IO.Directory;

namespace PhotoCopy.Tests.Integration;

/// <summary>
/// Critical validation tests to ensure MockImageGenerator produces images
/// that MetadataExtractor can actually parse. If these tests fail,
/// MockImageGenerator needs to be fixed before proper integration testing is possible.
/// </summary>
[Property("Category", "Integration")]
public class MockImageGeneratorValidationTests
{
    private readonly string _baseTestDirectory;

    public MockImageGeneratorValidationTests()
    {
        _baseTestDirectory = Path.Combine(Path.GetTempPath(), "MockImageGeneratorValidationTests");
        
        if (!Directory.Exists(_baseTestDirectory))
        {
            Directory.CreateDirectory(_baseTestDirectory);
        }
    }

    private string CreateUniqueTestDirectory()
    {
        var uniquePath = Path.Combine(_baseTestDirectory, Guid.NewGuid().ToString());
        Directory.CreateDirectory(uniquePath);
        return uniquePath;
    }

    private void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Directory.Delete(path, true);
            }
        }
        catch (IOException) { }
    }

    [Test]
    public async Task CreateJpeg_WithDateTaken_MetadataExtractorCanParseDateTimeOriginal()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var expectedDate = new DateTime(2023, 7, 15, 14, 30, 45);
        var filePath = Path.Combine(testDir, "test_with_date.jpg");

        try
        {
            // Act - Create JPEG with known date
            var jpegBytes = MockImageGenerator.CreateJpeg(dateTaken: expectedDate);
            await File.WriteAllBytesAsync(filePath, jpegBytes);

            // Parse with MetadataExtractor
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var exifSubIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

            // Assert - EXIF directory exists
            await Assert.That(exifSubIfdDirectory).IsNotNull()
                .Because("MockImageGenerator should create valid EXIF Sub-IFD directory");

            // Assert - DateTimeOriginal can be extracted
            var hasDate = exifSubIfdDirectory!.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var extractedDate);
            
            await Assert.That(hasDate).IsTrue()
                .Because("MockImageGenerator should embed DateTimeOriginal tag that MetadataExtractor can parse");

            await Assert.That(extractedDate).IsEqualTo(expectedDate)
                .Because("Extracted date should match the date that was embedded");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task CreateJpeg_WithGpsCoordinates_MetadataExtractorCanParseGpsDirectory()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var expectedLatitude = 40.7128;   // New York City latitude
        var expectedLongitude = -74.0060; // New York City longitude
        var filePath = Path.Combine(testDir, "test_with_gps.jpg");

        try
        {
            // Act - Create JPEG with known GPS coordinates
            var jpegBytes = MockImageGenerator.CreateJpeg(gps: (expectedLatitude, expectedLongitude));
            await File.WriteAllBytesAsync(filePath, jpegBytes);

            // Parse with MetadataExtractor
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();

            // Assert - GPS directory exists
            await Assert.That(gpsDirectory).IsNotNull()
                .Because("MockImageGenerator should create valid GPS IFD directory");

            // Assert - GPS coordinates can be extracted
            var hasLocation = gpsDirectory!.TryGetGeoLocation(out var location);
            
            await Assert.That(hasLocation).IsTrue()
                .Because("MockImageGenerator should embed GPS tags that MetadataExtractor can parse");

            await Assert.That(location.IsZero).IsFalse()
                .Because("GPS location should not be zero");

            // Assert with tolerance for floating point conversion through DMS
            await Assert.That(location.Latitude).IsEqualTo(expectedLatitude).Within(0.001)
                .Because("Extracted latitude should match the embedded value within precision tolerance");

            await Assert.That(location.Longitude).IsEqualTo(expectedLongitude).Within(0.001)
                .Because("Extracted longitude should match the embedded value within precision tolerance");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task CreateJpeg_WithDateAndGps_MetadataExtractorCanParseBoth()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var expectedDate = new DateTime(2024, 12, 25, 10, 0, 0);
        var expectedLatitude = 48.8566;   // Paris latitude
        var expectedLongitude = 2.3522;   // Paris longitude
        var filePath = Path.Combine(testDir, "test_with_both.jpg");

        try
        {
            // Act - Create JPEG with both date and GPS
            var jpegBytes = MockImageGenerator.CreateJpeg(
                dateTaken: expectedDate,
                gps: (expectedLatitude, expectedLongitude));
            await File.WriteAllBytesAsync(filePath, jpegBytes);

            // Parse with MetadataExtractor
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            
            // Validate EXIF date
            var exifSubIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            await Assert.That(exifSubIfdDirectory).IsNotNull();
            
            var hasDate = exifSubIfdDirectory!.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var extractedDate);
            await Assert.That(hasDate).IsTrue();
            await Assert.That(extractedDate).IsEqualTo(expectedDate);

            // Validate GPS coordinates
            var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();
            await Assert.That(gpsDirectory).IsNotNull();
            
            var hasLocation = gpsDirectory!.TryGetGeoLocation(out var location);
            await Assert.That(hasLocation).IsTrue();
            await Assert.That(location.Latitude).IsEqualTo(expectedLatitude).Within(0.001);
            await Assert.That(location.Longitude).IsEqualTo(expectedLongitude).Within(0.001);
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task CreateJpeg_WithNegativeGpsCoordinates_MetadataExtractorParsesCorrectly()
    {
        // Arrange - Test Southern and Western hemisphere (negative coordinates)
        var testDir = CreateUniqueTestDirectory();
        var expectedLatitude = -33.8688;  // Sydney, Australia (South)
        var expectedLongitude = 151.2093; // Sydney, Australia (East)
        var filePath = Path.Combine(testDir, "test_negative_lat.jpg");

        try
        {
            // Act
            var jpegBytes = MockImageGenerator.CreateJpeg(gps: (expectedLatitude, expectedLongitude));
            await File.WriteAllBytesAsync(filePath, jpegBytes);

            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();

            // Assert
            await Assert.That(gpsDirectory).IsNotNull();
            var hasLocation = gpsDirectory!.TryGetGeoLocation(out var location);
            await Assert.That(hasLocation).IsTrue();
            
            await Assert.That(location.Latitude).IsEqualTo(expectedLatitude).Within(0.001)
                .Because("Negative latitude (Southern hemisphere) should be parsed correctly");
            await Assert.That(location.Longitude).IsEqualTo(expectedLongitude).Within(0.001);
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task CreatePng_WithDateTaken_MetadataExtractorCanParseDateTimeOriginal()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var expectedDate = new DateTime(2022, 1, 1, 12, 0, 0);
        var filePath = Path.Combine(testDir, "test_with_date.png");

        try
        {
            // Act - Create PNG with known date
            var pngBytes = MockImageGenerator.CreatePng(dateTaken: expectedDate);
            await File.WriteAllBytesAsync(filePath, pngBytes);

            // Parse with MetadataExtractor
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var exifSubIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

            // Assert - EXIF directory exists
            await Assert.That(exifSubIfdDirectory).IsNotNull()
                .Because("MockImageGenerator.CreatePng should create valid EXIF Sub-IFD in eXIf chunk");

            // Assert - DateTimeOriginal can be extracted
            var hasDate = exifSubIfdDirectory!.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var extractedDate);
            
            await Assert.That(hasDate).IsTrue()
                .Because("MockImageGenerator should embed DateTimeOriginal in PNG that MetadataExtractor can parse");

            await Assert.That(extractedDate).IsEqualTo(expectedDate);
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task CreatePng_WithGpsCoordinates_MetadataExtractorCanParseGpsDirectory()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var expectedLatitude = 51.5074;   // London latitude
        var expectedLongitude = -0.1278;  // London longitude
        var filePath = Path.Combine(testDir, "test_with_gps.png");

        try
        {
            // Act - Create PNG with known GPS coordinates
            var pngBytes = MockImageGenerator.CreatePng(gps: (expectedLatitude, expectedLongitude));
            await File.WriteAllBytesAsync(filePath, pngBytes);

            // Parse with MetadataExtractor
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();

            // Assert - GPS directory exists
            await Assert.That(gpsDirectory).IsNotNull()
                .Because("MockImageGenerator.CreatePng should create valid GPS IFD in eXIf chunk");

            // Assert - GPS coordinates can be extracted
            var hasLocation = gpsDirectory!.TryGetGeoLocation(out var location);
            
            await Assert.That(hasLocation).IsTrue();
            await Assert.That(location.Latitude).IsEqualTo(expectedLatitude).Within(0.001);
            await Assert.That(location.Longitude).IsEqualTo(expectedLongitude).Within(0.001);
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task CreateJpeg_WithoutMetadata_ProducesValidParseableJpeg()
    {
        // Arrange - Ensure basic JPEG without metadata is still valid
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "test_no_metadata.jpg");

        try
        {
            // Act
            var jpegBytes = MockImageGenerator.CreateJpeg();
            await File.WriteAllBytesAsync(filePath, jpegBytes);

            // Parse with MetadataExtractor - should not throw
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            // Assert - At minimum we should get some directories (JFIF at least)
            await Assert.That(directories).IsNotNull();
            await Assert.That(directories.Count()).IsGreaterThan(0)
                .Because("Even a minimal JPEG should have parseable structure");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task CreateJpeg_VariousDateFormats_AllParsedCorrectly()
    {
        // Arrange - Test edge cases for dates
        var testDir = CreateUniqueTestDirectory();
        var testDates = new[]
        {
            new DateTime(2000, 1, 1, 0, 0, 0),    // Y2K
            new DateTime(2023, 12, 31, 23, 59, 59), // End of year
            new DateTime(2024, 2, 29, 12, 0, 0),  // Leap year
        };

        try
        {
            foreach (var expectedDate in testDates)
            {
                var filePath = Path.Combine(testDir, $"test_{expectedDate:yyyyMMdd_HHmmss}.jpg");
                
                var jpegBytes = MockImageGenerator.CreateJpeg(dateTaken: expectedDate);
                await File.WriteAllBytesAsync(filePath, jpegBytes);

                var directories = ImageMetadataReader.ReadMetadata(filePath);
                var exifSubIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                
                await Assert.That(exifSubIfdDirectory).IsNotNull();
                
                var hasDate = exifSubIfdDirectory!.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var extractedDate);
                await Assert.That(hasDate).IsTrue();
                await Assert.That(extractedDate).IsEqualTo(expectedDate)
                    .Because($"Date {expectedDate} should be correctly embedded and extracted");
            }
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task CreateJpeg_GpsAtEquatorAndPrimeMeridian_ParsedCorrectly()
    {
        // Arrange - Test GPS at 0,0 (null island)
        var testDir = CreateUniqueTestDirectory();
        var expectedLatitude = 0.0;
        var expectedLongitude = 0.0;
        var filePath = Path.Combine(testDir, "test_null_island.jpg");

        try
        {
            var jpegBytes = MockImageGenerator.CreateJpeg(gps: (expectedLatitude, expectedLongitude));
            await File.WriteAllBytesAsync(filePath, jpegBytes);

            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();

            await Assert.That(gpsDirectory).IsNotNull();
            var hasLocation = gpsDirectory!.TryGetGeoLocation(out var location);
            
            // Note: Exact 0,0 might be considered "zero" by MetadataExtractor
            // This test validates the behavior at this edge case
            await Assert.That(hasLocation).IsTrue();
            await Assert.That(location.Latitude).IsEqualTo(expectedLatitude).Within(0.001);
            await Assert.That(location.Longitude).IsEqualTo(expectedLongitude).Within(0.001);
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }
}
