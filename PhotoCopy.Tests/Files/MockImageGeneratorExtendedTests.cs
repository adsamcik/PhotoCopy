using System;
using System.IO;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using PhotoCopy.Tests.TestingImplementation;

namespace PhotoCopy.Tests.Files;

public class MockImageGeneratorExtendedTests : TestBase
{
    #region Helper Methods

    private static IReadOnlyList<MetadataExtractor.Directory> ReadMetadata(byte[] imageBytes)
    {
        using var stream = new MemoryStream(imageBytes);
        return ImageMetadataReader.ReadMetadata(stream);
    }

    private static string? GetExifTag(IReadOnlyList<MetadataExtractor.Directory> directories, int tagId)
    {
        foreach (var directory in directories)
        {
            var desc = directory.GetDescription(tagId);
            if (desc != null)
                return desc;
        }
        return null;
    }

    private static int? GetExifIntTag(IReadOnlyList<MetadataExtractor.Directory> directories, int tagId)
    {
        foreach (var directory in directories)
        {
            if (directory.TryGetInt32(tagId, out var value))
                return value;
        }
        return null;
    }

    private static double? GetGpsLatitude(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        var gpsDir = directories.OfType<GpsDirectory>().FirstOrDefault();
        if (gpsDir == null) return null;

        if (gpsDir.TryGetGeoLocation(out var location))
            return location.Latitude;
        return null;
    }

    private static double? GetGpsLongitude(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        var gpsDir = directories.OfType<GpsDirectory>().FirstOrDefault();
        if (gpsDir == null) return null;

        if (gpsDir.TryGetGeoLocation(out var location))
            return location.Longitude;
        return null;
    }

    #endregion

    #region Basic JPEG Generation Tests

    [Test]
    public async Task Jpeg_WithJustDate_GeneratesValidJpegWithDateTimeOriginal()
    {
        // Arrange
        var expectedDate = new DateTime(2024, 6, 15, 14, 30, 45);

        // Act
        var imageBytes = MockImageGeneratorExtended.Jpeg()
            .WithDate(expectedDate)
            .Build();

        // Assert
        await Assert.That(imageBytes).IsNotNull();
        await Assert.That(imageBytes.Length).IsGreaterThan(0);
        
        // Verify JPEG header
        await Assert.That(imageBytes[0]).IsEqualTo((byte)0xFF);
        await Assert.That(imageBytes[1]).IsEqualTo((byte)0xD8);

        // Verify EXIF data using MetadataExtractor
        var directories = ReadMetadata(imageBytes);
        var dateTimeOriginal = GetExifTag(directories, ExifDirectoryBase.TagDateTimeOriginal);
        
        await Assert.That(dateTimeOriginal).IsNotNull();
        await Assert.That(dateTimeOriginal).IsEqualTo("2024:06:15 14:30:45");
    }

    [Test]
    public async Task Jpeg_WithNoMetadata_GeneratesValidJpeg()
    {
        // Act
        var imageBytes = MockImageGeneratorExtended.Jpeg().Build();

        // Assert
        await Assert.That(imageBytes).IsNotNull();
        await Assert.That(imageBytes.Length).IsGreaterThan(0);
        
        // Verify JPEG header (SOI marker)
        await Assert.That(imageBytes[0]).IsEqualTo((byte)0xFF);
        await Assert.That(imageBytes[1]).IsEqualTo((byte)0xD8);
        
        // Verify JPEG footer (EOI marker)
        await Assert.That(imageBytes[^2]).IsEqualTo((byte)0xFF);
        await Assert.That(imageBytes[^1]).IsEqualTo((byte)0xD9);
    }

    #endregion

    #region Basic PNG Generation Tests

