using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Directories;
using PhotoCopy.Files;
using PhotoCopy.Files.Metadata;
using PhotoCopy.Tests.TestingImplementation;

namespace PhotoCopy.Tests.Integration;

[Property("Category", "Integration")]
public class DirectoryScannerIntegrationTests
{
    private readonly string _baseTestDirectory;

    public DirectoryScannerIntegrationTests()
    {
        _baseTestDirectory = Path.Combine(Path.GetTempPath(), "DirectoryScannerIntegrationTests");
        
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

    private DirectoryScanner CreateScanner(RelatedFileLookup relatedFileMode = RelatedFileLookup.None, HashSet<string>? allowedExtensions = null)
    {
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        var config = new PhotoCopyConfig
        {
            Source = "test-source",
            Destination = "test-destination",
            RelatedFileMode = relatedFileMode,
            CalculateChecksums = false // Disable for faster tests
        };

        if (allowedExtensions != null)
        {
            config.AllowedExtensions = allowedExtensions;
        }

        options.Value.Returns(config);

        var metadataExtractorLogger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var metadataExtractor = new FileMetadataExtractor(metadataExtractorLogger, options);
        
        var reverseGeocodingService = Substitute.For<IReverseGeocodingService>();
        var fileWithMetadataLogger = Substitute.For<ILogger<FileWithMetadata>>();
        var checksumCalculator = new Sha256ChecksumCalculator();
        var metadataEnricher = new MetadataEnricher(new IMetadataEnrichmentStep[]
        {
            new DateTimeMetadataEnrichmentStep(metadataExtractor),
            new LocationMetadataEnrichmentStep(metadataExtractor, reverseGeocodingService),
            new ChecksumMetadataEnrichmentStep(checksumCalculator, options)
        });
        var fileFactory = new FileFactory(metadataEnricher, fileWithMetadataLogger, options);

        var scannerLogger = Substitute.For<ILogger<DirectoryScanner>>();
        return new DirectoryScanner(scannerLogger, options, fileFactory);
    }

    #region Basic Enumeration Tests

    [Test]
    public async Task EnumerateFiles_WithSingleFile_ReturnsSingleFile()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var testFile = Path.Combine(testDirectory, "photo.jpg");
            File.WriteAllBytes(testFile, TestSampleImages.JpegWithNoExif);

            var scanner = CreateScanner();

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(files.Count).IsEqualTo(1);
            await Assert.That(files[0].File.Name).IsEqualTo("photo.jpg");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task EnumerateFiles_WithMultipleFiles_ReturnsAllFiles()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var files = new[] { "photo1.jpg", "photo2.png", "video.mp4" };
            foreach (var file in files)
            {
                File.WriteAllText(Path.Combine(testDirectory, file), "test content");
            }

            var scanner = CreateScanner();

            // Act
            var result = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(result.Count).IsEqualTo(3);
            var fileNames = result.Select(f => f.File.Name).OrderBy(n => n).ToList();
            await Assert.That(fileNames).IsEquivalentTo(files.OrderBy(f => f).ToList());
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task EnumerateFiles_WithEmptyDirectory_ReturnsEmptyCollection()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var scanner = CreateScanner();

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(files.Count).IsEqualTo(0);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task EnumerateFiles_WithNonExistentDirectory_ReturnsEmptyCollection()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_baseTestDirectory, "NonExistent_" + Guid.NewGuid().ToString());
        var scanner = CreateScanner();

        // Act
        var files = scanner.EnumerateFiles(nonExistentPath).ToList();

