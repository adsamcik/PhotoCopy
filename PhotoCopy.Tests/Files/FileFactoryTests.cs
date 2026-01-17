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

public class FileFactoryTests : TestBase
{
    private readonly IMetadataEnricher _mockMetadataEnricher;
    private readonly ILogger<FileWithMetadata> _mockLogger;
    private readonly IOptions<PhotoCopyConfig> _mockOptions;
    private readonly PhotoCopyConfig _config;
    private readonly FileFactory _factory;

    public FileFactoryTests()
    {
        _mockMetadataEnricher = Substitute.For<IMetadataEnricher>();
        _mockLogger = Substitute.For<ILogger<FileWithMetadata>>();
        
        _config = new PhotoCopyConfig
        {
            AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff",
                ".heic", ".webp", ".raw", ".cr2", ".nef", ".arw", ".dng", ".raf",
                ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".m4v"
            }
        };
        
        _mockOptions = Microsoft.Extensions.Options.Options.Create(_config);
        _factory = new FileFactory(_mockMetadataEnricher, _mockLogger, _mockOptions);
    }

    #region Helper Methods

    private FileInfo CreateFileInfo(string fileName)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), fileName);
        return new FileInfo(tempPath);
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

    #region Image Extension Tests

    [Test]
    public void CreateFile_WithImageExtension_CreatesFileWithMetadata()
    {
        // Arrange
        var fileInfo = CreateFileInfo("photo.jpg");
        var metadata = CreateMockMetadata(new DateTime(2023, 6, 15, 10, 30, 0));
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
        result.File.Should().Be(fileInfo);
        result.FileDateTime.DateTime.Should().Be(metadata.DateTime.DateTime);
    }

    [Test]
    public void CreateFile_WithJpegExtension_CreatesFileWithMetadata()
    {
        // Arrange
        var fileInfo = CreateFileInfo("photo.jpeg");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    [Test]
    public void CreateFile_WithPngExtension_CreatesFileWithMetadata()
    {
        // Arrange
        var fileInfo = CreateFileInfo("image.png");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    [Test]
    public void CreateFile_WithGifExtension_CreatesFileWithMetadata()
    {
        // Arrange
        var fileInfo = CreateFileInfo("animation.gif");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    [Test]
    public void CreateFile_WithHeicExtension_CreatesFileWithMetadata()
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

    [Test]
    public void CreateFile_WithBmpExtension_CreatesFileWithMetadata()
    {
        // Arrange
        var fileInfo = CreateFileInfo("bitmap.bmp");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    [Test]
    public void CreateFile_WithTiffExtension_CreatesFileWithMetadata()
    {
        // Arrange
        var fileInfo = CreateFileInfo("document.tiff");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    [Test]
    public void CreateFile_WithWebpExtension_CreatesFileWithMetadata()
    {
        // Arrange
        var fileInfo = CreateFileInfo("modern.webp");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    #endregion

    #region RAW Image Format Tests

    [Test]
    public void CreateFile_WithCr2Extension_CreatesFileWithMetadata()
    {
        // Arrange - Canon RAW
        var fileInfo = CreateFileInfo("canon_raw.cr2");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    [Test]
    public void CreateFile_WithNefExtension_CreatesFileWithMetadata()
    {
        // Arrange - Nikon RAW
        var fileInfo = CreateFileInfo("nikon_raw.nef");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    [Test]
    public void CreateFile_WithArwExtension_CreatesFileWithMetadata()
    {
        // Arrange - Sony RAW
        var fileInfo = CreateFileInfo("sony_raw.arw");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    [Test]
    public void CreateFile_WithDngExtension_CreatesFileWithMetadata()
    {
        // Arrange - Digital Negative
        var fileInfo = CreateFileInfo("digital_negative.dng");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    [Test]
    public void CreateFile_WithRafExtension_CreatesFileWithMetadata()
    {
        // Arrange - Fujifilm RAW
        var fileInfo = CreateFileInfo("fuji_raw.raf");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    #endregion

    #region Video Extension Tests

    [Test]
    public void CreateFile_WithVideoExtension_CreatesFileWithMetadata()
    {
        // Arrange
        var fileInfo = CreateFileInfo("video.mp4");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    [Test]
    public void CreateFile_WithMovExtension_CreatesFileWithMetadata()
    {
        // Arrange
        var fileInfo = CreateFileInfo("iphone_video.mov");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    [Test]
    public void CreateFile_WithAviExtension_CreatesFileWithMetadata()
    {
        // Arrange
        var fileInfo = CreateFileInfo("old_video.avi");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    [Test]
    public void CreateFile_WithMkvExtension_CreatesFileWithMetadata()
    {
        // Arrange
        var fileInfo = CreateFileInfo("matroska.mkv");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    [Test]
    public void CreateFile_WithWmvExtension_CreatesFileWithMetadata()
    {
        // Arrange
        var fileInfo = CreateFileInfo("windows_video.wmv");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    [Test]
    public void CreateFile_WithM4vExtension_CreatesFileWithMetadata()
    {
        // Arrange
        var fileInfo = CreateFileInfo("itunes_video.m4v");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    #endregion

    #region Unknown Extension Tests

    [Test]
    public void CreateFile_WithUnknownExtension_CreatesGenericFile()
    {
        // Arrange
        var fileInfo = CreateFileInfo("document.txt");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<GenericFile>();
    }

    [Test]
    public void CreateFile_WithPdfExtension_CreatesGenericFile()
    {
        // Arrange
        var fileInfo = CreateFileInfo("document.pdf");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<GenericFile>();
    }

    [Test]
    public void CreateFile_WithDocxExtension_CreatesGenericFile()
    {
        // Arrange
        var fileInfo = CreateFileInfo("document.docx");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<GenericFile>();
    }

    [Test]
    public void CreateFile_WithXmlExtension_CreatesGenericFile()
    {
        // Arrange
        var fileInfo = CreateFileInfo("config.xml");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<GenericFile>();
    }

    [Test]
    public void CreateFile_WithZipExtension_CreatesGenericFile()
    {
        // Arrange
        var fileInfo = CreateFileInfo("archive.zip");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<GenericFile>();
    }

    #endregion

    #region Dependency Passing Tests

    [Test]
    public void CreateFile_PassesCorrectDependencies_ToFileWithMetadata()
    {
        // Arrange
        var fileInfo = CreateFileInfo("photo.jpg");
        var expectedDateTime = new DateTime(2023, 8, 20, 15, 45, 30);
        var expectedLocation = new LocationData("Paris", "Paris", null, "Île-de-France", "France");
        var expectedChecksum = "abc123def456";
        
        var metadata = CreateMockMetadata(expectedDateTime, expectedLocation, expectedChecksum);
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
        var fileWithMetadata = (FileWithMetadata)result;
        
        fileWithMetadata.File.Should().Be(fileInfo);
        fileWithMetadata.FileDateTime.DateTime.Should().Be(expectedDateTime);
        fileWithMetadata.Location.Should().Be(expectedLocation);
        fileWithMetadata.Checksum.Should().Be(expectedChecksum);
    }

    [Test]
    public void CreateFile_PassesCorrectDependencies_ToGenericFile()
    {
        // Arrange
        var fileInfo = CreateFileInfo("document.txt");
        var expectedDateTime = new DateTime(2023, 9, 10, 11, 30, 0);
        var expectedChecksum = "xyz789";
        
        var metadata = CreateMockMetadata(expectedDateTime, null, expectedChecksum);
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<GenericFile>();
        var genericFile = (GenericFile)result;
        
        genericFile.File.Should().Be(fileInfo);
        genericFile.FileDateTime.DateTime.Should().Be(expectedDateTime);
    }

    [Test]
    public void CreateFile_CallsMetadataEnricher_WithCorrectFileInfo()
    {
        // Arrange
        var fileInfo = CreateFileInfo("test_photo.jpg");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        _factory.Create(fileInfo);

        // Assert
        _mockMetadataEnricher.Received(1).Enrich(fileInfo);
    }

    #endregion

    #region Location Data Tests

    [Test]
    public void CreateFile_WithLocation_SetsLocationOnFileWithMetadata()
    {
        // Arrange
        var fileInfo = CreateFileInfo("geotagged.jpg");
        var location = new LocationData("London", "London", null, null, "UK");
        var metadata = CreateMockMetadata(location: location);
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
        result.Location.Should().NotBeNull();
        result.Location!.City.Should().Be("London");
        result.Location.Country.Should().Be("UK");
    }

    [Test]
    public void CreateFile_WithoutLocation_LocationIsNull()
    {
        // Arrange
        var fileInfo = CreateFileInfo("no_gps.jpg");
        var metadata = CreateMockMetadata(location: null);
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Location.Should().BeNull();
    }

    [Test]
    public void CreateFile_GenericFile_LocationIsAlwaysNull()
    {
        // Arrange
        var fileInfo = CreateFileInfo("document.txt");
        var location = new LocationData("New York", "New York", null, "New York", "USA");
        var metadata = CreateMockMetadata(location: location);
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<GenericFile>();
        result.Location.Should().BeNull(); // GenericFile doesn't support location
    }

    #endregion

    #region Checksum Tests

    [Test]
    public void CreateFile_WithChecksum_SetsChecksumOnFileWithMetadata()
    {
        // Arrange
        var fileInfo = CreateFileInfo("photo.jpg");
        var checksum = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        var metadata = CreateMockMetadata(checksum: checksum);
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
        result.Checksum.Should().Be(checksum);
    }

    [Test]
    public void CreateFile_WithNullChecksum_ChecksumIsEmpty()
    {
        // Arrange
        var fileInfo = CreateFileInfo("photo.jpg");
        var metadata = CreateMockMetadata(checksum: null);
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Checksum.Should().BeEmpty();
    }

    [Test]
    public void CreateFile_WithEmptyChecksum_ChecksumIsEmpty()
    {
        // Arrange
        var fileInfo = CreateFileInfo("photo.jpg");
        var metadata = CreateMockMetadata(checksum: "");
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Checksum.Should().BeEmpty();
    }

    [Test]
    public void CreateFile_WithWhitespaceChecksum_ChecksumIsEmpty()
    {
        // Arrange
        var fileInfo = CreateFileInfo("photo.jpg");
        var metadata = CreateMockMetadata(checksum: "   ");
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Checksum.Should().BeEmpty();
    }

    #endregion

    #region Case Sensitivity Tests

    [Test]
    public void CreateFile_WithUppercaseExtension_CreatesFileWithMetadata()
    {
        // Arrange
        var fileInfo = CreateFileInfo("PHOTO.JPG");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    [Test]
    public void CreateFile_WithMixedCaseExtension_CreatesFileWithMetadata()
    {
        // Arrange
        var fileInfo = CreateFileInfo("Photo.JpEg");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    [Test]
    public void CreateFile_WithUppercaseUnknownExtension_CreatesGenericFile()
    {
        // Arrange
        var fileInfo = CreateFileInfo("DOCUMENT.TXT");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<GenericFile>();
    }

    #endregion

    #region Edge Cases

    [Test]
    public void CreateFile_WithNoExtension_CreatesGenericFile()
    {
        // Arrange
        var fileInfo = CreateFileInfo("noextension");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<GenericFile>();
    }

    [Test]
    public void CreateFile_WithDotOnlyExtension_CreatesGenericFile()
    {
        // Arrange
        var fileInfo = CreateFileInfo("file.");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<GenericFile>();
    }

    [Test]
    public void CreateFile_WithMultipleDots_UsesLastExtension()
    {
        // Arrange
        var fileInfo = CreateFileInfo("photo.backup.jpg");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    [Test]
    public void CreateFile_WithHiddenFile_ProcessesCorrectly()
    {
        // Arrange
        var fileInfo = CreateFileInfo(".hidden.jpg");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<FileWithMetadata>();
    }

    #endregion

    #region Custom Config Tests

    [Test]
    public void CreateFile_WithCustomAllowedExtensions_RespectsConfig()
    {
        // Arrange - Create factory with custom config that only allows .xyz
        var customConfig = new PhotoCopyConfig
        {
            AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xyz" }
        };
        var customFactory = new FileFactory(
            _mockMetadataEnricher,
            _mockLogger,
            Microsoft.Extensions.Options.Options.Create(customConfig));

        var jpgFile = CreateFileInfo("photo.jpg");
        var xyzFile = CreateFileInfo("custom.xyz");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var jpgResult = customFactory.Create(jpgFile);
        var xyzResult = customFactory.Create(xyzFile);

        // Assert
        jpgResult.Should().BeOfType<GenericFile>(); // .jpg not in custom config
        xyzResult.Should().BeOfType<FileWithMetadata>(); // .xyz is in custom config
    }

    [Test]
    public void CreateFile_WithEmptyAllowedExtensions_CreatesGenericFileForAll()
    {
        // Arrange
        var emptyConfig = new PhotoCopyConfig
        {
            AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };
        var emptyFactory = new FileFactory(
            _mockMetadataEnricher,
            _mockLogger,
            Microsoft.Extensions.Options.Options.Create(emptyConfig));

        var fileInfo = CreateFileInfo("photo.jpg");
        var metadata = CreateMockMetadata();
        SetupMetadataEnricher(metadata);

        // Act
        var result = emptyFactory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<GenericFile>();
    }

    #endregion

    #region DateTime Metadata Tests

    [Test]
    public void CreateFile_PreservesFileDateTimeFromMetadata()
    {
        // Arrange
        var fileInfo = CreateFileInfo("photo.jpg");
        var expectedDate = new DateTime(2022, 12, 25, 18, 30, 45);
        var fileDateTime = new FileDateTime(expectedDate, DateTimeSource.ExifDateTimeOriginal);
        var metadata = new FileMetadata(fileDateTime);
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.FileDateTime.DateTime.Should().Be(expectedDate);
        result.FileDateTime.Source.Should().Be(DateTimeSource.ExifDateTimeOriginal);
    }

    [Test]
    public void CreateFile_GenericFile_PreservesFileDateTimeFromMetadata()
    {
        // Arrange
        var fileInfo = CreateFileInfo("document.txt");
        var expectedDate = new DateTime(2021, 6, 15, 9, 0, 0);
        var fileDateTime = new FileDateTime(expectedDate, DateTimeSource.FileModification);
        var metadata = new FileMetadata(fileDateTime);
        SetupMetadataEnricher(metadata);

        // Act
        var result = _factory.Create(fileInfo);

        // Assert
        result.Should().BeOfType<GenericFile>();
        result.FileDateTime.DateTime.Should().Be(expectedDate);
        result.FileDateTime.Source.Should().Be(DateTimeSource.FileModification);
    }

    #endregion
}