    [Test]
    public async Task Png_WithJustDate_GeneratesValidPngWithDateTimeOriginal()
    {
        // Arrange
        var expectedDate = new DateTime(2023, 12, 25, 10, 0, 0);

        // Act
        var imageBytes = MockImageGeneratorExtended.Png()
            .WithDate(expectedDate)
            .Build();

        // Assert
        await Assert.That(imageBytes).IsNotNull();
        await Assert.That(imageBytes.Length).IsGreaterThan(0);
        
        // Verify PNG signature
        await Assert.That(imageBytes[0]).IsEqualTo((byte)0x89);
        await Assert.That(imageBytes[1]).IsEqualTo((byte)0x50); // 'P'
        await Assert.That(imageBytes[2]).IsEqualTo((byte)0x4E); // 'N'
        await Assert.That(imageBytes[3]).IsEqualTo((byte)0x47); // 'G'

        // Verify EXIF data using MetadataExtractor
        var directories = ReadMetadata(imageBytes);
        var dateTimeOriginal = GetExifTag(directories, ExifDirectoryBase.TagDateTimeOriginal);
        
        await Assert.That(dateTimeOriginal).IsNotNull();
        await Assert.That(dateTimeOriginal).IsEqualTo("2023:12:25 10:00:00");
    }

    [Test]
    public async Task Png_WithNoMetadata_GeneratesValidPng()
    {
        // Act
        var imageBytes = MockImageGeneratorExtended.Png().Build();

        // Assert
        await Assert.That(imageBytes).IsNotNull();
        await Assert.That(imageBytes.Length).IsGreaterThan(0);
        
        // Verify PNG signature
        await Assert.That(imageBytes[0]).IsEqualTo((byte)0x89);
        await Assert.That(imageBytes[1]).IsEqualTo((byte)0x50); // 'P'
        await Assert.That(imageBytes[2]).IsEqualTo((byte)0x4E); // 'N'
        await Assert.That(imageBytes[3]).IsEqualTo((byte)0x47); // 'G'
    }

    #endregion

    #region Camera Make and Model Tests

    [Test]
    public async Task Jpeg_WithCameraMake_IncludesMakeInExif()
    {
        // Arrange
        const string expectedMake = "Canon";

        // Act
        var imageBytes = MockImageGeneratorExtended.Jpeg()
            .WithCameraMake(expectedMake)
            .Build();

        // Assert
        var directories = ReadMetadata(imageBytes);
        var make = GetExifTag(directories, ExifDirectoryBase.TagMake);
        
        await Assert.That(make).IsNotNull();
        await Assert.That(make).IsEqualTo(expectedMake);
    }

    [Test]
    public async Task Jpeg_WithCameraModel_IncludesModelInExif()
    {
        // Arrange
        const string expectedModel = "EOS R5";

        // Act
        var imageBytes = MockImageGeneratorExtended.Jpeg()
            .WithCameraModel(expectedModel)
            .Build();

        // Assert
        var directories = ReadMetadata(imageBytes);
        var model = GetExifTag(directories, ExifDirectoryBase.TagModel);
        
        await Assert.That(model).IsNotNull();
        await Assert.That(model).IsEqualTo(expectedModel);
    }

    [Test]
    public async Task Jpeg_WithCamera_IncludesBothMakeAndModel()
    {
        // Arrange
        const string expectedMake = "Nikon";
        const string expectedModel = "D850";

        // Act
        var imageBytes = MockImageGeneratorExtended.Jpeg()
            .WithCamera(expectedMake, expectedModel)
            .Build();

        // Assert
        var directories = ReadMetadata(imageBytes);
        var make = GetExifTag(directories, ExifDirectoryBase.TagMake);
        var model = GetExifTag(directories, ExifDirectoryBase.TagModel);
        
        await Assert.That(make).IsEqualTo(expectedMake);
        await Assert.That(model).IsEqualTo(expectedModel);
    }

    [Test]
    public async Task Png_WithCamera_IncludesBothMakeAndModel()
    {
        // Arrange
        const string expectedMake = "Sony";
        const string expectedModel = "A7R IV";

        // Act
        var imageBytes = MockImageGeneratorExtended.Png()
            .WithCamera(expectedMake, expectedModel)
            .Build();

        // Assert
        var directories = ReadMetadata(imageBytes);
        var make = GetExifTag(directories, ExifDirectoryBase.TagMake);
        var model = GetExifTag(directories, ExifDirectoryBase.TagModel);
        
        await Assert.That(make).IsEqualTo(expectedMake);
        await Assert.That(model).IsEqualTo(expectedModel);
    }

    #endregion

    #region Orientation Tests

