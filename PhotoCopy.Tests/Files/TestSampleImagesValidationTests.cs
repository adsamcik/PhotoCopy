using System;
using System.IO;
using System.Linq;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Configuration;
using PhotoCopy.Files;
using PhotoCopy.Tests.TestingImplementation;
using Directory = System.IO.Directory;

namespace PhotoCopy.Tests.Files;

/// <summary>
/// Validates that all test sample images defined in TestSampleImages.cs are correctly
/// parseable and contain the expected EXIF metadata when processed by FileMetadataExtractor.
/// </summary>
[Property("Category", "Integration")]
public class TestSampleImagesValidationTests
{
    private readonly string _baseTestDirectory;

    public TestSampleImagesValidationTests()
    {
        _baseTestDirectory = Path.Combine(Path.GetTempPath(), "TestSampleImagesValidationTests");
        
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

    private static void SafeDeleteDirectory(string path)
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

    private static FileMetadataExtractor CreateExtractor()
    {
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            AllowedExtensions = [".jpg", ".jpeg", ".png"],
            LogLevel = OutputLevel.Verbose
        });
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        return new FileMetadataExtractor(logger, options);
    }

    #region Valid JPEG Images Should Be Parseable

    [Test]
    public async Task JpegWithFullExif_ShouldBeParseableByMetadataExtractor()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "full_exif.jpg");

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegWithFullExif);

            // Act
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            // Assert
            await Assert.That(directories).IsNotNull();
            await Assert.That(directories.Any()).IsTrue()
                .Because("JpegWithFullExif should contain parseable metadata directories");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task JpegWithDateOnly_ShouldBeParseableByMetadataExtractor()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "date_only.jpg");

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegWithDateOnly);

            // Act
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            // Assert
            await Assert.That(directories).IsNotNull();
            await Assert.That(directories.Any()).IsTrue()
                .Because("JpegWithDateOnly should contain parseable metadata directories");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task JpegWithGpsOnly_ShouldBeParseableByMetadataExtractor()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "gps_only.jpg");

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegWithGpsOnly);

            // Act
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            // Assert
            await Assert.That(directories).IsNotNull();
            await Assert.That(directories.Any()).IsTrue()
                .Because("JpegWithGpsOnly should contain parseable metadata directories");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task JpegWithNoExif_ShouldBeParseableByMetadataExtractor()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "no_exif.jpg");

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegWithNoExif);

            // Act
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            // Assert - Should be parseable even if no EXIF data exists
            await Assert.That(directories).IsNotNull();
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task JpegSouthernHemisphere_ShouldBeParseableByMetadataExtractor()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "southern.jpg");

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegSouthernHemisphere);

            // Act
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            // Assert
            await Assert.That(directories).IsNotNull();
            await Assert.That(directories.Any()).IsTrue();
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task JpegWesternHemisphere_ShouldBeParseableByMetadataExtractor()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "western.jpg");

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegWesternHemisphere);

            // Act
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            // Assert
            await Assert.That(directories).IsNotNull();
            await Assert.That(directories.Any()).IsTrue();
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task JpegZeroGps_ShouldBeParseableByMetadataExtractor()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "zero_gps.jpg");

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegZeroGps);

            // Act
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            // Assert
            await Assert.That(directories).IsNotNull();
            await Assert.That(directories.Any()).IsTrue();
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task JpegNorthPole_ShouldBeParseableByMetadataExtractor()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "north_pole.jpg");

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegNorthPole);

            // Act
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            // Assert
            await Assert.That(directories).IsNotNull();
            await Assert.That(directories.Any()).IsTrue();
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    #endregion

    #region Valid PNG Images Should Be Parseable

    [Test]
    public async Task PngWithExif_ShouldBeParseableByMetadataExtractor()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "exif.png");

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.PngWithExif);

            // Act
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            // Assert
            await Assert.That(directories).IsNotNull();
            await Assert.That(directories.Any()).IsTrue()
                .Because("PngWithExif should contain parseable metadata directories");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task PngWithDateOnly_ShouldBeParseableByMetadataExtractor()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "date_only.png");

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.PngWithDateOnly);

            // Act
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            // Assert
            await Assert.That(directories).IsNotNull();
            await Assert.That(directories.Any()).IsTrue()
                .Because("PngWithDateOnly should contain parseable metadata directories");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task PngWithNoExif_ShouldBeParseableByMetadataExtractor()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "no_exif.png");

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.PngWithNoExif);

            // Act
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            // Assert - Should be parseable even if no EXIF data exists
            await Assert.That(directories).IsNotNull();
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    #endregion

    #region JpegWithFullExif Should Have Correct Date and GPS Coordinates

    [Test]
    public async Task JpegWithFullExif_ShouldHaveCorrectDateTimeOriginal()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "full_exif.jpg");
        var expectedDate = TestSampleImages.JpegWithFullExifDate;

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegWithFullExif);

            // Act
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var exifSubIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

            // Assert
            await Assert.That(exifSubIfdDirectory).IsNotNull()
                .Because("JpegWithFullExif should have EXIF Sub-IFD directory");

            var hasDate = exifSubIfdDirectory!.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var extractedDate);
            
            await Assert.That(hasDate).IsTrue()
                .Because("JpegWithFullExif should have DateTimeOriginal tag");
            await Assert.That(extractedDate).IsEqualTo(expectedDate)
                .Because("DateTimeOriginal should match the expected date (2023-06-15 14:30:00)");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task JpegWithFullExif_ShouldHaveCorrectGpsCoordinates()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "full_exif.jpg");
        var expectedGps = TestSampleImages.JpegWithFullExifGps;

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegWithFullExif);

            // Act
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();

            // Assert
            await Assert.That(gpsDirectory).IsNotNull()
                .Because("JpegWithFullExif should have GPS directory");

            var hasLocation = gpsDirectory!.TryGetGeoLocation(out var location);
            
            await Assert.That(hasLocation).IsTrue()
                .Because("JpegWithFullExif should have valid GPS coordinates");
            await Assert.That(location.Latitude).IsEqualTo(expectedGps.Lat).Within(0.001)
                .Because("Latitude should match Paris coordinates (48.8566)");
            await Assert.That(location.Longitude).IsEqualTo(expectedGps.Lon).Within(0.001)
                .Because("Longitude should match Paris coordinates (2.3522)");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task JpegWithFullExif_FileMetadataExtractor_ShouldExtractDateAndCoordinates()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "full_exif.jpg");
        var expectedDate = TestSampleImages.JpegWithFullExifDate;
        var expectedGps = TestSampleImages.JpegWithFullExifGps;
        var extractor = CreateExtractor();

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegWithFullExif);
            var fileInfo = new FileInfo(filePath);

            // Act
            var dateTime = extractor.GetDateTime(fileInfo);
            var coordinates = extractor.GetCoordinates(fileInfo);

            // Assert
            await Assert.That(dateTime.Taken).IsEqualTo(expectedDate);
            await Assert.That(coordinates).IsNotNull();
            await Assert.That(coordinates!.Value.Latitude).IsEqualTo(expectedGps.Lat).Within(0.001);
            await Assert.That(coordinates!.Value.Longitude).IsEqualTo(expectedGps.Lon).Within(0.001);
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    #endregion

    #region JpegWithDateOnly Should Have Date But No GPS

    [Test]
    public async Task JpegWithDateOnly_ShouldHaveCorrectDateTimeOriginal()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "date_only.jpg");
        var expectedDate = TestSampleImages.JpegWithDateOnlyDate;

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegWithDateOnly);

            // Act
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var exifSubIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

            // Assert
            await Assert.That(exifSubIfdDirectory).IsNotNull();

            var hasDate = exifSubIfdDirectory!.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var extractedDate);
            
            await Assert.That(hasDate).IsTrue();
            await Assert.That(extractedDate).IsEqualTo(expectedDate)
                .Because("DateTimeOriginal should match (2022-12-25 10:00:00)");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task JpegWithDateOnly_ShouldNotHaveGpsCoordinates()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "date_only.jpg");
        var extractor = CreateExtractor();

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegWithDateOnly);
            var fileInfo = new FileInfo(filePath);

            // Act
            var coordinates = extractor.GetCoordinates(fileInfo);

            // Assert
            await Assert.That(coordinates).IsNull()
                .Because("JpegWithDateOnly should not have GPS coordinates");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    #endregion

    #region JpegWithGpsOnly Should Have GPS But No Date in EXIF

    [Test]
    public async Task JpegWithGpsOnly_ShouldHaveCorrectGpsCoordinates()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "gps_only.jpg");
        var expectedGps = TestSampleImages.JpegWithGpsOnlyGps;
        var extractor = CreateExtractor();

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegWithGpsOnly);
            var fileInfo = new FileInfo(filePath);

            // Act
            var coordinates = extractor.GetCoordinates(fileInfo);

            // Assert
            await Assert.That(coordinates).IsNotNull();
            await Assert.That(coordinates!.Value.Latitude).IsEqualTo(expectedGps.Lat).Within(0.001)
                .Because("Latitude should match New York coordinates (40.7128)");
            await Assert.That(coordinates!.Value.Longitude).IsEqualTo(expectedGps.Lon).Within(0.001)
                .Because("Longitude should match New York coordinates (-74.0060)");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task JpegWithGpsOnly_ShouldNotHaveDateTimeOriginal()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "gps_only.jpg");

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegWithGpsOnly);

            // Act
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var exifSubIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

            // Assert - either no EXIF directory or no DateTimeOriginal tag
            if (exifSubIfdDirectory != null)
            {
                var hasDate = exifSubIfdDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out _);
                await Assert.That(hasDate).IsFalse()
                    .Because("JpegWithGpsOnly should not have DateTimeOriginal tag");
            }
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task JpegWithGpsOnly_FileMetadataExtractor_ShouldReturnDefaultDate()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "gps_only.jpg");
        var extractor = CreateExtractor();

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegWithGpsOnly);
            var fileInfo = new FileInfo(filePath);

            // Act
            var dateTime = extractor.GetDateTime(fileInfo);

            // Assert
            await Assert.That(dateTime.Taken).IsEqualTo(default(DateTime))
                .Because("JpegWithGpsOnly should not have a date taken in EXIF");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    #endregion

    #region JpegSouthernHemisphere Should Have Correct Negative Latitude

    [Test]
    public async Task JpegSouthernHemisphere_ShouldHaveCorrectNegativeLatitude()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "southern.jpg");
        var expectedGps = TestSampleImages.JpegSouthernHemisphereGps;
        var extractor = CreateExtractor();

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegSouthernHemisphere);
            var fileInfo = new FileInfo(filePath);

            // Act
            var coordinates = extractor.GetCoordinates(fileInfo);

            // Assert
            await Assert.That(coordinates).IsNotNull();
            await Assert.That(coordinates!.Value.Latitude).IsEqualTo(expectedGps.Lat).Within(0.001)
                .Because("Latitude should be negative for Sydney (-33.8688)");
            await Assert.That(coordinates!.Value.Latitude).IsLessThan(0)
                .Because("Southern hemisphere latitude should be negative");
            await Assert.That(coordinates!.Value.Longitude).IsEqualTo(expectedGps.Lon).Within(0.001)
                .Because("Longitude should match Sydney coordinates (151.2093)");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    #endregion

    #region JpegWesternHemisphere Should Have Correct Negative Longitude

    [Test]
    public async Task JpegWesternHemisphere_ShouldHaveCorrectNegativeLongitude()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "western.jpg");
        var expectedGps = TestSampleImages.JpegWesternHemisphereGps;
        var extractor = CreateExtractor();

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegWesternHemisphere);
            var fileInfo = new FileInfo(filePath);

            // Act
            var coordinates = extractor.GetCoordinates(fileInfo);

            // Assert
            await Assert.That(coordinates).IsNotNull();
            await Assert.That(coordinates!.Value.Latitude).IsEqualTo(expectedGps.Lat).Within(0.001)
                .Because("Latitude should match Los Angeles coordinates (34.0522)");
            await Assert.That(coordinates!.Value.Longitude).IsEqualTo(expectedGps.Lon).Within(0.001)
                .Because("Longitude should be negative for Los Angeles (-118.2437)");
            await Assert.That(coordinates!.Value.Longitude).IsLessThan(0)
                .Because("Western hemisphere longitude should be negative");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    #endregion

    #region JpegZeroGps Should Have 0,0 Coordinates

    [Test]
    public async Task JpegZeroGps_ShouldHaveZeroCoordinates()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "zero_gps.jpg");
        var expectedGps = TestSampleImages.JpegZeroGpsCoordinates;

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegZeroGps);

            // Act
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();

            // Assert
            await Assert.That(gpsDirectory).IsNotNull()
                .Because("JpegZeroGps should have GPS directory");

            var hasLocation = gpsDirectory!.TryGetGeoLocation(out var location);
            
            await Assert.That(hasLocation).IsTrue();
            await Assert.That(location.Latitude).IsEqualTo(expectedGps.Lat).Within(0.001)
                .Because("Latitude should be 0 (Null Island)");
            await Assert.That(location.Longitude).IsEqualTo(expectedGps.Lon).Within(0.001)
                .Because("Longitude should be 0 (Null Island)");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task JpegZeroGps_FileMetadataExtractor_ShouldReturnNullForZeroCoordinates()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "zero_gps.jpg");
        var extractor = CreateExtractor();

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegZeroGps);
            var fileInfo = new FileInfo(filePath);

            // Act
            var coordinates = extractor.GetCoordinates(fileInfo);

            // Assert - FileMetadataExtractor treats IsZero as null (no valid location)
            await Assert.That(coordinates).IsNull()
                .Because("FileMetadataExtractor should return null for 0,0 coordinates (IsZero check)");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    #endregion

    #region Edge Case Dates Should Be Parseable

    [Test]
    public async Task JpegDateEdgeY2K_ShouldBeParseableWithCorrectDate()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "y2k.jpg");
        var expectedDate = TestSampleImages.JpegDateEdgeY2KDate;
        var extractor = CreateExtractor();

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegDateEdgeY2K);
            var fileInfo = new FileInfo(filePath);

            // Act
            var dateTime = extractor.GetDateTime(fileInfo);

            // Assert
            await Assert.That(dateTime.Taken).IsEqualTo(expectedDate)
                .Because("Y2K date (2000-01-01 00:00:00) should be correctly parsed");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task JpegDateEdgeLeapYear_ShouldBeParseableWithCorrectDate()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "leap_year.jpg");
        var expectedDate = TestSampleImages.JpegDateEdgeLeapYearDate;
        var extractor = CreateExtractor();

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegDateEdgeLeapYear);
            var fileInfo = new FileInfo(filePath);

            // Act
            var dateTime = extractor.GetDateTime(fileInfo);

            // Assert
            await Assert.That(dateTime.Taken).IsEqualTo(expectedDate)
                .Because("Leap year date (2024-02-29 12:00:00) should be correctly parsed");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task JpegVeryOldDate_ShouldBeParseableWithCorrectDate()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "old_date.jpg");
        var expectedDate = TestSampleImages.JpegVeryOldDateValue;
        var extractor = CreateExtractor();

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegVeryOldDate);
            var fileInfo = new FileInfo(filePath);

            // Act
            var dateTime = extractor.GetDateTime(fileInfo);

            // Assert
            await Assert.That(dateTime.Taken).IsEqualTo(expectedDate)
                .Because("Very old date (1990-05-15 08:30:00) should be correctly parsed");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task PngWithExif_ShouldHaveCorrectDateAndGps()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "exif.png");
        var expectedDate = TestSampleImages.PngWithExifDate;
        var expectedGps = TestSampleImages.PngWithExifGps;
        var extractor = CreateExtractor();

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.PngWithExif);
            var fileInfo = new FileInfo(filePath);

            // Act
            var dateTime = extractor.GetDateTime(fileInfo);
            var coordinates = extractor.GetCoordinates(fileInfo);

            // Assert
            await Assert.That(dateTime.Taken).IsEqualTo(expectedDate)
                .Because("PNG date (2023-08-20 16:45:00) should be correctly parsed");
            await Assert.That(coordinates).IsNotNull();
            await Assert.That(coordinates!.Value.Latitude).IsEqualTo(expectedGps.Lat).Within(0.001)
                .Because("PNG latitude should match Tokyo coordinates (35.6762)");
            await Assert.That(coordinates!.Value.Longitude).IsEqualTo(expectedGps.Lon).Within(0.001)
                .Because("PNG longitude should match Tokyo coordinates (139.6503)");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task PngWithDateOnly_ShouldHaveCorrectDate()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "date_only.png");
        var expectedDate = TestSampleImages.PngWithDateOnlyDate;
        var extractor = CreateExtractor();

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.PngWithDateOnly);
            var fileInfo = new FileInfo(filePath);

            // Act
            var dateTime = extractor.GetDateTime(fileInfo);

            // Assert
            await Assert.That(dateTime.Taken).IsEqualTo(expectedDate)
                .Because("PNG date (2023-04-10 11:20:00) should be correctly parsed");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    #endregion

    #region Corrupt/Empty/Random Images Should Throw or Return No Metadata

    [Test]
    public async Task CorruptJpeg_ShouldThrowOrReturnNoValidMetadata()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "corrupt.jpg");
        var extractor = CreateExtractor();

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.CorruptJpeg);
            var fileInfo = new FileInfo(filePath);

            // Act & Assert - Should handle gracefully (return null/default or throw)
            var coordinates = extractor.GetCoordinates(fileInfo);
            var dateTime = extractor.GetDateTime(fileInfo);

            await Assert.That(coordinates).IsNull()
                .Because("Corrupt JPEG should not return GPS coordinates");
            await Assert.That(dateTime.Taken).IsEqualTo(default(DateTime))
                .Because("Corrupt JPEG should not return a date taken");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task EmptyJpeg_ShouldHandleGracefully()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "empty.jpg");
        var extractor = CreateExtractor();

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.EmptyJpeg);
            var fileInfo = new FileInfo(filePath);

            // Act - Should handle gracefully
            var coordinates = extractor.GetCoordinates(fileInfo);
            var dateTime = extractor.GetDateTime(fileInfo);

            // Assert
            await Assert.That(coordinates).IsNull()
                .Because("Empty file should not return GPS coordinates");
            await Assert.That(dateTime.Taken).IsEqualTo(default(DateTime))
                .Because("Empty file should not return a date taken");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task RandomBinaryJpeg_ShouldHandleGracefully()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "random.jpg");
        var extractor = CreateExtractor();

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.RandomBinaryJpeg);
            var fileInfo = new FileInfo(filePath);

            // Act - Should handle gracefully
            var coordinates = extractor.GetCoordinates(fileInfo);
            var dateTime = extractor.GetDateTime(fileInfo);

            // Assert
            await Assert.That(coordinates).IsNull()
                .Because("Random binary data should not return GPS coordinates");
            await Assert.That(dateTime.Taken).IsEqualTo(default(DateTime))
                .Because("Random binary data should not return a date taken");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task CorruptJpeg_MetadataExtractor_ShouldThrowImageProcessingException()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "corrupt.jpg");

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.CorruptJpeg);

            // Act & Assert - MetadataExtractor should throw for corrupt files
            var threwException = false;
            try
            {
                ImageMetadataReader.ReadMetadata(filePath);
            }
            catch (ImageProcessingException)
            {
                threwException = true;
            }
            catch (IOException)
            {
                threwException = true;
            }

            await Assert.That(threwException).IsTrue()
                .Because("MetadataExtractor should throw an exception for corrupt JPEG data");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task EmptyJpeg_MetadataExtractor_ShouldThrowImageProcessingException()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "empty.jpg");

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.EmptyJpeg);

            // Act & Assert - MetadataExtractor should throw for empty files
            var threwException = false;
            try
            {
                ImageMetadataReader.ReadMetadata(filePath);
            }
            catch (ImageProcessingException)
            {
                threwException = true;
            }
            catch (IOException)
            {
                threwException = true;
            }

            await Assert.That(threwException).IsTrue()
                .Because("MetadataExtractor should throw an exception for empty file");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task RandomBinaryJpeg_MetadataExtractor_ShouldThrowImageProcessingException()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "random.jpg");

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.RandomBinaryJpeg);

            // Act & Assert - MetadataExtractor should throw for random binary data
            var threwException = false;
            try
            {
                ImageMetadataReader.ReadMetadata(filePath);
            }
            catch (ImageProcessingException)
            {
                threwException = true;
            }
            catch (IOException)
            {
                threwException = true;
            }

            await Assert.That(threwException).IsTrue()
                .Because("MetadataExtractor should throw an exception for random binary data");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    #endregion

    #region New GPS Edge Case Tests

    [Test]
    public async Task JpegSouthPole_ShouldHaveCorrectNegativeLatitude()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "south_pole.jpg");

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegSouthPole);

            // Act
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();

            // Assert
            await Assert.That(gpsDirectory).IsNotNull();
            var hasLocation = gpsDirectory!.TryGetGeoLocation(out var location);
            await Assert.That(hasLocation).IsTrue();
            await Assert.That(location.Latitude).IsEqualTo(TestSampleImages.JpegSouthPoleGps.Lat).Within(0.001);
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task JpegDateLinePlus_ShouldHaveCorrectPositiveLongitude()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "dateline_plus.jpg");

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegDateLinePlus);

            // Act
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();

            // Assert
            await Assert.That(gpsDirectory).IsNotNull();
            var hasLocation = gpsDirectory!.TryGetGeoLocation(out var location);
            await Assert.That(hasLocation).IsTrue();
            await Assert.That(location.Longitude).IsEqualTo(TestSampleImages.JpegDateLinePlusGps.Lon).Within(0.001);
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task JpegDateLineMinus_ShouldHaveCorrectNegativeLongitude()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "dateline_minus.jpg");

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegDateLineMinus);

            // Act
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();

            // Assert
            await Assert.That(gpsDirectory).IsNotNull();
            var hasLocation = gpsDirectory!.TryGetGeoLocation(out var location);
            await Assert.That(hasLocation).IsTrue();
            await Assert.That(location.Longitude).IsEqualTo(TestSampleImages.JpegDateLineMinusGps.Lon).Within(0.001);
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task JpegNearZeroGps_ShouldNotBeTreatedAsZero()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "near_zero.jpg");

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegNearZeroGps);

            // Act
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();

            // Assert
            await Assert.That(gpsDirectory).IsNotNull();
            var hasLocation = gpsDirectory!.TryGetGeoLocation(out var location);
            await Assert.That(hasLocation).IsTrue();
            // The location should NOT be treated as "zero" since it's near-zero but not exactly zero
            await Assert.That(location.IsZero).IsFalse()
                .Because("Near-zero coordinates (0.0001, 0.0001) should not be treated as IsZero");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    #endregion

    #region DateTimeDigitized Fallback Tests

    [Test]
    public async Task JpegWithDigitizedOnly_FileMetadataExtractor_ShouldFallbackToDigitizedDate()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "digitized_only.jpg");

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegWithDigitizedOnly);
            var extractor = CreateExtractor();
            var fileInfo = new FileInfo(filePath);

            // Act
            var result = extractor.GetDateTime(fileInfo);

            // Assert - Should fallback to DateTimeDigitized when DateTimeOriginal is not present
            await Assert.That(result.Taken).IsEqualTo(TestSampleImages.JpegWithDigitizedOnlyDate)
                .Because("FileMetadataExtractor should fallback to DateTimeDigitized when DateTimeOriginal is missing");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task JpegWithBothDates_FileMetadataExtractor_ShouldPreferOriginalOverDigitized()
    {
        // Arrange
        var testDir = CreateUniqueTestDirectory();
        var filePath = Path.Combine(testDir, "both_dates.jpg");

        try
        {
            await File.WriteAllBytesAsync(filePath, TestSampleImages.JpegWithBothDates);
            var extractor = CreateExtractor();
            var fileInfo = new FileInfo(filePath);

            // Act
            var result = extractor.GetDateTime(fileInfo);

            // Assert - DateTimeOriginal should take precedence over DateTimeDigitized
            await Assert.That(result.Taken).IsEqualTo(TestSampleImages.JpegWithBothDatesOriginal)
                .Because("FileMetadataExtractor should prefer DateTimeOriginal over DateTimeDigitized");
            await Assert.That(result.Taken).IsNotEqualTo(TestSampleImages.JpegWithBothDatesDigitized)
                .Because("DateTimeDigitized should not be used when DateTimeOriginal is present");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    #endregion
}
