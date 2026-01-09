using System;
using System.IO;
using AwesomeAssertions;
using NSubstitute;
using PhotoCopy.Configuration;
using PhotoCopy.Files;

namespace PhotoCopy.Tests.Files;

public class GenericFileTests : TestBase
{
    private readonly string _testDirectory;

    public GenericFileTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"GenericFileTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    #region Helper Methods

    private string CreateTempFile(string fileName, string content = "Test content for generic file")
    {
        var filePath = Path.Combine(_testDirectory, fileName);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(filePath, content);
        return filePath;
    }

    private GenericFile CreateGenericFile(string fileName, string content = "Test content for generic file", string? checksum = null)
    {
        var filePath = CreateTempFile(fileName, content);
        var fileInfo = new FileInfo(filePath);
        var fileDateTime = new FileDateTime(DateTime.Now, DateTimeSource.FileCreation);
        return new GenericFile(fileInfo, fileDateTime, checksum);
    }

    private void Cleanup()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #endregion

    #region Properties Tests

    [Test]
    public void Properties_ReturnCorrectValues()
    {
        // Arrange
        var fileName = "test_properties.txt";
        var content = "Properties test content";
        var filePath = CreateTempFile(fileName, content);
        var fileInfo = new FileInfo(filePath);
        var expectedDateTime = new DateTime(2024, 6, 15, 10, 30, 0);
        var fileDateTime = new FileDateTime(expectedDateTime, DateTimeSource.ExifDateTimeOriginal);
        var preCalculatedChecksum = "abc123def456";

        // Act
        var genericFile = new GenericFile(fileInfo, fileDateTime, preCalculatedChecksum);

        // Assert
        genericFile.File.Should().NotBeNull();
        genericFile.File.FullName.Should().Be(filePath);
        genericFile.FileDateTime.Should().NotBeNull();
        genericFile.FileDateTime.DateTime.Should().Be(expectedDateTime);
        genericFile.FileDateTime.Source.Should().Be(DateTimeSource.ExifDateTimeOriginal);
        genericFile.Location.Should().BeNull();

        Cleanup();
    }

    #endregion

    #region Checksum Tests

    [Test]
    public void Checksum_WithoutCalculation_ReturnsEmptyString()
    {
        // Arrange
        var content = "Hello, World!";
        var genericFile = CreateGenericFile("checksum_test.txt", content);

        // Act
        var checksum = genericFile.Checksum;

        // Assert - Checksum property should not trigger calculation (no side effects)
        checksum.Should().BeEmpty();

        Cleanup();
    }

    [Test]
    public void EnsureChecksum_CalculatesAndReturnsValue()
    {
        // Arrange
        var content = "Hello, World!";
        var genericFile = CreateGenericFile("checksum_test.txt", content);

        // Act - Explicit calculation
        var checksum = genericFile.EnsureChecksum();

        // Assert
        checksum.Should().NotBeNullOrEmpty();
        checksum.Should().HaveLength(64); // SHA256 produces 64 hex characters
        checksum.Should().MatchRegex("^[a-f0-9]{64}$"); // Should be lowercase hex
        // Property should now return the cached value
        genericFile.Checksum.Should().Be(checksum);

        Cleanup();
    }

    [Test]
    public void Checksum_WithPreCalculatedValue_ReturnsStoredValue()
    {
        // Arrange
        var preCalculatedChecksum = "precalculated123456789abcdef";
        var filePath = CreateTempFile("pre_checksum.txt", "Some content");
        var fileInfo = new FileInfo(filePath);
        var fileDateTime = new FileDateTime(DateTime.Now, DateTimeSource.FileCreation);
        var genericFile = new GenericFile(fileInfo, fileDateTime, preCalculatedChecksum);

        // Act
        var checksum = genericFile.Checksum;

        // Assert
        checksum.Should().Be(preCalculatedChecksum);

        Cleanup();
    }

    [Test]
    public void EnsureChecksum_CalledMultipleTimes_ReturnsSameValue()
    {
        // Arrange
        var genericFile = CreateGenericFile("multi_checksum.txt", "Consistent content");

        // Act
        var checksum1 = genericFile.EnsureChecksum();
        var checksum2 = genericFile.EnsureChecksum();
        var checksum3 = genericFile.Checksum; // Should return cached value

        // Assert
        checksum1.Should().Be(checksum2);
        checksum2.Should().Be(checksum3);
        checksum1.Should().NotBeEmpty();

        Cleanup();
    }

