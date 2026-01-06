using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Files;
using PhotoCopy.Abstractions;
using PhotoCopy.Directories;
using PhotoCopy.Configuration;
using Xunit;

namespace PhotoCopy.Tests.Directories;

public class DirectoryScannerTests
{
    private readonly IDirectoryScanner _scanner;
    private readonly IOptions<PhotoCopyConfig> _options;
    private readonly ILogger<DirectoryScanner> _logger;
    private readonly IFileFactory _fileFactory;

    public DirectoryScannerTests()
    {
        _logger = Substitute.For<ILogger<DirectoryScanner>>();
        _options = Substitute.For<IOptions<PhotoCopyConfig>>();
        _options.Value.Returns(new PhotoCopyConfig
        {
            Source = "test-source",
            Destination = "test-destination",
            RelatedFileMode = RelatedFileLookup.None
        });
        
        _fileFactory = Substitute.For<IFileFactory>();
        _fileFactory.Create(Arg.Any<FileInfo>()).Returns(callInfo => 
        {
            var fi = callInfo.Arg<FileInfo>();
            var dt = new FileDateTime(fi.CreationTime, DateTimeSource.FileCreation);
            return new GenericFile(fi, dt);
        });

        // Create scanner directly
        _scanner = new DirectoryScanner(_logger, _options, _fileFactory);
    }

    [Fact]
    public void EnumerateFiles_MultipleFiles_ReturnsAllFiles()
    {
        // Arrange
        var tempDirectory = Path.Combine(Path.GetTempPath(), "DirectoryScannerTests", "Input");
        var subDirectory = Path.Combine(tempDirectory, "SubDir");

        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, true);
        }

        Directory.CreateDirectory(tempDirectory);
        Directory.CreateDirectory(subDirectory);

        var filePath1 = Path.Combine(tempDirectory, "file1.jpg");
        var filePath2 = Path.Combine(subDirectory, "file2.jpg");

        File.WriteAllText(filePath1, "File1 content");
        File.WriteAllText(filePath2, "File2 content");

        try
        {
            // Act
            var files = _scanner.EnumerateFiles(tempDirectory);

            // Assert
            var fileList = files.ToList();
            fileList.Should().HaveCount(2);
            fileList.Should().ContainSingle(f => f.File.FullName == filePath1);
            fileList.Should().ContainSingle(f => f.File.FullName == filePath2);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void EnumerateFiles_DirectoryDoesNotExist_SkipsDirectory()
    {
        // Arrange
        var nonExistentDirectory = Path.Combine(Path.GetTempPath(), "DirectoryScannerTests", "NonExistentDirectory");

        // Act
        var files = _scanner.EnumerateFiles(nonExistentDirectory);

        // Assert
        files.Should().BeEmpty();
    }
}
