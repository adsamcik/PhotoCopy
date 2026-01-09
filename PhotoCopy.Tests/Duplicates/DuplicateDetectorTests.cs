using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PhotoCopy.Duplicates;
using PhotoCopy.Files;

namespace PhotoCopy.Tests.Duplicates;

public class DuplicateDetectorTests
{
    private readonly ILogger<DuplicateDetector> _logger;
    private readonly DuplicateDetector _detector;

    public DuplicateDetectorTests()
    {
        _logger = Substitute.For<ILogger<DuplicateDetector>>();
        _detector = new DuplicateDetector(_logger);
    }

    #region FindDuplicateOf Tests

    [Test]
    public void IsDuplicate_WithNewFile_ReturnsFalse()
    {
        // Arrange
        var file = CreateMockFileWithChecksum("photo.jpg", "checksum123");
        
        // Act
        var result = _detector.FindDuplicateOf(file);

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void IsDuplicate_WithExactDuplicate_ReturnsTrue()
    {
        // Arrange
        var originalFile = CreateMockFileWithChecksum("original.jpg", "checksum123", @"C:\Photos\original.jpg");
        var duplicateFile = CreateMockFileWithChecksum("original.jpg", "checksum123", @"C:\Backup\original.jpg");
        
        _detector.RegisterFile(originalFile);

        // Act
        var result = _detector.FindDuplicateOf(duplicateFile);

        // Assert
        result.Should().NotBeNull();
        result!.File.FullName.Should().Be(@"C:\Photos\original.jpg");
    }

    [Test]
    public void IsDuplicate_WithSameChecksumDifferentName_ReturnsTrue()
    {
        // Arrange
        var originalFile = CreateMockFileWithChecksum("original.jpg", "checksum123", @"C:\Photos\original.jpg");
        var duplicateFile = CreateMockFileWithChecksum("renamed.jpg", "checksum123", @"C:\Backup\renamed.jpg");
        
        _detector.RegisterFile(originalFile);

        // Act
        var result = _detector.FindDuplicateOf(duplicateFile);

        // Assert
        result.Should().NotBeNull();
        result!.File.FullName.Should().Be(@"C:\Photos\original.jpg");
    }

    [Test]
    public void IsDuplicate_WithDifferentChecksum_ReturnsFalse()
    {
        // Arrange
        var originalFile = CreateMockFileWithChecksum("original.jpg", "checksum123", @"C:\Photos\original.jpg");
        var differentFile = CreateMockFileWithChecksum("different.jpg", "checksum456", @"C:\Photos\different.jpg");
        
        _detector.RegisterFile(originalFile);

        // Act
        var result = _detector.FindDuplicateOf(differentFile);

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void IsDuplicate_WithSameFilePath_ReturnsFalse()
    {
        // Arrange - same file should not be considered a duplicate of itself
        var file = CreateMockFileWithChecksum("photo.jpg", "checksum123", @"C:\Photos\photo.jpg");
        
        _detector.RegisterFile(file);

        // Act
        var result = _detector.FindDuplicateOf(file);

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void IsDuplicate_WithNullChecksum_ReturnsFalse()
    {
        // Arrange
        var fileWithChecksum = CreateMockFileWithChecksum("photo.jpg", "checksum123", @"C:\Photos\photo.jpg");
        var fileWithoutChecksum = CreateMockFileWithChecksum("nocheck.jpg", null!, @"C:\Photos\nocheck.jpg");
        
        _detector.RegisterFile(fileWithChecksum);

        // Act
        var result = _detector.FindDuplicateOf(fileWithoutChecksum);

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void IsDuplicate_WithEmptyChecksum_ReturnsFalse()
    {
        // Arrange
        var fileWithChecksum = CreateMockFileWithChecksum("photo.jpg", "checksum123", @"C:\Photos\photo.jpg");
        var fileWithEmptyChecksum = CreateMockFileWithChecksum("empty.jpg", string.Empty, @"C:\Photos\empty.jpg");
        
        _detector.RegisterFile(fileWithChecksum);

        // Act
        var result = _detector.FindDuplicateOf(fileWithEmptyChecksum);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region RegisterFile Tests

    [Test]
    public void AddToIndex_AddsFileToIndex()
    {
        // Arrange
        var file = CreateMockFileWithChecksum("photo.jpg", "checksum123", @"C:\Photos\photo.jpg");
        var duplicateFile = CreateMockFileWithChecksum("duplicate.jpg", "checksum123", @"C:\Other\duplicate.jpg");

        // Act
        _detector.RegisterFile(file);
        var result = _detector.FindDuplicateOf(duplicateFile);

        // Assert
        result.Should().NotBeNull();
        result!.File.FullName.Should().Be(@"C:\Photos\photo.jpg");
    }

    [Test]
    public void AddToIndex_WithExistingFile_DoesNotUpdateIndex()
    {
        // Arrange - TryAdd semantics means first one wins
        var firstFile = CreateMockFileWithChecksum("first.jpg", "checksum123", @"C:\Photos\first.jpg");
        var secondFile = CreateMockFileWithChecksum("second.jpg", "checksum123", @"C:\Photos\second.jpg");
        var testFile = CreateMockFileWithChecksum("test.jpg", "checksum123", @"C:\Other\test.jpg");

        // Act
        _detector.RegisterFile(firstFile);
        _detector.RegisterFile(secondFile); // Should not overwrite first

        var result = _detector.FindDuplicateOf(testFile);

        // Assert
        result.Should().NotBeNull();
        result!.File.FullName.Should().Be(@"C:\Photos\first.jpg");
    }

    [Test]
    public void AddToIndex_WithNullChecksum_DoesNotAddToIndex()
    {
        // Arrange
        var fileWithNullChecksum = CreateMockFileWithChecksum("photo.jpg", null!, @"C:\Photos\photo.jpg");
        var testFile = CreateMockFileWithChecksum("test.jpg", "somechecksum", @"C:\Other\test.jpg");

        // Act
        _detector.RegisterFile(fileWithNullChecksum);
        var result = _detector.FindDuplicateOf(testFile);

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void AddToIndex_WithEmptyChecksum_DoesNotAddToIndex()
    {
        // Arrange
        var fileWithEmptyChecksum = CreateMockFileWithChecksum("photo.jpg", string.Empty, @"C:\Photos\photo.jpg");
        var testFile = CreateMockFileWithChecksum("test.jpg", string.Empty, @"C:\Other\test.jpg");

        // Act
        _detector.RegisterFile(fileWithEmptyChecksum);
        var result = _detector.FindDuplicateOf(testFile);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Clear Tests

    [Test]
    public void ClearIndex_RemovesAllEntries()
    {
        // Arrange
        var file1 = CreateMockFileWithChecksum("photo1.jpg", "checksum1", @"C:\Photos\photo1.jpg");
        var file2 = CreateMockFileWithChecksum("photo2.jpg", "checksum2", @"C:\Photos\photo2.jpg");
        var file3 = CreateMockFileWithChecksum("photo3.jpg", "checksum3", @"C:\Photos\photo3.jpg");
        
        _detector.RegisterFile(file1);
        _detector.RegisterFile(file2);
        _detector.RegisterFile(file3);

        // Act
        _detector.Clear();

        // Assert - all files should now not find duplicates
        var testFile1 = CreateMockFileWithChecksum("test1.jpg", "checksum1", @"C:\Other\test1.jpg");
        var testFile2 = CreateMockFileWithChecksum("test2.jpg", "checksum2", @"C:\Other\test2.jpg");
        var testFile3 = CreateMockFileWithChecksum("test3.jpg", "checksum3", @"C:\Other\test3.jpg");
        
        _detector.FindDuplicateOf(testFile1).Should().BeNull();
        _detector.FindDuplicateOf(testFile2).Should().BeNull();
        _detector.FindDuplicateOf(testFile3).Should().BeNull();
    }

    [Test]
    public void ClearIndex_AllowsReregistration()
    {
        // Arrange
        var originalFile = CreateMockFileWithChecksum("original.jpg", "checksum123", @"C:\Photos\original.jpg");
        var newFile = CreateMockFileWithChecksum("new.jpg", "checksum123", @"C:\New\new.jpg");
        
        _detector.RegisterFile(originalFile);
        _detector.Clear();

        // Act
        _detector.RegisterFile(newFile);
        
        var testFile = CreateMockFileWithChecksum("test.jpg", "checksum123", @"C:\Other\test.jpg");
        var result = _detector.FindDuplicateOf(testFile);

        // Assert
        result.Should().NotBeNull();
        result!.File.FullName.Should().Be(@"C:\New\new.jpg");
    }

    #endregion

    #region ScanForDuplicatesAsync Tests

    [Test]
    public async Task FindDuplicates_WithNoDuplicates_ReturnsEmpty()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFileWithChecksum("photo1.jpg", "checksum1"),
            CreateMockFileWithChecksum("photo2.jpg", "checksum2"),
            CreateMockFileWithChecksum("photo3.jpg", "checksum3")
        };

        // Act
        var result = await _detector.ScanForDuplicatesAsync(files);

        // Assert
        result.DuplicateGroups.Should().BeEmpty();
        result.UniqueFiles.Should().HaveCount(3);
        result.TotalFilesScanned.Should().Be(3);
        result.DuplicateFilesFound.Should().Be(0);
    }

    [Test]
    public async Task FindDuplicates_WithDuplicates_ReturnsDuplicateGroups()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFileWithChecksum("photo1.jpg", "checksum1"),
            CreateMockFileWithChecksum("photo2.jpg", "checksum2"),
            CreateMockFileWithChecksum("photo3.jpg", "checksum1"), // Duplicate of photo1
            CreateMockFileWithChecksum("photo4.jpg", "checksum3"),
            CreateMockFileWithChecksum("photo5.jpg", "checksum2")  // Duplicate of photo2
        };

        // Act
        var result = await _detector.ScanForDuplicatesAsync(files);

        // Assert
        result.DuplicateGroups.Should().HaveCount(2);
        result.TotalFilesScanned.Should().Be(5);
        result.DuplicateFilesFound.Should().Be(2);
    }

    [Test]
    public async Task FindDuplicates_GroupsByChecksum()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFileWithChecksum("photo1.jpg", "checksumA"),
            CreateMockFileWithChecksum("photo2.jpg", "checksumA"), // Duplicate
            CreateMockFileWithChecksum("photo3.jpg", "checksumA"), // Duplicate
            CreateMockFileWithChecksum("photo4.jpg", "checksumB"),
            CreateMockFileWithChecksum("photo5.jpg", "checksumB")  // Duplicate
        };

        // Act
        var result = await _detector.ScanForDuplicatesAsync(files);

        // Assert
        result.DuplicateGroups.Should().HaveCount(2);
        
        var groupA = result.DuplicateGroups.First(g => g.Checksum == "checksumA");
        groupA.Files.Should().HaveCount(3);
        
        var groupB = result.DuplicateGroups.First(g => g.Checksum == "checksumB");
        groupB.Files.Should().HaveCount(2);
    }

    [Test]
    public async Task FindDuplicates_WithEmptyList_ReturnsEmptyResult()
    {
        // Arrange
        var files = new List<IFile>();

        // Act
        var result = await _detector.ScanForDuplicatesAsync(files);

        // Assert
        result.DuplicateGroups.Should().BeEmpty();
        result.UniqueFiles.Should().BeEmpty();
        result.TotalFilesScanned.Should().Be(0);
        result.DuplicateFilesFound.Should().Be(0);
    }

    [Test]
    public async Task FindDuplicates_WithNullChecksums_SkipsThoseFiles()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFileWithChecksum("photo1.jpg", "checksum1"),
            CreateMockFileWithChecksum("photo2.jpg", null!),
            CreateMockFileWithChecksum("photo3.jpg", "checksum1") // Duplicate of photo1
        };

