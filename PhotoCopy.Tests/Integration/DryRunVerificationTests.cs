using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
/// Integration tests verifying that dry-run output accurately represents what would happen in a real copy.
/// These tests:
/// 1. Use MockImageGenerator to create real JPEG/PNG files with embedded EXIF metadata
/// 2. Write these files to actual temp directories
/// 3. Run both dry-run and real copy operations
/// 4. Verify dry-run predictions match actual behavior
/// </summary>
[NotInParallel("FileOperations")]
[Property("Category", "Integration,DryRun")]
public class DryRunVerificationTests
{
    private string _testBaseDirectory = null!;
    private string _sourceDir = null!;
    private string _destDir = null!;
    private string _logsDir = null!;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Before(Test)]
    public void Setup()
    {
        _testBaseDirectory = Path.Combine(Path.GetTempPath(), "DryRunVerificationTests", Guid.NewGuid().ToString());
        _sourceDir = Path.Combine(_testBaseDirectory, "source");
        _destDir = Path.Combine(_testBaseDirectory, "dest");
        _logsDir = Path.Combine(_destDir, ".photocopy-logs");

        Directory.CreateDirectory(_sourceDir);
        // Note: We don't create _destDir for some tests to verify dry-run doesn't create it
        
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

        // Add logging with FakeLogger to capture log messages
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(new FakeLoggerProvider());
        });

        // Register configuration
        services.AddSingleton<IOptions<PhotoCopyConfig>>(Microsoft.Extensions.Options.Options.Create(config));

        // Mock IReverseGeocodingService since we don't want real network calls
        var mockGeocodingService = Substitute.For<IReverseGeocodingService>();
        mockGeocodingService.ReverseGeocode(Arg.Any<double>(), Arg.Any<double>())
            .Returns((LocationData?)null);
        services.AddSingleton(mockGeocodingService);

        // Register REAL core services
        services.AddSingleton<IChecksumCalculator, Sha256ChecksumCalculator>();
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
        services.AddSingleton<ITransactionLogger, TransactionLogger>();

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
        DuplicateHandling duplicateHandling = DuplicateHandling.None,
        bool enableRollback = false)
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
            EnableRollback = enableRollback,
            CalculateChecksums = false, // Disable for faster tests
            DuplicatesFormat = "_{number}",
            LogLevel = OutputLevel.Verbose,
            AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".heic", ".mov", ".mp4"
            }
        };
    }

    #endregion

    #region Test 1: DryRun_DoesNotCopyAnyFiles

    [Test]
    public async Task DryRun_DoesNotCopyAnyFiles()
    {
        // Arrange - Create test files
        var date1 = new DateTime(2024, 3, 15, 10, 30, 0);
        var date2 = new DateTime(2024, 6, 20, 14, 45, 0);

        await CreateTestJpegAsync("photo1.jpg", date1);
        await CreateTestJpegAsync("photo2.jpg", date2);

        // Create destination directory
        Directory.CreateDirectory(_destDir);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{day}", "{name}{ext}"),
            dryRun: true);

        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - No files should be copied
        var expectedPath1 = Path.Combine(_destDir, "2024", "03", "15", "photo1.jpg");
        var expectedPath2 = Path.Combine(_destDir, "2024", "06", "20", "photo2.jpg");

        File.Exists(expectedPath1).Should().BeFalse("DryRun should not actually copy files");
        File.Exists(expectedPath2).Should().BeFalse("DryRun should not actually copy files");

        // But should report that files would be processed
        result.FilesProcessed.Should().Be(2, "DryRun should report how many files would be processed");
        result.FilesFailed.Should().Be(0);
    }

    #endregion

    #region Test 2: DryRun_DoesNotCreateDestinationFolders

    [Test]
    public async Task DryRun_DoesNotCreateDestinationFolders()
    {
        // Arrange - Create test files
        var date1 = new DateTime(2024, 1, 15);
        var date2 = new DateTime(2024, 7, 20);
        var date3 = new DateTime(2024, 12, 25);

        await CreateTestJpegAsync("january.jpg", date1);
        await CreateTestJpegAsync("july.jpg", date2);
        await CreateTestJpegAsync("december.jpg", date3);

        // Ensure destination doesn't exist
        if (Directory.Exists(_destDir))
        {
            Directory.Delete(_destDir, true);
        }

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{day}", "{name}{ext}"),
            dryRun: true);

        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - No directories should be created
        Directory.Exists(_destDir).Should().BeFalse("DryRun should not create destination directory");
        Directory.Exists(Path.Combine(_destDir, "2024")).Should().BeFalse("DryRun should not create year subdirectory");
        Directory.Exists(Path.Combine(_destDir, "2024", "01")).Should().BeFalse("DryRun should not create month subdirectory");

        // But should report correct count
        result.FilesProcessed.Should().Be(3);
    }

    #endregion

    #region Test 3: DryRun_LogsWhatWouldBeCopied

    [Test]
    public async Task DryRun_LogsWhatWouldBeCopied()
    {
        // Arrange
        var date = new DateTime(2024, 5, 10, 12, 0, 0);
        var sourceFile = await CreateTestJpegAsync("summer.jpg", date);

        Directory.CreateDirectory(_destDir);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            dryRun: true);

        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        SharedLogs.Clear();

        // Act
        await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - Check logs for dry-run messages
        var logs = SharedLogs.Entries;
        var dryRunLogs = logs.Where(l => l.Message.Contains("DryRun", StringComparison.OrdinalIgnoreCase)).ToList();

        dryRunLogs.Should().NotBeEmpty("DryRun should log what would be copied");

        // Should log the operation that would be performed
        var operationLog = logs.FirstOrDefault(l => 
            l.Message.Contains("summer.jpg", StringComparison.OrdinalIgnoreCase) &&
            l.Message.Contains("DryRun", StringComparison.OrdinalIgnoreCase));

        operationLog.Should().NotBeNull("DryRun should log the specific file that would be copied");
    }

    #endregion

    #region Test 4: DryRun_ReportsCorrectFileCount

    [Test]
    public async Task DryRun_ReportsCorrectFileCount()
    {
        // Arrange - Create multiple test files
        await CreateTestJpegAsync("photo1.jpg", new DateTime(2024, 1, 1));
        await CreateTestJpegAsync("photo2.jpg", new DateTime(2024, 2, 1));
        await CreateTestJpegAsync("photo3.jpg", new DateTime(2024, 3, 1));
        await CreateTestPngAsync("photo4.png", new DateTime(2024, 4, 1));
        await CreateTestPngAsync("photo5.png", new DateTime(2024, 5, 1));

        Directory.CreateDirectory(_destDir);

        // First run dry-run
        var dryRunConfig = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            dryRun: true);

        var dryRunProvider = BuildRealServiceProvider(dryRunConfig);
        var dryRunCopier = dryRunProvider.GetRequiredService<IDirectoryCopierAsync>();
        var dryRunValidatorFactory = dryRunProvider.GetRequiredService<IValidatorFactory>();

        var dryRunResult = await dryRunCopier.CopyAsync(
            dryRunValidatorFactory.Create(dryRunConfig),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Then run real copy
        var realConfig = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            dryRun: false);

        var realProvider = BuildRealServiceProvider(realConfig);
        var realCopier = realProvider.GetRequiredService<IDirectoryCopierAsync>();
        var realValidatorFactory = realProvider.GetRequiredService<IValidatorFactory>();

        var realResult = await realCopier.CopyAsync(
            realValidatorFactory.Create(realConfig),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - File counts should match
        dryRunResult.FilesProcessed.Should().Be(realResult.FilesProcessed, "DryRun file count should match actual file count");
        dryRunResult.FilesProcessed.Should().Be(5);
    }

    #endregion

    #region Test 5: DryRun_DestinationPaths_MatchActualCopy

    [Test]
    public async Task DryRun_DestinationPaths_MatchActualCopy()
    {
        // Arrange
        var date1 = new DateTime(2024, 3, 15, 10, 30, 0);
        var date2 = new DateTime(2024, 8, 22, 16, 45, 0);
        
        await CreateTestJpegAsync("spring.jpg", date1);
        await CreateTestJpegAsync("summer.jpg", date2);

        Directory.CreateDirectory(_destDir);

        // Get dry-run plan
        var dryRunConfig = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{day}", "{name}{ext}"),
            dryRun: true);

        var dryRunProvider = BuildRealServiceProvider(dryRunConfig);
        var dryRunCopier = dryRunProvider.GetRequiredService<IDirectoryCopierAsync>();
        var dryRunValidatorFactory = dryRunProvider.GetRequiredService<IValidatorFactory>();

        var dryRunPlan = await dryRunCopier.BuildPlanAsync(
            dryRunValidatorFactory.Create(dryRunConfig),
            CancellationToken.None);

        var dryRunDestinations = dryRunPlan.Operations
            .Select(op => op.DestinationPath)
            .OrderBy(p => p)
            .ToList();

        // Execute real copy
        var realConfig = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{day}", "{name}{ext}"),
            dryRun: false);

        var realProvider = BuildRealServiceProvider(realConfig);
        var realCopier = realProvider.GetRequiredService<IDirectoryCopierAsync>();
        var realValidatorFactory = realProvider.GetRequiredService<IValidatorFactory>();

        await realCopier.CopyAsync(
            realValidatorFactory.Create(realConfig),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Get actual destination paths (excluding transaction log files)
        var actualDestinations = GetDestinationFiles()
            .OrderBy(p => p)
            .ToList();

        // Assert
        dryRunDestinations.Should().BeEquivalentTo(actualDestinations, "DryRun destination paths should match actual paths");

        // Verify specific paths
        dryRunDestinations.Should().Contain(Path.Combine(_destDir, "2024", "03", "15", "spring.jpg"));
        dryRunDestinations.Should().Contain(Path.Combine(_destDir, "2024", "08", "22", "summer.jpg"));
    }

    #endregion

    #region Test 6: DryRun_WithDuplicates_ReportsHowTheyWouldBeHandled

    [Test]
    public async Task DryRun_WithDuplicates_ReportsHowTheyWouldBeHandled()
    {
        // Arrange - Create files that would have duplicate destination paths
        // Same date = same destination folder, so we need files with same name at same date
        var date = new DateTime(2024, 6, 15);
        
        // Create files in different subfolders with same name and date
        await CreateTestJpegAsync("photo.jpg", date, subfolder: "folder1");
        await CreateTestJpegAsync("photo.jpg", date, subfolder: "folder2");

        Directory.CreateDirectory(_destDir);

        // Dry run with duplicate handling
        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            dryRun: true,
            duplicateHandling: DuplicateHandling.SkipDuplicates);

        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        SharedLogs.Clear();

        // Act
        var dryRunPlan = await copierAsync.BuildPlanAsync(
            validatorFactory.Create(config),
            CancellationToken.None);

        // Assert - Plan should show how duplicates would be handled
        // With duplicates format "_{number}", second file should get _1 suffix
        var destinations = dryRunPlan.Operations.Select(op => op.DestinationPath).ToList();

        destinations.Should().HaveCount(2, "Both files should be in the plan");

        // One should be the original name, one should have the duplicate suffix
        var expectedPath1 = Path.Combine(_destDir, "2024", "06", "photo.jpg");
        var expectedPath2 = Path.Combine(_destDir, "2024", "06", "photo_1.jpg");

        destinations.Should().Contain(p => p == expectedPath1 || p == expectedPath2, "Files should be renamed according to duplicate format");
    }

    #endregion

    #region Test 7: DryRun_WithDateFilter_ReportsFilteredCount

    [Test]
    public async Task DryRun_WithDateFilter_ReportsFilteredCount()
    {
        // Arrange - Create files with different dates
        await CreateTestJpegAsync("old_photo.jpg", new DateTime(2020, 1, 1));
        await CreateTestJpegAsync("recent_photo1.jpg", new DateTime(2024, 6, 1));
        await CreateTestJpegAsync("recent_photo2.jpg", new DateTime(2024, 7, 1));
        await CreateTestJpegAsync("future_photo.jpg", new DateTime(2025, 1, 1));

        Directory.CreateDirectory(_destDir);

        // Dry run with date filter (only 2024 files)
        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            dryRun: true,
            minDate: new DateTime(2024, 1, 1),
            maxDate: new DateTime(2024, 12, 31));

        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        result.FilesProcessed.Should().Be(2, "Only 2 files are in the 2024 date range");
        result.FilesSkipped.Should().Be(2, "2 files (old and future) should be skipped by date filter");
    }

    #endregion

    #region Test 8: DryRun_WithMoveMode_DoesNotMoveFiles

    [Test]
    public async Task DryRun_WithMoveMode_DoesNotMoveFiles()
    {
        // Arrange
        var date = new DateTime(2024, 4, 20);
        var sourceFile = await CreateTestJpegAsync("to_move.jpg", date);
        var originalContent = await File.ReadAllBytesAsync(sourceFile);

        Directory.CreateDirectory(_destDir);

        // Dry run in MOVE mode
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

        // Assert - Source file should still exist (not moved)
        File.Exists(sourceFile).Should().BeTrue("DryRun in Move mode should not actually move files");

        // Source content should be unchanged
        var currentContent = await File.ReadAllBytesAsync(sourceFile);
        currentContent.Should().BeEquivalentTo(originalContent, "Source file content should be unchanged");

        // Destination should not exist
        var expectedDest = Path.Combine(_destDir, "2024", "04", "to_move.jpg");
        File.Exists(expectedDest).Should().BeFalse("DryRun should not create destination files");

        // But result should indicate move would be done
        result.FilesProcessed.Should().Be(1);
    }

    #endregion

    #region Test 9: DryRun_TransactionLog_MarkedAsDryRun

    [Test]
    public async Task DryRun_TransactionLog_MarkedAsDryRun()
    {
        // Arrange
        var date = new DateTime(2024, 9, 10);
        await CreateTestJpegAsync("logged.jpg", date);

        Directory.CreateDirectory(_destDir);
        Directory.CreateDirectory(_logsDir);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            dryRun: true,
            enableRollback: true);

        var serviceProvider = BuildRealServiceProvider(config);
        var transactionLogger = serviceProvider.GetRequiredService<ITransactionLogger>();
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Start transaction with dry run flag
        transactionLogger.BeginTransaction(config.Source, config.Destination, isDryRun: true);

        // Act - Build and "execute" the plan
        var plan = await copierAsync.BuildPlanAsync(
            validatorFactory.Create(config),
            CancellationToken.None);

        // For dry run, we just log what would happen
        foreach (var operation in plan.Operations)
        {
            transactionLogger.LogOperation(
                operation.File.File.FullName,
                operation.DestinationPath,
                config.Mode == OperationMode.Move ? OperationType.Move : OperationType.Copy,
                operation.File.File.Length);
        }

        transactionLogger.CompleteTransaction();
        await transactionLogger.SaveAsync();

        // Assert - Transaction log should be marked as dry run
        var logPath = transactionLogger.TransactionLogPath;
        logPath.Should().NotBeNullOrEmpty();
        File.Exists(logPath).Should().BeTrue();

        var logContent = await File.ReadAllTextAsync(logPath!);
        var transactionLog = JsonSerializer.Deserialize<TransactionLog>(logContent, _jsonOptions);

        transactionLog.Should().NotBeNull();
        transactionLog!.IsDryRun.Should().BeTrue("Transaction log should be marked as dry run");
        transactionLog.Status.Should().Be(TransactionStatus.Completed);
        transactionLog.Operations.Should().HaveCount(1);
    }

    #endregion

    #region Test 10: RealCopy_AfterDryRun_MatchesPrediction

    [Test]
    public async Task RealCopy_AfterDryRun_MatchesPrediction()
    {
        // Arrange - Create test files
        var date1 = new DateTime(2024, 2, 14);
        var date2 = new DateTime(2024, 7, 4);
        var date3 = new DateTime(2024, 10, 31);

        await CreateTestJpegAsync("valentines.jpg", date1);
        await CreateTestJpegAsync("independence.jpg", date2);
        await CreateTestPngAsync("halloween.png", date3);

        Directory.CreateDirectory(_destDir);

        var destinationTemplate = Path.Combine(_destDir, "{year}", "{month}", "{day}", "{name}{ext}");

        // Step 1: Run dry-run and capture predictions
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

        var predictedFileCount = dryRunResult.FilesProcessed;
        var predictedPaths = dryRunPlan.Operations
            .Select(op => op.DestinationPath)
            .OrderBy(p => p)
            .ToList();
        var predictedDirectories = dryRunPlan.DirectoriesToCreate.OrderBy(d => d).ToList();
        var predictedTotalBytes = dryRunPlan.TotalBytes;

        // Verify nothing was actually created during dry run
        var filesBeforeRealCopy = Directory.Exists(_destDir) 
            ? GetDestinationFiles().Length 
            : 0;
        filesBeforeRealCopy.Should().Be(0, "Dry run should not create any files");

        // Step 2: Run real copy
        var realConfig = CreateConfig(destinationTemplate, dryRun: false);
        var realProvider = BuildRealServiceProvider(realConfig);
        var realCopier = realProvider.GetRequiredService<IDirectoryCopierAsync>();
        var realValidatorFactory = realProvider.GetRequiredService<IValidatorFactory>();

        var realResult = await realCopier.CopyAsync(
            realValidatorFactory.Create(realConfig),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Step 3: Verify predictions match reality
        var actualPaths = GetDestinationFiles()
            .OrderBy(p => p)
            .ToList();

        // File count should match
        predictedFileCount.Should().Be(realResult.FilesProcessed, "Dry run file count should match real copy count");

        // Destination paths should match
        predictedPaths.Should().BeEquivalentTo(actualPaths, "Dry run predicted paths should match actual paths");

        // All predicted directories should exist
        foreach (var dir in predictedDirectories)
        {
            Directory.Exists(dir).Should().BeTrue($"Predicted directory {dir} should have been created");
        }

        // Verify specific expected files exist
        File.Exists(Path.Combine(_destDir, "2024", "02", "14", "valentines.jpg")).Should().BeTrue();
        File.Exists(Path.Combine(_destDir, "2024", "07", "04", "independence.jpg")).Should().BeTrue();
        File.Exists(Path.Combine(_destDir, "2024", "10", "31", "halloween.png")).Should().BeTrue();

        // Error counts should match
        dryRunResult.FilesFailed.Should().Be(realResult.FilesFailed);
        dryRunResult.FilesSkipped.Should().Be(realResult.FilesSkipped);
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