        // Assert
        await Assert.That(files.Count).IsEqualTo(0);
    }

    #endregion

    #region Recursive Scanning Tests

    [Test]
    public async Task EnumerateFiles_WithSubdirectories_ReturnsFilesFromAllLevels()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var subDir1 = Path.Combine(testDirectory, "subdir1");
            var subDir2 = Path.Combine(testDirectory, "subdir2");
            var nestedDir = Path.Combine(subDir1, "nested");
            
            Directory.CreateDirectory(subDir1);
            Directory.CreateDirectory(subDir2);
            Directory.CreateDirectory(nestedDir);

            File.WriteAllText(Path.Combine(testDirectory, "root.jpg"), "content");
            File.WriteAllText(Path.Combine(subDir1, "sub1.jpg"), "content");
            File.WriteAllText(Path.Combine(subDir2, "sub2.jpg"), "content");
            File.WriteAllText(Path.Combine(nestedDir, "nested.jpg"), "content");

            var scanner = CreateScanner();

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(files.Count).IsEqualTo(4);
            var fileNames = files.Select(f => f.File.Name).OrderBy(n => n).ToList();
            await Assert.That(fileNames).Contains("root.jpg");
            await Assert.That(fileNames).Contains("sub1.jpg");
            await Assert.That(fileNames).Contains("sub2.jpg");
            await Assert.That(fileNames).Contains("nested.jpg");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task EnumerateFiles_WithDeeplyNestedStructure_ReturnsAllFiles()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange - Create a deeply nested structure
            var currentDir = testDirectory;
            var expectedFiles = new List<string>();
            
            for (int i = 0; i < 5; i++)
            {
                var fileName = $"level{i}.jpg";
                File.WriteAllText(Path.Combine(currentDir, fileName), "content");
                expectedFiles.Add(fileName);
                
                currentDir = Path.Combine(currentDir, $"level{i}");
                Directory.CreateDirectory(currentDir);
            }

            var scanner = CreateScanner();

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(files.Count).IsEqualTo(5);
            var fileNames = files.Select(f => f.File.Name).ToList();
            foreach (var expected in expectedFiles)
            {
                await Assert.That(fileNames).Contains(expected);
            }
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task EnumerateFiles_WithEmptySubdirectories_IgnoresEmptyDirs()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var emptyDir1 = Path.Combine(testDirectory, "empty1");
            var emptyDir2 = Path.Combine(testDirectory, "empty2");
            var dirWithFile = Path.Combine(testDirectory, "withFile");
            
            Directory.CreateDirectory(emptyDir1);
            Directory.CreateDirectory(emptyDir2);
            Directory.CreateDirectory(dirWithFile);
            
            File.WriteAllText(Path.Combine(dirWithFile, "photo.jpg"), "content");

            var scanner = CreateScanner();

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(files.Count).IsEqualTo(1);
            await Assert.That(files[0].File.Name).IsEqualTo("photo.jpg");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region File Type Tests

    [Test]
    public async Task EnumerateFiles_WithSupportedExtensions_ReturnsFileWithMetadata()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var testFile = Path.Combine(testDirectory, "photo.jpg");
            File.WriteAllBytes(testFile, TestSampleImages.JpegWithNoExif);

            var scanner = CreateScanner();

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(files.Count).IsEqualTo(1);
            await Assert.That(files[0]).IsTypeOf<FileWithMetadata>();
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task EnumerateFiles_WithUnsupportedExtensions_ReturnsGenericFile()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var testFile = Path.Combine(testDirectory, "document.txt");
            File.WriteAllText(testFile, "some text content");

            var scanner = CreateScanner();

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(files.Count).IsEqualTo(1);
            await Assert.That(files[0]).IsTypeOf<GenericFile>();
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task EnumerateFiles_WithMixedExtensions_ReturnsMixedFileTypes()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            File.WriteAllBytes(Path.Combine(testDirectory, "photo.jpg"), TestSampleImages.JpegWithNoExif);
            File.WriteAllText(Path.Combine(testDirectory, "document.txt"), "text");
            File.WriteAllText(Path.Combine(testDirectory, "video.mp4"), "video");

            var scanner = CreateScanner();

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(files.Count).IsEqualTo(3);
            
            var jpg = files.First(f => f.File.Name == "photo.jpg");
            var txt = files.First(f => f.File.Name == "document.txt");
            var mp4 = files.First(f => f.File.Name == "video.mp4");
            
            await Assert.That(jpg).IsTypeOf<FileWithMetadata>();
            await Assert.That(txt).IsTypeOf<GenericFile>();
            await Assert.That(mp4).IsTypeOf<FileWithMetadata>();
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task EnumerateFiles_WithCustomAllowedExtensions_RespectsConfiguration()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            File.WriteAllText(Path.Combine(testDirectory, "photo.jpg"), "content");
            File.WriteAllText(Path.Combine(testDirectory, "custom.xyz"), "content");

            var customExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xyz" };
            var scanner = CreateScanner(allowedExtensions: customExtensions);

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(files.Count).IsEqualTo(2);
            
            var jpg = files.First(f => f.File.Name == "photo.jpg");
            var xyz = files.First(f => f.File.Name == "custom.xyz");
            
            // .jpg is not in custom extensions, so it should be GenericFile
            await Assert.That(jpg).IsTypeOf<GenericFile>();
            // .xyz is in custom extensions, so it should be FileWithMetadata
            await Assert.That(xyz).IsTypeOf<FileWithMetadata>();
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region Related File Detection Tests

    [Test]
    public async Task EnumerateFiles_WithRelatedFileModeNone_DoesNotDetectRelatedFiles()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            File.WriteAllText(Path.Combine(testDirectory, "photo.jpg"), "content");
            File.WriteAllText(Path.Combine(testDirectory, "photo.xmp"), "sidecar content");

            var scanner = CreateScanner(RelatedFileLookup.None);

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(files.Count).IsEqualTo(2);
            
            var photo = files.OfType<FileWithMetadata>().First(f => f.File.Name == "photo.jpg");
            await Assert.That(photo.RelatedFiles.Count).IsEqualTo(0);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task EnumerateFiles_WithRelatedFileModeStrict_DetectsXmpSidecar()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            File.WriteAllText(Path.Combine(testDirectory, "photo.jpg"), "content");
            File.WriteAllText(Path.Combine(testDirectory, "photo.xmp"), "sidecar content");

            var scanner = CreateScanner(RelatedFileLookup.Strict);

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(files.Count).IsEqualTo(2);
            
            var photo = files.OfType<FileWithMetadata>().First(f => f.File.Name == "photo.jpg");
            await Assert.That(photo.RelatedFiles.Count).IsEqualTo(1);
            await Assert.That(photo.RelatedFiles.First().File.Name).IsEqualTo("photo.xmp");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task EnumerateFiles_WithRelatedFileModeStrict_DetectsJpgXmpSidecar()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange - Common pattern: photo.jpg and photo.jpg.xmp
            File.WriteAllText(Path.Combine(testDirectory, "photo.jpg"), "content");
            File.WriteAllText(Path.Combine(testDirectory, "photo.jpg.xmp"), "sidecar content");

            var scanner = CreateScanner(RelatedFileLookup.Strict);

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            var photo = files.OfType<FileWithMetadata>().First(f => f.File.Name == "photo.jpg");
            await Assert.That(photo.RelatedFiles.Count).IsEqualTo(1);
            await Assert.That(photo.RelatedFiles.First().File.Name).IsEqualTo("photo.jpg.xmp");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task EnumerateFiles_WithRelatedFileModeStrict_DetectsUnderscoreSuffix()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            File.WriteAllText(Path.Combine(testDirectory, "photo.jpg"), "content");
            File.WriteAllText(Path.Combine(testDirectory, "photo_original.jpg"), "original content");

            var scanner = CreateScanner(RelatedFileLookup.Strict);

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            var photo = files.OfType<FileWithMetadata>().First(f => f.File.Name == "photo.jpg");
            await Assert.That(photo.RelatedFiles.Count).IsEqualTo(1);
            await Assert.That(photo.RelatedFiles.First().File.Name).IsEqualTo("photo_original.jpg");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task EnumerateFiles_WithRelatedFileModeLoose_DetectsMoreRelatedFiles()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            File.WriteAllText(Path.Combine(testDirectory, "photo.jpg"), "content");
            File.WriteAllText(Path.Combine(testDirectory, "photo_edit.jpg"), "edited content");
            File.WriteAllText(Path.Combine(testDirectory, "photo123.jpg"), "numbered content");

            var scanner = CreateScanner(RelatedFileLookup.Loose);

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            var photo = files.OfType<FileWithMetadata>().First(f => f.File.Name == "photo.jpg");
            await Assert.That(photo.RelatedFiles.Count).IsEqualTo(2);
            
            var relatedNames = photo.RelatedFiles.Select(f => f.File.Name).ToList();
            await Assert.That(relatedNames).Contains("photo_edit.jpg");
            await Assert.That(relatedNames).Contains("photo123.jpg");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task EnumerateFiles_WithMultipleRelatedFiles_DetectsAll()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange - Simulate RAW + JPEG + XMP workflow
            File.WriteAllText(Path.Combine(testDirectory, "IMG_0001.jpg"), "jpeg content");
            File.WriteAllText(Path.Combine(testDirectory, "IMG_0001.xmp"), "xmp sidecar");
            File.WriteAllText(Path.Combine(testDirectory, "IMG_0001.cr2"), "raw content");

            var scanner = CreateScanner(RelatedFileLookup.Strict);

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(files.Count).IsEqualTo(3);
            
            var jpg = files.OfType<FileWithMetadata>().First(f => f.File.Name == "IMG_0001.jpg");
            await Assert.That(jpg.RelatedFiles.Count).IsEqualTo(2);
            
            var relatedNames = jpg.RelatedFiles.Select(f => f.File.Name).ToList();
            await Assert.That(relatedNames).Contains("IMG_0001.xmp");
            await Assert.That(relatedNames).Contains("IMG_0001.cr2");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task EnumerateFiles_RelatedFilesOnlyInSameDirectory()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var subDir = Path.Combine(testDirectory, "subdir");
            Directory.CreateDirectory(subDir);
            
            File.WriteAllText(Path.Combine(testDirectory, "photo.jpg"), "content");
            File.WriteAllText(Path.Combine(subDir, "photo.xmp"), "sidecar in different dir");

            var scanner = CreateScanner(RelatedFileLookup.Strict);

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            var photo = files.OfType<FileWithMetadata>().First(f => f.File.Name == "photo.jpg");
            // XMP is in a different directory, so it should not be detected as related
            await Assert.That(photo.RelatedFiles.Count).IsEqualTo(0);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region File Path and Metadata Tests

    [Test]
    public async Task EnumerateFiles_PreservesFullFilePath()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var subDir = Path.Combine(testDirectory, "photos", "2024");
            Directory.CreateDirectory(subDir);
            var testFile = Path.Combine(subDir, "vacation.jpg");
            File.WriteAllText(testFile, "content");

            var scanner = CreateScanner();

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(files.Count).IsEqualTo(1);
            await Assert.That(files[0].File.FullName).IsEqualTo(testFile);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task EnumerateFiles_ReturnsCorrectFileInfo()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var testFile = Path.Combine(testDirectory, "test.jpg");
            var content = new byte[1024]; // 1KB file
            new Random().NextBytes(content);
            File.WriteAllBytes(testFile, content);

            var scanner = CreateScanner();

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(files.Count).IsEqualTo(1);
            await Assert.That(files[0].File.Length).IsEqualTo(1024);
            await Assert.That(files[0].File.Extension).IsEqualTo(".jpg");
            await Assert.That(files[0].File.Exists).IsTrue();
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task EnumerateFiles_WithSpecialCharactersInFileName_HandlesCorrectly()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var specialNames = new[] { "photo with spaces.jpg", "photo-with-dashes.jpg", "photo_with_underscores.jpg" };
            foreach (var name in specialNames)
            {
                File.WriteAllText(Path.Combine(testDirectory, name), "content");
            }

            var scanner = CreateScanner();

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(files.Count).IsEqualTo(3);
            var fileNames = files.Select(f => f.File.Name).ToList();
            foreach (var expected in specialNames)
            {
                await Assert.That(fileNames).Contains(expected);
            }
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task EnumerateFiles_WithHiddenFiles_IncludesHiddenFiles()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var normalFile = Path.Combine(testDirectory, "normal.jpg");
            var hiddenFile = Path.Combine(testDirectory, "hidden.jpg");
            
            File.WriteAllText(normalFile, "content");
            File.WriteAllText(hiddenFile, "hidden content");
            File.SetAttributes(hiddenFile, FileAttributes.Hidden);

            var scanner = CreateScanner();

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(files.Count).IsEqualTo(2);
            var fileNames = files.Select(f => f.File.Name).ToList();
            await Assert.That(fileNames).Contains("normal.jpg");
            await Assert.That(fileNames).Contains("hidden.jpg");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task EnumerateFiles_WithCaseInsensitiveExtensions_HandlesCorrectly()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            File.WriteAllText(Path.Combine(testDirectory, "photo1.JPG"), "content");
            File.WriteAllText(Path.Combine(testDirectory, "photo2.jpg"), "content");
            File.WriteAllText(Path.Combine(testDirectory, "photo3.Jpg"), "content");

            var scanner = CreateScanner();

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(files.Count).IsEqualTo(3);
            // All should be FileWithMetadata since .jpg is supported (case-insensitive)
            foreach (var file in files)
            {
                await Assert.That(file).IsTypeOf<FileWithMetadata>();
            }
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task EnumerateFiles_WithLargeNumberOfFiles_HandlesEfficiently()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange - Create 100 files
            for (int i = 0; i < 100; i++)
            {
                File.WriteAllText(Path.Combine(testDirectory, $"photo_{i:D4}.jpg"), $"content {i}");
            }

            var scanner = CreateScanner();

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(files.Count).IsEqualTo(100);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task EnumerateFiles_WithZeroByteFile_HandlesCorrectly()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var emptyFile = Path.Combine(testDirectory, "empty.jpg");
            File.WriteAllBytes(emptyFile, Array.Empty<byte>());

            var scanner = CreateScanner();

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(files.Count).IsEqualTo(1);
            await Assert.That(files[0].File.Length).IsEqualTo(0);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region Multiple Directories in Same Level Tests

    [Test]
    public async Task EnumerateFiles_WithParallelDirectoryStructure_ReturnsAllFiles()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange - Simulate a typical photo library structure
            var dirs = new[]
            {
                Path.Combine(testDirectory, "2023", "January"),
                Path.Combine(testDirectory, "2023", "February"),
                Path.Combine(testDirectory, "2024", "January"),
                Path.Combine(testDirectory, "2024", "February")
            };

            foreach (var dir in dirs)
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "photo.jpg"), "content");
            }

            var scanner = CreateScanner();

            // Act
            var files = scanner.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(files.Count).IsEqualTo(4);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion
}