    [Test]
    public void EnsureChecksum_WithSameContent_ProducesSameHash()
    {
        // Arrange
        var content = "Identical content for both files";
        var genericFile1 = CreateGenericFile("identical1.txt", content);
        var genericFile2 = CreateGenericFile("identical2.txt", content);

        // Act - Explicitly calculate checksums
        var checksum1 = genericFile1.EnsureChecksum();
        var checksum2 = genericFile2.EnsureChecksum();

        // Assert
        checksum1.Should().Be(checksum2);

        Cleanup();
    }

    [Test]
    public void CalculateChecksum_AlwaysRecalculates()
    {
        // Arrange
        var genericFile = CreateGenericFile("recalc_test.txt", "Some content");

        // Act
        var checksum1 = genericFile.CalculateChecksum();
        var checksum2 = genericFile.CalculateChecksum();

        // Assert - Both calls should produce the same result
        checksum1.Should().Be(checksum2);
        checksum1.Should().NotBeEmpty();
        genericFile.Checksum.Should().Be(checksum1);

        Cleanup();
    }

    #endregion

    #region File Property Tests

    [Test]
    public void File_ReturnsFileInfo()
    {
        // Arrange
        var fileName = "file_info_test.txt";
        var filePath = CreateTempFile(fileName, "File info content");
        var fileInfo = new FileInfo(filePath);
        var fileDateTime = new FileDateTime(DateTime.Now, DateTimeSource.FileCreation);
        var genericFile = new GenericFile(fileInfo, fileDateTime);

        // Act
        var returnedFileInfo = genericFile.File;

        // Assert
        returnedFileInfo.Should().NotBeNull();
        returnedFileInfo.Should().BeSameAs(fileInfo);
        returnedFileInfo.Name.Should().Be(fileName);
        returnedFileInfo.Exists.Should().BeTrue();

        Cleanup();
    }

    #endregion

    #region CopyTo Tests

    [Test]
    public void CopyTo_CopiesFile()
    {
        // Arrange
        var content = "Content to be copied";
        var genericFile = CreateGenericFile("source_file.txt", content);
        var destPath = Path.Combine(_testDirectory, "copied_file.txt");

        // Act
        genericFile.File.CopyTo(destPath);

        // Assert
        File.Exists(destPath).Should().BeTrue();
        File.ReadAllText(destPath).Should().Be(content);

        Cleanup();
    }

    [Test]
    public void CopyTo_WithOverwrite_ReplacesExistingFile()
    {
        // Arrange
        var originalContent = "Original content";
        var newContent = "New content to copy";
        var genericFile = CreateGenericFile("source_overwrite.txt", newContent);
        var destPath = Path.Combine(_testDirectory, "existing_file.txt");
        File.WriteAllText(destPath, originalContent);

        // Act
        genericFile.File.CopyTo(destPath, overwrite: true);

        // Assert
        File.Exists(destPath).Should().BeTrue();
        File.ReadAllText(destPath).Should().Be(newContent);

        Cleanup();
    }

    #endregion

    #region MoveTo Tests

    [Test]
    public void MoveTo_MovesFile()
    {
        // Arrange
        var content = "Content to be moved";
        var sourcePath = CreateTempFile("move_source.txt", content);
        var fileInfo = new FileInfo(sourcePath);
        var fileDateTime = new FileDateTime(DateTime.Now, DateTimeSource.FileCreation);
        var genericFile = new GenericFile(fileInfo, fileDateTime);
        var destPath = Path.Combine(_testDirectory, "moved_file.txt");

        // Act
        genericFile.File.MoveTo(destPath);

        // Assert
        File.Exists(sourcePath).Should().BeFalse();
        File.Exists(destPath).Should().BeTrue();
        File.ReadAllText(destPath).Should().Be(content);

        Cleanup();
    }

    [Test]
    public void MoveTo_WithOverwrite_ReplacesExistingFile()
    {
        // Arrange
        var originalContent = "Original content";
        var newContent = "New content to move";
        var sourcePath = CreateTempFile("move_overwrite_source.txt", newContent);
        var fileInfo = new FileInfo(sourcePath);
        var fileDateTime = new FileDateTime(DateTime.Now, DateTimeSource.FileCreation);
        var genericFile = new GenericFile(fileInfo, fileDateTime);
        var destPath = Path.Combine(_testDirectory, "move_existing.txt");
        File.WriteAllText(destPath, originalContent);

        // Act
        genericFile.File.MoveTo(destPath, overwrite: true);

        // Assert
        File.Exists(sourcePath).Should().BeFalse();
        File.Exists(destPath).Should().BeTrue();
        File.ReadAllText(destPath).Should().Be(newContent);

        Cleanup();
    }

    #endregion
}