        // Act
        var result = await _detector.ScanForDuplicatesAsync(files);

        // Assert
        result.DuplicateGroups.Should().HaveCount(1);
        result.UniqueFiles.Should().HaveCount(1);
        result.TotalFilesScanned.Should().Be(3);
        result.DuplicateFilesFound.Should().Be(1);
    }

    [Test]
    public async Task FindDuplicates_WithEmptyChecksums_SkipsThoseFiles()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFileWithChecksum("photo1.jpg", "checksum1"),
            CreateMockFileWithChecksum("photo2.jpg", string.Empty),
            CreateMockFileWithChecksum("photo3.jpg", "checksum2")
        };

        // Act
        var result = await _detector.ScanForDuplicatesAsync(files);

        // Assert
        result.DuplicateGroups.Should().BeEmpty();
        result.UniqueFiles.Should().HaveCount(2);
        result.TotalFilesScanned.Should().Be(3);
    }

    [Test]
    public async Task FindDuplicates_WithCancellation_ThrowsOperationCancelledException()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFileWithChecksum("photo1.jpg", "checksum1"),
            CreateMockFileWithChecksum("photo2.jpg", "checksum2")
        };
        
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _detector.ScanForDuplicatesAsync(files, cts.Token));
    }

    [Test]
    public async Task FindDuplicates_RegistersUniqueFilesToIndex()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFileWithChecksum("photo1.jpg", "checksum1", @"C:\Photos\photo1.jpg"),
            CreateMockFileWithChecksum("photo2.jpg", "checksum2", @"C:\Photos\photo2.jpg")
        };

        // Act
        await _detector.ScanForDuplicatesAsync(files);

        // Assert - files should be registered and detectable
        var testFile = CreateMockFileWithChecksum("test.jpg", "checksum1", @"C:\Other\test.jpg");
        var duplicate = _detector.FindDuplicateOf(testFile);
        
        duplicate.Should().NotBeNull();
        duplicate!.File.FullName.Should().Be(@"C:\Photos\photo1.jpg");
    }

    [Test]
    public async Task FindDuplicates_CaseInsensitiveChecksum()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFileWithChecksum("photo1.jpg", "ABCDEF123"),
            CreateMockFileWithChecksum("photo2.jpg", "abcdef123") // Same checksum, different case
        };

        // Act
        var result = await _detector.ScanForDuplicatesAsync(files);

        // Assert - should be treated as duplicates (case insensitive)
        result.DuplicateGroups.Should().HaveCount(1);
        result.DuplicateFilesFound.Should().Be(1);
    }

    [Test]
    public async Task FindDuplicates_ReturnsDuplicateGroupWithCorrectFiles()
    {
        // Arrange
        var file1 = CreateMockFileWithChecksum("original.jpg", "sharedChecksum");
        var file2 = CreateMockFileWithChecksum("copy1.jpg", "sharedChecksum");
        var file3 = CreateMockFileWithChecksum("copy2.jpg", "sharedChecksum");
        var files = new List<IFile> { file1, file2, file3 };

        // Act
        var result = await _detector.ScanForDuplicatesAsync(files);

        // Assert
        result.DuplicateGroups.Should().HaveCount(1);
        
        var group = result.DuplicateGroups.First();
        group.Checksum.Should().Be("sharedChecksum");
        group.Files.Should().HaveCount(3);
        group.Files.Should().Contain(f => f.File.Name == "original.jpg");
        group.Files.Should().Contain(f => f.File.Name == "copy1.jpg");
        group.Files.Should().Contain(f => f.File.Name == "copy2.jpg");
    }

    [Test]
    public async Task FindDuplicates_UniqueFilesContainsFirstOccurrence()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFileWithChecksum("first.jpg", "checksum1"),
            CreateMockFileWithChecksum("second.jpg", "checksum1"),
            CreateMockFileWithChecksum("unique.jpg", "checksum2")
        };

        // Act
        var result = await _detector.ScanForDuplicatesAsync(files);

        // Assert
        result.UniqueFiles.Should().HaveCount(2);
        result.UniqueFiles["checksum1"].File.Name.Should().Be("first.jpg");
        result.UniqueFiles["checksum2"].File.Name.Should().Be("unique.jpg");
    }

    #endregion

    #region Helper Methods

    private static IFile CreateMockFileWithChecksum(string name, string checksum, string? fullPath = null)
    {
        var file = Substitute.For<IFile>();
        var path = fullPath ?? Path.Combine(Path.GetTempPath(), name);
        var fileInfo = new FileInfo(path);
        
        file.File.Returns(fileInfo);
        file.Checksum.Returns(checksum);
        file.FileDateTime.Returns(new FileDateTime(DateTime.Now, DateTimeSource.FileCreation));
        file.Location.Returns((LocationData?)null);
        
        return file;
    }

    #endregion
}