    [Test]
    [Arguments((ushort)1)]
    [Arguments((ushort)2)]
    [Arguments((ushort)3)]
    [Arguments((ushort)4)]
    [Arguments((ushort)5)]
    [Arguments((ushort)6)]
    [Arguments((ushort)7)]
    [Arguments((ushort)8)]
    public async Task Jpeg_WithValidOrientation_IncludesOrientationInExif(ushort orientation)
    {
        // Act
        var imageBytes = MockImageGeneratorExtended.Jpeg()
            .WithOrientation(orientation)
            .Build();

        // Assert
        var directories = ReadMetadata(imageBytes);
        var actualOrientation = GetExifIntTag(directories, ExifDirectoryBase.TagOrientation);
        
        await Assert.That(actualOrientation).IsNotNull();
        await Assert.That(actualOrientation!.Value).IsEqualTo(orientation);
    }

    [Test]
    [Arguments((ushort)1)]
    [Arguments((ushort)2)]
    [Arguments((ushort)3)]
    [Arguments((ushort)4)]
    [Arguments((ushort)5)]
    [Arguments((ushort)6)]
    [Arguments((ushort)7)]
    [Arguments((ushort)8)]
    public async Task Png_WithValidOrientation_IncludesOrientationInExif(ushort orientation)
    {
        // Act
        var imageBytes = MockImageGeneratorExtended.Png()
            .WithOrientation(orientation)
            .Build();

        // Assert
        var directories = ReadMetadata(imageBytes);
        var actualOrientation = GetExifIntTag(directories, ExifDirectoryBase.TagOrientation);
        
        await Assert.That(actualOrientation).IsNotNull();
        await Assert.That(actualOrientation!.Value).IsEqualTo(orientation);
    }

    #endregion

    #region GPS Coordinates Tests

    [Test]
    public async Task Jpeg_WithGpsNorthEast_IncludesGpsInExif()
    {
        // Arrange - Paris, France (Northern/Eastern hemisphere)
        const double expectedLat = 48.8566;
        const double expectedLon = 2.3522;

        // Act
        var imageBytes = MockImageGeneratorExtended.Jpeg()
            .WithGps(expectedLat, expectedLon)
            .Build();

        // Assert
        var directories = ReadMetadata(imageBytes);
        var actualLat = GetGpsLatitude(directories);
        var actualLon = GetGpsLongitude(directories);
        
        await Assert.That(actualLat).IsNotNull();
        await Assert.That(actualLon).IsNotNull();
        await Assert.That(actualLat!.Value).IsEqualTo(expectedLat).Within(0.001);
        await Assert.That(actualLon!.Value).IsEqualTo(expectedLon).Within(0.001);
    }

    [Test]
    public async Task Jpeg_WithGpsSouthWest_IncludesGpsInExif()
    {
        // Arrange - São Paulo, Brazil (Southern/Western hemisphere)
        const double expectedLat = -23.5505;
        const double expectedLon = -46.6333;

        // Act
        var imageBytes = MockImageGeneratorExtended.Jpeg()
            .WithGps(expectedLat, expectedLon)
            .Build();

        // Assert
        var directories = ReadMetadata(imageBytes);
        var actualLat = GetGpsLatitude(directories);
        var actualLon = GetGpsLongitude(directories);
        
        await Assert.That(actualLat).IsNotNull();
        await Assert.That(actualLon).IsNotNull();
        await Assert.That(actualLat!.Value).IsEqualTo(expectedLat).Within(0.001);
        await Assert.That(actualLon!.Value).IsEqualTo(expectedLon).Within(0.001);
    }

    [Test]
    public async Task Jpeg_WithGpsSouthEast_IncludesGpsInExif()
    {
        // Arrange - Sydney, Australia (Southern/Eastern hemisphere)
        const double expectedLat = -33.8688;
        const double expectedLon = 151.2093;

        // Act
        var imageBytes = MockImageGeneratorExtended.Jpeg()
            .WithGps(expectedLat, expectedLon)
            .Build();

        // Assert
        var directories = ReadMetadata(imageBytes);
        var actualLat = GetGpsLatitude(directories);
        var actualLon = GetGpsLongitude(directories);
        
        await Assert.That(actualLat).IsNotNull();
        await Assert.That(actualLon).IsNotNull();
        await Assert.That(actualLat!.Value).IsEqualTo(expectedLat).Within(0.001);
        await Assert.That(actualLon!.Value).IsEqualTo(expectedLon).Within(0.001);
    }

