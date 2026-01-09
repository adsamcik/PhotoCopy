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
/// Comprehensive integration tests that verify dry-run output accurately represents
/// what would happen in a real copy operation.
/// 
/// These tests:
/// 1. Run scenarios in dry-run mode and capture the output/plan
/// 2. Run the same scenario in normal mode
/// 3. Verify the dry-run predictions matched actual results
/// 4. Test edge cases: duplicates, conflicts, destination patterns, etc.
/// 
/// Uses:
/// - Real temp directories for file operations
/// - MockImageGenerator for creating valid JPEG/PNG files with EXIF metadata
/// - Real implementations of all services
/// </summary>
[NotInParallel("FileOperations")]
[Property("Category", "Integration,DryRun,OutputVerification")]
public class DryRunOutputVerificationTests
{
    private string _testBaseDirectory = null!;
    private string _sourceDir = null!;
    private string _destDir = null!;

    [Before(Test)]
    public void Setup()
    {
        _testBaseDirectory = Path.Combine(
            Path.GetTempPath(),
            "DryRunOutputVerificationTests",
            Guid.NewGuid().ToString());
        _sourceDir = Path.Combine(_testBaseDirectory, "source");
        _destDir = Path.Combine(_testBaseDirectory, "dest");

        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_destDir);
        
