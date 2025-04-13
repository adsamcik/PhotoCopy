using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using FluentAssertions;

namespace PhotoCopy.Tests.Directories;

public class DirectoryScannerTests : IClassFixture<ApplicationStateFixture>
{
    private readonly ApplicationStateFixture _fixture;

    public DirectoryScannerTests(ApplicationStateFixture fixture)
    {
        _fixture = fixture;
        ApplicationState.Options = new Options();
    }

    [Fact]
    public void EnumerateFiles_MultipleFiles_ReturnsAllFiles()
    {
        // Arrange
        var tempDirectory = Path.Combine(Path.GetTempPath(), "DirectoryScannerTests", "Input");
        var subDirectory = Path.Combine(tempDirectory, "SubDir");

        System.IO.Directory.CreateDirectory(tempDirectory);
        System.IO.Directory.CreateDirectory(subDirectory);

        var filePath1 = Path.Combine(tempDirectory, "file1.jpg");
        var filePath2 = Path.Combine(subDirectory, "file2.jpg");

        File.WriteAllText(filePath1, "File1 content");
        File.WriteAllText(filePath2, "File2 content");

        var options = new Options
        {
            RelatedFileMode = Options.RelatedFileLookup.none
        };

        try
        {
            // Act
            var files = DirectoryScanner.EnumerateFiles(tempDirectory, options);

            // Assert
            var fileList = files.ToList();
            fileList.Should().HaveCount(2);
            fileList.Should().ContainSingle(f => f.File.FullName == filePath1);
            fileList.Should().ContainSingle(f => f.File.FullName == filePath2);
        }
        finally
        {
            // Cleanup
            System.IO.Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void EnumerateFiles_DirectoryDoesNotExist_SkipsDirectory()
    {
        // Arrange
        var nonExistentDirectory = Path.Combine(Path.GetTempPath(), "DirectoryScannerTests", "NonExistentDirectory");
        var options = new Options
        {
            RelatedFileMode = Options.RelatedFileLookup.none
        };

        // Act
        var files = DirectoryScanner.EnumerateFiles(nonExistentDirectory, options);

        // Assert
        files.Should().BeEmpty();
    }
}