    [Test]
    public async Task Jpeg_WithGpsNorthWest_IncludesGpsInExif()
    {
        // Arrange - New York, USA (Northern/Western hemisphere)
        const double expectedLat = 40.7128;
        const double expectedLon = -74.0060;

        // Act
        var imageBytes = MockImageGeneratorExtended.Jpeg()
            .WithGps(expectedLat, expectedLon)
            .Build();

        // Assert
        var directories = ReadMetadata(imageBytes);
        var actualLat = GetGpsLatitude(directories);
        var actualLon = GetGpsLongitude(directories);
        
        await Assert.That(actualLat).IsNotNull();
        await Assert.That(actualLon).IsNotNull();
        await Assert.That(actualLat!.Value).IsEqualTo(expectedLat).Within(0.001);
        await Assert.That(actualLon!.Value).IsEqualTo(expectedLon).Within(0.001);
    }

    [Test]
    public async Task Png_WithGps_IncludesGpsInExif()
    {
        // Arrange - Tokyo, Japan
        const double expectedLat = 35.6762;
        const double expectedLon = 139.6503;

        // Act
        var imageBytes = MockImageGeneratorExtended.Png()
            .WithGps(expectedLat, expectedLon)
            .Build();

        // Assert
        var directories = ReadMetadata(imageBytes);
        var actualLat = GetGpsLatitude(directories);
        var actualLon = GetGpsLongitude(directories);
        
        await Assert.That(actualLat).IsNotNull();
        await Assert.That(actualLon).IsNotNull();
        await Assert.That(actualLat!.Value).IsEqualTo(expectedLat).Within(0.001);
        await Assert.That(actualLon!.Value).IsEqualTo(expectedLon).Within(0.001);
    }

    #endregion

    #region Image Dimensions Tests

    [Test]
    public async Task Jpeg_WithDimensions_IncludesDimensionsInExif()
    {
        // Arrange
        const int expectedWidth = 4000;
        const int expectedHeight = 3000;

        // Act
        var imageBytes = MockImageGeneratorExtended.Jpeg()
            .WithDimensions(expectedWidth, expectedHeight)
            .Build();

        // Assert
        var directories = ReadMetadata(imageBytes);
        var width = GetExifIntTag(directories, ExifDirectoryBase.TagImageWidth);
        var height = GetExifIntTag(directories, ExifDirectoryBase.TagImageHeight);
        
        await Assert.That(width).IsNotNull();
        await Assert.That(height).IsNotNull();
        await Assert.That(width!.Value).IsEqualTo(expectedWidth);
        await Assert.That(height!.Value).IsEqualTo(expectedHeight);
    }

    [Test]
    public async Task Png_WithDimensions_IncludesDimensionsInExif()
    {
        // Arrange
        const int expectedWidth = 1920;
        const int expectedHeight = 1080;

        // Act
        var imageBytes = MockImageGeneratorExtended.Png()
            .WithDimensions(expectedWidth, expectedHeight)
            .Build();

        // Assert
        var directories = ReadMetadata(imageBytes);
        var width = GetExifIntTag(directories, ExifDirectoryBase.TagImageWidth);
        var height = GetExifIntTag(directories, ExifDirectoryBase.TagImageHeight);
        
        await Assert.That(width).IsNotNull();
        await Assert.That(height).IsNotNull();
        await Assert.That(width!.Value).IsEqualTo(expectedWidth);
        await Assert.That(height!.Value).IsEqualTo(expectedHeight);
    }

    #endregion

    #region DateTimeDigitized Tests

    [Test]
    public async Task Jpeg_WithDateDigitized_IncludesDateTimeDigitizedInExif()
    {
        // Arrange
        var expectedDate = new DateTime(2024, 3, 20, 16, 45, 30);

        // Act
        var imageBytes = MockImageGeneratorExtended.Jpeg()
            .WithDateDigitized(expectedDate)
            .Build();

        // Assert
        var directories = ReadMetadata(imageBytes);
        var dateTimeDigitized = GetExifTag(directories, ExifDirectoryBase.TagDateTimeDigitized);
        
        await Assert.That(dateTimeDigitized).IsNotNull();
        await Assert.That(dateTimeDigitized).IsEqualTo("2024:03:20 16:45:30");
    }

