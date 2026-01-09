using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Directories;
using PhotoCopy.Files;

namespace PhotoCopy.Tests.Directories;

/// <summary>
/// Tests for the MaxDepth feature in DirectoryScanner.
/// MaxDepth controls how deep the scanner will recurse into subdirectories.
/// - null or 0 = unlimited (default)
/// - 1 = root only
/// - 2 = root + 1 level
/// - Negative values = treated as unlimited
/// </summary>
public class DirectoryScannerMaxDepthTests
{
    private readonly ILogger<DirectoryScanner> _logger;
    private readonly string _baseTestDirectory;

    public DirectoryScannerMaxDepthTests()
    {
        _logger = Substitute.For<ILogger<DirectoryScanner>>();
        _baseTestDirectory = Path.Combine(Path.GetTempPath(), "DirectoryScannerMaxDepthTests");
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
        catch (IOException)
        {
            // If we can't delete now, don't fail the test
        }
    }

    private DirectoryScanner CreateScanner(int? maxDepth)
    {
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            Source = "test-source",
            Destination = "test-destination",
            RelatedFileMode = RelatedFileLookup.None,
            MaxDepth = maxDepth
        });

        var fileFactory = Substitute.For<IFileFactory>();
        fileFactory.Create(Arg.Any<FileInfo>()).Returns(callInfo =>
        {
            var fi = callInfo.Arg<FileInfo>();
            var dt = new FileDateTime(fi.CreationTime, DateTimeSource.FileCreation);
            return new GenericFile(fi, dt);
        });

        return new DirectoryScanner(_logger, options, fileFactory);
    }

    /// <summary>
    /// Creates a test directory structure with files at multiple depth levels:
    /// root/
    ///   level1.jpg           (depth 1)
    ///   sub1/
    ///     level2.jpg         (depth 2)
    ///     sub1a/
    ///       level3.jpg       (depth 3)
    ///       sub1a1/
    ///         level4.jpg     (depth 4)
    /// </summary>
    private void CreateTestDirectoryStructure(string rootPath)
    {
        // Create directory structure
        var level1Dir = rootPath;
        var level2Dir = Path.Combine(rootPath, "sub1");
        var level3Dir = Path.Combine(level2Dir, "sub1a");
        var level4Dir = Path.Combine(level3Dir, "sub1a1");

        Directory.CreateDirectory(level1Dir);
        Directory.CreateDirectory(level2Dir);
        Directory.CreateDirectory(level3Dir);
        Directory.CreateDirectory(level4Dir);

        // Create test files at each level
        File.WriteAllText(Path.Combine(level1Dir, "level1.jpg"), "Level 1 content");
        File.WriteAllText(Path.Combine(level2Dir, "level2.jpg"), "Level 2 content");
        File.WriteAllText(Path.Combine(level3Dir, "level3.jpg"), "Level 3 content");
        File.WriteAllText(Path.Combine(level4Dir, "level4.jpg"), "Level 4 content");
    }

    [Test]
    public void MaxDepth_Null_ScansAllDirectories()
    {
        // Arrange
        var testDirectory = CreateUniqueTestDirectory();
        CreateTestDirectoryStructure(testDirectory);
        var scanner = CreateScanner(maxDepth: null);

        try
        {
            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            files.Should().HaveCount(4);
            files.Should().ContainSingle(f => f.File.Name == "level1.jpg");
            files.Should().ContainSingle(f => f.File.Name == "level2.jpg");
            files.Should().ContainSingle(f => f.File.Name == "level3.jpg");
            files.Should().ContainSingle(f => f.File.Name == "level4.jpg");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public void MaxDepth_Zero_ScansAllDirectories()
    {
        // Arrange
        var testDirectory = CreateUniqueTestDirectory();
        CreateTestDirectoryStructure(testDirectory);
        var scanner = CreateScanner(maxDepth: 0);

        try
        {
            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            files.Should().HaveCount(4);
            files.Should().ContainSingle(f => f.File.Name == "level1.jpg");
            files.Should().ContainSingle(f => f.File.Name == "level2.jpg");
            files.Should().ContainSingle(f => f.File.Name == "level3.jpg");
            files.Should().ContainSingle(f => f.File.Name == "level4.jpg");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public void MaxDepth_One_OnlyScansRootDirectory()
    {
        // Arrange
        var testDirectory = CreateUniqueTestDirectory();
        CreateTestDirectoryStructure(testDirectory);
        var scanner = CreateScanner(maxDepth: 1);

        try
        {
            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            files.Should().HaveCount(1);
            files.Should().ContainSingle(f => f.File.Name == "level1.jpg");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public void MaxDepth_Two_ScansRootAndOneLevel()
    {
        // Arrange
        var testDirectory = CreateUniqueTestDirectory();
        CreateTestDirectoryStructure(testDirectory);
        var scanner = CreateScanner(maxDepth: 2);

        try
        {
            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            files.Should().HaveCount(2);
            files.Should().ContainSingle(f => f.File.Name == "level1.jpg");
            files.Should().ContainSingle(f => f.File.Name == "level2.jpg");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public void MaxDepth_Three_ScansUpToThreeLevels()
    {
        // Arrange
        var testDirectory = CreateUniqueTestDirectory();
        CreateTestDirectoryStructure(testDirectory);
        var scanner = CreateScanner(maxDepth: 3);

        try
        {
            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            files.Should().HaveCount(3);
            files.Should().ContainSingle(f => f.File.Name == "level1.jpg");
            files.Should().ContainSingle(f => f.File.Name == "level2.jpg");
            files.Should().ContainSingle(f => f.File.Name == "level3.jpg");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public void MaxDepth_ExceedsActualDepth_ReturnsAllFiles()
    {
        // Arrange
        var testDirectory = CreateUniqueTestDirectory();
        CreateTestDirectoryStructure(testDirectory);
        var scanner = CreateScanner(maxDepth: 100); // Much larger than actual depth (4)

        try
        {
            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            files.Should().HaveCount(4);
            files.Should().ContainSingle(f => f.File.Name == "level1.jpg");
            files.Should().ContainSingle(f => f.File.Name == "level2.jpg");
            files.Should().ContainSingle(f => f.File.Name == "level3.jpg");
            files.Should().ContainSingle(f => f.File.Name == "level4.jpg");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public void MaxDepth_Negative_TreatedAsUnlimited()
    {
        // Arrange
        var testDirectory = CreateUniqueTestDirectory();
        CreateTestDirectoryStructure(testDirectory);
        var scanner = CreateScanner(maxDepth: -1);

        try
        {
            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert - negative values should scan all directories (same as unlimited)
            files.Should().HaveCount(4);
            files.Should().ContainSingle(f => f.File.Name == "level1.jpg");
            files.Should().ContainSingle(f => f.File.Name == "level2.jpg");
            files.Should().ContainSingle(f => f.File.Name == "level3.jpg");
            files.Should().ContainSingle(f => f.File.Name == "level4.jpg");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public void MaxDepth_EmptyRootWithFilesInSubdirs_ReturnsEmpty()
    {
        // Arrange
        var testDirectory = CreateUniqueTestDirectory();
        
        // Create files only in subdirectories (not in root)
        var subDir = Path.Combine(testDirectory, "subdir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "file.jpg"), "Content");
        
        var scanner = CreateScanner(maxDepth: 1); // Only scan root

        try
        {
            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert - should be empty since MaxDepth 1 only scans root, which has no files
            files.Should().BeEmpty();
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public void MaxDepth_VeryLargeNegative_TreatedAsUnlimited()
    {
        // Arrange
        var testDirectory = CreateUniqueTestDirectory();
        CreateTestDirectoryStructure(testDirectory);
        var scanner = CreateScanner(maxDepth: int.MinValue);

        try
        {
            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert - large negative values should also scan all directories
            files.Should().HaveCount(4);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public void MaxDepth_WithMultipleSubdirectoriesAtSameLevel_ScansAllAtLevel()
    {
        // Arrange
        var testDirectory = CreateUniqueTestDirectory();
        
        // Create multiple subdirectories at the same level
        var subDir1 = Path.Combine(testDirectory, "photos");
        var subDir2 = Path.Combine(testDirectory, "videos");
        var subDir3 = Path.Combine(testDirectory, "documents");
        
        Directory.CreateDirectory(subDir1);
        Directory.CreateDirectory(subDir2);
        Directory.CreateDirectory(subDir3);
        
        File.WriteAllText(Path.Combine(testDirectory, "root.jpg"), "Root content");
        File.WriteAllText(Path.Combine(subDir1, "photo1.jpg"), "Photo content");
        File.WriteAllText(Path.Combine(subDir2, "video1.jpg"), "Video content");
        File.WriteAllText(Path.Combine(subDir3, "doc1.jpg"), "Doc content");
        
        var scanner = CreateScanner(maxDepth: 2);

        try
        {
            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert - should find root + all files in immediate subdirectories
            files.Should().HaveCount(4);
            files.Should().ContainSingle(f => f.File.Name == "root.jpg");
            files.Should().ContainSingle(f => f.File.Name == "photo1.jpg");
            files.Should().ContainSingle(f => f.File.Name == "video1.jpg");
            files.Should().ContainSingle(f => f.File.Name == "doc1.jpg");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public void MaxDepth_WithMultipleFilesPerLevel_ScansAllFilesWithinDepth()
    {
        // Arrange
        var testDirectory = CreateUniqueTestDirectory();
        var subDir = Path.Combine(testDirectory, "subdir");
        
        Directory.CreateDirectory(subDir);
        
        // Multiple files at root
        File.WriteAllText(Path.Combine(testDirectory, "file1.jpg"), "Content 1");
        File.WriteAllText(Path.Combine(testDirectory, "file2.jpg"), "Content 2");
        File.WriteAllText(Path.Combine(testDirectory, "file3.jpg"), "Content 3");
        
        // Multiple files in subdir
        File.WriteAllText(Path.Combine(subDir, "sub1.jpg"), "Sub content 1");
        File.WriteAllText(Path.Combine(subDir, "sub2.jpg"), "Sub content 2");
        
        var scanner = CreateScanner(maxDepth: 2);

        try
        {
            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert - should find all 5 files (3 root + 2 subdir)
            files.Should().HaveCount(5);
            files.Should().ContainSingle(f => f.File.Name == "file1.jpg");
            files.Should().ContainSingle(f => f.File.Name == "file2.jpg");
            files.Should().ContainSingle(f => f.File.Name == "file3.jpg");
            files.Should().ContainSingle(f => f.File.Name == "sub1.jpg");
            files.Should().ContainSingle(f => f.File.Name == "sub2.jpg");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public void MaxDepth_One_WithNoFilesInRoot_ReturnsEmpty()
    {
        // Arrange
        var testDirectory = CreateUniqueTestDirectory();
        
        // Only create empty root, no files at all
        var scanner = CreateScanner(maxDepth: 1);

        try
        {
            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            files.Should().BeEmpty();
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public void MaxDepth_FourMatchesExactDepth_ReturnsAllFiles()
    {
        // Arrange
        var testDirectory = CreateUniqueTestDirectory();
        CreateTestDirectoryStructure(testDirectory); // Creates 4 levels
        var scanner = CreateScanner(maxDepth: 4);

        try
        {
            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert - MaxDepth exactly matches actual depth, should return all
            files.Should().HaveCount(4);
            files.Should().ContainSingle(f => f.File.Name == "level1.jpg");
            files.Should().ContainSingle(f => f.File.Name == "level2.jpg");
            files.Should().ContainSingle(f => f.File.Name == "level3.jpg");
            files.Should().ContainSingle(f => f.File.Name == "level4.jpg");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }
}
