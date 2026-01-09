using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Directories;
using PhotoCopy.Duplicates;
using PhotoCopy.Files;
using PhotoCopy.Files.Metadata;
using PhotoCopy.Progress;
using PhotoCopy.Rollback;
using PhotoCopy.Tests.TestingImplementation;
using PhotoCopy.Validators;
using Directory = System.IO.Directory;

namespace PhotoCopy.Tests.Integration;

/// <summary>
/// Integration tests for duplicate handling during copy operations.
/// These tests validate the behavior of SkipExisting, Overwrite, and DuplicatesFormat
/// options when files with the same name or content exist in the destination.
/// 
/// Tests use:
/// 1. Real temp directories for file operations
/// 2. MockImageGenerator for creating test JPEG/PNG files
/// 3. Real implementations of DirectoryScanner, FileSystem, DirectoryCopierAsync
/// </summary>
[NotInParallel("FileOperations")]
[Property("Category", "Integration")]
public class DuplicateHandlingCopyTests
{
    private string _testBaseDirectory = null!;
    private string _sourceDir = null!;
    private string _destDir = null!;

    [Before(Test)]
    public void Setup()
    {
        _testBaseDirectory = Path.Combine(Path.GetTempPath(), "DuplicateHandlingCopyTests", Guid.NewGuid().ToString());
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
                Thread.Sleep(50);
                Directory.Delete(path, true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Creates a test JPEG file with real EXIF metadata.
    /// </summary>
    private async Task<string> CreateTestJpegAsync(
        string filename,
        DateTime dateTaken,
        (double Lat, double Lon)? gps = null,
        string? directory = null)
    {
        var targetDirectory = directory ?? _sourceDir;
        
        if (!Directory.Exists(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        var filePath = Path.Combine(targetDirectory, filename);
        var jpegBytes = MockImageGenerator.CreateJpeg(dateTaken: dateTaken, gps: gps);
        await File.WriteAllBytesAsync(filePath, jpegBytes);
        
        return filePath;
    }

    /// <summary>
    /// Creates a test file with specific content (for checksum testing).
    /// </summary>
    private async Task<string> CreateTestFileWithContentAsync(
        string filename,
        byte[] content,
        string? directory = null)
    {
        var targetDirectory = directory ?? _sourceDir;
        
        if (!Directory.Exists(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        var filePath = Path.Combine(targetDirectory, filename);
        await File.WriteAllBytesAsync(filePath, content);
        
        return filePath;
    }

    /// <summary>
    /// Builds a fully configured service provider with REAL implementations.
    /// </summary>
    private IServiceProvider BuildRealServiceProvider(PhotoCopyConfig config)
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
        services.AddTransient<IDirectoryCopierAsync, DirectoryCopierAsync>();
        services.AddTransient<IDirectoryCopier, DirectoryCopier>();
        services.AddTransient<IValidatorFactory, ValidatorFactory>();
        services.AddTransient<IFileValidationService, FileValidationService>();
        services.AddTransient<IDuplicateDetector, DuplicateDetector>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates a PhotoCopyConfig with the test directories.
    /// </summary>
    private PhotoCopyConfig CreateConfig(
        string destinationTemplate,
        bool dryRun = false,
        bool skipExisting = false,
        bool overwrite = false,
        string duplicatesFormat = "_{number}",
        bool calculateChecksums = false,
        DuplicateHandling duplicateHandling = DuplicateHandling.None)
    {
        return new PhotoCopyConfig
        {
            Source = _sourceDir,
            Destination = destinationTemplate,
            DryRun = dryRun,
            Mode = OperationMode.Copy,
            SkipExisting = skipExisting,
            Overwrite = overwrite,
            DuplicatesFormat = duplicatesFormat,
            CalculateChecksums = calculateChecksums,
            DuplicateHandling = duplicateHandling,
            LogLevel = OutputLevel.Verbose,
            AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".heic", ".mov", ".mp4"
            }
        };
    }

    #region Skip Handling Tests

    [Test]
    public async Task DuplicateHandling_Skip_DoesNotOverwriteExisting()
    {
        // Arrange - Create source file and existing destination file with different content
        var dateTaken = new DateTime(2024, 5, 15);
        await CreateTestJpegAsync("photo.jpg", dateTaken);
        
        var destFolder = Path.Combine(_destDir, "2024", "05");
        Directory.CreateDirectory(destFolder);
        var existingDestPath = Path.Combine(destFolder, "photo.jpg");
        var originalContent = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        await File.WriteAllBytesAsync(existingDestPath, originalContent);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            skipExisting: true);
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - Original file should be preserved
        var actualContent = await File.ReadAllBytesAsync(existingDestPath);
        await Assert.That(actualContent).IsEquivalentTo(originalContent)
            .Because("Skip mode should preserve the existing file content");
        
        // No numbered version should be created
        await Assert.That(File.Exists(Path.Combine(destFolder, "photo_1.jpg"))).IsFalse()
            .Because("Skip mode should not create a numbered duplicate");
    }

    #endregion

    #region Replace/Overwrite Handling Tests

    [Test]
    public async Task DuplicateHandling_Replace_OverwritesExistingFile()
    {
        // Arrange - Create source file and existing destination file
        var dateTaken = new DateTime(2024, 6, 20);
        var sourceFile = await CreateTestJpegAsync("photo.jpg", dateTaken);
        var sourceContent = await File.ReadAllBytesAsync(sourceFile);
        
        var destFolder = Path.Combine(_destDir, "2024", "06");
        Directory.CreateDirectory(destFolder);
        var existingDestPath = Path.Combine(destFolder, "photo.jpg");
        var originalContent = new byte[] { 0xAA, 0xBB, 0xCC };
        await File.WriteAllBytesAsync(existingDestPath, originalContent);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            overwrite: true);
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - File should be overwritten with source content
        var actualContent = await File.ReadAllBytesAsync(existingDestPath);
        await Assert.That(actualContent).IsEquivalentTo(sourceContent)
            .Because("Overwrite mode should replace the existing file with source content");
        await Assert.That(actualContent.Length).IsGreaterThan(originalContent.Length)
            .Because("Source JPEG content should be larger than the stub original");
    }

    #endregion

    #region Rename/Numbered Version Tests

    [Test]
    public async Task DuplicateHandling_Rename_CreatesNumberedVersion()
    {
        // Arrange - Create source file and existing destination file
        var dateTaken = new DateTime(2024, 7, 10);
        await CreateTestJpegAsync("vacation.jpg", dateTaken);
        
        var destFolder = Path.Combine(_destDir, "2024", "07");
        Directory.CreateDirectory(destFolder);
        var existingDestPath = Path.Combine(destFolder, "vacation.jpg");
        await File.WriteAllBytesAsync(existingDestPath, new byte[] { 0x01 });

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            duplicatesFormat: "_{number}");
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - Both original and numbered version should exist
        await Assert.That(File.Exists(existingDestPath)).IsTrue()
            .Because("Original destination file should be preserved");
        await Assert.That(File.Exists(Path.Combine(destFolder, "vacation_1.jpg"))).IsTrue()
            .Because("A numbered version should be created when duplicate exists");
    }

    [Test]
    public async Task MultipleDuplicates_AllHandledCorrectly()
    {
        // Arrange - Create source file and multiple existing duplicates
        var dateTaken = new DateTime(2024, 8, 5);
        await CreateTestJpegAsync("image.jpg", dateTaken);
        
        var destFolder = Path.Combine(_destDir, "2024", "08");
        Directory.CreateDirectory(destFolder);
        
        // Pre-create existing files: image.jpg, image_1.jpg, image_2.jpg
        await File.WriteAllBytesAsync(Path.Combine(destFolder, "image.jpg"), new byte[] { 0x01 });
        await File.WriteAllBytesAsync(Path.Combine(destFolder, "image_1.jpg"), new byte[] { 0x02 });
        await File.WriteAllBytesAsync(Path.Combine(destFolder, "image_2.jpg"), new byte[] { 0x03 });

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            duplicatesFormat: "_{number}");
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - Should create image_3.jpg
        await Assert.That(File.Exists(Path.Combine(destFolder, "image_3.jpg"))).IsTrue()
            .Because("Should find next available number suffix (3) when 0, 1, 2 exist");
        
        // Verify original files still exist
        await Assert.That(File.Exists(Path.Combine(destFolder, "image.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(destFolder, "image_1.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(destFolder, "image_2.jpg"))).IsTrue();
    }

    #endregion

    #region Checksum-Based Duplicate Detection Tests

    [Test]
    public async Task DuplicatesWithChecksum_IdenticalContent_DetectedAsDuplicate()
    {
        // Arrange - Create two source files with identical content but different names
        var dateTaken = new DateTime(2024, 9, 1);
        var jpegContent = MockImageGenerator.CreateJpeg(dateTaken: dateTaken);
        
        await CreateTestFileWithContentAsync("photo_original.jpg", jpegContent);
        await CreateTestFileWithContentAsync("photo_copy.jpg", jpegContent);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            calculateChecksums: true,
            duplicateHandling: DuplicateHandling.SkipDuplicates);
        var serviceProvider = BuildRealServiceProvider(config);
        
        var detector = serviceProvider.GetRequiredService<IDuplicateDetector>();
        var checksumCalculator = serviceProvider.GetRequiredService<IChecksumCalculator>();

        // Calculate checksums for both files
        var file1Info = new FileInfo(Path.Combine(_sourceDir, "photo_original.jpg"));
        var file2Info = new FileInfo(Path.Combine(_sourceDir, "photo_copy.jpg"));
        var checksum1 = checksumCalculator.Calculate(file1Info);
        var checksum2 = checksumCalculator.Calculate(file2Info);

        // Assert - Both files should have the same checksum
        await Assert.That(checksum1).IsEqualTo(checksum2)
            .Because("Files with identical content should have the same checksum");
        await Assert.That(checksum1).IsNotNull();
    }

    [Test]
    public async Task DuplicatesWithChecksum_DifferentContent_NotDetectedAsDuplicate()
    {
        // Arrange - Create two files with same name pattern but different content
        var date1 = new DateTime(2024, 9, 10);
        var date2 = new DateTime(2024, 9, 11);
        
        // Different dates = different EXIF = different content = different checksums
        var jpegContent1 = MockImageGenerator.CreateJpeg(dateTaken: date1);
        var jpegContent2 = MockImageGenerator.CreateJpeg(dateTaken: date2);
        
        await CreateTestFileWithContentAsync("photo.jpg", jpegContent1, Path.Combine(_sourceDir, "folder1"));
        await CreateTestFileWithContentAsync("photo.jpg", jpegContent2, Path.Combine(_sourceDir, "folder2"));

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            calculateChecksums: true);
        var serviceProvider = BuildRealServiceProvider(config);
        
        var checksumCalculator = serviceProvider.GetRequiredService<IChecksumCalculator>();

        // Calculate checksums for both files
        var file1Info = new FileInfo(Path.Combine(_sourceDir, "folder1", "photo.jpg"));
        var file2Info = new FileInfo(Path.Combine(_sourceDir, "folder2", "photo.jpg"));
        var checksum1 = checksumCalculator.Calculate(file1Info);
        var checksum2 = checksumCalculator.Calculate(file2Info);

        // Assert - Files with different content should have different checksums
        await Assert.That(checksum1).IsNotEqualTo(checksum2)
            .Because("Files with different content should have different checksums");
    }