    [Test]
    public async Task Jpeg_WithDates_IncludesBothDateFieldsInExif()
    {
        // Arrange
        var expectedDate = new DateTime(2024, 7, 4, 12, 0, 0);

        // Act
        var imageBytes = MockImageGeneratorExtended.Jpeg()
            .WithDates(expectedDate)
            .Build();

        // Assert
        var directories = ReadMetadata(imageBytes);
        var dateTimeOriginal = GetExifTag(directories, ExifDirectoryBase.TagDateTimeOriginal);
        var dateTimeDigitized = GetExifTag(directories, ExifDirectoryBase.TagDateTimeDigitized);
        
        await Assert.That(dateTimeOriginal).IsNotNull();
        await Assert.That(dateTimeDigitized).IsNotNull();
        await Assert.That(dateTimeOriginal).IsEqualTo("2024:07:04 12:00:00");
        await Assert.That(dateTimeDigitized).IsEqualTo("2024:07:04 12:00:00");
    }

    [Test]
    public async Task Jpeg_WithDifferentDateAndDateDigitized_IncludesBothDifferentValues()
    {
        // Arrange
        var originalDate = new DateTime(2024, 1, 15, 10, 30, 0);
        var digitizedDate = new DateTime(2024, 1, 16, 14, 0, 0);

        // Act
        var imageBytes = MockImageGeneratorExtended.Jpeg()
            .WithDate(originalDate)
            .WithDateDigitized(digitizedDate)
            .Build();

        // Assert
        var directories = ReadMetadata(imageBytes);
        var dateTimeOriginal = GetExifTag(directories, ExifDirectoryBase.TagDateTimeOriginal);
        var dateTimeDigitized = GetExifTag(directories, ExifDirectoryBase.TagDateTimeDigitized);
        
        await Assert.That(dateTimeOriginal).IsNotNull();
        await Assert.That(dateTimeDigitized).IsNotNull();
        await Assert.That(dateTimeOriginal).IsEqualTo("2024:01:15 10:30:00");
        await Assert.That(dateTimeDigitized).IsEqualTo("2024:01:16 14:00:00");
    }

    [Test]
    public async Task Png_WithDateDigitized_IncludesDateTimeDigitizedInExif()
    {
        // Arrange
        var expectedDate = new DateTime(2023, 11, 11, 11, 11, 11);

        // Act
        var imageBytes = MockImageGeneratorExtended.Png()
            .WithDateDigitized(expectedDate)
            .Build();

        // Assert
        var directories = ReadMetadata(imageBytes);
        var dateTimeDigitized = GetExifTag(directories, ExifDirectoryBase.TagDateTimeDigitized);
        
        await Assert.That(dateTimeDigitized).IsNotNull();
        await Assert.That(dateTimeDigitized).IsEqualTo("2023:11:11 11:11:11");
    }

    #endregion

    #region All Fields Combined Tests

