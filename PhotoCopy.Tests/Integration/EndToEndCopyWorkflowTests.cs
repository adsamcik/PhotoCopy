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
/// End-to-end tests that exercise the REAL metadata extraction pipeline.
/// These tests:
/// 1. Use MockImageGenerator to create real JPEG/PNG files with embedded EXIF metadata
/// 2. Write these files to actual temp directories
/// 3. Use REAL implementations of DirectoryScanner, FileSystem, FileFactory, FileMetadataExtractor
/// 4. Verify files are copied to correct destination paths based on EXTRACTED metadata
/// 
/// This bypasses the InMemoryFileSystem and tests the actual production code path.
/// </summary>
[NotInParallel("FileOperations")]
[Property("Category", "Integration,EndToEnd")]
public class EndToEndCopyWorkflowTests
{
    private string _testBaseDirectory = null!;
    private string _sourceDir = null!;
    private string _destDir = null!;

    [Before(Test)]
    public void Setup()
    {
        _testBaseDirectory = Path.Combine(Path.GetTempPath(), "EndToEndCopyWorkflowTests", Guid.NewGuid().ToString());
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

    /// <summary>
    /// Creates a test JPEG file with real EXIF metadata.
    /// </summary>
    private async Task<string> CreateTestJpegAsync(
        string filename,
        DateTime dateTaken,
        (double Lat, double Lon)? gps = null,
        string? subfolder = null)
    {
        var directory = subfolder != null 
            ? Path.Combine(_sourceDir, subfolder) 
            : _sourceDir;
        
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var filePath = Path.Combine(directory, filename);
        var jpegBytes = MockImageGenerator.CreateJpeg(dateTaken: dateTaken, gps: gps);
        await File.WriteAllBytesAsync(filePath, jpegBytes);
        
        return filePath;
    }

    /// <summary>
    /// Creates a test PNG file with real EXIF metadata.
    /// </summary>
    private async Task<string> CreateTestPngAsync(
        string filename,
        DateTime dateTaken,
        (double Lat, double Lon)? gps = null,
        string? subfolder = null)
    {
        var directory = subfolder != null 
            ? Path.Combine(_sourceDir, subfolder) 
            : _sourceDir;
        
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var filePath = Path.Combine(directory, filename);
        var pngBytes = MockImageGenerator.CreatePng(dateTaken: dateTaken, gps: gps);
        await File.WriteAllBytesAsync(filePath, pngBytes);
        
        return filePath;
    }

    /// <summary>
    /// Builds a fully configured service provider with REAL implementations.
    /// </summary>
    private IServiceProvider BuildRealServiceProvider(PhotoCopyConfig config)
    {
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));

        // Register configuration
        services.AddSingleton<IOptions<PhotoCopyConfig>>(Microsoft.Extensions.Options.Options.Create(config));

        // Mock IReverseGeocodingService since we don't want real network calls
        var mockGeocodingService = Substitute.For<IReverseGeocodingService>();
        mockGeocodingService.ReverseGeocode(Arg.Any<double>(), Arg.Any<double>())
            .Returns((LocationData?)null);
        services.AddSingleton(mockGeocodingService);

