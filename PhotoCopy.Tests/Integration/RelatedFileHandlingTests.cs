using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Directories;
using PhotoCopy.Files;
using PhotoCopy.Files.Metadata;
using PhotoCopy.Progress;
using PhotoCopy.Rollback;
using PhotoCopy.Tests.TestingImplementation;
using PhotoCopy.Validators;

namespace PhotoCopy.Tests.Integration;

/// <summary>
/// Integration tests for RelatedFileLookup modes (RAW+JPEG pairs, XMP sidecars, etc.).
/// Tests verify that related files (same base name, different extensions) are properly
/// detected and copied together based on the configured RelatedFileLookup mode.
/// </summary>
[NotInParallel("FileOperations")]
[Property("Category", "Integration,RelatedFiles")]
public class RelatedFileHandlingTests
{
    private string _testBaseDirectory = null!;
    private string _sourceDir = null!;
    private string _destDir = null!;

    [Before(Test)]
    public void Setup()
    {
        _testBaseDirectory = Path.Combine(Path.GetTempPath(), "RelatedFileHandlingTests", Guid.NewGuid().ToString());
        _sourceDir = Path.Combine(_testBaseDirectory, "source");
        _destDir = Path.Combine(_testBaseDirectory, "dest");

        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_destDir);
    }

    [After(Test)]
    public void Cleanup()
    {
        SafeDeleteDirectory(_testBaseDirectory);
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(50); // Brief pause to release file handles
                Directory.Delete(path, true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup - directory may be locked
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup
        }
    }

    #region Helper Methods

    /// <summary>
    /// Creates a test file with specified content.
    /// </summary>
    private async Task<string> CreateTestFileAsync(string filename, string content = "test content", string? subfolder = null)
    {
        var directory = subfolder != null
            ? Path.Combine(_sourceDir, subfolder)
            : _sourceDir;

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var filePath = Path.Combine(directory, filename);
        await File.WriteAllTextAsync(filePath, content);
        return filePath;
    }

    /// <summary>
    /// Creates a test JPEG file with EXIF metadata.
    /// </summary>
    private async Task<string> CreateTestJpegAsync(string filename, DateTime dateTaken, string? subfolder = null)
    {
        var directory = subfolder != null
            ? Path.Combine(_sourceDir, subfolder)
            : _sourceDir;

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var filePath = Path.Combine(directory, filename);
        var jpegBytes = MockImageGenerator.CreateJpeg(dateTaken: dateTaken);
        await File.WriteAllBytesAsync(filePath, jpegBytes);
        return filePath;
    }

    /// <summary>
    /// Creates a RAW file (CR2/NEF/ARW/etc.) with dummy content.
    /// Since we can't create real RAW metadata, we create a file with RAW extension.
    /// </summary>
    private async Task<string> CreateTestRawFileAsync(string filename, string? subfolder = null)
    {
        var directory = subfolder != null
            ? Path.Combine(_sourceDir, subfolder)
            : _sourceDir;

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var filePath = Path.Combine(directory, filename);
        // Create a dummy RAW file with minimal content
        await File.WriteAllBytesAsync(filePath, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 });
        return filePath;
    }

    private DirectoryScanner CreateScanner(RelatedFileLookup relatedFileMode, HashSet<string>? allowedExtensions = null)
    {
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        var config = new PhotoCopyConfig
        {
            Source = _sourceDir,
            Destination = _destDir,
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

    private IServiceProvider BuildServiceProvider(PhotoCopyConfig config)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<IOptions<PhotoCopyConfig>>(Microsoft.Extensions.Options.Options.Create(config));

        var mockGeocodingService = Substitute.For<IReverseGeocodingService>();
        mockGeocodingService.ReverseGeocode(Arg.Any<double>(), Arg.Any<double>())
            .Returns((LocationData?)null);
        services.AddSingleton(mockGeocodingService);

        services.AddSingleton<IChecksumCalculator, Sha256ChecksumCalculator>();
        services.AddTransient<IFileMetadataExtractor, FileMetadataExtractor>();
        services.AddTransient<IMetadataEnricher, MetadataEnricher>();
        services.AddTransient<IMetadataEnrichmentStep, DateTimeMetadataEnrichmentStep>();
        services.AddTransient<IMetadataEnrichmentStep, LocationMetadataEnrichmentStep>();
        services.AddTransient<IMetadataEnrichmentStep, ChecksumMetadataEnrichmentStep>();
        services.AddTransient<IFileFactory, FileFactory>();
        services.AddTransient<IDirectoryScanner, DirectoryScanner>();
        services.AddTransient<IFileSystem, FileSystem>();
        services.AddSingleton<ITransactionLogger, TransactionLogger>();
        services.AddTransient<IDirectoryCopier, DirectoryCopier>();
        services.AddTransient<IValidatorFactory, ValidatorFactory>();
        services.AddTransient<IFileValidationService, FileValidationService>();

        return services.BuildServiceProvider();
    }

    private PhotoCopyConfig CreateConfig(
        RelatedFileLookup relatedFileMode,
        bool dryRun = false,
        OperationMode mode = OperationMode.Copy)
    {
        return new PhotoCopyConfig
        {
            Source = _sourceDir,
            Destination = Path.Combine(_destDir, "{Year}", "{Month}"),
            DryRun = dryRun,
            Mode = mode,
            RelatedFileMode = relatedFileMode,
            CalculateChecksums = false,
            DuplicatesFormat = "_{number}",
            LogLevel = OutputLevel.Verbose,
            AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".heic", ".mov", ".mp4", ".avi",
                ".cr2", ".raf", ".nef", ".arw", ".dng", ".xmp", ".json"
            }
        };
    }

    #endregion

    #region Strict Mode Tests - RAW+JPEG Pairs Detected Together

    [Test]
    public async Task StrictMode_RawAndJpegWithSameBaseName_BothDetectedAsRelated()
    {
        // Arrange - Create a RAW+JPEG pair with the same base name
        await CreateTestJpegAsync("DSC_0001.jpg", new DateTime(2024, 6, 15));
        await CreateTestRawFileAsync("DSC_0001.CR2");

        var scanner = CreateScanner(RelatedFileLookup.Strict);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert - Both files should be enumerated
        files.Should().HaveCount(2);

        // The JPEG should have the RAW as a related file (and vice versa)
        var jpegFile = files.OfType<FileWithMetadata>().FirstOrDefault(f => f.File.Name == "DSC_0001.jpg");
        jpegFile.Should().NotBeNull();
        jpegFile!.RelatedFiles.Should().HaveCount(1);
        jpegFile.RelatedFiles.First().File.Name.Should().Be("DSC_0001.CR2");
    }

    [Test]
    public async Task StrictMode_RawJpegAndXmp_AllDetectedAsRelated()
    {
        // Arrange - Create RAW + JPEG + XMP sidecar with the same base name
        await CreateTestJpegAsync("IMG_1234.jpg", new DateTime(2024, 7, 20));
        await CreateTestRawFileAsync("IMG_1234.CR2");
        await CreateTestFileAsync("IMG_1234.xmp", "<xmp>metadata</xmp>");

        var scanner = CreateScanner(RelatedFileLookup.Strict);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert
        files.Should().HaveCount(3);

        var jpegFile = files.OfType<FileWithMetadata>().FirstOrDefault(f => f.File.Name == "IMG_1234.jpg");
        jpegFile.Should().NotBeNull();
        jpegFile!.RelatedFiles.Should().HaveCount(2);

        var relatedNames = jpegFile.RelatedFiles.Select(f => f.File.Name).ToList();
        relatedNames.Should().Contain("IMG_1234.CR2");
        relatedNames.Should().Contain("IMG_1234.xmp");
    }

    [Test]
    public async Task StrictMode_JpegWithXmpSidecar_DotNotationDetected()
    {
        // Arrange - Create photo.jpg and photo.jpg.xmp pattern (common Lightroom export)
        await CreateTestJpegAsync("vacation.jpg", new DateTime(2024, 5, 10));
        await CreateTestFileAsync("vacation.jpg.xmp", "<xmp>sidecar</xmp>");

        var scanner = CreateScanner(RelatedFileLookup.Strict);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert
        var jpegFile = files.OfType<FileWithMetadata>().FirstOrDefault(f => f.File.Name == "vacation.jpg");
        jpegFile.Should().NotBeNull();
        jpegFile!.RelatedFiles.Should().HaveCount(1);
        jpegFile.RelatedFiles.First().File.Name.Should().Be("vacation.jpg.xmp");
    }

    #endregion

    #region None Mode Tests - No Related File Detection

    [Test]
    public async Task NoneMode_RawAndJpegWithSameBaseName_NotDetectedAsRelated()
    {
        // Arrange
        await CreateTestJpegAsync("DSC_0001.jpg", new DateTime(2024, 6, 15));
        await CreateTestRawFileAsync("DSC_0001.CR2");

        var scanner = CreateScanner(RelatedFileLookup.None);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert - Both files enumerated but no related file relationships
        files.Should().HaveCount(2);

        var jpegFile = files.OfType<FileWithMetadata>().FirstOrDefault(f => f.File.Name == "DSC_0001.jpg");
        jpegFile.Should().NotBeNull();
        jpegFile!.RelatedFiles.Should().BeEmpty();
    }

    [Test]
    public async Task NoneMode_FilesWithSameBaseName_TreatedIndependently()
    {
        // Arrange - Create multiple files with same base name
        await CreateTestJpegAsync("photo.jpg", new DateTime(2024, 1, 1));
        await CreateTestFileAsync("photo.xmp", "xmp content");
        await CreateTestFileAsync("photo.json", "json content");

        var scanner = CreateScanner(RelatedFileLookup.None);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert - All files exist but none have related files
        files.Should().HaveCount(3);
        
        foreach (var file in files.OfType<FileWithMetadata>())
        {
            file.RelatedFiles.Should().BeEmpty();
        }
    }

    #endregion

    #region Loose Mode Tests - Broader Matching

    [Test]
    public async Task LooseMode_FilesWithSimilarNames_DetectedAsRelated()
    {
        // Arrange - Loose mode should match files that start with the same base name
        await CreateTestJpegAsync("photo.jpg", new DateTime(2024, 3, 15));
        await CreateTestFileAsync("photo_edit.jpg", "edited version");
        await CreateTestFileAsync("photography.jpg", "should match in loose mode");

        var scanner = CreateScanner(RelatedFileLookup.Loose);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert
        var photoFile = files.OfType<FileWithMetadata>().FirstOrDefault(f => f.File.Name == "photo.jpg");
        photoFile.Should().NotBeNull();
        
        // In loose mode, both photo_edit.jpg and photography.jpg should be detected as related
        photoFile!.RelatedFiles.Should().HaveCount(2);
        var relatedNames = photoFile.RelatedFiles.Select(f => f.File.Name).ToList();
        relatedNames.Should().Contain("photo_edit.jpg");
        relatedNames.Should().Contain("photography.jpg");
    }

    [Test]
    public async Task LooseMode_NumberedSequence_DetectedAsRelated()
    {
        // Arrange - Loose mode detects numbered sequences
        await CreateTestJpegAsync("DSC0001.jpg", new DateTime(2024, 4, 20));
        await CreateTestFileAsync("DSC00010.jpg", "similar numbering");
        await CreateTestFileAsync("DSC000100.jpg", "extended numbering");

        var scanner = CreateScanner(RelatedFileLookup.Loose);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert
        var baseFile = files.OfType<FileWithMetadata>().FirstOrDefault(f => f.File.Name == "DSC0001.jpg");
        baseFile.Should().NotBeNull();
        
        // Files starting with DSC0001 should match
        baseFile!.RelatedFiles.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    #endregion

    #region RAW Format Recognition Tests

    [Test]
    public async Task RawExtensions_CanonCr2_IsRecognized()
    {
        // Arrange
        await CreateTestRawFileAsync("canon_photo.CR2");

        var scanner = CreateScanner(RelatedFileLookup.Strict);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert
        files.Should().HaveCount(1);
        files.First().File.Extension.Should().BeEquivalentTo(".CR2");
    }

    [Test]
    public async Task RawExtensions_FujifilmRaf_IsRecognized()
    {
        // Arrange
        await CreateTestRawFileAsync("fuji_photo.RAF");

        var scanner = CreateScanner(RelatedFileLookup.Strict);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert
        files.Should().HaveCount(1);
        files.First().File.Extension.Should().BeEquivalentTo(".RAF");
    }

    [Test]
    public async Task RawExtensions_NikonNef_IsRecognized()
    {
        // Arrange
        await CreateTestRawFileAsync("nikon_photo.NEF");

        var scanner = CreateScanner(RelatedFileLookup.Strict);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert
        files.Should().HaveCount(1);
        files.First().File.Extension.Should().BeEquivalentTo(".NEF");
    }

    [Test]
    public async Task RawExtensions_SonyArw_IsRecognized()
    {
        // Arrange
        await CreateTestRawFileAsync("sony_photo.ARW");

        var scanner = CreateScanner(RelatedFileLookup.Strict);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert
        files.Should().HaveCount(1);
        files.First().File.Extension.Should().BeEquivalentTo(".ARW");
    }

    [Test]
    public async Task RawExtensions_AdobeDng_IsRecognized()
    {
        // Arrange
        await CreateTestRawFileAsync("adobe_photo.DNG");

        var scanner = CreateScanner(RelatedFileLookup.Strict);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert
        files.Should().HaveCount(1);
        files.First().File.Extension.Should().BeEquivalentTo(".DNG");
    }

    [Test]
    public async Task RawExtensions_AllSupported_AreEnumerated()
    {
        // Arrange - Create files for all supported RAW formats
        await CreateTestRawFileAsync("photo.CR2");
        await CreateTestRawFileAsync("photo2.RAF");
        await CreateTestRawFileAsync("photo3.NEF");
        await CreateTestRawFileAsync("photo4.ARW");
        await CreateTestRawFileAsync("photo5.DNG");

        var scanner = CreateScanner(RelatedFileLookup.None);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert - All RAW formats should be recognized
        files.Should().HaveCount(5);
        var extensions = files.Select(f => f.File.Extension.ToUpperInvariant()).ToList();
        extensions.Should().Contain(".CR2");
        extensions.Should().Contain(".RAF");
        extensions.Should().Contain(".NEF");
        extensions.Should().Contain(".ARW");
        extensions.Should().Contain(".DNG");
    }

    #endregion

    #region Different Base Names Tests

    [Test]
    public async Task StrictMode_DifferentBaseNames_NotTreatedAsRelated()
    {
        // Arrange - DSC_0001.CR2 and DSC_0002.JPG should NOT be related
        await CreateTestRawFileAsync("DSC_0001.CR2");
        await CreateTestJpegAsync("DSC_0002.jpg", new DateTime(2024, 6, 15));

        var scanner = CreateScanner(RelatedFileLookup.Strict);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert
        files.Should().HaveCount(2);

        // Neither file should have related files
        foreach (var file in files.OfType<FileWithMetadata>())
        {
            file.RelatedFiles.Should().BeEmpty();
        }
    }

    [Test]
    public async Task StrictMode_SimilarButNotSameBaseName_NotTreatedAsRelated()
    {
        // Arrange - IMG_1234 and IMG_12345 should NOT be related in strict mode
        await CreateTestJpegAsync("IMG_1234.jpg", new DateTime(2024, 7, 20));
        await CreateTestRawFileAsync("IMG_12345.CR2");

        var scanner = CreateScanner(RelatedFileLookup.Strict);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert
        files.Should().HaveCount(2);

        var img1234 = files.OfType<FileWithMetadata>().FirstOrDefault(f => f.File.Name == "IMG_1234.jpg");
        img1234.Should().NotBeNull();
        // IMG_12345.CR2 should NOT be related because the base name is different
        img1234!.RelatedFiles.Should().BeEmpty();
    }

    #endregion

    #region Same Name Different Folders Tests

    [Test]
    public async Task StrictMode_SameBaseName_DifferentFolders_NotTreatedAsRelated()
    {
        // Arrange - Same base name in different folders should NOT be related
        await CreateTestJpegAsync("photo.jpg", new DateTime(2024, 8, 1), subfolder: "folder1");
        await CreateTestRawFileAsync("photo.CR2", subfolder: "folder2");

        var scanner = CreateScanner(RelatedFileLookup.Strict);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert
        files.Should().HaveCount(2);

        // Files in different directories should NOT be related
        foreach (var file in files.OfType<FileWithMetadata>())
        {
            file.RelatedFiles.Should().BeEmpty();
        }
    }

    [Test]
    public async Task StrictMode_SameBaseName_SameFolder_AreRelated()
    {
        // Arrange - Same base name in same folder SHOULD be related
        await CreateTestJpegAsync("photo.jpg", new DateTime(2024, 8, 1), subfolder: "vacation");
        await CreateTestRawFileAsync("photo.CR2", subfolder: "vacation");
        await CreateTestFileAsync("photo.xmp", "xmp data", subfolder: "vacation");

        var scanner = CreateScanner(RelatedFileLookup.Strict);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert
        files.Should().HaveCount(3);

        var jpegFile = files.OfType<FileWithMetadata>().FirstOrDefault(f => f.File.Name == "photo.jpg");
        jpegFile.Should().NotBeNull();
        jpegFile!.RelatedFiles.Should().HaveCount(2);
    }

    [Test]
    public async Task StrictMode_MultipleSubfolders_EachGroupedSeparately()
    {
        // Arrange - Create photo sets in different folders
        await CreateTestJpegAsync("DSC_0001.jpg", new DateTime(2024, 1, 1), subfolder: "day1");
        await CreateTestRawFileAsync("DSC_0001.CR2", subfolder: "day1");

        await CreateTestJpegAsync("DSC_0001.jpg", new DateTime(2024, 1, 2), subfolder: "day2");
        await CreateTestRawFileAsync("DSC_0001.CR2", subfolder: "day2");

        var scanner = CreateScanner(RelatedFileLookup.Strict);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert
        files.Should().HaveCount(4);

        // Group files by directory
        var day1Files = files.Where(f => f.File.DirectoryName!.EndsWith("day1")).ToList();
        var day2Files = files.Where(f => f.File.DirectoryName!.EndsWith("day2")).ToList();

        day1Files.Should().HaveCount(2);
        day2Files.Should().HaveCount(2);

        // Each JPEG in its own folder should only have its sibling as related
        var day1Jpeg = day1Files.OfType<FileWithMetadata>().FirstOrDefault(f => f.File.Name == "DSC_0001.jpg");
        day1Jpeg.Should().NotBeNull();
        day1Jpeg!.RelatedFiles.Should().HaveCount(1);
        day1Jpeg.RelatedFiles.First().File.DirectoryName.Should().EndWith("day1");
    }

    #endregion

    #region Underscore Suffix Pattern Tests

    [Test]
    public async Task StrictMode_UnderscoreSuffix_DetectedAsRelated()
    {
        // Arrange - photo_original.jpg should be related to photo.jpg
        await CreateTestJpegAsync("photo.jpg", new DateTime(2024, 9, 1));
        await CreateTestFileAsync("photo_original.jpg", "original version");
        await CreateTestFileAsync("photo_edited.jpg", "edited version");

        var scanner = CreateScanner(RelatedFileLookup.Strict);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert
        var mainPhoto = files.OfType<FileWithMetadata>().FirstOrDefault(f => f.File.Name == "photo.jpg");
        mainPhoto.Should().NotBeNull();
        mainPhoto!.RelatedFiles.Should().HaveCount(2);

        var relatedNames = mainPhoto.RelatedFiles.Select(f => f.File.Name).ToList();
        relatedNames.Should().Contain("photo_original.jpg");
        relatedNames.Should().Contain("photo_edited.jpg");
    }

    #endregion

    #region Case Sensitivity Tests

    [Test]
    public async Task StrictMode_CaseInsensitive_RelatedFilesDetected()
    {
        // Arrange - Mixed case should still be detected as related
        await CreateTestJpegAsync("Photo.JPG", new DateTime(2024, 10, 1));
        await CreateTestFileAsync("PHOTO.xmp", "xmp uppercase");
        await CreateTestRawFileAsync("photo.cr2");

        var scanner = CreateScanner(RelatedFileLookup.Strict);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert
        files.Should().HaveCount(3);

        var jpgFile = files.OfType<FileWithMetadata>().FirstOrDefault(f => 
            f.File.Name.Equals("Photo.JPG", StringComparison.OrdinalIgnoreCase));
        jpgFile.Should().NotBeNull();
        jpgFile!.RelatedFiles.Should().HaveCount(2);
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task StrictMode_SingleFile_NoRelatedFiles()
    {
        // Arrange - Just one file, no related files
        await CreateTestJpegAsync("lonely.jpg", new DateTime(2024, 11, 1));

        var scanner = CreateScanner(RelatedFileLookup.Strict);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert
        files.Should().HaveCount(1);
        var file = files.OfType<FileWithMetadata>().First();
        file.RelatedFiles.Should().BeEmpty();
    }

    [Test]
    public async Task StrictMode_EmptyDirectory_ReturnsNoFiles()
    {
        // Arrange - Empty source directory (already created in Setup)
        var scanner = CreateScanner(RelatedFileLookup.Strict);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert
        files.Should().BeEmpty();
    }

    [Test]
    public async Task StrictMode_JsonSidecar_DetectedAsRelated()
    {
        // Arrange - JSON metadata files (common with Google Takeout exports)
        await CreateTestJpegAsync("IMG_20240101.jpg", new DateTime(2024, 1, 1));
        await CreateTestFileAsync("IMG_20240101.json", "{\"title\": \"photo\"}");

        var scanner = CreateScanner(RelatedFileLookup.Strict);

        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert
        files.Should().HaveCount(2);

        var jpgFile = files.OfType<FileWithMetadata>().FirstOrDefault(f => f.File.Name == "IMG_20240101.jpg");
        jpgFile.Should().NotBeNull();
        jpgFile!.RelatedFiles.Should().HaveCount(1);
        jpgFile.RelatedFiles.First().File.Name.Should().Be("IMG_20240101.json");
    }

    #endregion
}
