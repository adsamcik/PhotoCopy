using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PhotoCopy.Abstractions;
using PhotoCopy.Files;

namespace PhotoCopy.Tests.Files;

public class FileSystemTests : TestBase, IDisposable
{
    private readonly ILogger<FileSystem> _mockLogger;
    private readonly IDirectoryScanner _mockDirectoryScanner;
    private readonly FileSystem _fileSystem;
    private readonly string _testDirectory;

    public FileSystemTests()
    {
        _mockLogger = Substitute.For<ILogger<FileSystem>>();
        _mockDirectoryScanner = Substitute.For<IDirectoryScanner>();
        _fileSystem = new FileSystem(_mockLogger, _mockDirectoryScanner);
        
        _testDirectory = Path.Combine(Path.GetTempPath(), $"FileSystemTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    public new void Dispose()
    {
        Cleanup();
        base.Dispose();
    }

    #region Helper Methods

    private string CreateTempFile(string fileName, string content = "Test content")
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

    private string GetTempFilePath(string fileName)
    {
        return Path.Combine(_testDirectory, fileName);
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

    #region DirectoryExists Tests

    [Test]
    public void DirectoryExists_WithExistingDirectory_ReturnsTrue()
    {
        // Arrange
        var subDir = Path.Combine(_testDirectory, "existing_subdir");
        Directory.CreateDirectory(subDir);

        // Act
        var result = _fileSystem.DirectoryExists(subDir);

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void DirectoryExists_WithNonExistentDirectory_ReturnsFalse()
    {
        // Arrange
        var nonExistentDir = Path.Combine(_testDirectory, "nonexistent_subdir");

        // Act
        var result = _fileSystem.DirectoryExists(nonExistentDir);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void DirectoryExists_WithFilePath_ReturnsFalse()
    {
        // Arrange
        var filePath = CreateTempFile("some_file.txt");

        // Act
        var result = _fileSystem.DirectoryExists(filePath);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void DirectoryExists_WithNullPath_ReturnsFalse()
    {
        // Act
        var result = _fileSystem.DirectoryExists(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void DirectoryExists_WithEmptyPath_ReturnsFalse()
    {
        // Act
        var result = _fileSystem.DirectoryExists(string.Empty);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region FileExists Tests

    [Test]
    public void FileExists_WithExistingFile_ReturnsTrue()
    {
        // Arrange
        var filePath = CreateTempFile("existing_file.txt");

        // Act
        var result = _fileSystem.FileExists(filePath);

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void FileExists_WithNonExistentFile_ReturnsFalse()
    {
        // Arrange
        var nonExistentFile = GetTempFilePath("nonexistent_file.txt");

        // Act
        var result = _fileSystem.FileExists(nonExistentFile);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void FileExists_WithDirectoryPath_ReturnsFalse()
    {
        // Arrange
        var subDir = Path.Combine(_testDirectory, "subdir");
        Directory.CreateDirectory(subDir);

        // Act
        var result = _fileSystem.FileExists(subDir);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void FileExists_WithNullPath_ReturnsFalse()
    {
        // Act
        var result = _fileSystem.FileExists(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void FileExists_WithEmptyPath_ReturnsFalse()
    {
        // Act
        var result = _fileSystem.FileExists(string.Empty);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region CreateDirectory Tests

    [Test]
    public void CreateDirectory_WithValidPath_CreatesDirectory()
    {
        // Arrange
        var newDir = Path.Combine(_testDirectory, "new_directory");

        // Act
        _fileSystem.CreateDirectory(newDir);

        // Assert
        Directory.Exists(newDir).Should().BeTrue();
    }

    [Test]
    public void CreateDirectory_WithNestedPath_CreatesAllDirectories()
    {
        // Arrange
        var nestedDir = Path.Combine(_testDirectory, "level1", "level2", "level3");

        // Act
        _fileSystem.CreateDirectory(nestedDir);

        // Assert
        Directory.Exists(nestedDir).Should().BeTrue();
    }

    [Test]
    public void CreateDirectory_WithExistingDirectory_DoesNotThrow()
    {
        // Arrange
        var existingDir = Path.Combine(_testDirectory, "existing_dir");
        Directory.CreateDirectory(existingDir);

        // Act & Assert
        var act = () => _fileSystem.CreateDirectory(existingDir);
        act.Should().NotThrow();
    }

    [Test]
    public void CreateDirectory_WithNullPath_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => _fileSystem.CreateDirectory(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void CreateDirectory_WithEmptyPath_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => _fileSystem.CreateDirectory(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region CopyFile Tests

    [Test]
    public void CopyFile_WithValidPaths_CopiesFile()
    {
        // Arrange
        var content = "Content to copy";
        var sourcePath = CreateTempFile("source.txt", content);
        var destPath = GetTempFilePath("destination.txt");

        // Act
        _fileSystem.CopyFile(sourcePath, destPath);

        // Assert
        File.Exists(destPath).Should().BeTrue();
        File.ReadAllText(destPath).Should().Be(content);
    }

    [Test]
    public void CopyFile_WithOverwriteFalse_ThrowsWhenDestinationExists()
    {
        // Arrange
        var sourcePath = CreateTempFile("source.txt", "Source content");
        var destPath = CreateTempFile("dest.txt", "Existing content");

        // Act & Assert
        var act = () => _fileSystem.CopyFile(sourcePath, destPath, overwrite: false);
        act.Should().Throw<IOException>();
    }

    [Test]
    public void CopyFile_WithOverwriteTrue_OverwritesExistingFile()
    {
        // Arrange
        var newContent = "New content";
        var sourcePath = CreateTempFile("source.txt", newContent);
        var destPath = CreateTempFile("dest.txt", "Old content");

        // Act
        _fileSystem.CopyFile(sourcePath, destPath, overwrite: true);

        // Assert
        File.ReadAllText(destPath).Should().Be(newContent);
    }

    [Test]
    public void CopyFile_WithNonExistentSource_ThrowsFileNotFoundException()
    {
        // Arrange
        var sourcePath = GetTempFilePath("nonexistent_source.txt");
        var destPath = GetTempFilePath("destination.txt");

        // Act & Assert
        var act = () => _fileSystem.CopyFile(sourcePath, destPath);
        act.Should().Throw<FileNotFoundException>();
    }

    [Test]
    public void CopyFile_WithNullSourcePath_ThrowsArgumentNullException()
    {
        // Arrange
        var destPath = GetTempFilePath("destination.txt");

        // Act & Assert
        var act = () => _fileSystem.CopyFile(null!, destPath);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void CopyFile_WithNullDestinationPath_ThrowsArgumentNullException()
    {
        // Arrange
        var sourcePath = CreateTempFile("source.txt");

        // Act & Assert
        var act = () => _fileSystem.CopyFile(sourcePath, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void CopyFile_PreservesFileContent()
    {
        // Arrange
        var largeContent = string.Join(Environment.NewLine, Enumerable.Range(0, 1000).Select(i => $"Line {i}"));
        var sourcePath = CreateTempFile("large_source.txt", largeContent);
        var destPath = GetTempFilePath("large_dest.txt");

        // Act
        _fileSystem.CopyFile(sourcePath, destPath);

        // Assert
        File.ReadAllText(destPath).Should().Be(largeContent);
    }

    #endregion

    #region MoveFile Tests

    [Test]
    public void MoveFile_WithValidPaths_MovesFile()
    {
        // Arrange
        var content = "Content to move";
        var sourcePath = CreateTempFile("source_move.txt", content);
        var destPath = GetTempFilePath("moved.txt");

        // Act
        _fileSystem.MoveFile(sourcePath, destPath);

        // Assert
        File.Exists(sourcePath).Should().BeFalse();
        File.Exists(destPath).Should().BeTrue();
        File.ReadAllText(destPath).Should().Be(content);
    }

    [Test]
    public void MoveFile_WithExistingDestination_ThrowsIOException()
    {
        // Arrange
        var sourcePath = CreateTempFile("source_move.txt", "Source content");
        var destPath = CreateTempFile("existing_dest.txt", "Existing content");

        // Act & Assert
        var act = () => _fileSystem.MoveFile(sourcePath, destPath);
        act.Should().Throw<IOException>();
    }

    [Test]
    public void MoveFile_WithNonExistentSource_ThrowsFileNotFoundException()
    {
        // Arrange
        var sourcePath = GetTempFilePath("nonexistent_source.txt");
        var destPath = GetTempFilePath("destination.txt");

        // Act & Assert
        var act = () => _fileSystem.MoveFile(sourcePath, destPath);
        act.Should().Throw<FileNotFoundException>();
    }

    [Test]
    public void MoveFile_WithNullSourcePath_ThrowsArgumentNullException()
    {
        // Arrange
        var destPath = GetTempFilePath("destination.txt");

        // Act & Assert
        var act = () => _fileSystem.MoveFile(null!, destPath);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void MoveFile_WithNullDestinationPath_ThrowsArgumentNullException()
    {
        // Arrange
        var sourcePath = CreateTempFile("source.txt");

        // Act & Assert
        var act = () => _fileSystem.MoveFile(sourcePath, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void MoveFile_ToDifferentDirectory_MovesFile()
    {
        // Arrange
        var content = "Content to move to different directory";
        var sourcePath = CreateTempFile("source_move.txt", content);
        var destDir = Path.Combine(_testDirectory, "dest_dir");
        Directory.CreateDirectory(destDir);
        var destPath = Path.Combine(destDir, "moved_file.txt");

        // Act
        _fileSystem.MoveFile(sourcePath, destPath);

        // Assert
        File.Exists(sourcePath).Should().BeFalse();
        File.Exists(destPath).Should().BeTrue();
        File.ReadAllText(destPath).Should().Be(content);
    }

    #endregion

    #region GetFileInfo Tests

    [Test]
    public void GetFileInfo_WithExistingFile_ReturnsCorrectInfo()
    {
        // Arrange
        var content = "File info test content";
        var filePath = CreateTempFile("fileinfo_test.txt", content);

        // Act
        var fileInfo = _fileSystem.GetFileInfo(filePath);

        // Assert
        fileInfo.Should().NotBeNull();
        fileInfo.Exists.Should().BeTrue();
        fileInfo.FullName.Should().Be(filePath);
        fileInfo.Name.Should().Be("fileinfo_test.txt");
        fileInfo.Length.Should().Be(content.Length);
        fileInfo.Extension.Should().Be(".txt");
    }

    [Test]
    public void GetFileInfo_WithNonExistentFile_ReturnsFileInfoWithExistsFalse()
    {
        // Arrange
        var nonExistentPath = GetTempFilePath("nonexistent.txt");

        // Act
        var fileInfo = _fileSystem.GetFileInfo(nonExistentPath);

        // Assert
        fileInfo.Should().NotBeNull();
        fileInfo.Exists.Should().BeFalse();
        fileInfo.FullName.Should().Be(nonExistentPath);
    }

    [Test]
    public void GetFileInfo_WithNullPath_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => _fileSystem.GetFileInfo(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void GetFileInfo_WithEmptyPath_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => _fileSystem.GetFileInfo(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void GetFileInfo_WithDirectory_ReturnsFileInfoWithExistsFalse()
    {
        // Arrange
        var subDir = Path.Combine(_testDirectory, "subdir");
        Directory.CreateDirectory(subDir);

        // Act
        var fileInfo = _fileSystem.GetFileInfo(subDir);

        // Assert
        fileInfo.Should().NotBeNull();
        fileInfo.Exists.Should().BeFalse();
    }

    #endregion

    #region GetDirectoryInfo Tests

    [Test]
    public void GetDirectoryInfo_WithExistingDirectory_ReturnsCorrectInfo()
    {
        // Arrange
        var subDir = Path.Combine(_testDirectory, "dirinfo_test");
        Directory.CreateDirectory(subDir);

        // Act
        var dirInfo = _fileSystem.GetDirectoryInfo(subDir);

        // Assert
        dirInfo.Should().NotBeNull();
        dirInfo.Exists.Should().BeTrue();
        dirInfo.FullName.Should().Be(subDir);
        dirInfo.Name.Should().Be("dirinfo_test");
    }

    [Test]
    public void GetDirectoryInfo_WithNonExistentDirectory_ReturnsDirInfoWithExistsFalse()
    {
        // Arrange
        var nonExistentDir = Path.Combine(_testDirectory, "nonexistent_dir");

        // Act
        var dirInfo = _fileSystem.GetDirectoryInfo(nonExistentDir);

        // Assert
        dirInfo.Should().NotBeNull();
        dirInfo.Exists.Should().BeFalse();
        dirInfo.FullName.Should().Be(nonExistentDir);
    }

    [Test]
    public void GetDirectoryInfo_WithNullPath_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => _fileSystem.GetDirectoryInfo(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void GetDirectoryInfo_WithEmptyPath_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => _fileSystem.GetDirectoryInfo(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void GetDirectoryInfo_WithFilePath_ReturnsDirInfoWithExistsFalse()
    {
        // Arrange
        var filePath = CreateTempFile("not_a_dir.txt");

        // Act
        var dirInfo = _fileSystem.GetDirectoryInfo(filePath);

        // Assert
        dirInfo.Should().NotBeNull();
        dirInfo.Exists.Should().BeFalse();
    }

    #endregion

    #region EnumerateFiles Tests

    [Test]
    public void EnumerateFiles_DelegatesToDirectoryScanner()
    {
        // Arrange
        var testPath = _testDirectory;
        var mockFiles = new IFile[]
        {
            Substitute.For<IFile>(),
            Substitute.For<IFile>()
        };
        _mockDirectoryScanner.EnumerateFiles(testPath).Returns(mockFiles);

        // Act
        var result = _fileSystem.EnumerateFiles(testPath).ToList();

        // Assert
        result.Should().HaveCount(2);
        _mockDirectoryScanner.Received(1).EnumerateFiles(testPath);
    }

    [Test]
    public void EnumerateFiles_WithEmptyDirectory_ReturnsEmptyEnumerable()
    {
        // Arrange
        var emptyDir = Path.Combine(_testDirectory, "empty_dir");
        Directory.CreateDirectory(emptyDir);
        _mockDirectoryScanner.EnumerateFiles(emptyDir).Returns(Enumerable.Empty<IFile>());

        // Act
        var result = _fileSystem.EnumerateFiles(emptyDir).ToList();

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void EnumerateFiles_CallsDirectoryScannerWithCorrectPath()
    {
        // Arrange
        var specificPath = Path.Combine(_testDirectory, "specific_path");
        _mockDirectoryScanner.EnumerateFiles(Arg.Any<string>()).Returns(Enumerable.Empty<IFile>());

        // Act
        _fileSystem.EnumerateFiles(specificPath);

        // Assert
        _mockDirectoryScanner.Received(1).EnumerateFiles(specificPath);
    }

    #endregion
}