        SharedLogs.Clear();
    }

    [After(Test)]
    public void Cleanup()
    {
        SafeDeleteDirectory(_testBaseDirectory);
        SharedLogs.Clear();
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
    /// Gets all destination files, excluding the .photocopy-logs directory which contains transaction logs.
    /// </summary>
    private string[] GetDestinationFiles()
    {
        return Directory.GetFiles(_destDir, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains(".photocopy-logs"))
            .ToArray();
    }

    /// <summary>
    /// Gets all destination directories, excluding the .photocopy-logs directory.
    /// </summary>
    private string[] GetDestinationDirectories()
    {
        return Directory.GetDirectories(_destDir, "*", SearchOption.AllDirectories)
            .Where(d => !d.Contains(".photocopy-logs"))
            .ToArray();
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

        // Add logging with FakeLogger to capture log messages
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(new FakeLoggerProvider());
        });

        // Register configuration
        services.AddSingleton<IOptions<PhotoCopyConfig>>(
            Microsoft.Extensions.Options.Options.Create(config));

        // Mock IReverseGeocodingService since we don't want real network calls
        var mockGeocodingService = Substitute.For<IReverseGeocodingService>();
        mockGeocodingService.ReverseGeocode(Arg.Any<double>(), Arg.Any<double>())
            .Returns((LocationData?)null);
        services.AddSingleton(mockGeocodingService);

        // Register REAL core services
        services.AddSingleton<IChecksumCalculator, Sha256ChecksumCalculator>();
        services.AddSingleton<ITransactionLogger, TransactionLogger>();
        services.AddTransient<IFileMetadataExtractor, FileMetadataExtractor>();
        services.AddTransient<IMetadataEnricher, MetadataEnricher>();
        services.AddTransient<IMetadataEnrichmentStep, DateTimeMetadataEnrichmentStep>();
        services.AddTransient<IMetadataEnrichmentStep, LocationMetadataEnrichmentStep>();
        services.AddTransient<IMetadataEnrichmentStep, ChecksumMetadataEnrichmentStep>();
        services.AddTransient<IFileFactory, FileFactory>();
        services.AddTransient<IDirectoryScanner, DirectoryScanner>();
        services.AddTransient<IFileSystem, FileSystem>();
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
        OperationMode mode = OperationMode.Copy,
        DateTime? minDate = null,
        DateTime? maxDate = null,
        bool skipExisting = false,
        bool overwrite = false,
        DuplicateHandling duplicateHandling = DuplicateHandling.None,
        bool calculateChecksums = false,
        string duplicatesFormat = "_{number}")
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
            Overwrite = overwrite,
            DuplicateHandling = duplicateHandling,
            CalculateChecksums = calculateChecksums,
            DuplicatesFormat = duplicatesFormat,
            LogLevel = OutputLevel.Verbose,
            AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".heic", ".mov", ".mp4"
            }
        };
    }

    /// <summary>
    /// Captures a snapshot of all files in a directory.
    /// </summary>
    private static Dictionary<string, byte[]> CaptureDirectorySnapshot(string path)
    {
        var snapshot = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        
        if (!Directory.Exists(path))
        {
            return snapshot;
        }

        foreach (var file in Directory.GetFiles(path, "*.*", SearchOption.AllDirectories))
        {
            snapshot[file] = File.ReadAllBytes(file);
        }

        return snapshot;
    }

    /// <summary>
    /// Verifies that two directory snapshots are identical.
    /// </summary>
    private static void VerifySnapshotsMatch(
        Dictionary<string, byte[]> expected,
        Dictionary<string, byte[]> actual,
        string description)
    {
        expected.Keys.Should().BeEquivalentTo(actual.Keys);

        foreach (var (path, expectedContent) in expected)
        {
            actual[path].Should().BeEquivalentTo(expectedContent);
        }
    }

    #endregion

    #region Test 1: DryRun Correctly Predicts Copied File Count

    [Test]
    public async Task DryRun_CorrectlyPredicts_CopiedFileCount()
    {
        // Arrange - Create various test files
        await CreateTestJpegAsync("photo1.jpg", new DateTime(2024, 1, 15));
        await CreateTestJpegAsync("photo2.jpg", new DateTime(2024, 3, 20));
        await CreateTestJpegAsync("photo3.jpg", new DateTime(2024, 6, 10));
        await CreateTestPngAsync("image4.png", new DateTime(2024, 8, 5));
        await CreateTestPngAsync("image5.png", new DateTime(2024, 11, 25));

        var destinationTemplate = Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}");

        // Act - Run dry-run first
        var dryRunConfig = CreateConfig(destinationTemplate, dryRun: true);
        var dryRunProvider = BuildRealServiceProvider(dryRunConfig);
        var dryRunCopier = dryRunProvider.GetRequiredService<IDirectoryCopierAsync>();
        var dryRunValidatorFactory = dryRunProvider.GetRequiredService<IValidatorFactory>();

        var dryRunResult = await dryRunCopier.CopyAsync(
            dryRunValidatorFactory.Create(dryRunConfig),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Act - Run actual copy
        var realConfig = CreateConfig(destinationTemplate, dryRun: false);
        var realProvider = BuildRealServiceProvider(realConfig);
        var realCopier = realProvider.GetRequiredService<IDirectoryCopierAsync>();
        var realValidatorFactory = realProvider.GetRequiredService<IValidatorFactory>();

        var realResult = await realCopier.CopyAsync(
            realValidatorFactory.Create(realConfig),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        dryRunResult.FilesProcessed.Should().Be(realResult.FilesProcessed);
        dryRunResult.FilesProcessed.Should().Be(5);
        dryRunResult.FilesFailed.Should().Be(realResult.FilesFailed);
        dryRunResult.FilesSkipped.Should().Be(realResult.FilesSkipped);
    }

    #endregion

    #region Test 2: DryRun Correctly Predicts Destination Paths

    [Test]
    public async Task DryRun_CorrectlyPredicts_DestinationPaths()
    {
        // Arrange - Create files with specific dates for predictable paths
        var file1Date = new DateTime(2023, 5, 15, 10, 30, 0);
        var file2Date = new DateTime(2024, 12, 25, 14, 45, 0);
        var file3Date = new DateTime(2022, 1, 1, 8, 0, 0);

        await CreateTestJpegAsync("vacation.jpg", file1Date);
        await CreateTestJpegAsync("christmas.jpg", file2Date);
        await CreateTestPngAsync("newyear.png", file3Date);

        var destinationTemplate = Path.Combine(_destDir, "{year}", "{month}", "{day}", "{name}{ext}");

        // Act - Get dry-run plan
        var dryRunConfig = CreateConfig(destinationTemplate, dryRun: true);
        var dryRunProvider = BuildRealServiceProvider(dryRunConfig);
        var dryRunCopier = dryRunProvider.GetRequiredService<IDirectoryCopierAsync>();
        var dryRunValidatorFactory = dryRunProvider.GetRequiredService<IValidatorFactory>();

        var dryRunPlan = await dryRunCopier.BuildPlanAsync(
            dryRunValidatorFactory.Create(dryRunConfig),
            CancellationToken.None);

        var predictedPaths = dryRunPlan.Operations
            .Select(op => op.DestinationPath)
            .OrderBy(p => p)
            .ToList();

        // Act - Execute actual copy
        var realConfig = CreateConfig(destinationTemplate, dryRun: false);
        var realProvider = BuildRealServiceProvider(realConfig);
        var realCopier = realProvider.GetRequiredService<IDirectoryCopierAsync>();
        var realValidatorFactory = realProvider.GetRequiredService<IValidatorFactory>();

        await realCopier.CopyAsync(
            realValidatorFactory.Create(realConfig),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Get actual paths
        var actualPaths = GetDestinationFiles()
            .OrderBy(p => p)
            .ToList();

        // Assert
        predictedPaths.Should().BeEquivalentTo(actualPaths);

        // Verify specific expected paths
        predictedPaths.Should().Contain(Path.Combine(_destDir, "2023", "05", "15", "vacation.jpg"));
        predictedPaths.Should().Contain(Path.Combine(_destDir, "2024", "12", "25", "christmas.jpg"));
        predictedPaths.Should().Contain(Path.Combine(_destDir, "2022", "01", "01", "newyear.png"));
    }

    #endregion

    #region Test 3: DryRun Correctly Identifies Duplicates That Would Be Skipped

    [Test]
    public async Task DryRun_CorrectlyIdentifies_DuplicatesThatWouldBeSkipped()
    {
        // Arrange - Create duplicate files (same name, same destination date = conflict)
        var commonDate = new DateTime(2024, 7, 20);
        
        // Create files with same name in different subfolders - they'll target the same destination
        await CreateTestJpegAsync("photo.jpg", commonDate, subfolder: "folder1");
        await CreateTestJpegAsync("photo.jpg", commonDate, subfolder: "folder2");
        await CreateTestJpegAsync("photo.jpg", commonDate, subfolder: "folder3");

        var destinationTemplate = Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}");

        // Act - Get dry-run plan
        var dryRunConfig = CreateConfig(
            destinationTemplate,
            dryRun: true,
            duplicateHandling: DuplicateHandling.None);
        var dryRunProvider = BuildRealServiceProvider(dryRunConfig);
        var dryRunCopier = dryRunProvider.GetRequiredService<IDirectoryCopierAsync>();
        var dryRunValidatorFactory = dryRunProvider.GetRequiredService<IValidatorFactory>();

        var dryRunPlan = await dryRunCopier.BuildPlanAsync(
            dryRunValidatorFactory.Create(dryRunConfig),
            CancellationToken.None);

        // Act - Execute actual copy
        var realConfig = CreateConfig(
            destinationTemplate,
            dryRun: false,
            duplicateHandling: DuplicateHandling.None);
        var realProvider = BuildRealServiceProvider(realConfig);
        var realCopier = realProvider.GetRequiredService<IDirectoryCopierAsync>();
        var realValidatorFactory = realProvider.GetRequiredService<IValidatorFactory>();

        var realResult = await realCopier.CopyAsync(
            realValidatorFactory.Create(realConfig),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - Plan should have all 3 operations
        dryRunPlan.Operations.Should().HaveCount(3);

        var destinations = dryRunPlan.Operations.Select(op => op.DestinationPath).ToList();
        var baseDestPath = Path.Combine(_destDir, "2024", "07", "photo.jpg");
        
        // With thread-safe path reservation during planning, each file gets a unique destination.
        // This accurately reflects what would happen during actual copy with default duplicate handling.
        destinations.Should().Contain(baseDestPath);
        destinations.Should().Contain(Path.Combine(_destDir, "2024", "07", "photo_1.jpg"));
        destinations.Should().Contain(Path.Combine(_destDir, "2024", "07", "photo_2.jpg"));

        // All 3 files are processed and result in 3 files at destination (numbered duplicates)
        await Assert.That(realResult.FilesProcessed).IsEqualTo(3);
        var actualFiles = GetDestinationFiles();
        actualFiles.Should().HaveCount(3);
    }

    #endregion

    #region Test 4: DryRun Doesn't Modify Any Files

    [Test]
    public async Task DryRun_DoesNotModify_SourceOrDestinationFiles()
    {
        // Arrange - Create source files
        await CreateTestJpegAsync("source1.jpg", new DateTime(2024, 4, 15));
        await CreateTestJpegAsync("source2.jpg", new DateTime(2024, 5, 20));

        // Create existing destination file
        var existingDestFolder = Path.Combine(_destDir, "existing");
        Directory.CreateDirectory(existingDestFolder);
        var existingContent = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        await File.WriteAllBytesAsync(
            Path.Combine(existingDestFolder, "existing.jpg"),
            existingContent);

        // Capture snapshots before dry-run
        var sourceSnapshotBefore = CaptureDirectorySnapshot(_sourceDir);
        var destSnapshotBefore = CaptureDirectorySnapshot(_destDir);

        var destinationTemplate = Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}");

        // Act - Run dry-run
        var dryRunConfig = CreateConfig(destinationTemplate, dryRun: true);
        var dryRunProvider = BuildRealServiceProvider(dryRunConfig);
        var dryRunCopier = dryRunProvider.GetRequiredService<IDirectoryCopierAsync>();
        var dryRunValidatorFactory = dryRunProvider.GetRequiredService<IValidatorFactory>();

        var dryRunResult = await dryRunCopier.CopyAsync(
            dryRunValidatorFactory.Create(dryRunConfig),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Also run BuildPlanAsync to ensure it doesn't modify anything
        var dryRunPlan = await dryRunCopier.BuildPlanAsync(
            dryRunValidatorFactory.Create(dryRunConfig),
            CancellationToken.None);

        // Capture snapshots after dry-run
        var sourceSnapshotAfter = CaptureDirectorySnapshot(_sourceDir);
        var destSnapshotAfter = CaptureDirectorySnapshot(_destDir);

        // Assert - Snapshots should be identical
        VerifySnapshotsMatch(sourceSnapshotBefore, sourceSnapshotAfter, "Source directory");
        VerifySnapshotsMatch(destSnapshotBefore, destSnapshotAfter, "Destination directory");

        // Verify dry-run still reports work would be done
        dryRunResult.FilesProcessed.Should().Be(2);
        dryRunPlan.Operations.Should().HaveCount(2);
    }

    #endregion

    #region Test 5: DryRun With Date-Based Destination Pattern Shows Correct Paths

    [Test]
    public async Task DryRun_WithDateBasedPattern_ShowsCorrectPaths()
    {
        // Arrange - Create files with various dates to test pattern expansion
        var dates = new[]
        {
            new DateTime(2020, 1, 1, 0, 0, 0),   // 2020/01/01
            new DateTime(2021, 6, 15, 12, 30, 0), // 2021/06/15
            new DateTime(2022, 12, 31, 23, 59, 0), // 2022/12/31
            new DateTime(2023, 3, 10, 8, 0, 0),   // 2023/03/10
            new DateTime(2024, 9, 22, 16, 45, 0)  // 2024/09/22
        };

        for (int i = 0; i < dates.Length; i++)
        {
            await CreateTestJpegAsync($"photo{i + 1}.jpg", dates[i]);
        }

        var destinationTemplate = Path.Combine(_destDir, "{year}", "{month}", "{day}", "{name}{ext}");

        // Act - Get dry-run plan
        var dryRunConfig = CreateConfig(destinationTemplate, dryRun: true);
        var dryRunProvider = BuildRealServiceProvider(dryRunConfig);
        var dryRunCopier = dryRunProvider.GetRequiredService<IDirectoryCopierAsync>();
        var dryRunValidatorFactory = dryRunProvider.GetRequiredService<IValidatorFactory>();

        var dryRunPlan = await dryRunCopier.BuildPlanAsync(
            dryRunValidatorFactory.Create(dryRunConfig),
            CancellationToken.None);

        // Assert - Verify each path matches expected pattern
        var expectedPaths = new[]
        {
            Path.Combine(_destDir, "2020", "01", "01", "photo1.jpg"),
            Path.Combine(_destDir, "2021", "06", "15", "photo2.jpg"),
            Path.Combine(_destDir, "2022", "12", "31", "photo3.jpg"),
            Path.Combine(_destDir, "2023", "03", "10", "photo4.jpg"),
            Path.Combine(_destDir, "2024", "09", "22", "photo5.jpg")
        };

        var actualPaths = dryRunPlan.Operations
            .Select(op => op.DestinationPath)
            .OrderBy(p => p)
            .ToList();

        actualPaths.Should().BeEquivalentTo(expectedPaths);

        // Verify predicted directories to create
        dryRunPlan.DirectoriesToCreate.Should().Contain(Path.Combine(_destDir, "2020", "01", "01"));
        dryRunPlan.DirectoriesToCreate.Should().Contain(Path.Combine(_destDir, "2024", "09", "22"));
    }

    #endregion

    #region Test 6: DryRun With SkipExisting Correctly Predicts Skips

    [Test]
    public async Task DryRun_WithSkipExisting_CorrectlyPredictsSkips()
    {
        // Arrange - Create source files
        var date1 = new DateTime(2024, 6, 15);
        var date2 = new DateTime(2024, 6, 20);
        
        await CreateTestJpegAsync("existing.jpg", date1);
        await CreateTestJpegAsync("new.jpg", date2);

        // Pre-create one destination file
        var destFolder = Path.Combine(_destDir, "2024", "06");
        Directory.CreateDirectory(destFolder);
        var existingDestPath = Path.Combine(destFolder, "existing.jpg");
        await File.WriteAllBytesAsync(existingDestPath, new byte[] { 0x01, 0x02, 0x03 });

        var destinationTemplate = Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}");

        // Act - Run dry-run with skipExisting
        var dryRunConfig = CreateConfig(destinationTemplate, dryRun: true, skipExisting: true);
        var dryRunProvider = BuildRealServiceProvider(dryRunConfig);
        var dryRunCopier = dryRunProvider.GetRequiredService<IDirectoryCopierAsync>();
        var dryRunValidatorFactory = dryRunProvider.GetRequiredService<IValidatorFactory>();

        var dryRunResult = await dryRunCopier.CopyAsync(
            dryRunValidatorFactory.Create(dryRunConfig),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Act - Run actual copy
        var realConfig = CreateConfig(destinationTemplate, dryRun: false, skipExisting: true);
        var realProvider = BuildRealServiceProvider(realConfig);
        var realCopier = realProvider.GetRequiredService<IDirectoryCopierAsync>();
        var realValidatorFactory = realProvider.GetRequiredService<IValidatorFactory>();

        var realResult = await realCopier.CopyAsync(
            realValidatorFactory.Create(realConfig),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        dryRunResult.FilesProcessed.Should().Be(realResult.FilesProcessed);
        dryRunResult.FilesSkipped.Should().Be(realResult.FilesSkipped);
    }

    #endregion

    #region Test 7: DryRun With DateFilter Correctly Predicts Filtered Count

    [Test]
    public async Task DryRun_WithDateFilter_CorrectlyPredictsFilteredCount()
    {
        // Arrange - Create files spanning multiple years
        await CreateTestJpegAsync("old2019.jpg", new DateTime(2019, 6, 15));
        await CreateTestJpegAsync("old2020.jpg", new DateTime(2020, 6, 15));
        await CreateTestJpegAsync("target2023.jpg", new DateTime(2023, 6, 15));
        await CreateTestJpegAsync("target2024.jpg", new DateTime(2024, 6, 15));
        await CreateTestJpegAsync("future2025.jpg", new DateTime(2025, 6, 15));

        var destinationTemplate = Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}");

        // Filter to only 2023-2024
        var minDate = new DateTime(2023, 1, 1);
        var maxDate = new DateTime(2024, 12, 31);

        // Act - Run dry-run with date filter
        var dryRunConfig = CreateConfig(
            destinationTemplate,
            dryRun: true,
            minDate: minDate,
            maxDate: maxDate);
        var dryRunProvider = BuildRealServiceProvider(dryRunConfig);
        var dryRunCopier = dryRunProvider.GetRequiredService<IDirectoryCopierAsync>();
        var dryRunValidatorFactory = dryRunProvider.GetRequiredService<IValidatorFactory>();

        var dryRunResult = await dryRunCopier.CopyAsync(
            dryRunValidatorFactory.Create(dryRunConfig),
            NullProgressReporter.Instance,
            CancellationToken.None);

        var dryRunPlan = await dryRunCopier.BuildPlanAsync(
            dryRunValidatorFactory.Create(dryRunConfig),
            CancellationToken.None);

        // Act - Run actual copy
        var realConfig = CreateConfig(
            destinationTemplate,
            dryRun: false,
            minDate: minDate,
            maxDate: maxDate);
        var realProvider = BuildRealServiceProvider(realConfig);
        var realCopier = realProvider.GetRequiredService<IDirectoryCopierAsync>();
        var realValidatorFactory = realProvider.GetRequiredService<IValidatorFactory>();

        var realResult = await realCopier.CopyAsync(
            realValidatorFactory.Create(realConfig),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        dryRunResult.FilesProcessed.Should().Be(2);
        dryRunResult.FilesProcessed.Should().Be(realResult.FilesProcessed);
        dryRunResult.FilesSkipped.Should().Be(realResult.FilesSkipped);

        // Verify only target files are in the plan
        dryRunPlan.Operations.Should().HaveCount(2);
        var planPaths = dryRunPlan.Operations.Select(op => op.DestinationPath).ToList();
        planPaths.Should().AllSatisfy(p => 
            p.Should().Match(path => 
                path.Contains("2023") || path.Contains("2024")));
    }

    #endregion

    #region Test 8: DryRun Move Mode Doesn't Delete Source Files

    [Test]
    public async Task DryRun_MoveMode_DoesNotDeleteSourceFiles()
    {
        // Arrange - Create source files
        var sourceFile1 = await CreateTestJpegAsync("tomove1.jpg", new DateTime(2024, 3, 10));
        var sourceFile2 = await CreateTestJpegAsync("tomove2.jpg", new DateTime(2024, 4, 20));

        var originalContent1 = await File.ReadAllBytesAsync(sourceFile1);
        var originalContent2 = await File.ReadAllBytesAsync(sourceFile2);

        var destinationTemplate = Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}");

        // Act - Run dry-run in MOVE mode
        var dryRunConfig = CreateConfig(
            destinationTemplate,
            dryRun: true,
            mode: OperationMode.Move);
        var dryRunProvider = BuildRealServiceProvider(dryRunConfig);
        var dryRunCopier = dryRunProvider.GetRequiredService<IDirectoryCopierAsync>();
        var dryRunValidatorFactory = dryRunProvider.GetRequiredService<IValidatorFactory>();

        var dryRunResult = await dryRunCopier.CopyAsync(
            dryRunValidatorFactory.Create(dryRunConfig),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - Source files should still exist with original content
        File.Exists(sourceFile1).Should().BeTrue();
        File.Exists(sourceFile2).Should().BeTrue();

        var currentContent1 = await File.ReadAllBytesAsync(sourceFile1);
        var currentContent2 = await File.ReadAllBytesAsync(sourceFile2);

        currentContent1.Should().BeEquivalentTo(originalContent1);
        currentContent2.Should().BeEquivalentTo(originalContent2);

        // Destination files should NOT exist
        var destFiles = Directory.GetFiles(_destDir, "*.*", SearchOption.AllDirectories);
        destFiles.Should().BeEmpty();

        // But result should indicate move would be done
        dryRunResult.FilesProcessed.Should().Be(2);
    }

    #endregion

    #region Test 9: DryRun TotalBytes Prediction Matches Actual

    [Test]
    public async Task DryRun_TotalBytesInPlan_MatchesActualBytesTransferred()
    {
        // Arrange - Create files with known sizes
        var file1 = await CreateTestJpegAsync("file1.jpg", new DateTime(2024, 5, 10));
        var file2 = await CreateTestJpegAsync("file2.jpg", new DateTime(2024, 6, 15));
        var file3 = await CreateTestPngAsync("file3.png", new DateTime(2024, 7, 20));

        var totalSourceBytes = new FileInfo(file1).Length +
                              new FileInfo(file2).Length +
                              new FileInfo(file3).Length;

        var destinationTemplate = Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}");

        // Act - Get dry-run plan
        var dryRunConfig = CreateConfig(destinationTemplate, dryRun: true);
        var dryRunProvider = BuildRealServiceProvider(dryRunConfig);
        var dryRunCopier = dryRunProvider.GetRequiredService<IDirectoryCopierAsync>();
        var dryRunValidatorFactory = dryRunProvider.GetRequiredService<IValidatorFactory>();

        var dryRunPlan = await dryRunCopier.BuildPlanAsync(
            dryRunValidatorFactory.Create(dryRunConfig),
            CancellationToken.None);

        // Act - Execute actual copy
        var realConfig = CreateConfig(destinationTemplate, dryRun: false);
        var realProvider = BuildRealServiceProvider(realConfig);
        var realCopier = realProvider.GetRequiredService<IDirectoryCopierAsync>();
        var realValidatorFactory = realProvider.GetRequiredService<IValidatorFactory>();

        var realResult = await realCopier.CopyAsync(
            realValidatorFactory.Create(realConfig),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        dryRunPlan.TotalBytes.Should().Be(totalSourceBytes);
        dryRunPlan.TotalBytes.Should().Be(realResult.BytesProcessed);
    }

    #endregion

    #region Test 10: DryRun DirectoriesToCreate Prediction Is Accurate

    [Test]
    public async Task DryRun_DirectoriesToCreate_MatchesActualDirectoriesCreated()
    {
        // Arrange - Create files that will need various directories
        await CreateTestJpegAsync("jan.jpg", new DateTime(2024, 1, 15));
        await CreateTestJpegAsync("mar.jpg", new DateTime(2024, 3, 10));
        await CreateTestJpegAsync("jun.jpg", new DateTime(2024, 6, 22));
        await CreateTestPngAsync("oct.png", new DateTime(2024, 10, 5));
        await CreateTestPngAsync("dec.png", new DateTime(2024, 12, 25));

        var destinationTemplate = Path.Combine(_destDir, "{year}", "{month}", "{day}", "{name}{ext}");

        // Act - Get dry-run plan
        var dryRunConfig = CreateConfig(destinationTemplate, dryRun: true);
        var dryRunProvider = BuildRealServiceProvider(dryRunConfig);
        var dryRunCopier = dryRunProvider.GetRequiredService<IDirectoryCopierAsync>();
        var dryRunValidatorFactory = dryRunProvider.GetRequiredService<IValidatorFactory>();

        var dryRunPlan = await dryRunCopier.BuildPlanAsync(
            dryRunValidatorFactory.Create(dryRunConfig),
            CancellationToken.None);

        var predictedDirs = dryRunPlan.DirectoriesToCreate
            .OrderBy(d => d)
            .ToList();

        // Act - Execute actual copy
        var realConfig = CreateConfig(destinationTemplate, dryRun: false);
        var realProvider = BuildRealServiceProvider(realConfig);
        var realCopier = realProvider.GetRequiredService<IDirectoryCopierAsync>();
        var realValidatorFactory = realProvider.GetRequiredService<IValidatorFactory>();

        await realCopier.CopyAsync(
            realValidatorFactory.Create(realConfig),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Get actual directories created (only leaf directories with files, excluding transaction logs)
        var actualDirs = GetDestinationFiles()
            .Select(f => Path.GetDirectoryName(f)!)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        // Assert
        predictedDirs.Should().BeEquivalentTo(actualDirs);
    }

    #endregion

    #region Test 11: DryRun Logs Show Accurate Operation Preview

    [Test]
    public async Task DryRun_Logs_ShowAccurateOperationPreview()
    {
        // Arrange
        await CreateTestJpegAsync("preview.jpg", new DateTime(2024, 8, 15));

        var destinationTemplate = Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}");

        SharedLogs.Clear();

        // Act - Run dry-run
        var dryRunConfig = CreateConfig(destinationTemplate, dryRun: true);
        var dryRunProvider = BuildRealServiceProvider(dryRunConfig);
        var dryRunCopier = dryRunProvider.GetRequiredService<IDirectoryCopierAsync>();
        var dryRunValidatorFactory = dryRunProvider.GetRequiredService<IValidatorFactory>();

        await dryRunCopier.CopyAsync(
            dryRunValidatorFactory.Create(dryRunConfig),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - Check logs contain dry-run indication
        var logs = SharedLogs.Entries;
        var dryRunLogs = logs.Where(l =>
            l.Message.Contains("DryRun", StringComparison.OrdinalIgnoreCase) ||
            l.Message.Contains("dry run", StringComparison.OrdinalIgnoreCase) ||
            l.Message.Contains("dry-run", StringComparison.OrdinalIgnoreCase))
            .ToList();

        dryRunLogs.Should().NotBeEmpty();

        // Should mention the file being processed
        var fileLog = logs.FirstOrDefault(l =>
            l.Message.Contains("preview.jpg", StringComparison.OrdinalIgnoreCase));

        fileLog.Should().NotBeNull();
    }

    #endregion

    #region Test 12: DryRun With Mixed File Types Predicts Correctly

    [Test]
    public async Task DryRun_WithMixedFileTypes_PredictsCorrectly()
    {
        // Arrange - Create various file types
        await CreateTestJpegAsync("photo.jpg", new DateTime(2024, 5, 10));
        await CreateTestJpegAsync("image.jpeg", new DateTime(2024, 5, 15));
        await CreateTestPngAsync("screenshot.png", new DateTime(2024, 5, 20));

        var destinationTemplate = Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}");

        // Act - Get dry-run plan
        var dryRunConfig = CreateConfig(destinationTemplate, dryRun: true);
        var dryRunProvider = BuildRealServiceProvider(dryRunConfig);
        var dryRunCopier = dryRunProvider.GetRequiredService<IDirectoryCopierAsync>();
        var dryRunValidatorFactory = dryRunProvider.GetRequiredService<IValidatorFactory>();

        var dryRunPlan = await dryRunCopier.BuildPlanAsync(
            dryRunValidatorFactory.Create(dryRunConfig),
            CancellationToken.None);

        // Execute actual copy
        var realConfig = CreateConfig(destinationTemplate, dryRun: false);
        var realProvider = BuildRealServiceProvider(realConfig);
        var realCopier = realProvider.GetRequiredService<IDirectoryCopierAsync>();
        var realValidatorFactory = realProvider.GetRequiredService<IValidatorFactory>();

        await realCopier.CopyAsync(
            realValidatorFactory.Create(realConfig),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        var predictedPaths = dryRunPlan.Operations
            .Select(op => op.DestinationPath)
            .ToList();
        var actualPaths = GetDestinationFiles()
            .ToList();

        predictedPaths.Should().HaveCount(3);
        actualPaths.Should().HaveCount(3);
        predictedPaths.Should().BeEquivalentTo(actualPaths);

        // Verify extensions are preserved
        actualPaths.Should().Contain(p => p.EndsWith(".jpg"));
        actualPaths.Should().Contain(p => p.EndsWith(".jpeg"));
        actualPaths.Should().Contain(p => p.EndsWith(".png"));
    }

    #endregion

    #region Test 13: DryRun Complete Workflow Comparison

    [Test]
    public async Task DryRun_CompleteWorkflow_PredictionsMatchActualResults()
    {
        // Arrange - Create a realistic scenario with multiple files
        await CreateTestJpegAsync("vacation1.jpg", new DateTime(2023, 7, 10));
        await CreateTestJpegAsync("vacation2.jpg", new DateTime(2023, 7, 11));
        await CreateTestJpegAsync("vacation3.jpg", new DateTime(2023, 7, 12));
        await CreateTestPngAsync("screenshot1.png", new DateTime(2024, 1, 5));
        await CreateTestPngAsync("screenshot2.png", new DateTime(2024, 2, 10));
        
        // Create files in subfolders
        await CreateTestJpegAsync("nested1.jpg", new DateTime(2024, 3, 15), subfolder: "subfolder1");
        await CreateTestJpegAsync("nested2.jpg", new DateTime(2024, 4, 20), subfolder: "subfolder2");

        var destinationTemplate = Path.Combine(_destDir, "{year}", "{month}", "{day}", "{name}{ext}");

        // Act - Run dry-run and capture all predictions
        var dryRunConfig = CreateConfig(destinationTemplate, dryRun: true);
        var dryRunProvider = BuildRealServiceProvider(dryRunConfig);
        var dryRunCopier = dryRunProvider.GetRequiredService<IDirectoryCopierAsync>();
        var dryRunValidatorFactory = dryRunProvider.GetRequiredService<IValidatorFactory>();

        var dryRunPlan = await dryRunCopier.BuildPlanAsync(
            dryRunValidatorFactory.Create(dryRunConfig),
            CancellationToken.None);

        var dryRunResult = await dryRunCopier.CopyAsync(
            dryRunValidatorFactory.Create(dryRunConfig),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Capture predictions
        var predictedFileCount = dryRunResult.FilesProcessed;
        var predictedPaths = dryRunPlan.Operations
            .Select(op => op.DestinationPath)
            .OrderBy(p => p)
            .ToList();
        var predictedDirectories = dryRunPlan.DirectoriesToCreate.OrderBy(d => d).ToList();
        var predictedBytes = dryRunPlan.TotalBytes;
        var predictedFailures = dryRunResult.FilesFailed;
        var predictedSkips = dryRunResult.FilesSkipped;

        // Verify nothing was created during dry-run (exclude any residual logs from previous runs)
        GetDestinationFiles()
            .Should().BeEmpty();

        // Act - Run actual copy
        var realConfig = CreateConfig(destinationTemplate, dryRun: false);
        var realProvider = BuildRealServiceProvider(realConfig);
        var realCopier = realProvider.GetRequiredService<IDirectoryCopierAsync>();
        var realValidatorFactory = realProvider.GetRequiredService<IValidatorFactory>();

        var realResult = await realCopier.CopyAsync(
            realValidatorFactory.Create(realConfig),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Get actual results (excluding transaction log files)
        var actualPaths = GetDestinationFiles()
            .OrderBy(p => p)
            .ToList();
        var actualDirectories = GetDestinationFiles()
            .Select(f => Path.GetDirectoryName(f)!)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        // Assert - All predictions should match reality
        predictedFileCount.Should().Be(realResult.FilesProcessed);
        
        predictedPaths.Should().BeEquivalentTo(actualPaths);
        
        predictedDirectories.Should().BeEquivalentTo(actualDirectories);
        
        predictedBytes.Should().Be(realResult.BytesProcessed);
        
        predictedFailures.Should().Be(realResult.FilesFailed);
        
        predictedSkips.Should().Be(realResult.FilesSkipped);

        // Final count verification
        actualPaths.Should().HaveCount(7);
    }

    #endregion
}

/// <summary>
/// Custom logger provider for capturing logs in tests.
/// </summary>
file class FakeLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new FakeLogger();

    public void Dispose() { }
}


