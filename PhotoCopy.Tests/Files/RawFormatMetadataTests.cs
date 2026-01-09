using System;
using System.Collections.Generic;
using System.IO;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Configuration;
using PhotoCopy.Files;
using PhotoCopy.Files.Metadata;

namespace PhotoCopy.Tests.Files;

/// <summary>
/// Tests for HEIC and RAW image format metadata extraction in PhotoCopy.
/// These tests focus on extension recognition and behavior rather than actual 
/// metadata extraction since we cannot programmatically generate real HEIC/RAW files.
/// </summary>
public class RawFormatMetadataTests : TestBase
{
    private readonly IMetadataEnricher _mockMetadataEnricher;
    private readonly ILogger<FileWithMetadata> _mockLogger;
    private readonly ILogger<FileMetadataExtractor> _extractorLogger;
    private readonly PhotoCopyConfig _config;
    private readonly IOptions<PhotoCopyConfig> _options;
    private readonly FileFactory _factory;
    private readonly FileMetadataExtractor _extractor;

    public RawFormatMetadataTests()
    {
        _mockMetadataEnricher = Substitute.For<IMetadataEnricher>();
        _mockLogger = Substitute.For<ILogger<FileWithMetadata>>();
        _extractorLogger = Substitute.For<ILogger<FileMetadataExtractor>>();

        _config = new PhotoCopyConfig
        {
            AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".heic", ".mov", ".mp4", ".avi",
                ".cr2", ".raf", ".nef", ".arw", ".dng"
            },
            LogLevel = OutputLevel.Verbose
        };