    [Test]
    public async Task Jpeg_WithAllFields_GeneratesCompleteExifData()
    {
        // Arrange
        var expectedDateOriginal = new DateTime(2024, 8, 15, 9, 30, 0);
        var expectedDateDigitized = new DateTime(2024, 8, 15, 9, 30, 1);
        const string expectedMake = "Apple";
        const string expectedModel = "iPhone 15 Pro";
        const ushort expectedOrientation = 6;
        const int expectedWidth = 4032;
        const int expectedHeight = 3024;
        const double expectedLat = 37.7749;
        const double expectedLon = -122.4194;

        // Act
        var imageBytes = MockImageGeneratorExtended.Jpeg()
            .WithDate(expectedDateOriginal)
            .WithDateDigitized(expectedDateDigitized)
            .WithCamera(expectedMake, expectedModel)
            .WithOrientation(expectedOrientation)
            .WithDimensions(expectedWidth, expectedHeight)
            .WithGps(expectedLat, expectedLon)
            .Build();

        // Assert
        var directories = ReadMetadata(imageBytes);
        
        // Verify dates
        var dateTimeOriginal = GetExifTag(directories, ExifDirectoryBase.TagDateTimeOriginal);
        var dateTimeDigitized = GetExifTag(directories, ExifDirectoryBase.TagDateTimeDigitized);
        await Assert.That(dateTimeOriginal).IsEqualTo("2024:08:15 09:30:00");
        await Assert.That(dateTimeDigitized).IsEqualTo("2024:08:15 09:30:01");
        
        // Verify camera info
        var make = GetExifTag(directories, ExifDirectoryBase.TagMake);
        var model = GetExifTag(directories, ExifDirectoryBase.TagModel);
        await Assert.That(make).IsEqualTo(expectedMake);
        await Assert.That(model).IsEqualTo(expectedModel);
        
        // Verify orientation
        var orientation = GetExifIntTag(directories, ExifDirectoryBase.TagOrientation);
        await Assert.That(orientation!.Value).IsEqualTo(expectedOrientation);
        
        // Verify dimensions
        var width = GetExifIntTag(directories, ExifDirectoryBase.TagImageWidth);
        var height = GetExifIntTag(directories, ExifDirectoryBase.TagImageHeight);
        await Assert.That(width!.Value).IsEqualTo(expectedWidth);
        await Assert.That(height!.Value).IsEqualTo(expectedHeight);
        
        // Verify GPS
        var actualLat = GetGpsLatitude(directories);
        var actualLon = GetGpsLongitude(directories);
        await Assert.That(actualLat!.Value).IsEqualTo(expectedLat).Within(0.001);
        await Assert.That(actualLon!.Value).IsEqualTo(expectedLon).Within(0.001);
    }

    [Test]
    public async Task Png_WithAllFields_GeneratesCompleteExifData()
    {
        // Arrange
        var expectedDate = new DateTime(2024, 5, 1, 12, 0, 0);
        const string expectedMake = "Google";
        const string expectedModel = "Pixel 8 Pro";
        const ushort expectedOrientation = 1;
        const int expectedWidth = 4080;
        const int expectedHeight = 3072;
        const double expectedLat = 51.5074;
        const double expectedLon = -0.1278;

        // Act
        var imageBytes = MockImageGeneratorExtended.Png()
            .WithDates(expectedDate)
            .WithCamera(expectedMake, expectedModel)
            .WithOrientation(expectedOrientation)
            .WithDimensions(expectedWidth, expectedHeight)
            .WithGps(expectedLat, expectedLon)
            .Build();

        // Assert
        var directories = ReadMetadata(imageBytes);
        
        // Verify dates
        var dateTimeOriginal = GetExifTag(directories, ExifDirectoryBase.TagDateTimeOriginal);
        var dateTimeDigitized = GetExifTag(directories, ExifDirectoryBase.TagDateTimeDigitized);
        await Assert.That(dateTimeOriginal).IsEqualTo("2024:05:01 12:00:00");
        await Assert.That(dateTimeDigitized).IsEqualTo("2024:05:01 12:00:00");
        
        // Verify camera info
        var make = GetExifTag(directories, ExifDirectoryBase.TagMake);
        var model = GetExifTag(directories, ExifDirectoryBase.TagModel);
        await Assert.That(make).IsEqualTo(expectedMake);
        await Assert.That(model).IsEqualTo(expectedModel);
        
        // Verify orientation
        var orientation = GetExifIntTag(directories, ExifDirectoryBase.TagOrientation);
        await Assert.That(orientation!.Value).IsEqualTo(expectedOrientation);
        
        // Verify dimensions
        var width = GetExifIntTag(directories, ExifDirectoryBase.TagImageWidth);
        var height = GetExifIntTag(directories, ExifDirectoryBase.TagImageHeight);
        await Assert.That(width!.Value).IsEqualTo(expectedWidth);
        await Assert.That(height!.Value).IsEqualTo(expectedHeight);
        
        // Verify GPS
        var actualLat = GetGpsLatitude(directories);
        var actualLon = GetGpsLongitude(directories);
        await Assert.That(actualLat!.Value).IsEqualTo(expectedLat).Within(0.001);
        await Assert.That(actualLon!.Value).IsEqualTo(expectedLon).Within(0.001);
    }