        // Register REAL core services - this is the key difference from other tests
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

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates a PhotoCopyConfig with the test directories.
    /// </summary>
    private PhotoCopyConfig CreateConfig(
        string destinationTemplate,
        bool dryRun = false,
        OperationMode mode = OperationMode.Copy,
        DateTime? minDate = null,
        DateTime? maxDate = null,
        bool skipExisting = false,
        DuplicateHandling duplicateHandling = DuplicateHandling.None)
    {
        return new PhotoCopyConfig
        {
            Source = _sourceDir,
            Destination = destinationTemplate,
            DryRun = dryRun,
            Mode = mode,
            MinDate = minDate,
            MaxDate = maxDate,
            SkipExisting = skipExisting,
            DuplicateHandling = duplicateHandling,
            CalculateChecksums = false, // Disable for faster tests
            DuplicatesFormat = "_{number}",
            LogLevel = OutputLevel.Verbose,
            AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".heic", ".mov", ".mp4"
            }
        };
    }

    #region Single Photo Copy Tests

    [Test]
    public async Task SinglePhoto_CopiedToCorrectYearMonthPath_BasedOnExtractedExifDate()
    {
        // Arrange - Create a JPEG with a specific date embedded in EXIF
        var dateTaken = new DateTime(2023, 7, 15, 14, 30, 45);
        await CreateTestJpegAsync("vacation.jpg", dateTaken);

        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - File should be in 2023/07 based on EXTRACTED EXIF date
        var expectedPath = Path.Combine(_destDir, "2023", "07", "vacation.jpg");
        await Assert.That(File.Exists(expectedPath)).IsTrue()
            .Because($"File should be copied to {expectedPath} based on EXIF DateTimeOriginal");
        await Assert.That(result.FilesProcessed).IsEqualTo(1);
        await Assert.That(result.FilesFailed).IsEqualTo(0);
    }

    [Test]
    public async Task SinglePhoto_WithFullDateTemplate_CreatesCorrectPath()
    {
        // Arrange
        var dateTaken = new DateTime(2024, 12, 25, 10, 0, 0);
        await CreateTestJpegAsync("christmas.jpg", dateTaken);

        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{day}", "{name}{ext}"));
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        var expectedPath = Path.Combine(_destDir, "2024", "12", "25", "christmas.jpg");
        await Assert.That(File.Exists(expectedPath)).IsTrue()
            .Because("File should be organized by year/month/day based on EXIF date");
        await Assert.That(result.FilesProcessed).IsEqualTo(1);
    }

    [Test]
    public async Task PngPhoto_WithExifMetadata_CopiedToCorrectPath()
    {
        // Arrange - Test PNG with EXIF in eXIf chunk
        var dateTaken = new DateTime(2022, 3, 20, 9, 15, 0);
        await CreateTestPngAsync("screenshot.png", dateTaken);

        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        var expectedPath = Path.Combine(_destDir, "2022", "03", "screenshot.png");
        await Assert.That(File.Exists(expectedPath)).IsTrue()
            .Because("PNG with EXIF metadata should also be organized correctly");
        await Assert.That(result.FilesProcessed).IsEqualTo(1);
    }

    #endregion

    #region Multiple Photos Tests

    [Test]
    public async Task MultiplePhotos_CopiedToDifferentMonths_BasedOnExtractedDates()
    {
        // Arrange - Create photos with different dates
        await CreateTestJpegAsync("january.jpg", new DateTime(2024, 1, 15));
        await CreateTestJpegAsync("march.jpg", new DateTime(2024, 3, 10));
        await CreateTestJpegAsync("july.jpg", new DateTime(2024, 7, 4));
        await CreateTestJpegAsync("december.jpg", new DateTime(2024, 12, 25));

        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - Each file should be in its correct month folder
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "01", "january.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "03", "march.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "07", "july.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "12", "december.jpg"))).IsTrue();
        await Assert.That(result.FilesProcessed).IsEqualTo(4);
    }

    [Test]
    public async Task PhotosFromDifferentYears_OrganizedCorrectly()
    {
        // Arrange
        await CreateTestJpegAsync("photo2020.jpg", new DateTime(2020, 6, 15));
        await CreateTestJpegAsync("photo2021.jpg", new DateTime(2021, 6, 15));
        await CreateTestJpegAsync("photo2022.jpg", new DateTime(2022, 6, 15));

        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(File.Exists(Path.Combine(_destDir, "2020", "06", "photo2020.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(_destDir, "2021", "06", "photo2021.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(_destDir, "2022", "06", "photo2022.jpg"))).IsTrue();
        await Assert.That(result.FilesProcessed).IsEqualTo(3);
    }

    [Test]
    public async Task MixedJpegAndPng_AllCopiedCorrectly()
    {
        // Arrange
        await CreateTestJpegAsync("photo1.jpg", new DateTime(2024, 5, 1));
        await CreateTestPngAsync("photo2.png", new DateTime(2024, 5, 2));
        await CreateTestJpegAsync("photo3.jpeg", new DateTime(2024, 5, 3));

        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "05", "photo1.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "05", "photo2.png"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "05", "photo3.jpeg"))).IsTrue();
        await Assert.That(result.FilesProcessed).IsEqualTo(3);
    }

    #endregion

    #region Move Mode Tests

    [Test]
    public async Task MoveMode_DeletesSourceFile_AfterSuccessfulCopy()
    {
        // Arrange
        var dateTaken = new DateTime(2024, 8, 15);
        var sourceFile = await CreateTestJpegAsync("tomove.jpg", dateTaken);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            mode: OperationMode.Move);
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        var expectedDestPath = Path.Combine(_destDir, "2024", "08", "tomove.jpg");
        await Assert.That(File.Exists(expectedDestPath)).IsTrue()
            .Because("File should exist at destination");
        await Assert.That(File.Exists(sourceFile)).IsFalse()
            .Because("Source file should be deleted in move mode");
        await Assert.That(result.FilesProcessed).IsEqualTo(1);
    }

    [Test]
    public async Task MoveMode_MultipleFiles_AllSourcesDeleted()
    {
        // Arrange
        var source1 = await CreateTestJpegAsync("move1.jpg", new DateTime(2024, 1, 1));
        var source2 = await CreateTestJpegAsync("move2.jpg", new DateTime(2024, 2, 1));
        var source3 = await CreateTestJpegAsync("move3.jpg", new DateTime(2024, 3, 1));

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            mode: OperationMode.Move);
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(File.Exists(source1)).IsFalse();
        await Assert.That(File.Exists(source2)).IsFalse();
        await Assert.That(File.Exists(source3)).IsFalse();
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "01", "move1.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "02", "move2.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "03", "move3.jpg"))).IsTrue();
        await Assert.That(result.FilesProcessed).IsEqualTo(3);
    }

    #endregion

    #region Date Range Filtering Tests

    [Test]
    public async Task MinDateFilter_ExcludesOlderPhotos()
    {
        // Arrange
        await CreateTestJpegAsync("old.jpg", new DateTime(2020, 1, 1));
        await CreateTestJpegAsync("recent.jpg", new DateTime(2024, 1, 1));

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            minDate: new DateTime(2023, 1, 1));
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "01", "recent.jpg"))).IsTrue()
            .Because("Recent file should be copied");
        await Assert.That(File.Exists(Path.Combine(_destDir, "2020", "01", "old.jpg"))).IsFalse()
            .Because("Old file should be excluded by MinDate filter");
        await Assert.That(result.FilesProcessed).IsEqualTo(1);
        await Assert.That(result.FilesSkipped).IsEqualTo(1);
    }

    [Test]
    public async Task MaxDateFilter_ExcludesNewerPhotos()
    {
        // Arrange
        await CreateTestJpegAsync("old.jpg", new DateTime(2020, 1, 1));
        await CreateTestJpegAsync("recent.jpg", new DateTime(2024, 1, 1));

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            maxDate: new DateTime(2022, 12, 31));
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(File.Exists(Path.Combine(_destDir, "2020", "01", "old.jpg"))).IsTrue()
            .Because("Old file should be copied");
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "01", "recent.jpg"))).IsFalse()
            .Because("Recent file should be excluded by MaxDate filter");
        await Assert.That(result.FilesProcessed).IsEqualTo(1);
        await Assert.That(result.FilesSkipped).IsEqualTo(1);
    }

    [Test]
    public async Task DateRangeFilter_IncludesOnlyPhotosInRange()
    {
        // Arrange
        await CreateTestJpegAsync("before.jpg", new DateTime(2020, 1, 1));
        await CreateTestJpegAsync("during1.jpg", new DateTime(2022, 6, 1));
        await CreateTestJpegAsync("during2.jpg", new DateTime(2023, 6, 1));
        await CreateTestJpegAsync("after.jpg", new DateTime(2025, 1, 1));

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            minDate: new DateTime(2022, 1, 1),
            maxDate: new DateTime(2024, 1, 1));
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(File.Exists(Path.Combine(_destDir, "2022", "06", "during1.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(_destDir, "2023", "06", "during2.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(_destDir, "2020", "01", "before.jpg"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(_destDir, "2025", "01", "after.jpg"))).IsFalse();
        await Assert.That(result.FilesProcessed).IsEqualTo(2);
        await Assert.That(result.FilesSkipped).IsEqualTo(2);
    }

    #endregion

    #region Duplicate Handling Tests

    [Test]
    public async Task ExistingFileAtDestination_AddsNumberSuffix()
    {
        // Arrange - Create an existing file at the destination
        var destFolder = Path.Combine(_destDir, "2024", "05");
        Directory.CreateDirectory(destFolder);
        var existingFile = Path.Combine(destFolder, "photo.jpg");
        await File.WriteAllBytesAsync(existingFile, new byte[] { 0x00 });
        
        // Create source file that will go to the same destination
        await CreateTestJpegAsync("photo.jpg", new DateTime(2024, 5, 1));
        
        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - Source file should be copied with _1 suffix since photo.jpg exists
        await Assert.That(File.Exists(Path.Combine(destFolder, "photo.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(destFolder, "photo_1.jpg"))).IsTrue()
            .Because("When destination exists, a numeric suffix should be added");
        await Assert.That(result.FilesProcessed).IsEqualTo(1);
    }

    [Test]
    public async Task MultipleSequentialDuplicates_CreatesCorrectSuffixes()
    {
        // Arrange - Create existing files at destination
        var destFolder = Path.Combine(_destDir, "2024", "05");
        Directory.CreateDirectory(destFolder);
        File.WriteAllBytes(Path.Combine(destFolder, "photo.jpg"), new byte[] { 0x00 });
        File.WriteAllBytes(Path.Combine(destFolder, "photo_1.jpg"), new byte[] { 0x00 });
        
        // Create source file
        await CreateTestJpegAsync("photo.jpg", new DateTime(2024, 5, 1));

        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - Should create photo_2.jpg since photo.jpg and photo_1.jpg exist
        await Assert.That(File.Exists(Path.Combine(destFolder, "photo_2.jpg"))).IsTrue()
            .Because("Should create photo_2.jpg when photo.jpg and photo_1.jpg already exist");
        await Assert.That(result.FilesProcessed).IsEqualTo(1);
    }

    [Test]
    public async Task SkipExisting_DoesNotOverwriteOrRename()
    {
        // Arrange
        var destFolder = Path.Combine(_destDir, "2024", "05");
        Directory.CreateDirectory(destFolder);
        var existingContent = new byte[] { 0x01, 0x02, 0x03 };
        var existingPath = Path.Combine(destFolder, "photo.jpg");
        await File.WriteAllBytesAsync(existingPath, existingContent);
        
        await CreateTestJpegAsync("photo.jpg", new DateTime(2024, 5, 1));

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            skipExisting: true);
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        var actualContent = await File.ReadAllBytesAsync(existingPath);
        await Assert.That(actualContent).IsEquivalentTo(existingContent)
            .Because("Existing file content should not be overwritten");
        await Assert.That(File.Exists(Path.Combine(destFolder, "photo_1.jpg"))).IsFalse()
            .Because("Should not create duplicate when skipExisting is true");
    }

    #endregion

    #region Dry Run Tests

    [Test]
    public async Task DryRun_DoesNotCopyFiles()
    {
        // Arrange
        await CreateTestJpegAsync("dryrun.jpg", new DateTime(2024, 6, 1));

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            dryRun: true);
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "06", "dryrun.jpg"))).IsFalse()
            .Because("DryRun should not create any files");
        await Assert.That(Directory.GetFiles(_destDir, "*", SearchOption.AllDirectories).Length).IsEqualTo(0);
        await Assert.That(result.FilesProcessed).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task DryRun_MoveMode_DoesNotDeleteSource()
    {
        // Arrange
        var sourceFile = await CreateTestJpegAsync("dryrunmove.jpg", new DateTime(2024, 7, 1));

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            dryRun: true,
            mode: OperationMode.Move);
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(File.Exists(sourceFile)).IsTrue()
            .Because("DryRun move should not delete source files");
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "07", "dryrunmove.jpg"))).IsFalse();
    }

    #endregion

    #region Build Plan Tests

    [Test]
    public async Task BuildPlan_ReturnsCorrectDestinationPaths()
    {
        // Arrange
        await CreateTestJpegAsync("plantest.jpg", new DateTime(2024, 9, 15));

        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var plan = await copierAsync.BuildPlanAsync(
            validatorFactory.Create(config),
            CancellationToken.None);

        // Assert
        await Assert.That(plan.Operations.Count).IsEqualTo(1);
        var operation = plan.Operations.First();
        await Assert.That(operation.DestinationPath).IsEqualTo(Path.Combine(_destDir, "2024", "09", "plantest.jpg"));
    }

    [Test]
    public async Task BuildPlan_WithFilters_ReturnsSkippedFiles()
    {
        // Arrange
        await CreateTestJpegAsync("included.jpg", new DateTime(2024, 1, 1));
        await CreateTestJpegAsync("excluded.jpg", new DateTime(2020, 1, 1));

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            minDate: new DateTime(2023, 1, 1));
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var plan = await copierAsync.BuildPlanAsync(
            validatorFactory.Create(config),
            CancellationToken.None);

        // Assert
        await Assert.That(plan.Operations.Count).IsEqualTo(1);
        await Assert.That(plan.SkippedFiles.Count).IsEqualTo(1);
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task EmptySourceDirectory_CompletesWithoutError()
    {
        // Arrange - Source directory exists but is empty
        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(result.FilesProcessed).IsEqualTo(0);
        await Assert.That(result.FilesFailed).IsEqualTo(0);
    }

    [Test]
    public async Task PhotosInSubdirectories_ProcessedRecursively()
    {
        // Arrange
        await CreateTestJpegAsync("root.jpg", new DateTime(2024, 1, 1));
        await CreateTestJpegAsync("level1.jpg", new DateTime(2024, 2, 1), subfolder: "folder1");
        await CreateTestJpegAsync("level2.jpg", new DateTime(2024, 3, 1), subfolder: Path.Combine("folder1", "folder2"));

        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "01", "root.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "02", "level1.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "03", "level2.jpg"))).IsTrue();
        await Assert.That(result.FilesProcessed).IsEqualTo(3);
    }

    #endregion

    #region Metadata Extraction Verification

    [Test]
    public async Task ExtractedMetadata_MatchesEmbeddedExifData()
    {
        // Arrange - This test verifies the full metadata extraction pipeline
        var expectedDate = new DateTime(2023, 11, 20, 15, 45, 30);
        await CreateTestJpegAsync("metadata_test.jpg", expectedDate);

        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{day}", "{name}{ext}"));
        var serviceProvider = BuildRealServiceProvider(config);

        // Use DirectoryScanner directly to verify metadata extraction
        var scanner = serviceProvider.GetRequiredService<IDirectoryScanner>();
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert - Verify the extracted metadata matches what we embedded
        await Assert.That(files.Count).IsEqualTo(1);
        
        var file = files.First();
        await Assert.That(file.FileDateTime.Taken.Year).IsEqualTo(2023)
            .Because("Extracted year should match embedded EXIF date");
        await Assert.That(file.FileDateTime.Taken.Month).IsEqualTo(11)
            .Because("Extracted month should match embedded EXIF date");
        await Assert.That(file.FileDateTime.Taken.Day).IsEqualTo(20)
            .Because("Extracted day should match embedded EXIF date");
        await Assert.That(file.FileDateTime.Taken.Hour).IsEqualTo(15)
            .Because("Extracted hour should match embedded EXIF date");
    }

    [Test]
    public async Task MetadataEnrichmentPipeline_ProcessesAllSteps()
    {
        // Arrange - Create a file and verify the full enrichment pipeline runs
        var dateTaken = new DateTime(2024, 4, 15, 10, 30, 0);
        await CreateTestJpegAsync("enrichment_test.jpg", dateTaken);

        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        config.CalculateChecksums = true; // Enable checksums to verify ChecksumMetadataEnrichmentStep runs
        
        var serviceProvider = BuildRealServiceProvider(config);
        var scanner = serviceProvider.GetRequiredService<IDirectoryScanner>();
        
        // Act
        var files = scanner.EnumerateFiles(_sourceDir).ToList();

        // Assert
        await Assert.That(files.Count).IsEqualTo(1);
        
        var file = files.First();
        if (file is FileWithMetadata metadata)
        {
            // Verify DateTime enrichment ran
            await Assert.That(metadata.FileDateTime.Taken).IsEqualTo(dateTaken);
            
            // Verify checksum enrichment ran (should have a non-empty checksum)
            await Assert.That(string.IsNullOrEmpty(metadata.Checksum)).IsFalse()
                .Because("Checksum should be calculated when CalculateChecksums is true");
        }
        else
        {
            Assert.Fail("File should be of type FileWithMetadata");
        }
    }

    #endregion
}