        _options = Microsoft.Extensions.Options.Options.Create(_config);
        _factory = new FileFactory(_mockMetadataEnricher, _mockLogger, _options);
        _extractor = new FileMetadataExtractor(_extractorLogger, _options);
    }

    #region Helper Methods

    private FileInfo CreateFileInfo(string fileName)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), fileName);
        return new FileInfo(tempPath);
    }

    private string CreateTempFile(string fileName, string content = "Test content")
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
        File.WriteAllText(tempPath, content);
        return tempPath;
    }

    private FileMetadata CreateMockMetadata(DateTime? dateTime = null, LocationData? location = null, string? checksum = null)
    {
        var fileDateTime = new FileDateTime(dateTime ?? DateTime.Now, DateTimeSource.FileCreation);
        return new FileMetadata(fileDateTime)
        {
            Location = location,
            Checksum = checksum
        };
    }

    private void SetupMetadataEnricher(FileMetadata metadata)
    {
        _mockMetadataEnricher.Enrich(Arg.Any<FileInfo>()).Returns(metadata);
    }

    #endregion

    #region HEIC Format Tests

    [Test]
    public void HeicFile_WithExif_ExtractsMetadata()
    {
        // Arrange - HEIC file with mock EXIF date and location data
        var expectedDate = new DateTime(2024, 6, 15, 14, 30, 0);
        var expectedLocation = new LocationData("San Francisco", null, "California", "USA");
        var fileInfo = CreateFileInfo("IMG_1234.heic");
        var metadata = CreateMockMetadata(expectedDate, expectedLocation);
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert - HEIC is recognized and creates FileWithMetadata
        result.Should().BeOfType<FileWithMetadata>();
        var fileWithMetadata = (FileWithMetadata)result;
        fileWithMetadata.FileDateTime.DateTime.Should().Be(expectedDate);
        fileWithMetadata.Location.Should().NotBeNull();
        fileWithMetadata.Location!.City.Should().Be("San Francisco");
        fileWithMetadata.Location!.Country.Should().Be("USA");
    }

    [Test]
    public void HeicExtension_IsInAllowedExtensions()
    {
        // Assert
        _config.AllowedExtensions.Should().Contain(".heic");
    }

    [Test]
    public void HeicFile_CreatesFileWithMetadata()
    {
        // Arrange
        var fileInfo = CreateFileInfo("iphone_photo.heic");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    #endregion

    #region Canon CR2 Format Tests

    [Test]
    public void CanonCr2_ExtensionRecognized()
    {
        // Arrange - Canon RAW format
        var fileInfo = CreateFileInfo("IMG_5678.cr2");
        var metadata = CreateMockMetadata(new DateTime(2024, 3, 20, 10, 15, 0));
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert - CR2 extension is recognized as photo format
        result.Should().BeOfType<FileWithMetadata>();
        _config.AllowedExtensions.Should().Contain(".cr2");
    }

    [Test]
    public void CanonCr2_FileMetadataIsExtracted()
    {
        // Arrange
        var expectedDate = new DateTime(2024, 3, 20, 10, 15, 0);
        var fileInfo = CreateFileInfo("canon_photo.cr2");
        var metadata = CreateMockMetadata(expectedDate);
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
        ((FileWithMetadata)result).FileDateTime.DateTime.Should().Be(expectedDate);
    }

    #endregion

    #region Fujifilm RAF Format Tests

    [Test]
    public void FujifilmRaf_ExtensionRecognized()
    {
        // Arrange - Fujifilm RAW format
        var fileInfo = CreateFileInfo("DSCF1234.raf");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert - RAF extension is recognized as photo format
        result.Should().BeOfType<FileWithMetadata>();
        _config.AllowedExtensions.Should().Contain(".raf");
    }

    #endregion

    #region Nikon NEF Format Tests

    [Test]
    public void NikonNef_ExtensionRecognized()
    {
        // Arrange - Nikon RAW format
        var fileInfo = CreateFileInfo("DSC_0001.nef");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert - NEF extension is recognized as photo format
        result.Should().BeOfType<FileWithMetadata>();
        _config.AllowedExtensions.Should().Contain(".nef");
    }

    #endregion

    #region Sony ARW Format Tests

    [Test]
    public void SonyArw_ExtensionRecognized()
    {
        // Arrange - Sony RAW format
        var fileInfo = CreateFileInfo("DSC00001.arw");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert - ARW extension is recognized as photo format
        result.Should().BeOfType<FileWithMetadata>();
        _config.AllowedExtensions.Should().Contain(".arw");
    }

    #endregion

    #region Adobe DNG Format Tests

    [Test]
    public void AdobeDng_ExtensionRecognized()
    {
        // Arrange - Adobe Digital Negative format
        var fileInfo = CreateFileInfo("photo.dng");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert - DNG extension is recognized as photo format
        result.Should().BeOfType<FileWithMetadata>();
        _config.AllowedExtensions.Should().Contain(".dng");
    }

    #endregion

    #region Fallback Behavior Tests

    [Test]
    public async Task RawFile_WithoutMetadata_FallsBackToFileDate()
    {
        // Arrange - Create a temp file without EXIF metadata
        var tempFilePath = CreateTempFile("test_raw_file.cr2", "fake raw content");
        var fileInfo = new FileInfo(tempFilePath);

        try
        {
            // Act - Extract metadata from a file without EXIF
            var result = _extractor.GetDateTime(fileInfo);

            // Assert - Should fall back to file system dates
            await Assert.That(result.Taken).IsEqualTo(default(DateTime));
            await Assert.That(result.Created).IsEqualTo(fileInfo.CreationTime);
            await Assert.That(result.Modified).IsEqualTo(fileInfo.LastWriteTime);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
                Directory.Delete(Path.GetDirectoryName(tempFilePath)!);
            }
        }
    }

    [Test]
    public async Task RawFile_WithCorruptedData_FallsBackGracefully()
    {
        // Arrange - Create a temp file with invalid binary data
        var tempFilePath = CreateTempFile("corrupted.dng", "not a valid DNG file");
        var fileInfo = new FileInfo(tempFilePath);

        try
        {
            // Act - Should not throw, just return file dates
            var result = _extractor.GetDateTime(fileInfo);

            // Assert - Falls back to file dates without crashing
            await Assert.That(result.Created).IsEqualTo(fileInfo.CreationTime);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
                Directory.Delete(Path.GetDirectoryName(tempFilePath)!);
            }
        }
    }

    #endregion

    #region Mixed Format Processing Tests

    [Test]
    public void MixedRawAndJpeg_AllProcessedCorrectly()
    {
        // Arrange - Different formats in same batch
        var files = new[]
        {
            ("photo.jpg", new DateTime(2024, 1, 15)),
            ("raw_photo.cr2", new DateTime(2024, 2, 20)),
            ("iphone.heic", new DateTime(2024, 3, 25)),
            ("nikon.nef", new DateTime(2024, 4, 10)),
            ("sony.arw", new DateTime(2024, 5, 5)),
            ("fuji.raf", new DateTime(2024, 6, 1)),
            ("dng_photo.dng", new DateTime(2024, 7, 15))
        };

        var results = new List<IFile>();

        foreach (var (fileName, date) in files)
        {
            var fileInfo = CreateFileInfo(fileName);
            var metadata = CreateMockMetadata(date);
            SetupMetadataEnricher(metadata);

            // Act
            var result = _factory.Create(fileInfo);
            results.Add(result);
        }

        // Assert - All formats are processed as FileWithMetadata
        results.Should().HaveCount(7);
        results.Should().AllSatisfy(f => f.Should().BeOfType<FileWithMetadata>());
    }

    [Test]
    public void MixedRawAndJpeg_MetadataPreserved()
    {
        // Arrange
        var jpegDate = new DateTime(2024, 1, 15, 10, 0, 0);
        var cr2Date = new DateTime(2024, 2, 20, 11, 0, 0);
        var heicDate = new DateTime(2024, 3, 25, 12, 0, 0);

        // JPEG
        var jpegFileInfo = CreateFileInfo("photo.jpg");
        var jpegMetadata = CreateMockMetadata(jpegDate);
        _mockMetadataEnricher.Enrich(Arg.Is<FileInfo>(f => f.Name == "photo.jpg")).Returns(jpegMetadata);

        // CR2
        var cr2FileInfo = CreateFileInfo("raw.cr2");
        var cr2Metadata = CreateMockMetadata(cr2Date);
        _mockMetadataEnricher.Enrich(Arg.Is<FileInfo>(f => f.Name == "raw.cr2")).Returns(cr2Metadata);

        // HEIC
        var heicFileInfo = CreateFileInfo("iphone.heic");
        var heicMetadata = CreateMockMetadata(heicDate);
        _mockMetadataEnricher.Enrich(Arg.Is<FileInfo>(f => f.Name == "iphone.heic")).Returns(heicMetadata);

        // Act
        var jpegResult = (FileWithMetadata)_factory.Create(jpegFileInfo);
        var cr2Result = (FileWithMetadata)_factory.Create(cr2FileInfo);
        var heicResult = (FileWithMetadata)_factory.Create(heicFileInfo);

        // Assert - Each format preserves its own metadata
        jpegResult.FileDateTime.DateTime.Should().Be(jpegDate);
        cr2Result.FileDateTime.DateTime.Should().Be(cr2Date);
        heicResult.FileDateTime.DateTime.Should().Be(heicDate);
    }

    #endregion

    #region Case Sensitivity Tests

    [Test]
    [Property("Category", "CaseSensitivity")]
    public void RawExtensions_CaseInsensitive_LowerCase()
    {
        // Arrange
        var fileInfo = CreateFileInfo("photo.cr2");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    [Test]
    [Property("Category", "CaseSensitivity")]
    public void RawExtensions_CaseInsensitive_UpperCase()
    {
        // Arrange
        var fileInfo = CreateFileInfo("photo.CR2");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    [Test]
    [Property("Category", "CaseSensitivity")]
    public void RawExtensions_CaseInsensitive_MixedCase()
    {
        // Arrange
        var fileInfo = CreateFileInfo("photo.Cr2");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    [Test]
    [Property("Category", "CaseSensitivity")]
    public void HeicExtensions_CaseInsensitive()
    {
        // Arrange - Test all case variations
        var variations = new[] { ".heic", ".HEIC", ".Heic", ".HeIc" };

        foreach (var ext in variations)
        {
            // Assert - All variations should be contained (OrdinalIgnoreCase)
            _config.AllowedExtensions.Contains(ext).Should().BeTrue($"Extension {ext} should be recognized");
        }
    }

    [Test]
    [Property("Category", "CaseSensitivity")]
    public void AllRawExtensions_CaseInsensitive()
    {
        // Arrange - All raw format extensions
        var rawExtensions = new[] { ".cr2", ".raf", ".nef", ".arw", ".dng" };

        foreach (var ext in rawExtensions)
        {
            // Test lower, upper, and mixed case
            _config.AllowedExtensions.Contains(ext.ToLower()).Should().BeTrue($"Lowercase {ext} should be recognized");
            _config.AllowedExtensions.Contains(ext.ToUpper()).Should().BeTrue($"Uppercase {ext} should be recognized");
        }
    }

    #endregion

    #region Extension Validation Tests

    [Test]
    public void AllowedExtensions_ContainsAllExpectedRawFormats()
    {
        // Assert - Verify default config includes all expected RAW formats
        var expectedExtensions = new[] { ".heic", ".cr2", ".raf", ".nef", ".arw", ".dng" };
        
        foreach (var ext in expectedExtensions)
        {
            _config.AllowedExtensions.Should().Contain(ext, $"Config should include {ext}");
        }
    }

    [Test]
    public void DefaultConfig_IncludesRawFormats()
    {
        // Arrange - Create a default config (not our test config)
        var defaultConfig = new PhotoCopyConfig();

        // Assert - Default config should include RAW formats
        defaultConfig.AllowedExtensions.Should().Contain(".heic");
        defaultConfig.AllowedExtensions.Should().Contain(".cr2");
        defaultConfig.AllowedExtensions.Should().Contain(".raf");
        defaultConfig.AllowedExtensions.Should().Contain(".nef");
        defaultConfig.AllowedExtensions.Should().Contain(".arw");
        defaultConfig.AllowedExtensions.Should().Contain(".dng");
    }

    [Test]
    public void UnknownRawFormat_NotRecognized()
    {
        // Arrange - Use a RAW format not in AllowedExtensions
        var fileInfo = CreateFileInfo("photo.orf"); // Olympus RAW, not in our config
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert - Unknown format creates GenericFile, not FileWithMetadata
        result.Should().BeOfType<GenericFile>();
    }

    #endregion

    #region GPS Coordinates Tests

    [Test]
    public async Task RawFile_WithoutGps_ReturnsNull()
    {
        // Arrange - Create a temp file without GPS data
        var tempFilePath = CreateTempFile("no_gps.cr2", "fake raw content");
        var fileInfo = new FileInfo(tempFilePath);

        try
        {
            // Act
            var result = _extractor.GetCoordinates(fileInfo);

            // Assert - Should return null when no GPS data
            await Assert.That(result).IsNull();
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
                Directory.Delete(Path.GetDirectoryName(tempFilePath)!);
            }
        }
    }

    [Test]
    public void HeicFile_WithGps_ExtractsCoordinates()
    {
        // Arrange - Mock HEIC with location data (reverse-geocoded from GPS)
        var location = new LocationData("New York", null, "New York", "USA");
        var fileInfo = CreateFileInfo("gps_photo.heic");
        var metadata = CreateMockMetadata(DateTime.Now, location);
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
        var fileWithMetadata = (FileWithMetadata)result;
        fileWithMetadata.Location.Should().NotBeNull();
        fileWithMetadata.Location!.City.Should().Be("New York");
        fileWithMetadata.Location!.Country.Should().Be("USA");
    }

    #endregion
}
