using System;
using System.IO;
using System.Linq;
using PhotoCopy.Abstractions;
using PhotoCopy.Files;
using Xunit;

namespace PhotoCopy.Tests.Abstractions;

public class FileSystemTests : IClassFixture<ApplicationStateFixture>
{
    private readonly ApplicationStateFixture _fixture;
    private readonly FileSystem _fileSystem;
    private readonly string _baseTestDirectory;

    public FileSystemTests(ApplicationStateFixture fixture)
    {
        _fixture = fixture;
        ApplicationState.Options = new Options();
        _baseTestDirectory = Path.Combine(Path.GetTempPath(), "FileSystemTests");
        _fileSystem = new FileSystem();
        
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

    [Fact]
    public void EnumerateFiles_WithValidDirectory_ReturnsFiles()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var testFile = Path.Combine(testDirectory, "test.txt");
            File.WriteAllText(testFile, "test content");

            var options = new Options
            {
                Source = testDirectory,
                Destination = "dummy",
                RelatedFileMode = Options.RelatedFileLookup.none
            };

            // Act
            var files = _fileSystem.EnumerateFiles(testDirectory, options).ToList();

            // Assert
            Assert.Single(files);
            Assert.Equal(testFile, files[0].File.FullName);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Fact]
    public void CreateDirectory_CreatesNewDirectory()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var newDir = Path.Combine(testDirectory, "newDir");

            // Act
            _fileSystem.CreateDirectory(newDir);

            // Assert
            Assert.True(Directory.Exists(newDir));
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Fact]
    public void DirectoryExists_WithExistingDirectory_ReturnsTrue()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Act
            var exists = _fileSystem.DirectoryExists(testDirectory);

            // Assert
            Assert.True(exists);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Fact]
    public void DirectoryExists_WithNonExistentDirectory_ReturnsFalse()
    {
        var testDirectory = Path.Combine(_baseTestDirectory, "NonExistentDirectory");
        
        // Act
        var exists = _fileSystem.DirectoryExists(testDirectory);

        // Assert
        Assert.False(exists);
    }
}