    [Test]
    public async Task DuplicatesWithoutChecksum_SameNameOnly_DetectedAsDuplicate()
    {
        // Arrange - Without checksums, files are detected as duplicates by path alone
        var dateTaken = new DateTime(2024, 10, 1);
        await CreateTestJpegAsync("sunset.jpg", dateTaken);
        
        var destFolder = Path.Combine(_destDir, "2024", "10");
        Directory.CreateDirectory(destFolder);
        var existingDestPath = Path.Combine(destFolder, "sunset.jpg");
        await File.WriteAllBytesAsync(existingDestPath, new byte[] { 0xFF, 0xD8, 0xFF }); // Different content

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            calculateChecksums: false,  // Checksums disabled
            duplicatesFormat: "_{number}");
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - Name collision detected, numbered version created
        await Assert.That(File.Exists(existingDestPath)).IsTrue()
            .Because("Original destination file should be preserved");
        await Assert.That(File.Exists(Path.Combine(destFolder, "sunset_1.jpg"))).IsTrue()
            .Because("Name-based duplicate detection creates numbered version regardless of content");
    }

    #endregion

    #region Cross-Batch Duplicate Tests

    [Test]
    public async Task CrossBatchDuplicates_ExistingInDestination_HandledCorrectly()
    {
        // Arrange - Simulate a second batch copy where destination already has files from first batch
        var dateTaken = new DateTime(2024, 11, 15);
        
        // First batch: Create files already in destination
        var destFolder = Path.Combine(_destDir, "2024", "11");
        Directory.CreateDirectory(destFolder);
        var firstBatchContent = MockImageGenerator.CreateJpeg(dateTaken: dateTaken);
        await File.WriteAllBytesAsync(Path.Combine(destFolder, "photo.jpg"), firstBatchContent);
        await File.WriteAllBytesAsync(Path.Combine(destFolder, "photo_1.jpg"), firstBatchContent);
        
        // Second batch: Source file with same name
        await CreateTestJpegAsync("photo.jpg", dateTaken);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            duplicatesFormat: "_{number}");
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act - Copy second batch
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - Should create photo_2.jpg
        await Assert.That(File.Exists(Path.Combine(destFolder, "photo_2.jpg"))).IsTrue()
            .Because("Second batch should create next numbered version after existing files");
        await Assert.That(result.FilesProcessed).IsEqualTo(1);
        await Assert.That(result.FilesFailed).IsEqualTo(0);
    }

    #endregion

    #region Custom Rename Pattern Tests

    [Test]
    public async Task DuplicateRenamePattern_FollowsConfiguredFormat()
    {
        // Arrange - Use custom duplicate format
        var dateTaken = new DateTime(2024, 12, 1);
        await CreateTestJpegAsync("holiday.jpg", dateTaken);
        
        var destFolder = Path.Combine(_destDir, "2024", "12");
        Directory.CreateDirectory(destFolder);
        await File.WriteAllBytesAsync(Path.Combine(destFolder, "holiday.jpg"), new byte[] { 0x01 });

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            duplicatesFormat: "-copy{number}");  // Custom format: holiday-copy1.jpg
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - Should use custom format
        await Assert.That(File.Exists(Path.Combine(destFolder, "holiday-copy1.jpg"))).IsTrue()
            .Because("Should use the configured duplicate format pattern");
    }

    [Test]
    public async Task DuplicateRenamePattern_WithParentheses_CreatesCorrectFilename()
    {
        // Arrange - Use parentheses format like Windows Explorer
        var dateTaken = new DateTime(2024, 12, 10);
        await CreateTestJpegAsync("document.jpg", dateTaken);
        
        var destFolder = Path.Combine(_destDir, "2024", "12");
        Directory.CreateDirectory(destFolder);
        await File.WriteAllBytesAsync(Path.Combine(destFolder, "document.jpg"), new byte[] { 0x01 });

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            duplicatesFormat: " ({number})");  // Windows-style: document (1).jpg
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(File.Exists(Path.Combine(destFolder, "document (1).jpg"))).IsTrue()
            .Because("Should create Windows-style numbered duplicate");
    }

    [Test]
    public async Task DuplicateRenamePattern_MultipleWithCustomFormat_SequentialNumbers()
    {
        // Arrange
        var dateTaken = new DateTime(2024, 12, 20);
        await CreateTestJpegAsync("report.jpg", dateTaken);
        
        var destFolder = Path.Combine(_destDir, "2024", "12");
        Directory.CreateDirectory(destFolder);
        
        // Pre-create files with the custom pattern
        await File.WriteAllBytesAsync(Path.Combine(destFolder, "report.jpg"), new byte[] { 0x01 });
        await File.WriteAllBytesAsync(Path.Combine(destFolder, "report_v1.jpg"), new byte[] { 0x02 });
        await File.WriteAllBytesAsync(Path.Combine(destFolder, "report_v2.jpg"), new byte[] { 0x03 });

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            duplicatesFormat: "_v{number}");  // Version style: report_v1.jpg
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - Should find next available version number
        await Assert.That(File.Exists(Path.Combine(destFolder, "report_v3.jpg"))).IsTrue()
            .Because("Should continue sequence with configured format pattern");
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task DuplicateHandling_SkipAndOverwriteBothFalse_CreatesNumbered()
    {
        // Arrange - Default behavior: neither skip nor overwrite, should rename
        var dateTaken = new DateTime(2024, 4, 1);
        await CreateTestJpegAsync("default.jpg", dateTaken);
        
        var destFolder = Path.Combine(_destDir, "2024", "04");
        Directory.CreateDirectory(destFolder);
        await File.WriteAllBytesAsync(Path.Combine(destFolder, "default.jpg"), new byte[] { 0x01 });

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            skipExisting: false,
            overwrite: false,
            duplicatesFormat: "_{number}");
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - Default behavior is to create numbered version
        await Assert.That(File.Exists(Path.Combine(destFolder, "default_1.jpg"))).IsTrue()
            .Because("Default behavior (no skip, no overwrite) should create numbered version");
    }

    [Test]
    public async Task DuplicateHandling_MultipleSourceFiles_AllProcessedCorrectly()
    {
        // Arrange - Multiple source files targeting same destination folder
        var dateTaken = new DateTime(2024, 3, 15);
        await CreateTestJpegAsync("photo1.jpg", dateTaken);
        await CreateTestJpegAsync("photo2.jpg", dateTaken);
        await CreateTestJpegAsync("photo3.jpg", dateTaken);
        
        // Pre-create one existing file
        var destFolder = Path.Combine(_destDir, "2024", "03");
        Directory.CreateDirectory(destFolder);
        await File.WriteAllBytesAsync(Path.Combine(destFolder, "photo2.jpg"), new byte[] { 0x01 });

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            duplicatesFormat: "_{number}");
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(File.Exists(Path.Combine(destFolder, "photo1.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(destFolder, "photo2.jpg"))).IsTrue();  // Original preserved
        await Assert.That(File.Exists(Path.Combine(destFolder, "photo2_1.jpg"))).IsTrue(); // New file renamed
        await Assert.That(File.Exists(Path.Combine(destFolder, "photo3.jpg"))).IsTrue();
        await Assert.That(result.FilesProcessed).IsEqualTo(3);
    }

    [Test]
    public async Task DuplicateHandling_VeryLargeNumber_StillFindsAvailable()
    {
        // Arrange - Create many existing duplicates to test boundary conditions
        var dateTaken = new DateTime(2024, 2, 1);
        await CreateTestJpegAsync("many.jpg", dateTaken);
        
        var destFolder = Path.Combine(_destDir, "2024", "02");
        Directory.CreateDirectory(destFolder);
        
        // Pre-create 0 through 9
        await File.WriteAllBytesAsync(Path.Combine(destFolder, "many.jpg"), new byte[] { 0x01 });
        for (int i = 1; i <= 9; i++)
        {
            await File.WriteAllBytesAsync(Path.Combine(destFolder, $"many_{i}.jpg"), new byte[] { (byte)i });
        }

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            duplicatesFormat: "_{number}");
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - Should create many_10.jpg
        await Assert.That(File.Exists(Path.Combine(destFolder, "many_10.jpg"))).IsTrue()
            .Because("Should handle double-digit numbering correctly");
    }

    #endregion
}
