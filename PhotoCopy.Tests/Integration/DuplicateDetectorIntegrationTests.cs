using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PhotoCopy.Duplicates;
using PhotoCopy.Files;
using PhotoCopy.Tests.TestingImplementation;

namespace PhotoCopy.Tests.Integration;

[Property("Category", "Integration")]
public class DuplicateDetectorIntegrationTests
{
    private readonly string _baseTestDirectory;
    private readonly ILogger<DuplicateDetector> _logger;
    private readonly Sha256ChecksumCalculator _checksumCalculator;

    public DuplicateDetectorIntegrationTests()
    {
        _baseTestDirectory = Path.Combine(Path.GetTempPath(), "DuplicateDetectorIntegrationTests");
        _logger = Substitute.For<ILogger<DuplicateDetector>>();
        _checksumCalculator = new Sha256ChecksumCalculator();

        // Create base test directory if it doesn't exist
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
                // Give Windows a moment to release any locks
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Directory.Delete(path, true);
            }
        }
        catch (IOException)
        {
            // If we can't delete now, don't fail the test
            // The directory will be cleaned up on the next test run
        }
    }

    private IFile CreateRealFile(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        var checksum = _checksumCalculator.Calculate(fileInfo);
        var fileDateTime = new FileDateTime(fileInfo.CreationTime, fileInfo.LastWriteTime, fileInfo.LastWriteTime);
        return new GenericFile(fileInfo, fileDateTime, checksum);
    }

    private DuplicateDetector CreateDetector()
    {
        return new DuplicateDetector(_logger);
    }

    #region Identical Files Tests

    [Test]
    public async Task ScanForDuplicatesAsync_WithIdenticalFiles_DetectsDuplicates()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var content = "This is identical content for duplicate detection test"u8.ToArray();
            var file1Path = Path.Combine(testDirectory, "file1.txt");
            var file2Path = Path.Combine(testDirectory, "file2.txt");
            
            File.WriteAllBytes(file1Path, content);
            File.WriteAllBytes(file2Path, content);

            var file1 = CreateRealFile(file1Path);
            var file2 = CreateRealFile(file2Path);

            var detector = CreateDetector();

            // Act
            var result = await detector.ScanForDuplicatesAsync(new[] { file1, file2 });

            // Assert
            await Assert.That(result.TotalFilesScanned).IsEqualTo(2);
            await Assert.That(result.DuplicateFilesFound).IsEqualTo(1);
            await Assert.That(result.DuplicateGroups.Count).IsEqualTo(1);
            await Assert.That(result.DuplicateGroups[0].Files.Count).IsEqualTo(2);
            await Assert.That(file1.Checksum).IsEqualTo(file2.Checksum);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task ScanForDuplicatesAsync_WithThreeIdenticalFiles_DetectsAllDuplicates()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var content = "Triple duplicate content"u8.ToArray();
            var file1Path = Path.Combine(testDirectory, "original.txt");
            var file2Path = Path.Combine(testDirectory, "copy1.txt");
            var file3Path = Path.Combine(testDirectory, "copy2.txt");

            File.WriteAllBytes(file1Path, content);
            File.WriteAllBytes(file2Path, content);
            File.WriteAllBytes(file3Path, content);

            var files = new[]
            {
                CreateRealFile(file1Path),
                CreateRealFile(file2Path),
                CreateRealFile(file3Path)
            };

            var detector = CreateDetector();

            // Act
            var result = await detector.ScanForDuplicatesAsync(files);

            // Assert
            await Assert.That(result.TotalFilesScanned).IsEqualTo(3);
            await Assert.That(result.DuplicateFilesFound).IsEqualTo(2);
            await Assert.That(result.DuplicateGroups.Count).IsEqualTo(1);
            await Assert.That(result.DuplicateGroups[0].Files.Count).IsEqualTo(3);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task ScanForDuplicatesAsync_WithIdenticalBinaryFiles_DetectsDuplicates()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange - Create binary files with valid JPEG content
            var binaryContent = TestSampleImages.JpegWithNoExif;
            var file1Path = Path.Combine(testDirectory, "image1.jpg");
            var file2Path = Path.Combine(testDirectory, "image2.jpg");

            File.WriteAllBytes(file1Path, binaryContent);
            File.WriteAllBytes(file2Path, binaryContent);

            var file1 = CreateRealFile(file1Path);
            var file2 = CreateRealFile(file2Path);

            var detector = CreateDetector();

            // Act
            var result = await detector.ScanForDuplicatesAsync(new[] { file1, file2 });

            // Assert
            await Assert.That(result.DuplicateFilesFound).IsEqualTo(1);
            await Assert.That(result.DuplicateGroups.Count).IsEqualTo(1);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region Different Files Tests

    [Test]
    public async Task ScanForDuplicatesAsync_WithDifferentFiles_NoDuplicatesDetected()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var file1Path = Path.Combine(testDirectory, "file1.txt");
            var file2Path = Path.Combine(testDirectory, "file2.txt");
            
            File.WriteAllText(file1Path, "Content A");
            File.WriteAllText(file2Path, "Content B");

            var file1 = CreateRealFile(file1Path);
            var file2 = CreateRealFile(file2Path);

            var detector = CreateDetector();

            // Act
            var result = await detector.ScanForDuplicatesAsync(new[] { file1, file2 });

            // Assert
            await Assert.That(result.TotalFilesScanned).IsEqualTo(2);
            await Assert.That(result.DuplicateFilesFound).IsEqualTo(0);
            await Assert.That(result.DuplicateGroups.Count).IsEqualTo(0);
            await Assert.That(result.UniqueFiles.Count).IsEqualTo(2);
            await Assert.That(file1.Checksum).IsNotEqualTo(file2.Checksum);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task ScanForDuplicatesAsync_WithDifferentFilesSameName_NoDuplicatesDetected()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange - Same filename in different subdirectories with different content
            var subDir1 = Path.Combine(testDirectory, "dir1");
            var subDir2 = Path.Combine(testDirectory, "dir2");
            Directory.CreateDirectory(subDir1);
            Directory.CreateDirectory(subDir2);

            var file1Path = Path.Combine(subDir1, "samename.txt");
            var file2Path = Path.Combine(subDir2, "samename.txt");

            File.WriteAllText(file1Path, "Different content in file 1");
            File.WriteAllText(file2Path, "Different content in file 2");

            var file1 = CreateRealFile(file1Path);
            var file2 = CreateRealFile(file2Path);

            var detector = CreateDetector();

            // Act
            var result = await detector.ScanForDuplicatesAsync(new[] { file1, file2 });

            // Assert
            await Assert.That(result.DuplicateFilesFound).IsEqualTo(0);
            await Assert.That(result.DuplicateGroups.Count).IsEqualTo(0);
            await Assert.That(file1.File.Name).IsEqualTo(file2.File.Name);
            await Assert.That(file1.Checksum).IsNotEqualTo(file2.Checksum);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task ScanForDuplicatesAsync_WithSlightlyDifferentContent_NoDuplicatesDetected()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange - Files that differ by just one character
            var file1Path = Path.Combine(testDirectory, "file1.txt");
            var file2Path = Path.Combine(testDirectory, "file2.txt");

            File.WriteAllText(file1Path, "ABCDEFGHIJ");
            File.WriteAllText(file2Path, "ABCDEFGHIK"); // Only last character differs

            var file1 = CreateRealFile(file1Path);
            var file2 = CreateRealFile(file2Path);

            var detector = CreateDetector();

            // Act
            var result = await detector.ScanForDuplicatesAsync(new[] { file1, file2 });

            // Assert
            await Assert.That(result.DuplicateFilesFound).IsEqualTo(0);
            await Assert.That(file1.Checksum).IsNotEqualTo(file2.Checksum);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region Same File Different Names Tests

    [Test]
    public async Task ScanForDuplicatesAsync_WithSameFileDifferentNames_DetectsDuplicates()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var content = "This content will be the same in files with different names"u8.ToArray();
            var originalPath = Path.Combine(testDirectory, "original_photo.jpg");
            var renamedPath = Path.Combine(testDirectory, "vacation_2024.jpg");

            File.WriteAllBytes(originalPath, content);
            File.WriteAllBytes(renamedPath, content);

            var original = CreateRealFile(originalPath);
            var renamed = CreateRealFile(renamedPath);

            var detector = CreateDetector();

            // Act
            var result = await detector.ScanForDuplicatesAsync(new[] { original, renamed });

            // Assert
            await Assert.That(result.DuplicateFilesFound).IsEqualTo(1);
            await Assert.That(result.DuplicateGroups.Count).IsEqualTo(1);
            await Assert.That(original.File.Name).IsNotEqualTo(renamed.File.Name);
            await Assert.That(original.Checksum).IsEqualTo(renamed.Checksum);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task ScanForDuplicatesAsync_WithSameFileDifferentExtensions_DetectsDuplicates()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange - Same content but different extensions
            var content = "Same binary content regardless of extension"u8.ToArray();
            var file1Path = Path.Combine(testDirectory, "file.txt");
            var file2Path = Path.Combine(testDirectory, "file.bak");
            var file3Path = Path.Combine(testDirectory, "file.dat");

            File.WriteAllBytes(file1Path, content);
            File.WriteAllBytes(file2Path, content);
            File.WriteAllBytes(file3Path, content);

            var files = new[]
            {
                CreateRealFile(file1Path),
                CreateRealFile(file2Path),
                CreateRealFile(file3Path)
            };

            var detector = CreateDetector();

            // Act
            var result = await detector.ScanForDuplicatesAsync(files);

            // Assert
            await Assert.That(result.DuplicateFilesFound).IsEqualTo(2);
            await Assert.That(result.DuplicateGroups.Count).IsEqualTo(1);
            await Assert.That(result.DuplicateGroups[0].Files.Count).IsEqualTo(3);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region Mixed Scenarios Tests

    [Test]
    public async Task ScanForDuplicatesAsync_WithMixedDuplicatesAndUniques_CorrectlyCategorizesAll()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var duplicateContent = "This is duplicate content"u8.ToArray();
            var unique1Content = "This is unique content 1"u8.ToArray();
            var unique2Content = "This is unique content 2"u8.ToArray();

            var dup1Path = Path.Combine(testDirectory, "dup1.txt");
            var dup2Path = Path.Combine(testDirectory, "dup2.txt");
            var unique1Path = Path.Combine(testDirectory, "unique1.txt");
            var unique2Path = Path.Combine(testDirectory, "unique2.txt");

            File.WriteAllBytes(dup1Path, duplicateContent);
            File.WriteAllBytes(dup2Path, duplicateContent);
            File.WriteAllBytes(unique1Path, unique1Content);
            File.WriteAllBytes(unique2Path, unique2Content);

            var files = new[]
            {
                CreateRealFile(dup1Path),
                CreateRealFile(dup2Path),
                CreateRealFile(unique1Path),
                CreateRealFile(unique2Path)
            };

            var detector = CreateDetector();

            // Act
            var result = await detector.ScanForDuplicatesAsync(files);

            // Assert
            await Assert.That(result.TotalFilesScanned).IsEqualTo(4);
            await Assert.That(result.DuplicateFilesFound).IsEqualTo(1);
            await Assert.That(result.DuplicateGroups.Count).IsEqualTo(1);
            await Assert.That(result.UniqueFiles.Count).IsEqualTo(3); // 2 unique + 1 first of duplicate pair
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task ScanForDuplicatesAsync_WithMultipleDuplicateGroups_DetectsAllGroups()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange - Two different sets of duplicates
            var groupAContent = "Group A content"u8.ToArray();
            var groupBContent = "Group B content"u8.ToArray();

            var a1Path = Path.Combine(testDirectory, "a1.txt");
            var a2Path = Path.Combine(testDirectory, "a2.txt");
            var b1Path = Path.Combine(testDirectory, "b1.txt");
            var b2Path = Path.Combine(testDirectory, "b2.txt");

            File.WriteAllBytes(a1Path, groupAContent);
            File.WriteAllBytes(a2Path, groupAContent);
            File.WriteAllBytes(b1Path, groupBContent);
            File.WriteAllBytes(b2Path, groupBContent);

            var files = new[]
            {
                CreateRealFile(a1Path),
                CreateRealFile(a2Path),
                CreateRealFile(b1Path),
                CreateRealFile(b2Path)
            };

            var detector = CreateDetector();

            // Act
            var result = await detector.ScanForDuplicatesAsync(files);

            // Assert
            await Assert.That(result.TotalFilesScanned).IsEqualTo(4);
            await Assert.That(result.DuplicateFilesFound).IsEqualTo(2);
            await Assert.That(result.DuplicateGroups.Count).IsEqualTo(2);
            
            foreach (var group in result.DuplicateGroups)
            {
                await Assert.That(group.Files.Count).IsEqualTo(2);
            }
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region Checksum Verification Tests

    [Test]
    public async Task Checksum_VerifyConsistentChecksumForSameContent()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var content = "Test content for checksum verification"u8.ToArray();
            var file1Path = Path.Combine(testDirectory, "file1.txt");
            var file2Path = Path.Combine(testDirectory, "file2.txt");

            File.WriteAllBytes(file1Path, content);
            File.WriteAllBytes(file2Path, content);

            // Act
            var checksum1 = _checksumCalculator.Calculate(new FileInfo(file1Path));
            var checksum2 = _checksumCalculator.Calculate(new FileInfo(file2Path));

            // Assert
            await Assert.That(checksum1).IsEqualTo(checksum2);
            await Assert.That(checksum1).IsNotEmpty();
            await Assert.That(checksum1.Length).IsEqualTo(64); // SHA-256 produces 64 hex characters
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task Checksum_VerifyDifferentChecksumForDifferentContent()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var file1Path = Path.Combine(testDirectory, "file1.txt");
            var file2Path = Path.Combine(testDirectory, "file2.txt");

            File.WriteAllText(file1Path, "Content A");
            File.WriteAllText(file2Path, "Content B");

            // Act
            var checksum1 = _checksumCalculator.Calculate(new FileInfo(file1Path));
            var checksum2 = _checksumCalculator.Calculate(new FileInfo(file2Path));

            // Assert
            await Assert.That(checksum1).IsNotEqualTo(checksum2);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task Checksum_VerifyDeterministicForSameFile()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var filePath = Path.Combine(testDirectory, "test.txt");
            File.WriteAllText(filePath, "Consistent content");
            var fileInfo = new FileInfo(filePath);

            // Act - Calculate checksum multiple times
            var checksum1 = _checksumCalculator.Calculate(fileInfo);
            var checksum2 = _checksumCalculator.Calculate(fileInfo);
            var checksum3 = _checksumCalculator.Calculate(fileInfo);

            // Assert
            await Assert.That(checksum1).IsEqualTo(checksum2);
            await Assert.That(checksum2).IsEqualTo(checksum3);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region FindDuplicateOf Tests

    [Test]
    public async Task FindDuplicateOf_AfterScan_FindsExistingDuplicate()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var content = "Find duplicate test content"u8.ToArray();
            var file1Path = Path.Combine(testDirectory, "existing.txt");
            var file2Path = Path.Combine(testDirectory, "new.txt");

            File.WriteAllBytes(file1Path, content);
            File.WriteAllBytes(file2Path, content);

            var existingFile = CreateRealFile(file1Path);
            var newFile = CreateRealFile(file2Path);

            var detector = CreateDetector();
            
            // Scan the existing file first to populate the index
            await detector.ScanForDuplicatesAsync(new[] { existingFile });

            // Act
            var duplicate = detector.FindDuplicateOf(newFile);

            // Assert
            await Assert.That(duplicate).IsNotNull();
            await Assert.That(duplicate!.File.FullName).IsEqualTo(existingFile.File.FullName);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task FindDuplicateOf_WithNoDuplicate_ReturnsNull()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var file1Path = Path.Combine(testDirectory, "file1.txt");
            var file2Path = Path.Combine(testDirectory, "file2.txt");

            File.WriteAllText(file1Path, "Content A");
            File.WriteAllText(file2Path, "Content B");

            var file1 = CreateRealFile(file1Path);
            var file2 = CreateRealFile(file2Path);

            var detector = CreateDetector();
            await detector.ScanForDuplicatesAsync(new[] { file1 });

            // Act
            var duplicate = detector.FindDuplicateOf(file2);

            // Assert
            await Assert.That(duplicate).IsNull();
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region RegisterFile Tests

    [Test]
    public async Task RegisterFile_ThenFindDuplicate_WorksCorrectly()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var content = "Register file test content"u8.ToArray();
            var file1Path = Path.Combine(testDirectory, "registered.txt");
            var file2Path = Path.Combine(testDirectory, "toFind.txt");

            File.WriteAllBytes(file1Path, content);
            File.WriteAllBytes(file2Path, content);

            var registeredFile = CreateRealFile(file1Path);
            var toFindFile = CreateRealFile(file2Path);

            var detector = CreateDetector();

            // Act
            detector.RegisterFile(registeredFile);
            var duplicate = detector.FindDuplicateOf(toFindFile);

            // Assert
            await Assert.That(duplicate).IsNotNull();
            await Assert.That(duplicate!.File.FullName).IsEqualTo(registeredFile.File.FullName);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region Clear Tests

    [Test]
    public async Task Clear_AfterScan_RemovesIndex()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var content = "Clear test content"u8.ToArray();
            var file1Path = Path.Combine(testDirectory, "file1.txt");
            var file2Path = Path.Combine(testDirectory, "file2.txt");

            File.WriteAllBytes(file1Path, content);
            File.WriteAllBytes(file2Path, content);

            var file1 = CreateRealFile(file1Path);
            var file2 = CreateRealFile(file2Path);

            var detector = CreateDetector();
            await detector.ScanForDuplicatesAsync(new[] { file1 });

            // Verify index is populated
            var foundBefore = detector.FindDuplicateOf(file2);
            await Assert.That(foundBefore).IsNotNull();

            // Act
            detector.Clear();

            // Assert
            var foundAfter = detector.FindDuplicateOf(file2);
            await Assert.That(foundAfter).IsNull();
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region Cancellation Tests

    [Test]
    public async Task ScanForDuplicatesAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var files = new List<IFile>();
            for (int i = 0; i < 100; i++)
            {
                var filePath = Path.Combine(testDirectory, $"file{i}.txt");
                File.WriteAllText(filePath, $"Content {i}");
                files.Add(CreateRealFile(filePath));
            }

            var detector = CreateDetector();
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await detector.ScanForDuplicatesAsync(files, cts.Token));
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region Empty Files Tests

    [Test]
    public async Task ScanForDuplicatesAsync_WithEmptyFiles_DetectsDuplicates()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange - Empty files should have the same checksum
            var file1Path = Path.Combine(testDirectory, "empty1.txt");
            var file2Path = Path.Combine(testDirectory, "empty2.txt");

            File.WriteAllBytes(file1Path, Array.Empty<byte>());
            File.WriteAllBytes(file2Path, Array.Empty<byte>());

            var file1 = CreateRealFile(file1Path);
            var file2 = CreateRealFile(file2Path);

            var detector = CreateDetector();

            // Act
            var result = await detector.ScanForDuplicatesAsync(new[] { file1, file2 });

            // Assert
            await Assert.That(result.DuplicateFilesFound).IsEqualTo(1);
            await Assert.That(file1.Checksum).IsEqualTo(file2.Checksum);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task ScanForDuplicatesAsync_WithSingleFile_NoDuplicates()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var filePath = Path.Combine(testDirectory, "single.txt");
            File.WriteAllText(filePath, "Single file content");

            var file = CreateRealFile(filePath);
            var detector = CreateDetector();

            // Act
            var result = await detector.ScanForDuplicatesAsync(new[] { file });

            // Assert
            await Assert.That(result.TotalFilesScanned).IsEqualTo(1);
            await Assert.That(result.DuplicateFilesFound).IsEqualTo(0);
            await Assert.That(result.DuplicateGroups.Count).IsEqualTo(0);
            await Assert.That(result.UniqueFiles.Count).IsEqualTo(1);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task ScanForDuplicatesAsync_WithNoFiles_ReturnsEmptyResult()
    {
        // Arrange
        var detector = CreateDetector();

        // Act
        var result = await detector.ScanForDuplicatesAsync(Array.Empty<IFile>());

        // Assert
        await Assert.That(result.TotalFilesScanned).IsEqualTo(0);
        await Assert.That(result.DuplicateFilesFound).IsEqualTo(0);
        await Assert.That(result.DuplicateGroups.Count).IsEqualTo(0);
        await Assert.That(result.UniqueFiles.Count).IsEqualTo(0);
    }

    #endregion

    #region Large File Tests

    [Test]
    public async Task ScanForDuplicatesAsync_WithLargeIdenticalFiles_DetectsDuplicates()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange - Create larger files (1MB)
            var largeContent = new byte[1024 * 1024];
            new Random(42).NextBytes(largeContent); // Use seed for deterministic content

            var file1Path = Path.Combine(testDirectory, "large1.bin");
            var file2Path = Path.Combine(testDirectory, "large2.bin");

            File.WriteAllBytes(file1Path, largeContent);
            File.WriteAllBytes(file2Path, largeContent);

            var file1 = CreateRealFile(file1Path);
            var file2 = CreateRealFile(file2Path);

            var detector = CreateDetector();

            // Act
            var result = await detector.ScanForDuplicatesAsync(new[] { file1, file2 });

            // Assert
            await Assert.That(result.DuplicateFilesFound).IsEqualTo(1);
            await Assert.That(file1.Checksum).IsEqualTo(file2.Checksum);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region DuplicateGroup Checksum Tests

    [Test]
    public async Task DuplicateGroup_ContainsCorrectChecksum()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var content = "Checksum verification content"u8.ToArray();
            var file1Path = Path.Combine(testDirectory, "file1.txt");
            var file2Path = Path.Combine(testDirectory, "file2.txt");

            File.WriteAllBytes(file1Path, content);
            File.WriteAllBytes(file2Path, content);

            var file1 = CreateRealFile(file1Path);
            var file2 = CreateRealFile(file2Path);

            var detector = CreateDetector();

            // Act
            var result = await detector.ScanForDuplicatesAsync(new[] { file1, file2 });

            // Assert
            await Assert.That(result.DuplicateGroups.Count).IsEqualTo(1);
            var group = result.DuplicateGroups[0];
            await Assert.That(group.Checksum).IsEqualTo(file1.Checksum);
            await Assert.That(group.Checksum).IsEqualTo(file2.Checksum);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion
}
