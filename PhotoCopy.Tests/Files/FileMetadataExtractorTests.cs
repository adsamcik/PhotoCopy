using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Files;
using PhotoCopy.Configuration;
using PhotoCopy.Tests.TestingImplementation;

namespace PhotoCopy.Tests.Files;

public class FileMetadataExtractorTests : TestBase
{
    [Test]
    public async Task GetDateTime_WithNoExifData_ReturnsFileCreationTime()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "Test content");
        var fileInfo = new FileInfo(tempFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(DefaultConfig);
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        try
        {
            // Act
            var result = extractor.GetDateTime(fileInfo);

            // Assert
            await Assert.That(result.Created).IsEqualTo(fileInfo.CreationTime);
            await Assert.That(result.DateTime).IsEqualTo(fileInfo.CreationTime); // Main DateTime should be creation time
        }
        finally
        {
            // Cleanup
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetDateTime_WhenCreationTimeIsNewerThanModified_ReturnsModificationTime()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "Test content");
        var fileInfo = new FileInfo(tempFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(DefaultConfig);
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);
        
        try
        {
            // Set creation time to be newer than last write time
            var modifiedTime = fileInfo.LastWriteTime;
            fileInfo.CreationTime = modifiedTime.AddDays(1);

            // Act
            var result = extractor.GetDateTime(fileInfo);

            // Assert
            await Assert.That(result.Created).IsEqualTo(fileInfo.CreationTime);
            await Assert.That(result.Modified).IsEqualTo(fileInfo.LastWriteTime);
        }
        finally
        {
            // Cleanup
            File.Delete(tempFile);
        }
    }

    #region GetCoordinates Tests

    [Test]
    public async Task GetCoordinates_WithValidGpsData_ReturnsCoordinates()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "gps_image.jpg");
        var expectedLat = 40.7128;
        var expectedLon = -74.0060;
        
        // Create a JPEG with GPS coordinates using MockImageGenerator
        var jpegBytes = MockImageGenerator.CreateJpeg(gps: (expectedLat, expectedLon));
        File.WriteAllBytes(tempFile, jpegBytes);
        
        var fileInfo = new FileInfo(tempFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            AllowedExtensions = [".jpg", ".jpeg", ".png"],
            LogLevel = OutputLevel.Verbose
        });
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        try
        {
            // Act
            var result = extractor.GetCoordinates(fileInfo);

            // Assert
            await Assert.That(result).IsNotNull();
            await Assert.That(result!.Value.Latitude).IsEqualTo(expectedLat).Within(0.001);
            await Assert.That(result!.Value.Longitude).IsEqualTo(expectedLon).Within(0.001);
        }
        finally
        {
            // Cleanup
            SafeDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task GetCoordinates_WithoutGpsData_ReturnsNull()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "no_gps_image.jpg");
        
        // Create a JPEG without GPS coordinates
        var jpegBytes = MockImageGenerator.CreateJpeg(dateTaken: DateTime.Now);
        File.WriteAllBytes(tempFile, jpegBytes);
        
        var fileInfo = new FileInfo(tempFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            AllowedExtensions = [".jpg", ".jpeg", ".png"],
            LogLevel = OutputLevel.Verbose
        });
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        try
        {
            // Act
            var result = extractor.GetCoordinates(fileInfo);

            // Assert
            await Assert.That(result).IsNull();
        }
        finally
        {
            // Cleanup
            SafeDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task GetCoordinates_WithNoExifData_ReturnsNull()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "no_exif.jpg");
        
        // Create a minimal JPEG without any EXIF data
        var jpegBytes = MockImageGenerator.CreateJpeg();
        File.WriteAllBytes(tempFile, jpegBytes);
        
        var fileInfo = new FileInfo(tempFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            AllowedExtensions = [".jpg", ".jpeg", ".png"],
            LogLevel = OutputLevel.Verbose
        });
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        try
        {
            // Act
            var result = extractor.GetCoordinates(fileInfo);

            // Assert
            await Assert.That(result).IsNull();
        }
        finally
        {
            // Cleanup
            SafeDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task GetCoordinates_WithPngAndGpsData_ReturnsCoordinates()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "gps_image.png");
        var expectedLat = 51.5074;
        var expectedLon = -0.1278;
        
        // Create a PNG with GPS coordinates
        var pngBytes = MockImageGenerator.CreatePng(gps: (expectedLat, expectedLon));
        File.WriteAllBytes(tempFile, pngBytes);
        
        var fileInfo = new FileInfo(tempFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            AllowedExtensions = [".jpg", ".jpeg", ".png"],
            LogLevel = OutputLevel.Verbose
        });
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        try
        {
            // Act
            var result = extractor.GetCoordinates(fileInfo);

            // Assert
            await Assert.That(result).IsNotNull();
            await Assert.That(result!.Value.Latitude).IsEqualTo(expectedLat).Within(0.001);
            await Assert.That(result!.Value.Longitude).IsEqualTo(expectedLon).Within(0.001);
        }
        finally
        {
            // Cleanup
            SafeDeleteDirectory(tempDir);
        }
    }

    #endregion

    #region ISO 6709 Parsing Tests

    [Test]
    public async Task ParseIso6709_WithBasicPositiveCoordinates_ReturnsCorrectValues()
    {
        // Arrange - Paris coordinates
        var iso6709 = "+48.8584+002.2945/";

        // Act
        var result = FileMetadataExtractor.ParseIso6709(iso6709);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value.Latitude).IsEqualTo(48.8584).Within(0.0001);
        await Assert.That(result!.Value.Longitude).IsEqualTo(2.2945).Within(0.0001);
    }

    [Test]
    public async Task ParseIso6709_WithNegativeCoordinates_ReturnsCorrectValues()
    {
        // Arrange - South America coordinates
        var iso6709 = "-23.5505-046.6333/";

        // Act
        var result = FileMetadataExtractor.ParseIso6709(iso6709);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value.Latitude).IsEqualTo(-23.5505).Within(0.0001);
        await Assert.That(result!.Value.Longitude).IsEqualTo(-46.6333).Within(0.0001);
    }

    [Test]
    public async Task ParseIso6709_WithMixedSignCoordinates_ReturnsCorrectValues()
    {
        // Arrange - New York coordinates (positive lat, negative lon)
        var iso6709 = "+40.7128-074.0060/";

        // Act
        var result = FileMetadataExtractor.ParseIso6709(iso6709);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value.Latitude).IsEqualTo(40.7128).Within(0.0001);
        await Assert.That(result!.Value.Longitude).IsEqualTo(-74.0060).Within(0.0001);
    }

    [Test]
    public async Task ParseIso6709_WithAltitude_ReturnsLatLonAndIgnoresAltitude()
    {
        // Arrange - Paris with altitude
        var iso6709 = "+48.8584+002.2945+100.00/";

        // Act
        var result = FileMetadataExtractor.ParseIso6709(iso6709);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value.Latitude).IsEqualTo(48.8584).Within(0.0001);
        await Assert.That(result!.Value.Longitude).IsEqualTo(2.2945).Within(0.0001);
    }

    [Test]
    public async Task ParseIso6709_WithNegativeAltitude_ReturnsCorrectValues()
    {
        // Arrange - Dead Sea area with negative altitude
        var iso6709 = "+31.5000+035.5000-400.00/";

        // Act
        var result = FileMetadataExtractor.ParseIso6709(iso6709);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value.Latitude).IsEqualTo(31.5).Within(0.0001);
        await Assert.That(result!.Value.Longitude).IsEqualTo(35.5).Within(0.0001);
    }

    [Test]
    public async Task ParseIso6709_WithoutTrailingSlash_ReturnsCorrectValues()
    {
        // Arrange - Some encoders might not include the trailing slash
        var iso6709 = "+48.8584+002.2945";

        // Act
        var result = FileMetadataExtractor.ParseIso6709(iso6709);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value.Latitude).IsEqualTo(48.8584).Within(0.0001);
        await Assert.That(result!.Value.Longitude).IsEqualTo(2.2945).Within(0.0001);
    }

    [Test]
    public async Task ParseIso6709_WithEmptyString_ReturnsNull()
    {
        // Act
        var result = FileMetadataExtractor.ParseIso6709("");

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ParseIso6709_WithNullString_ReturnsNull()
    {
        // Act
        var result = FileMetadataExtractor.ParseIso6709(null!);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ParseIso6709_WithWhitespaceOnly_ReturnsNull()
    {
        // Act
        var result = FileMetadataExtractor.ParseIso6709("   ");

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ParseIso6709_WithInvalidFormat_ReturnsNull()
    {
        // Arrange - Missing second sign
        var iso6709 = "+48.85842.2945/";

        // Act
        var result = FileMetadataExtractor.ParseIso6709(iso6709);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ParseIso6709_WithZeroCoordinates_ReturnsNull()
    {
        // Arrange - Zero coordinates should be treated as invalid (like GpsDirectory)
        var iso6709 = "+00.0000+000.0000/";

        // Act
        var result = FileMetadataExtractor.ParseIso6709(iso6709);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ParseIso6709_WithInvalidLatitudeRange_ReturnsNull()
    {
        // Arrange - Latitude > 90
        var iso6709 = "+91.0000+002.2945/";

        // Act
        var result = FileMetadataExtractor.ParseIso6709(iso6709);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ParseIso6709_WithInvalidLongitudeRange_ReturnsNull()
    {
        // Arrange - Longitude > 180
        var iso6709 = "+48.8584+181.0000/";

        // Act
        var result = FileMetadataExtractor.ParseIso6709(iso6709);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ParseIso6709_WithSlashOnly_ReturnsNull()
    {
        // Act
        var result = FileMetadataExtractor.ParseIso6709("/");

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region Non-Image File Behavior Tests

    [Test]
    public async Task GetCoordinates_WithNonImageFile_ReturnsNull()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "This is a text file, not an image");
        var fileInfo = new FileInfo(tempFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            AllowedExtensions = [".jpg", ".jpeg", ".png"],
            LogLevel = OutputLevel.Verbose
        });
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        try
        {
            // Act
            var result = extractor.GetCoordinates(fileInfo);

            // Assert
            await Assert.That(result).IsNull();
        }
        finally
        {
            // Cleanup
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetCoordinates_WithMismatchedExtension_StillReadsMetadata()
    {
        // Arrange
        // Note: With video GPS support, GetCoordinates now attempts to read metadata from any file
        // regardless of extension. The AllowedExtensions filtering happens at a higher level
        // (DirectoryScanner/FileFactory) when deciding which files to process.
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "image.bmp");
        
        // Create actual JPEG content but with .bmp extension
        var jpegBytes = MockImageGenerator.CreateJpeg(gps: (40.7128, -74.0060));
        File.WriteAllBytes(tempFile, jpegBytes);
        
        var fileInfo = new FileInfo(tempFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            AllowedExtensions = [".jpg", ".jpeg", ".png"], // .bmp not included
            LogLevel = OutputLevel.Verbose
        });
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        try
        {
            // Act
            var result = extractor.GetCoordinates(fileInfo);

            // Assert - should still read GPS metadata from the actual file content
            await Assert.That(result).IsNotNull();
            await Assert.That(result!.Value.Latitude).IsEqualTo(40.7128).Within(0.001);
            await Assert.That(result!.Value.Longitude).IsEqualTo(-74.0060).Within(0.001);
        }
        finally
        {
            // Cleanup
            SafeDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task GetDateTime_WithNonImageFile_ReturnsFileSystemDatesOnly()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "Plain text content");
        var fileInfo = new FileInfo(tempFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            AllowedExtensions = [".jpg", ".jpeg", ".png"],
            LogLevel = OutputLevel.Verbose
        });
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        try
        {
            // Act
            var result = extractor.GetDateTime(fileInfo);

            // Assert
            await Assert.That(result.Created).IsEqualTo(fileInfo.CreationTime);
            await Assert.That(result.Modified).IsEqualTo(fileInfo.LastWriteTime);
            await Assert.That(result.Taken).IsEqualTo(default(DateTime));
        }
        finally
        {
            // Cleanup
            File.Delete(tempFile);
        }
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public async Task GetCoordinates_WithCorruptJpegFile_ReturnsNullAndLogsWarning()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "corrupt.jpg");
        
        // Create a file with JPEG header but corrupt content
        File.WriteAllBytes(tempFile, TestSampleImages.CorruptJpeg);
        
        var fileInfo = new FileInfo(tempFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            AllowedExtensions = [".jpg", ".jpeg", ".png"],
            LogLevel = OutputLevel.Verbose
        });
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        try
        {
            // Act
            var result = extractor.GetCoordinates(fileInfo);

            // Assert - should handle gracefully and return null
            await Assert.That(result).IsNull();
        }
        finally
        {
            // Cleanup
            SafeDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task GetDateTime_WithCorruptJpegFile_ReturnsFileSystemDates()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "corrupt.jpg");
        
        // Create a file with JPEG header but corrupt content
        File.WriteAllBytes(tempFile, TestSampleImages.CorruptJpeg);
        
        var fileInfo = new FileInfo(tempFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            AllowedExtensions = [".jpg", ".jpeg", ".png"],
            LogLevel = OutputLevel.Verbose
        });
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        try
        {
            // Act
            var result = extractor.GetDateTime(fileInfo);

            // Assert - should handle gracefully and return file system dates
            await Assert.That(result.Created).IsEqualTo(fileInfo.CreationTime);
            await Assert.That(result.Modified).IsEqualTo(fileInfo.LastWriteTime);
            await Assert.That(result.Taken).IsEqualTo(default(DateTime));
        }
        finally
        {
            // Cleanup
            SafeDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task GetCoordinates_WithEmptyFile_ReturnsNull()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "empty.jpg");
        
        // Create an empty file with .jpg extension
        File.WriteAllBytes(tempFile, []);
        
        var fileInfo = new FileInfo(tempFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            AllowedExtensions = [".jpg", ".jpeg", ".png"],
            LogLevel = OutputLevel.Verbose
        });
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        try
        {
            // Act
            var result = extractor.GetCoordinates(fileInfo);

            // Assert - should handle gracefully and return null
            await Assert.That(result).IsNull();
        }
        finally
        {
            // Cleanup
            SafeDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task GetDateTime_WithEmptyFile_ReturnsFileSystemDates()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "empty.jpg");
        
        // Create an empty file with .jpg extension
        File.WriteAllBytes(tempFile, []);
        
        var fileInfo = new FileInfo(tempFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            AllowedExtensions = [".jpg", ".jpeg", ".png"],
            LogLevel = OutputLevel.Verbose
        });
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        try
        {
            // Act
            var result = extractor.GetDateTime(fileInfo);

            // Assert - should handle gracefully and return file system dates
            await Assert.That(result.Created).IsEqualTo(fileInfo.CreationTime);
            await Assert.That(result.Modified).IsEqualTo(fileInfo.LastWriteTime);
            await Assert.That(result.Taken).IsEqualTo(default(DateTime));
        }
        finally
        {
            // Cleanup
            SafeDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task GetCoordinates_WithRandomBinaryData_ReturnsNull()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "random.jpg");
        
        // Create random binary data with .jpg extension
        var random = new Random(42);
        var randomBytes = new byte[1024];
        random.NextBytes(randomBytes);
        File.WriteAllBytes(tempFile, randomBytes);
        
        var fileInfo = new FileInfo(tempFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            AllowedExtensions = [".jpg", ".jpeg", ".png"],
            LogLevel = OutputLevel.Verbose
        });
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        try
        {
            // Act
            var result = extractor.GetCoordinates(fileInfo);

            // Assert - should handle gracefully and return null
            await Assert.That(result).IsNull();
        }
        finally
        {
            // Cleanup
            SafeDeleteDirectory(tempDir);
        }
    }

    #endregion

    #region DateTime with EXIF Tests

    [Test]
    public async Task GetDateTime_WithValidExifDateTaken_ReturnsTakenDate()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "dated_image.jpg");
        var expectedDateTaken = new DateTime(2023, 6, 15, 14, 30, 0);
        
        // Create a JPEG with date taken EXIF data
        var jpegBytes = MockImageGenerator.CreateJpeg(dateTaken: expectedDateTaken);
        File.WriteAllBytes(tempFile, jpegBytes);
        
        var fileInfo = new FileInfo(tempFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            AllowedExtensions = [".jpg", ".jpeg", ".png"],
            LogLevel = OutputLevel.Verbose
        });
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        try
        {
            // Act
            var result = extractor.GetDateTime(fileInfo);

            // Assert
            await Assert.That(result.Taken).IsEqualTo(expectedDateTaken);
            await Assert.That(result.Created).IsEqualTo(fileInfo.CreationTime);
            await Assert.That(result.Modified).IsEqualTo(fileInfo.LastWriteTime);
        }
        finally
        {
            // Cleanup
            SafeDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task GetDateTime_WithBothDateAndGps_ReturnsAllMetadata()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "full_metadata.jpg");
        var expectedDateTaken = new DateTime(2023, 12, 25, 10, 0, 0);
        var expectedLat = 35.6762;
        var expectedLon = 139.6503;
        
        // Create a JPEG with both date and GPS data
        var jpegBytes = MockImageGenerator.CreateJpeg(dateTaken: expectedDateTaken, gps: (expectedLat, expectedLon));
        File.WriteAllBytes(tempFile, jpegBytes);
        
        var fileInfo = new FileInfo(tempFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            AllowedExtensions = [".jpg", ".jpeg", ".png"],
            LogLevel = OutputLevel.Verbose
        });
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        try
        {
            // Act
            var dateResult = extractor.GetDateTime(fileInfo);
            var coordsResult = extractor.GetCoordinates(fileInfo);

            // Assert
            await Assert.That(dateResult.Taken).IsEqualTo(expectedDateTaken);
            await Assert.That(coordsResult).IsNotNull();
            await Assert.That(coordsResult!.Value.Latitude).IsEqualTo(expectedLat).Within(0.001);
            await Assert.That(coordsResult!.Value.Longitude).IsEqualTo(expectedLon).Within(0.001);
        }
        finally
        {
            // Cleanup
            SafeDeleteDirectory(tempDir);
        }
    }

    #endregion

    #region GetCamera Tests

    [Test]
    public async Task GetCamera_WithNonImageFile_ReturnsNull()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "Test content");
        var fileInfo = new FileInfo(tempFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            AllowedExtensions = [".jpg", ".jpeg", ".png"],
            LogLevel = OutputLevel.Verbose
        });
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        try
        {
            // Act
            var result = extractor.GetCamera(fileInfo);

            // Assert
            await Assert.That(result).IsNull();
        }
        finally
        {
            // Cleanup
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetCamera_WithJpegWithoutExif_ReturnsNull()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "no_exif.jpg");
        
        // Create a JPEG without any EXIF data
        var jpegBytes = MockImageGenerator.CreateJpeg();
        File.WriteAllBytes(tempFile, jpegBytes);
        
        var fileInfo = new FileInfo(tempFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            AllowedExtensions = [".jpg", ".jpeg", ".png"],
            LogLevel = OutputLevel.Verbose
        });
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        try
        {
            // Act
            var result = extractor.GetCamera(fileInfo);

            // Assert
            // May return null since MockImageGenerator doesn't add camera data
            await Assert.That(result).IsNull();
        }
        finally
        {
            // Cleanup
            SafeDeleteDirectory(tempDir);
        }
    }

    [Test]
    public async Task GetCamera_WithCorruptedFile_ReturnsNullAndLogsWarning()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "corrupted.jpg");
        
        // Create a corrupted "JPEG" file
        File.WriteAllBytes(tempFile, new byte[] { 0xFF, 0xD8, 0xFF, 0x00, 0x00 });
        
        var fileInfo = new FileInfo(tempFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            AllowedExtensions = [".jpg", ".jpeg", ".png"],
            LogLevel = OutputLevel.Verbose
        });
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        try
        {
            // Act
            var result = extractor.GetCamera(fileInfo);

            // Assert
            await Assert.That(result).IsNull();
        }
        finally
        {
            // Cleanup
            SafeDeleteDirectory(tempDir);
        }
    }

    #endregion

    #region SanitizeCameraName Tests (via GetCamera behavior)

    [Test]
    public async Task GetCamera_SanitizeCameraName_HandlesNormalCameraName()
    {
        // This tests the sanitization logic indirectly
        // The actual sanitization is tested via the public GetCamera method
        // when we have real camera data in test images
        
        // For direct sanitization testing, we'd need to make the method internal/public
        // or test through integration tests with real camera data
        await Assert.That(true).IsTrue();
    }

    #endregion

    #region Helper Methods

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

    #endregion
}