    #endregion

    #region Invalid Orientation Tests

    [Test]
    public async Task Jpeg_WithOrientationZero_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(MockImageGeneratorExtended.Jpeg().WithOrientation(0)));
    }

    [Test]
    public async Task Jpeg_WithOrientationNine_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(MockImageGeneratorExtended.Jpeg().WithOrientation(9)));
    }

    [Test]
    public async Task Png_WithOrientationZero_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(MockImageGeneratorExtended.Png().WithOrientation(0)));
    }

    [Test]
    public async Task Png_WithOrientationNine_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(MockImageGeneratorExtended.Png().WithOrientation(9)));
    }

    #endregion

    #region Invalid GPS Coordinates Tests

    [Test]
    public async Task Jpeg_WithLatitudeTooLow_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(MockImageGeneratorExtended.Jpeg().WithGps(-91, 0)));
    }

    [Test]
    public async Task Jpeg_WithLatitudeTooHigh_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(MockImageGeneratorExtended.Jpeg().WithGps(91, 0)));
    }

    [Test]
    public async Task Jpeg_WithLongitudeTooLow_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(MockImageGeneratorExtended.Jpeg().WithGps(0, -181)));
    }

    [Test]
    public async Task Jpeg_WithLongitudeTooHigh_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(MockImageGeneratorExtended.Jpeg().WithGps(0, 181)));
    }

    [Test]
    public async Task Png_WithInvalidLatitude_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(MockImageGeneratorExtended.Png().WithGps(-100, 50)));
    }

    [Test]
    public async Task Png_WithInvalidLongitude_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(MockImageGeneratorExtended.Png().WithGps(50, 200)));
    }

    #endregion

    #region Invalid Dimensions Tests

    [Test]
    public async Task Jpeg_WithZeroWidth_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(MockImageGeneratorExtended.Jpeg().WithDimensions(0, 100)));
    }

    [Test]
    public async Task Jpeg_WithNegativeWidth_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(MockImageGeneratorExtended.Jpeg().WithDimensions(-1, 100)));
    }

    [Test]
    public async Task Jpeg_WithZeroHeight_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(MockImageGeneratorExtended.Jpeg().WithDimensions(100, 0)));
    }

    [Test]
    public async Task Jpeg_WithNegativeHeight_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(MockImageGeneratorExtended.Jpeg().WithDimensions(100, -1)));
    }

    [Test]
    public async Task Png_WithZeroWidth_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(MockImageGeneratorExtended.Png().WithDimensions(0, 100)));
    }

    [Test]
    public async Task Png_WithZeroHeight_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(MockImageGeneratorExtended.Png().WithDimensions(100, 0)));
    }

    #endregion

    #region Null Argument Tests

    [Test]
    public async Task WithCameraMake_WithNull_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => Task.FromResult(MockImageGeneratorExtended.Jpeg().WithCameraMake(null!)));
    }

    [Test]
    public async Task WithCameraModel_WithNull_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => Task.FromResult(MockImageGeneratorExtended.Jpeg().WithCameraModel(null!)));
    }

    #endregion

    #region Builder Chaining Tests

    [Test]
    public async Task Builder_MethodChaining_ReturnsSameInstance()
    {
        // Arrange
        var builder = MockImageGeneratorExtended.Jpeg();

        // Act
        var result1 = builder.WithDate(DateTime.Now);
        var result2 = result1.WithCameraMake("Test");
        var result3 = result2.WithCameraModel("Model");
        var result4 = result3.WithOrientation(1);
        var result5 = result4.WithGps(0, 0);
        var result6 = result5.WithDimensions(100, 100);

        // Assert - all should be the same instance
        await Assert.That(ReferenceEquals(builder, result1)).IsTrue();
        await Assert.That(ReferenceEquals(builder, result2)).IsTrue();
        await Assert.That(ReferenceEquals(builder, result3)).IsTrue();
        await Assert.That(ReferenceEquals(builder, result4)).IsTrue();
        await Assert.That(ReferenceEquals(builder, result5)).IsTrue();
        await Assert.That(ReferenceEquals(builder, result6)).IsTrue();
    }

    #endregion
}
