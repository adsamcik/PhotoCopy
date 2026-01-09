using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Files;
using PhotoCopy.Abstractions;
using PhotoCopy.Directories;
using PhotoCopy.Configuration;

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

    [Test]
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

    [Test]
    public void EnumerateFiles_DirectoryDoesNotExist_SkipsDirectory()
    {
        // Arrange
        var nonExistentDirectory = Path.Combine(Path.GetTempPath(), "DirectoryScannerTests", "NonExistentDirectory");

        // Act
        var files = _scanner.EnumerateFiles(nonExistentDirectory);

        // Assert
        files.Should().BeEmpty();
    }

    [Test]
    public void EnumerateFiles_StrictMode_DiscoversRelatedFilesWithSameNameDifferentExtension()
    {
        // Arrange
        var tempDirectory = Path.Combine(Path.GetTempPath(), "DirectoryScannerTests", "StrictMode");
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, true);
        }
        Directory.CreateDirectory(tempDirectory);

        var mainFile = Path.Combine(tempDirectory, "photo.jpg");
        var xmpFile = Path.Combine(tempDirectory, "photo.xmp");
        var thmFile = Path.Combine(tempDirectory, "photo.thm");

        File.WriteAllText(mainFile, "Main photo content");
        File.WriteAllText(xmpFile, "XMP sidecar content");
        File.WriteAllText(thmFile, "Thumbnail content");

        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            Source = tempDirectory,
            Destination = "test-destination",
            RelatedFileMode = RelatedFileLookup.Strict,
            AllowedExtensions = new HashSet<string> { ".jpg" }
        });

        var fileLogger = Substitute.For<ILogger<FileWithMetadata>>();
        var fileFactory = Substitute.For<IFileFactory>();
        fileFactory.Create(Arg.Any<FileInfo>()).Returns(callInfo =>
        {
            var fi = callInfo.Arg<FileInfo>();
            var dt = new FileDateTime(fi.CreationTime, DateTimeSource.FileCreation);
            if (fi.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                return new FileWithMetadata(fi, dt, fileLogger);
            }
            return new GenericFile(fi, dt);
        });

        var scanner = new DirectoryScanner(_logger, options, fileFactory);

        try
        {
            // Act
            var files = scanner.EnumerateFiles(tempDirectory).ToList();

            // Assert
            files.Should().HaveCount(3);
            var photoFile = files.FirstOrDefault(f => f.File.Name == "photo.jpg") as FileWithMetadata;
            photoFile.Should().NotBeNull();
            photoFile!.RelatedFiles.Should().HaveCount(2);
            photoFile.RelatedFiles.Should().Contain(f => f.File.Name == "photo.xmp");
            photoFile.RelatedFiles.Should().Contain(f => f.File.Name == "photo.thm");
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Test]
    public void EnumerateFiles_StrictMode_DiscoversRelatedFilesWithUnderscoreSuffix()
    {
        // Arrange
        var tempDirectory = Path.Combine(Path.GetTempPath(), "DirectoryScannerTests", "StrictModeUnderscore");
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, true);
        }
        Directory.CreateDirectory(tempDirectory);

        var mainFile = Path.Combine(tempDirectory, "photo.jpg");
        var previewFile = Path.Combine(tempDirectory, "photo_preview.jpg");

        File.WriteAllText(mainFile, "Main photo content");
        File.WriteAllText(previewFile, "Preview content");

        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            Source = tempDirectory,
            Destination = "test-destination",
            RelatedFileMode = RelatedFileLookup.Strict,
            AllowedExtensions = new HashSet<string> { ".jpg" }
        });

        var fileLogger = Substitute.For<ILogger<FileWithMetadata>>();
        var fileFactory = Substitute.For<IFileFactory>();
        fileFactory.Create(Arg.Any<FileInfo>()).Returns(callInfo =>
        {
            var fi = callInfo.Arg<FileInfo>();
            var dt = new FileDateTime(fi.CreationTime, DateTimeSource.FileCreation);
            return new FileWithMetadata(fi, dt, fileLogger);
        });

        var scanner = new DirectoryScanner(_logger, options, fileFactory);

        try
        {
            // Act
            var files = scanner.EnumerateFiles(tempDirectory).ToList();

            // Assert
            files.Should().HaveCount(2);
            var photoFile = files.FirstOrDefault(f => f.File.Name == "photo.jpg") as FileWithMetadata;
            photoFile.Should().NotBeNull();
            photoFile!.RelatedFiles.Should().HaveCount(1);
            photoFile.RelatedFiles.Should().Contain(f => f.File.Name == "photo_preview.jpg");
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Test]
    public void EnumerateFiles_LooseMode_DiscoversRelatedFilesWithMatchingPrefix()
    {
        // Arrange
        var tempDirectory = Path.Combine(Path.GetTempPath(), "DirectoryScannerTests", "LooseMode");
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, true);
        }
        Directory.CreateDirectory(tempDirectory);

        var mainFile = Path.Combine(tempDirectory, "photo.jpg");
        var xmpFile = Path.Combine(tempDirectory, "photo.xmp");
        var editedFile = Path.Combine(tempDirectory, "photo_edited.jpg");
        var photoVariant = Path.Combine(tempDirectory, "photo2.jpg"); // Should match in loose mode

        File.WriteAllText(mainFile, "Main photo content");
        File.WriteAllText(xmpFile, "XMP content");
        File.WriteAllText(editedFile, "Edited content");
        File.WriteAllText(photoVariant, "Photo variant content");

        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            Source = tempDirectory,
            Destination = "test-destination",
            RelatedFileMode = RelatedFileLookup.Loose,
            AllowedExtensions = new HashSet<string> { ".jpg" }
        });

        var fileLogger = Substitute.For<ILogger<FileWithMetadata>>();
        var fileFactory = Substitute.For<IFileFactory>();
        fileFactory.Create(Arg.Any<FileInfo>()).Returns(callInfo =>
        {
            var fi = callInfo.Arg<FileInfo>();
            var dt = new FileDateTime(fi.CreationTime, DateTimeSource.FileCreation);
            return new FileWithMetadata(fi, dt, fileLogger);
        });

        var scanner = new DirectoryScanner(_logger, options, fileFactory);

        try
        {
            // Act
            var files = scanner.EnumerateFiles(tempDirectory).ToList();

            // Assert
            files.Should().HaveCount(4);
            var photoFile = files.FirstOrDefault(f => f.File.Name == "photo.jpg") as FileWithMetadata;
            photoFile.Should().NotBeNull();
            // Loose mode matches files where base name starts with main file's base name
            photoFile!.RelatedFiles.Should().HaveCount(3);
            photoFile.RelatedFiles.Should().Contain(f => f.File.Name == "photo.xmp");
            photoFile.RelatedFiles.Should().Contain(f => f.File.Name == "photo_edited.jpg");
            photoFile.RelatedFiles.Should().Contain(f => f.File.Name == "photo2.jpg");
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Test]
    public void EnumerateFiles_NoneMode_DoesNotAddRelatedFiles()
    {
        // Arrange
        var tempDirectory = Path.Combine(Path.GetTempPath(), "DirectoryScannerTests", "NoneMode");
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, true);
        }
        Directory.CreateDirectory(tempDirectory);

        var mainFile = Path.Combine(tempDirectory, "photo.jpg");
        var xmpFile = Path.Combine(tempDirectory, "photo.xmp");

        File.WriteAllText(mainFile, "Main photo content");
        File.WriteAllText(xmpFile, "XMP sidecar content");

        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            Source = tempDirectory,
            Destination = "test-destination",
            RelatedFileMode = RelatedFileLookup.None,
            AllowedExtensions = new HashSet<string> { ".jpg" }
        });

        var fileLogger = Substitute.For<ILogger<FileWithMetadata>>();
        var fileFactory = Substitute.For<IFileFactory>();
        fileFactory.Create(Arg.Any<FileInfo>()).Returns(callInfo =>
        {
            var fi = callInfo.Arg<FileInfo>();
            var dt = new FileDateTime(fi.CreationTime, DateTimeSource.FileCreation);
            if (fi.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                return new FileWithMetadata(fi, dt, fileLogger);
            }
            return new GenericFile(fi, dt);
        });

        var scanner = new DirectoryScanner(_logger, options, fileFactory);

        try
        {
            // Act
            var files = scanner.EnumerateFiles(tempDirectory).ToList();

            // Assert
            files.Should().HaveCount(2);
            var photoFile = files.FirstOrDefault(f => f.File.Name == "photo.jpg") as FileWithMetadata;
            photoFile.Should().NotBeNull();
            photoFile!.RelatedFiles.Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Test]
    public void EnumerateFiles_RawAndXmpCombination_DiscoversXmpAsRelatedFile()
    {
        // Arrange
        var tempDirectory = Path.Combine(Path.GetTempPath(), "DirectoryScannerTests", "RawXmp");
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, true);
        }
        Directory.CreateDirectory(tempDirectory);

        // Common RAW+XMP workflow patterns
        var rawFile = Path.Combine(tempDirectory, "DSC_1234.nef");
        var xmpFile = Path.Combine(tempDirectory, "DSC_1234.xmp");
        var jpgXmpFile = Path.Combine(tempDirectory, "DSC_1234.nef.xmp"); // Adobe-style sidecar

        File.WriteAllText(rawFile, "RAW content");
        File.WriteAllText(xmpFile, "XMP sidecar content");
        File.WriteAllText(jpgXmpFile, "Adobe-style XMP sidecar content");

        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            Source = tempDirectory,
            Destination = "test-destination",
            RelatedFileMode = RelatedFileLookup.Strict,
            AllowedExtensions = new HashSet<string> { ".nef" }
        });

        var fileLogger = Substitute.For<ILogger<FileWithMetadata>>();
        var fileFactory = Substitute.For<IFileFactory>();
        fileFactory.Create(Arg.Any<FileInfo>()).Returns(callInfo =>
        {
            var fi = callInfo.Arg<FileInfo>();
            var dt = new FileDateTime(fi.CreationTime, DateTimeSource.FileCreation);
            if (fi.Extension.Equals(".nef", StringComparison.OrdinalIgnoreCase))
            {
                return new FileWithMetadata(fi, dt, fileLogger);
            }
            return new GenericFile(fi, dt);
        });

        var scanner = new DirectoryScanner(_logger, options, fileFactory);

        try
        {
            // Act
            var files = scanner.EnumerateFiles(tempDirectory).ToList();

            // Assert
            files.Should().HaveCount(3);
            var rawPhotoFile = files.FirstOrDefault(f => f.File.Name == "DSC_1234.nef") as FileWithMetadata;
            rawPhotoFile.Should().NotBeNull();
            rawPhotoFile!.RelatedFiles.Should().HaveCount(2);
            rawPhotoFile.RelatedFiles.Should().Contain(f => f.File.Name == "DSC_1234.xmp");
            rawPhotoFile.RelatedFiles.Should().Contain(f => f.File.Name == "DSC_1234.nef.xmp");
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Test]
    public void EnumerateFiles_FileFactoryCalledForEachFile()
    {
        // Arrange
        var tempDirectory = Path.Combine(Path.GetTempPath(), "DirectoryScannerTests", "FileFactoryCalls");
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, true);
        }
        Directory.CreateDirectory(tempDirectory);

        var file1 = Path.Combine(tempDirectory, "photo1.jpg");
        var file2 = Path.Combine(tempDirectory, "photo2.jpg");
        var file3 = Path.Combine(tempDirectory, "document.pdf");

        File.WriteAllText(file1, "Photo 1 content");
        File.WriteAllText(file2, "Photo 2 content");
        File.WriteAllText(file3, "Document content");

        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            Source = tempDirectory,
            Destination = "test-destination",
            RelatedFileMode = RelatedFileLookup.None
        });

        var fileFactory = Substitute.For<IFileFactory>();
        fileFactory.Create(Arg.Any<FileInfo>()).Returns(callInfo =>
        {
            var fi = callInfo.Arg<FileInfo>();
            var dt = new FileDateTime(fi.CreationTime, DateTimeSource.FileCreation);
            return new GenericFile(fi, dt);
        });

        var scanner = new DirectoryScanner(_logger, options, fileFactory);

        try
        {
            // Act
            var files = scanner.EnumerateFiles(tempDirectory).ToList();

            // Assert
            files.Should().HaveCount(3);
            fileFactory.Received(3).Create(Arg.Any<FileInfo>());
            fileFactory.Received(1).Create(Arg.Is<FileInfo>(f => f.Name == "photo1.jpg"));
            fileFactory.Received(1).Create(Arg.Is<FileInfo>(f => f.Name == "photo2.jpg"));
            fileFactory.Received(1).Create(Arg.Is<FileInfo>(f => f.Name == "document.pdf"));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Test]
    public void EnumerateFiles_RelatedFilesOnlyInSameDirectory()
    {
        // Arrange
        var tempDirectory = Path.Combine(Path.GetTempPath(), "DirectoryScannerTests", "SameDir");
        var subDirectory = Path.Combine(tempDirectory, "SubDir");
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, true);
        }
        Directory.CreateDirectory(tempDirectory);
        Directory.CreateDirectory(subDirectory);

        var mainFile = Path.Combine(tempDirectory, "photo.jpg");
        var xmpInSameDir = Path.Combine(tempDirectory, "photo.xmp");
        var xmpInSubDir = Path.Combine(subDirectory, "photo.xmp"); // Should NOT be related

        File.WriteAllText(mainFile, "Main photo content");
        File.WriteAllText(xmpInSameDir, "XMP in same directory");
        File.WriteAllText(xmpInSubDir, "XMP in subdirectory");

        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            Source = tempDirectory,
            Destination = "test-destination",
            RelatedFileMode = RelatedFileLookup.Strict,
            AllowedExtensions = new HashSet<string> { ".jpg" }
        });

        var fileLogger = Substitute.For<ILogger<FileWithMetadata>>();
        var fileFactory = Substitute.For<IFileFactory>();
        fileFactory.Create(Arg.Any<FileInfo>()).Returns(callInfo =>
        {
            var fi = callInfo.Arg<FileInfo>();
            var dt = new FileDateTime(fi.CreationTime, DateTimeSource.FileCreation);
            if (fi.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                return new FileWithMetadata(fi, dt, fileLogger);
            }
            return new GenericFile(fi, dt);
        });

        var scanner = new DirectoryScanner(_logger, options, fileFactory);

        try
        {
            // Act
            var files = scanner.EnumerateFiles(tempDirectory).ToList();

            // Assert
            files.Should().HaveCount(3);
            var photoFile = files.FirstOrDefault(f => f.File.Name == "photo.jpg" && 
                Path.GetDirectoryName(f.File.FullName) == tempDirectory) as FileWithMetadata;
            photoFile.Should().NotBeNull();
            // Only the XMP in the same directory should be related
            photoFile!.RelatedFiles.Should().HaveCount(1);
            photoFile.RelatedFiles.Should().Contain(f => 
                f.File.Name == "photo.xmp" && 
                Path.GetDirectoryName(f.File.FullName) == tempDirectory);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }
}
