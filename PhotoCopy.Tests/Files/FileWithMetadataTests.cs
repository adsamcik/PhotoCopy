using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Files;

namespace PhotoCopy.Tests.Files;

public class FileWithMetadataTests : TestBase
{
    private readonly ILogger _mockLogger;
    private readonly IFileSystem _mockFileSystem;

    public FileWithMetadataTests()
    {
        _mockLogger = Substitute.For<ILogger>();
        _mockFileSystem = Substitute.For<IFileSystem>();
    }

    #region Helper Methods

    private FileWithMetadata CreateFileWithMetadata(string fileName, DateTime? dateTime = null)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), fileName);
        var fileInfo = new FileInfo(tempPath);
        var fileDateTime = new FileDateTime(dateTime ?? DateTime.Now, DateTimeSource.FileCreation);
        return new FileWithMetadata(fileInfo, fileDateTime, _mockLogger);
    }

    private IFile CreateMockFile(string fileName)
    {
        var mockFile = Substitute.For<IFile>();
        var tempPath = Path.Combine(Path.GetTempPath(), fileName);
        var fileInfo = new FileInfo(tempPath);
        mockFile.File.Returns(fileInfo);
        mockFile.FileDateTime.Returns(new FileDateTime(DateTime.Now, DateTimeSource.FileCreation));
        return mockFile;
    }

    private string CreateTempFile(string fileName, string content = "Test content")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, fileName);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    private void CleanupFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #endregion

    #region GetRelatedFiles Tests

    [Test]
    public void GetRelatedFiles_WithNoRelatedFiles_ReturnsEmpty()
    {
        // Arrange
        var file = CreateFileWithMetadata("photo.jpg");
        var otherFiles = new List<IFile>
        {
            CreateMockFile("completely_different.jpg"),
            CreateMockFile("another_file.png")
        };

        // Act
        file.AddRelatedFiles(otherFiles, RelatedFileLookup.Strict);

        // Assert
        file.RelatedFiles.Should().BeEmpty();
    }

    [Test]
    public void GetRelatedFiles_WithRelatedFiles_ReturnsMatching()
    {
        // Arrange
        var file = CreateFileWithMetadata("photo.jpg");
        var relatedXmp = CreateMockFile("photo.xmp");
        var relatedJson = CreateMockFile("photo.json");
        var unrelatedFile = CreateMockFile("other.jpg");
        
        var allFiles = new List<IFile> { relatedXmp, relatedJson, unrelatedFile };

        // Act
        file.AddRelatedFiles(allFiles, RelatedFileLookup.Strict);

        // Assert
        file.RelatedFiles.Should().HaveCount(2);
        file.RelatedFiles.Should().Contain(relatedXmp);
        file.RelatedFiles.Should().Contain(relatedJson);
    }

    [Test]
    public void GetRelatedFiles_MatchesByNameWithoutExtension()
    {
        // Arrange
        var file = CreateFileWithMetadata("IMG_1234.jpg");
        var xmpSidecar = CreateMockFile("IMG_1234.xmp");
        var jsonSidecar = CreateMockFile("IMG_1234.json");
        var rawVersion = CreateMockFile("IMG_1234.CR2");
        var differentFile = CreateMockFile("IMG_1235.jpg");
        
        var allFiles = new List<IFile> { xmpSidecar, jsonSidecar, rawVersion, differentFile };

        // Act
        file.AddRelatedFiles(allFiles, RelatedFileLookup.Strict);

        // Assert
        file.RelatedFiles.Should().HaveCount(3);
        file.RelatedFiles.Should().Contain(xmpSidecar);
        file.RelatedFiles.Should().Contain(jsonSidecar);
        file.RelatedFiles.Should().Contain(rawVersion);
        file.RelatedFiles.Should().NotContain(differentFile);
    }

    [Test]
    public void GetRelatedFiles_WithStrictLookup_MatchesExactPatterns()
    {
        // Arrange
        var file = CreateFileWithMetadata("photo.jpg");
        var exactMatch = CreateMockFile("photo.xmp");
        var dotSuffix = CreateMockFile("photo.jpg.xmp");
        var underscoreSuffix = CreateMockFile("photo_edit.jpg");
        var looseMatch = CreateMockFile("photography.jpg"); // Should NOT match in strict mode
        
        var allFiles = new List<IFile> { exactMatch, dotSuffix, underscoreSuffix, looseMatch };

        // Act
        file.AddRelatedFiles(allFiles, RelatedFileLookup.Strict);

        // Assert
        file.RelatedFiles.Should().Contain(exactMatch);
        file.RelatedFiles.Should().Contain(dotSuffix);
        file.RelatedFiles.Should().Contain(underscoreSuffix);
        file.RelatedFiles.Should().NotContain(looseMatch);
    }

    [Test]
    public void GetRelatedFiles_WithLooseLookup_MatchesStartsWith()
    {
        // Arrange
        var file = CreateFileWithMetadata("photo.jpg");
        var exactMatch = CreateMockFile("photo.xmp");
        var looseMatch = CreateMockFile("photography.jpg"); // Should match in loose mode
        var differentFile = CreateMockFile("other.jpg");
        
        var allFiles = new List<IFile> { exactMatch, looseMatch, differentFile };

        // Act
        file.AddRelatedFiles(allFiles, RelatedFileLookup.Loose);

        // Assert
        file.RelatedFiles.Should().Contain(exactMatch);
        file.RelatedFiles.Should().Contain(looseMatch);
        file.RelatedFiles.Should().NotContain(differentFile);
    }

    [Test]
    public void GetRelatedFiles_WithNoneLookup_AddsNoFiles()
    {
        // Arrange
        var file = CreateFileWithMetadata("photo.jpg");
        var relatedFile = CreateMockFile("photo.xmp");
        
        var allFiles = new List<IFile> { relatedFile };

        // Act
        file.AddRelatedFiles(allFiles, RelatedFileLookup.None);

        // Assert
        file.RelatedFiles.Should().BeEmpty();
    }

    [Test]
    public void GetRelatedFiles_SkipsSameFile()
    {
        // Arrange
        var file = CreateFileWithMetadata("photo.jpg");
        var sameFile = CreateMockFile("photo.jpg");
        
        var allFiles = new List<IFile> { sameFile };

        // Act
        file.AddRelatedFiles(allFiles, RelatedFileLookup.Strict);

        // Assert
        file.RelatedFiles.Should().BeEmpty();
    }

    [Test]
    public void GetRelatedFiles_HandlesNullFilesGracefully()
    {
        // Arrange
        var file = CreateFileWithMetadata("photo.jpg");
        var validFile = CreateMockFile("photo.xmp");
        var nullFile = Substitute.For<IFile>();
        nullFile.File.Returns((FileInfo?)null!);
        
        var allFiles = new List<IFile> { validFile, nullFile };

        // Act & Assert - should not throw
        file.AddRelatedFiles(allFiles, RelatedFileLookup.Strict);
        file.RelatedFiles.Should().Contain(validFile);
    }

    [Test]
    public void GetRelatedFiles_IsCaseInsensitive()
    {
        // Arrange
        var file = CreateFileWithMetadata("Photo.JPG");
        var relatedLower = CreateMockFile("photo.xmp");
        var relatedUpper = CreateMockFile("PHOTO.JSON");
        
        var allFiles = new List<IFile> { relatedLower, relatedUpper };

        // Act
        file.AddRelatedFiles(allFiles, RelatedFileLookup.Strict);

        // Assert
        file.RelatedFiles.Should().HaveCount(2);
    }

    #endregion

    #region TransformPath / GetRelatedPath Tests

    [Test]
    public void TransformPath_ChangesExtensionCorrectly()
    {
        // Arrange
        var file = CreateFileWithMetadata("photo.jpg");
        var relatedFile = CreateMockFile("photo.xmp");
        var destinationPath = @"C:\Destination\2023\01\photo.jpg";

        // Act
        var result = file.GetRelatedPath(destinationPath, relatedFile);

        // Assert
        result.Should().EndWith(".xmp");
        result.Should().Contain("photo");
    }

    [Test]
    public void TransformPath_PreservesDirectoryStructure()
    {
        // Arrange
        var file = CreateFileWithMetadata("vacation.jpg");
        var relatedFile = CreateMockFile("vacation.xmp");
        var destinationPath = Path.Combine("C:", "Photos", "2023", "Summer", "vacation.jpg");

        // Act
        var result = file.GetRelatedPath(destinationPath, relatedFile);

        // Assert
        var expectedDir = Path.Combine("C:", "Photos", "2023", "Summer");
        result.Should().StartWith(expectedDir);
        result.Should().EndWith(".xmp");
    }

    [Test]
    public void TransformPath_HandlesDoubleExtensionPattern()
    {
        // Arrange
        var file = CreateFileWithMetadata("photo.jpg");
        var relatedFile = CreateMockFile("photo.jpg.xmp");
        var destinationPath = @"C:\Destination\renamed_photo.jpg";

        // Act
        var result = file.GetRelatedPath(destinationPath, relatedFile);

        // Assert
        result.Should().EndWith(".xmp");
        result.Should().Contain("renamed_photo");
    }

    [Test]
    public void TransformPath_HandlesUnderscoreSuffix()
    {
        // Arrange
        var file = CreateFileWithMetadata("photo.jpg");
        var relatedFile = CreateMockFile("photo_edit.jpg");
        var destinationPath = @"C:\Destination\renamed.jpg";

        // Act
        var result = file.GetRelatedPath(destinationPath, relatedFile);

        // Assert
        result.Should().Contain("renamed_edit");
    }

    #endregion

    #region CopyTo Tests

    [Test]
    public void CopyTo_InDryRunMode_DoesNotCopy()
    {
        // Arrange
        var tempFilePath = CreateTempFile("source.jpg");
        var sourceFile = new FileInfo(tempFilePath);
        var fileDateTime = new FileDateTime(DateTime.Now, DateTimeSource.FileCreation);
        var file = new FileWithMetadata(sourceFile, fileDateTime, _mockLogger);
        
        var destinationPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "dest.jpg");

        try
        {
            // Act
            file.CopyTo(destinationPath, isDryRun: true);

            // Assert
            File.Exists(destinationPath).Should().BeFalse();
        }
        finally
        {
            CleanupFile(tempFilePath);
            CleanupFile(destinationPath);
        }
    }

    [Test]
    public void CopyTo_WhenNotDryRun_CopiesFile()
    {
        // Arrange
        var tempFilePath = CreateTempFile("source.jpg", "original content");
        var sourceFile = new FileInfo(tempFilePath);
        var fileDateTime = new FileDateTime(DateTime.Now, DateTimeSource.FileCreation);
        var file = new FileWithMetadata(sourceFile, fileDateTime, _mockLogger);
        
        var destDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var destinationPath = Path.Combine(destDir, "dest.jpg");

        try
        {
            // Act
            file.CopyTo(destinationPath, isDryRun: false);

            // Assert
            File.Exists(destinationPath).Should().BeTrue();
            File.ReadAllText(destinationPath).Should().Be("original content");
        }
        finally
        {
            CleanupFile(tempFilePath);
            CleanupFile(destinationPath);
        }
    }

    [Test]
    public void CopyTo_WithOverwrite_OverwritesExisting()
    {
        // Arrange
        var tempFilePath = CreateTempFile("source.jpg", "new content");
        var sourceFile = new FileInfo(tempFilePath);
        var fileDateTime = new FileDateTime(DateTime.Now, DateTimeSource.FileCreation);
        var file = new FileWithMetadata(sourceFile, fileDateTime, _mockLogger);
        
        var destDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(destDir);
        var destinationPath = Path.Combine(destDir, "dest.jpg");
        File.WriteAllText(destinationPath, "old content");

        try
        {
            // Act
            file.CopyTo(destinationPath, isDryRun: false);

            // Assert
            File.Exists(destinationPath).Should().BeTrue();
            File.ReadAllText(destinationPath).Should().Be("new content");
        }
        finally
        {
            CleanupFile(tempFilePath);
            CleanupFile(destinationPath);
        }
    }

    [Test]
    public void CopyTo_CreatesDestinationDirectory()
    {
        // Arrange
        var tempFilePath = CreateTempFile("source.jpg");
        var sourceFile = new FileInfo(tempFilePath);
        var fileDateTime = new FileDateTime(DateTime.Now, DateTimeSource.FileCreation);
        var file = new FileWithMetadata(sourceFile, fileDateTime, _mockLogger);
        
        var destDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "nested", "directory");
        var destinationPath = Path.Combine(destDir, "dest.jpg");

        try
        {
            // Act
            file.CopyTo(destinationPath, isDryRun: false);

            // Assert
            Directory.Exists(destDir).Should().BeTrue();
            File.Exists(destinationPath).Should().BeTrue();
        }
        finally
        {
            CleanupFile(tempFilePath);
            CleanupFile(destinationPath);
        }
    }

    [Test]
    public void CopyTo_LogsOperation()
    {
        // Arrange
        var tempFilePath = CreateTempFile("source.jpg");
        var sourceFile = new FileInfo(tempFilePath);
        var fileDateTime = new FileDateTime(DateTime.Now, DateTimeSource.FileCreation);
        var file = new FileWithMetadata(sourceFile, fileDateTime, _mockLogger);
        
        var destinationPath = @"C:\Destination\dest.jpg";

        try
        {
            // Act
            file.CopyTo(destinationPath, isDryRun: true);

            // Assert
            _mockLogger.Received().Log(
                LogLevel.Information,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString()!.Contains("Copying")),
                Arg.Any<Exception>(),
                Arg.Any<Func<object, Exception?, string>>());
        }
        finally
        {
            CleanupFile(tempFilePath);
        }
    }

    #endregion

    #region MoveTo Tests

    [Test]
    public void MoveTo_InDryRunMode_DoesNotMove()
    {
        // Arrange
        var tempFilePath = CreateTempFile("source.jpg");
        var sourceFile = new FileInfo(tempFilePath);
        var fileDateTime = new FileDateTime(DateTime.Now, DateTimeSource.FileCreation);
        var file = new FileWithMetadata(sourceFile, fileDateTime, _mockLogger);
        
        var destinationPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "dest.jpg");

        try
        {
            // Act
            file.MoveTo(destinationPath, isDryRun: true);

            // Assert
            File.Exists(tempFilePath).Should().BeTrue();
            File.Exists(destinationPath).Should().BeFalse();
        }
        finally
        {
            CleanupFile(tempFilePath);
        }
    }

    [Test]
    public void MoveTo_WhenNotDryRun_MovesFile()
    {
        // Arrange
        var tempFilePath = CreateTempFile("source.jpg", "file content");
        var sourceFile = new FileInfo(tempFilePath);
        var fileDateTime = new FileDateTime(DateTime.Now, DateTimeSource.FileCreation);
        var file = new FileWithMetadata(sourceFile, fileDateTime, _mockLogger);
        
        var destDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var destinationPath = Path.Combine(destDir, "dest.jpg");

        try
        {
            // Act
            file.MoveTo(destinationPath, isDryRun: false);

            // Assert
            File.Exists(tempFilePath).Should().BeFalse();
            File.Exists(destinationPath).Should().BeTrue();
            File.ReadAllText(destinationPath).Should().Be("file content");
        }
        finally
        {
            CleanupFile(destinationPath);
        }
    }

    [Test]
    public void MoveTo_WithOverwrite_OverwritesExisting()
    {
        // Arrange
        var tempFilePath = CreateTempFile("source.jpg", "new content");
        var sourceFile = new FileInfo(tempFilePath);
        var fileDateTime = new FileDateTime(DateTime.Now, DateTimeSource.FileCreation);
        var file = new FileWithMetadata(sourceFile, fileDateTime, _mockLogger);
        
        var destDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(destDir);
        var destinationPath = Path.Combine(destDir, "dest.jpg");
        File.WriteAllText(destinationPath, "old content");

        try
        {
            // Act
            file.MoveTo(destinationPath, isDryRun: false);

            // Assert
            File.Exists(tempFilePath).Should().BeFalse();
            File.Exists(destinationPath).Should().BeTrue();
            File.ReadAllText(destinationPath).Should().Be("new content");
        }
        finally
        {
            CleanupFile(destinationPath);
        }
    }

    [Test]
    public void MoveTo_CreatesDestinationDirectory()
    {
        // Arrange
        var tempFilePath = CreateTempFile("source.jpg");
        var sourceFile = new FileInfo(tempFilePath);
        var fileDateTime = new FileDateTime(DateTime.Now, DateTimeSource.FileCreation);
        var file = new FileWithMetadata(sourceFile, fileDateTime, _mockLogger);
        
        var destDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "nested", "directory");
        var destinationPath = Path.Combine(destDir, "dest.jpg");

        try
        {
            // Act
            file.MoveTo(destinationPath, isDryRun: false);

            // Assert
            Directory.Exists(destDir).Should().BeTrue();
            File.Exists(destinationPath).Should().BeTrue();
        }
        finally
        {
            CleanupFile(destinationPath);
        }
    }

    [Test]
    public void MoveTo_LogsOperation()
    {
        // Arrange
        var tempFilePath = CreateTempFile("source.jpg");
        var sourceFile = new FileInfo(tempFilePath);
        var fileDateTime = new FileDateTime(DateTime.Now, DateTimeSource.FileCreation);
        var file = new FileWithMetadata(sourceFile, fileDateTime, _mockLogger);
        
        var destinationPath = @"C:\Destination\dest.jpg";

        try
        {
            // Act
            file.MoveTo(destinationPath, isDryRun: true);

            // Assert
            _mockLogger.Received().Log(
                LogLevel.Information,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString()!.Contains("Moving")),
                Arg.Any<Exception>(),
                Arg.Any<Func<object, Exception?, string>>());
        }
        finally
        {
            CleanupFile(tempFilePath);
        }
    }

    #endregion

    #region Properties Tests

    [Test]
    public void Properties_ReturnCorrectValues()
    {
        // Arrange
        var fileName = "test_photo.jpg";
        var dateTime = new DateTime(2023, 6, 15, 14, 30, 0);
        var location = new LocationData("New York", null, "NY", "USA");
        
        var file = CreateFileWithMetadata(fileName, dateTime);
        file.Location = location;
        file.SetChecksum("abc123def456");

        // Assert
        file.File.Name.Should().Be(fileName);
        file.FileDateTime.DateTime.Should().Be(dateTime);
        file.Location.Should().Be(location);
        file.Location!.City.Should().Be("New York");
        file.Location.State.Should().Be("NY");
        file.Location.Country.Should().Be("USA");
        file.Checksum.Should().Be("abc123def456");
    }

    [Test]
    public void File_Property_ReturnsFileInfo()
    {
        // Arrange
        var fileName = "image.jpg";
        var file = CreateFileWithMetadata(fileName);

        // Assert
        file.File.Should().NotBeNull();
        file.File.Name.Should().Be(fileName);
    }

    [Test]
    public void FileDateTime_Property_ReturnsCorrectDateTime()
    {
        // Arrange
        var expectedDate = new DateTime(2022, 12, 25, 10, 0, 0);
        var file = CreateFileWithMetadata("christmas.jpg", expectedDate);

        // Assert
        file.FileDateTime.Should().NotBeNull();
        file.FileDateTime.DateTime.Should().Be(expectedDate);
        file.FileDateTime.Source.Should().Be(DateTimeSource.FileCreation);
    }

    [Test]
    public void Location_Property_IsNullByDefault()
    {
        // Arrange
        var file = CreateFileWithMetadata("photo.jpg");

        // Assert
        file.Location.Should().BeNull();
    }

    [Test]
    public void Location_Property_CanBeSet()
    {
        // Arrange
        var file = CreateFileWithMetadata("photo.jpg");
        var location = new LocationData("Paris", null, null, "France");

        // Act
        file.Location = location;

        // Assert
        file.Location.Should().NotBeNull();
        file.Location!.City.Should().Be("Paris");
        file.Location.Country.Should().Be("France");
    }

    [Test]
    public void Checksum_Property_IsEmptyByDefault()
    {
        // Arrange
        var file = CreateFileWithMetadata("photo.jpg");

        // Assert
        file.Checksum.Should().BeEmpty();
    }

    [Test]
    public void SetChecksum_SetsChecksumValue()
    {
        // Arrange
        var file = CreateFileWithMetadata("photo.jpg");
        var checksum = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        // Act
        file.SetChecksum(checksum);

        // Assert
        file.Checksum.Should().Be(checksum);
    }

    [Test]
    public void SetChecksum_WithEmptyString_DoesNotSetChecksum()
    {
        // Arrange
        var file = CreateFileWithMetadata("photo.jpg");
        file.SetChecksum("initial_checksum");

        // Act
        file.SetChecksum(string.Empty);

        // Assert
        file.Checksum.Should().Be("initial_checksum");
    }

    [Test]
    public void SetChecksum_WithWhitespace_DoesNotSetChecksum()
    {
        // Arrange
        var file = CreateFileWithMetadata("photo.jpg");
        file.SetChecksum("initial_checksum");

        // Act
        file.SetChecksum("   ");

        // Assert
        file.Checksum.Should().Be("initial_checksum");
    }

    [Test]
    public void SetChecksum_WithNull_DoesNotSetChecksum()
    {
        // Arrange
        var file = CreateFileWithMetadata("photo.jpg");
        file.SetChecksum("initial_checksum");

        // Act
        file.SetChecksum(null!);

        // Assert
        file.Checksum.Should().Be("initial_checksum");
    }

    [Test]
    public void RelatedFiles_IsEmptyByDefault()
    {
        // Arrange
        var file = CreateFileWithMetadata("photo.jpg");

        // Assert
        file.RelatedFiles.Should().BeEmpty();
        file.RelatedFiles.Should().NotBeNull();
    }

    [Test]
    public void RelatedFiles_IsReadOnly()
    {
        // Arrange
        var file = CreateFileWithMetadata("photo.jpg");

        // Assert
        file.RelatedFiles.Should().BeAssignableTo<IReadOnlyCollection<IFile>>();
    }

    #endregion

    #region Constructor Tests

    [Test]
    public void Constructor_InitializesPropertiesCorrectly()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), "test.jpg");
        var fileInfo = new FileInfo(tempPath);
        var fileDateTime = new FileDateTime(new DateTime(2023, 1, 1), DateTimeSource.ExifDateTimeOriginal);

        // Act
        var file = new FileWithMetadata(fileInfo, fileDateTime, _mockLogger);

        // Assert
        file.File.Should().Be(fileInfo);
        file.FileDateTime.Should().Be(fileDateTime);
        file.Location.Should().BeNull();
        file.Checksum.Should().BeEmpty();
        file.RelatedFiles.Should().BeEmpty();
    }

    #endregion
}
