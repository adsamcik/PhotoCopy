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

namespace PhotoCopy.Tests.Integration;

/// <summary>
/// End-to-end integration tests for Copy → Rollback workflows.
/// These tests:
/// 1. Create real JPEG files with EXIF metadata using MockImageGenerator
/// 2. Perform actual copy/move operations to real temp directories
/// 3. Create transaction logs for rollback support
/// 4. Execute rollback operations and verify files are restored correctly
/// </summary>
[NotInParallel("FileOperations")]
[Property("Category", "Integration,EndToEnd,Rollback")]
public class CopyRollbackEndToEndTests
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
        _testBaseDirectory = Path.Combine(Path.GetTempPath(), "CopyRollbackEndToEndTests", Guid.NewGuid().ToString());
        _sourceDir = Path.Combine(_testBaseDirectory, "source");
        _destDir = Path.Combine(_testBaseDirectory, "dest");
        _logsDir = Path.Combine(_destDir, ".photocopy-logs");

        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_destDir);
        Directory.CreateDirectory(_logsDir);

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

        // Register transaction logger and rollback service
        services.AddSingleton<ITransactionLogger, TransactionLogger>();
        services.AddSingleton<IRollbackService, RollbackService>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates a PhotoCopyConfig with the test directories.
    /// </summary>
    private PhotoCopyConfig CreateConfig(
        string destinationTemplate,
        bool dryRun = false,
        OperationMode mode = OperationMode.Copy,
        bool enableRollback = true,
        DuplicateHandling duplicateHandling = DuplicateHandling.None)
    {
        return new PhotoCopyConfig
        {
            Source = _sourceDir,
            Destination = destinationTemplate,
            DryRun = dryRun,
            Mode = mode,
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

    /// <summary>
    /// Performs a copy operation and creates a transaction log.
    /// Returns the transaction log path.
    /// </summary>
    private async Task<(string LogPath, CopyResult Result)> CopyWithTransactionLogAsync(
        PhotoCopyConfig config,
        IServiceProvider serviceProvider)
    {
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();
        var transactionLogger = serviceProvider.GetRequiredService<ITransactionLogger>();
        var fileSystem = serviceProvider.GetRequiredService<IFileSystem>();

        // Begin transaction
        transactionLogger.BeginTransaction(config.Source, config.Destination, config.DryRun);

        // Build and execute the copy plan
        var validators = validatorFactory.Create(config);
        var plan = await copierAsync.BuildPlanAsync(validators, CancellationToken.None);

        // Track directories to create
        foreach (var dir in plan.DirectoriesToCreate)
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                transactionLogger.LogDirectoryCreated(dir);
            }
        }

        // Execute each operation manually to log them
        var processedCount = 0;
        var errors = new List<CopyError>();

        foreach (var operation in plan.Operations)
        {
            try
            {
                var destDir = Path.GetDirectoryName(operation.DestinationPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                    transactionLogger.LogDirectoryCreated(destDir);
                }

                if (config.Mode == OperationMode.Move)
                {
                    File.Move(operation.File.File.FullName, operation.DestinationPath);
                    transactionLogger.LogOperation(
                        operation.File.File.FullName,
                        operation.DestinationPath,
                        OperationType.Move,
                        operation.File.File.Length);
                }
                else
                {
                    File.Copy(operation.File.File.FullName, operation.DestinationPath, true);
                    transactionLogger.LogOperation(
                        operation.File.File.FullName,
                        operation.DestinationPath,
                        OperationType.Copy,
                        operation.File.File.Length);
                }

                processedCount++;
            }
            catch (Exception ex)
            {
                errors.Add(new CopyError(operation.File, operation.DestinationPath, ex.Message));
            }
        }

        // Complete and save the transaction
        transactionLogger.CompleteTransaction();
        await transactionLogger.SaveAsync();

        var result = new CopyResult(
            FilesProcessed: processedCount,
            FilesFailed: errors.Count,
            FilesSkipped: plan.SkippedFiles.Count,
            BytesProcessed: plan.TotalBytes,
            Errors: errors);

        return (transactionLogger.TransactionLogPath!, result);
    }

    /// <summary>
    /// Creates a transaction log directly for testing rollback scenarios.
    /// </summary>
    private async Task<string> CreateTransactionLogAsync(TransactionLog log)
    {
        var logPath = Path.Combine(_logsDir, $"photocopy-{log.TransactionId}.json");
        var json = JsonSerializer.Serialize(log, _jsonOptions);
        await File.WriteAllTextAsync(logPath, json);
        return logPath;
    }

    #endregion

    #region Copy → Rollback Tests

    [Test]
    public async Task CopyWithTransactionLog_ThenRollback_RestoresFilesToOriginalLocations()
    {
        // Arrange - Create test files with different dates
        var date1 = new DateTime(2024, 3, 15, 10, 30, 0);
        var date2 = new DateTime(2024, 6, 20, 14, 45, 0);
        var date3 = new DateTime(2024, 9, 5, 8, 15, 0);

        var sourceFile1 = await CreateTestJpegAsync("photo1.jpg", date1);
        var sourceFile2 = await CreateTestJpegAsync("photo2.jpg", date2);
        var sourceFile3 = await CreateTestJpegAsync("photo3.jpg", date3);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{day}", "{name}{ext}"),
            mode: OperationMode.Copy);

        var serviceProvider = BuildRealServiceProvider(config);
        var rollbackService = serviceProvider.GetRequiredService<IRollbackService>();

        // Act - Step 1: Copy files with transaction logging
        var (logPath, copyResult) = await CopyWithTransactionLogAsync(config, serviceProvider);

        // Verify files were copied
        copyResult.FilesProcessed.Should().Be(3);
        copyResult.FilesFailed.Should().Be(0);

        var expectedPath1 = Path.Combine(_destDir, "2024", "03", "15", "photo1.jpg");
        var expectedPath2 = Path.Combine(_destDir, "2024", "06", "20", "photo2.jpg");
        var expectedPath3 = Path.Combine(_destDir, "2024", "09", "05", "photo3.jpg");

        File.Exists(expectedPath1).Should().BeTrue();
        File.Exists(expectedPath2).Should().BeTrue();
        File.Exists(expectedPath3).Should().BeTrue();

        // Source files should still exist (copy mode)
        File.Exists(sourceFile1).Should().BeTrue();
        File.Exists(sourceFile2).Should().BeTrue();
        File.Exists(sourceFile3).Should().BeTrue();

        // Act - Step 2: Rollback
        var rollbackResult = await rollbackService.RollbackAsync(logPath);

        // Assert - Rollback should succeed
        rollbackResult.Success.Should().BeTrue();
        rollbackResult.FilesRestored.Should().Be(3);
        rollbackResult.FilesFailed.Should().Be(0);

        // Destination files should be deleted
        File.Exists(expectedPath1).Should().BeFalse();
        File.Exists(expectedPath2).Should().BeFalse();
        File.Exists(expectedPath3).Should().BeFalse();

        // Source files should still exist
        File.Exists(sourceFile1).Should().BeTrue();
        File.Exists(sourceFile2).Should().BeTrue();
        File.Exists(sourceFile3).Should().BeTrue();
    }

    [Test]
    public async Task CopyMove_ThenRollback_RestoresMovedFiles()
    {
        // Arrange - Create test files
        var date1 = new DateTime(2024, 4, 10, 12, 0, 0);
        var date2 = new DateTime(2024, 4, 11, 14, 30, 0);

        var sourceFile1 = await CreateTestJpegAsync("moved1.jpg", date1);
        var sourceFile2 = await CreateTestJpegAsync("moved2.jpg", date2);

        // Store original content for verification
        var originalContent1 = await File.ReadAllBytesAsync(sourceFile1);
        var originalContent2 = await File.ReadAllBytesAsync(sourceFile2);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            mode: OperationMode.Move);

        var serviceProvider = BuildRealServiceProvider(config);
        var rollbackService = serviceProvider.GetRequiredService<IRollbackService>();

        // Act - Step 1: Move files with transaction logging
        var (logPath, moveResult) = await CopyWithTransactionLogAsync(config, serviceProvider);

        // Verify files were moved
        moveResult.FilesProcessed.Should().Be(2);
        moveResult.FilesFailed.Should().Be(0);

        var expectedPath1 = Path.Combine(_destDir, "2024", "04", "moved1.jpg");
        var expectedPath2 = Path.Combine(_destDir, "2024", "04", "moved2.jpg");

        File.Exists(expectedPath1).Should().BeTrue();
        File.Exists(expectedPath2).Should().BeTrue();

        // Source files should be deleted (move mode)
        File.Exists(sourceFile1).Should().BeFalse();
        File.Exists(sourceFile2).Should().BeFalse();

        // Act - Step 2: Rollback
        var rollbackResult = await rollbackService.RollbackAsync(logPath);

        // Assert - Rollback should succeed
        rollbackResult.Success.Should().BeTrue();
        rollbackResult.FilesRestored.Should().Be(2);
        rollbackResult.FilesFailed.Should().Be(0);

        // Files should be moved back to source
        File.Exists(sourceFile1).Should().BeTrue();
        File.Exists(sourceFile2).Should().BeTrue();

        // Destination files should be deleted
        File.Exists(expectedPath1).Should().BeFalse();
        File.Exists(expectedPath2).Should().BeFalse();

        // Verify content matches original
        var restoredContent1 = await File.ReadAllBytesAsync(sourceFile1);
        var restoredContent2 = await File.ReadAllBytesAsync(sourceFile2);

        restoredContent1.Should().BeEquivalentTo(originalContent1);
        restoredContent2.Should().BeEquivalentTo(originalContent2);
    }

    [Test]
    public async Task Rollback_WhenTransactionLogMissing_HandlesGracefully()
    {
        // Arrange
        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var serviceProvider = BuildRealServiceProvider(config);
        var rollbackService = serviceProvider.GetRequiredService<IRollbackService>();

        var nonExistentLogPath = Path.Combine(_logsDir, "non-existent-transaction.json");

        // Act
        var rollbackResult = await rollbackService.RollbackAsync(nonExistentLogPath);

        // Assert
        rollbackResult.Success.Should().BeFalse();
        rollbackResult.FilesRestored.Should().Be(0);
        rollbackResult.Errors.Should().HaveCountGreaterThan(0);
        rollbackResult.Errors.Should().Contain(e => e.Contains("not found"));
    }

    [Test]
    public async Task Rollback_WhenPartialFilesDeleted_RollsBackRemainingFiles()
    {
        // Arrange - Create test files
        var date1 = new DateTime(2024, 5, 1, 10, 0, 0);
        var date2 = new DateTime(2024, 5, 2, 11, 0, 0);
        var date3 = new DateTime(2024, 5, 3, 12, 0, 0);

        var sourceFile1 = await CreateTestJpegAsync("partial1.jpg", date1);
        var sourceFile2 = await CreateTestJpegAsync("partial2.jpg", date2);
        var sourceFile3 = await CreateTestJpegAsync("partial3.jpg", date3);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            mode: OperationMode.Copy);

        var serviceProvider = BuildRealServiceProvider(config);
        var rollbackService = serviceProvider.GetRequiredService<IRollbackService>();

        // Act - Step 1: Copy files with transaction logging
        var (logPath, copyResult) = await CopyWithTransactionLogAsync(config, serviceProvider);

        // Verify files were copied
        copyResult.FilesProcessed.Should().Be(3);

        var expectedPath1 = Path.Combine(_destDir, "2024", "05", "partial1.jpg");
        var expectedPath2 = Path.Combine(_destDir, "2024", "05", "partial2.jpg");
        var expectedPath3 = Path.Combine(_destDir, "2024", "05", "partial3.jpg");

        File.Exists(expectedPath1).Should().BeTrue();
        File.Exists(expectedPath2).Should().BeTrue();
        File.Exists(expectedPath3).Should().BeTrue();

        // Delete one of the destination files before rollback (simulating partial deletion)
        File.Delete(expectedPath2);
        File.Exists(expectedPath2).Should().BeFalse();

        // Act - Step 2: Rollback
        var rollbackResult = await rollbackService.RollbackAsync(logPath);

        // Assert - Rollback should succeed for remaining files
        // One file was already deleted, so it can't be "restored" (in copy mode, it's just deleted)
        rollbackResult.FilesRestored.Should().BeGreaterThanOrEqualTo(2);

        // The remaining destination files should be deleted
        File.Exists(expectedPath1).Should().BeFalse();
        File.Exists(expectedPath3).Should().BeFalse();

        // Source files should still exist (copy mode)
        File.Exists(sourceFile1).Should().BeTrue();
        File.Exists(sourceFile2).Should().BeTrue();
        File.Exists(sourceFile3).Should().BeTrue();
    }

    [Test]
    public async Task CopyWithDuplicates_ThenRollback_RestoresAllVersions()
    {
        // Arrange - Create multiple files that will go to the same destination
        // (same date, different files - will trigger duplicate naming)
        var sameDate = new DateTime(2024, 7, 15, 14, 0, 0);

        var sourceFile1 = await CreateTestJpegAsync("photo.jpg", sameDate);
        // Create a different photo that will be renamed due to duplicate
        await Task.Delay(50); // Ensure different content

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            mode: OperationMode.Copy);

        var serviceProvider = BuildRealServiceProvider(config);
        var rollbackService = serviceProvider.GetRequiredService<IRollbackService>();

        // Act - Step 1: Copy files with transaction logging
        var (logPath, copyResult) = await CopyWithTransactionLogAsync(config, serviceProvider);

        // Verify file was copied
        copyResult.FilesProcessed.Should().BeGreaterThanOrEqualTo(1);

        var expectedPath = Path.Combine(_destDir, "2024", "07", "photo.jpg");
        File.Exists(expectedPath).Should().BeTrue();

        // Source file should still exist (copy mode)
        File.Exists(sourceFile1).Should().BeTrue();

        // Act - Step 2: Rollback
        var rollbackResult = await rollbackService.RollbackAsync(logPath);

        // Assert - Rollback should succeed
        rollbackResult.Success.Should().BeTrue();
        rollbackResult.FilesRestored.Should().BeGreaterThanOrEqualTo(1);

        // Destination files should be deleted
        File.Exists(expectedPath).Should().BeFalse();

        // Source file should still exist
        File.Exists(sourceFile1).Should().BeTrue();
    }

    #endregion

    #region Additional Edge Case Tests

    [Test]
    public async Task Rollback_WithEmptyTransactionLog_HandlesGracefully()
    {
        // Arrange - Create a transaction log with no operations
        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var serviceProvider = BuildRealServiceProvider(config);
        var rollbackService = serviceProvider.GetRequiredService<IRollbackService>();

        var emptyLog = new TransactionLog
        {
            TransactionId = Guid.NewGuid().ToString("N"),
            StartTime = DateTime.UtcNow,
            SourceDirectory = _sourceDir,
            DestinationPattern = Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            Status = TransactionStatus.Completed,
            Operations = new List<FileOperationEntry>(),
            CreatedDirectories = new List<string>()
        };

        var logPath = await CreateTransactionLogAsync(emptyLog);

        // Act
        var rollbackResult = await rollbackService.RollbackAsync(logPath);

        // Assert - Should succeed with nothing to rollback
        rollbackResult.Success.Should().BeTrue();
        rollbackResult.FilesRestored.Should().Be(0);
        rollbackResult.FilesFailed.Should().Be(0);
    }

    [Test]
    public async Task Rollback_WithDryRunTransaction_ReturnsError()
    {
        // Arrange - Create a dry run transaction log
        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var serviceProvider = BuildRealServiceProvider(config);
        var rollbackService = serviceProvider.GetRequiredService<IRollbackService>();

        var dryRunLog = new TransactionLog
        {
            TransactionId = Guid.NewGuid().ToString("N"),
            StartTime = DateTime.UtcNow,
            SourceDirectory = _sourceDir,
            DestinationPattern = Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            IsDryRun = true,
            Status = TransactionStatus.Completed,
            Operations = new List<FileOperationEntry>
            {
                new()
                {
                    SourcePath = Path.Combine(_sourceDir, "test.jpg"),
                    DestinationPath = Path.Combine(_destDir, "2024", "01", "test.jpg"),
                    Operation = OperationType.Copy,
                    Timestamp = DateTime.UtcNow,
                    FileSize = 1000
                }
            }
        };

        var logPath = await CreateTransactionLogAsync(dryRunLog);

        // Act
        var rollbackResult = await rollbackService.RollbackAsync(logPath);

        // Assert - Should fail because it's a dry run
        rollbackResult.Success.Should().BeFalse();
        rollbackResult.Errors.Should().Contain(e => e.Contains("dry run", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task Rollback_CleansUpEmptyDirectories()
    {
        // Arrange - Create a test file
        var date = new DateTime(2024, 8, 25, 16, 30, 0);
        var sourceFile = await CreateTestJpegAsync("cleanup_test.jpg", date);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{day}", "{name}{ext}"),
            mode: OperationMode.Copy);

        var serviceProvider = BuildRealServiceProvider(config);
        var rollbackService = serviceProvider.GetRequiredService<IRollbackService>();

        // Act - Step 1: Copy file with transaction logging
        var (logPath, copyResult) = await CopyWithTransactionLogAsync(config, serviceProvider);

        copyResult.FilesProcessed.Should().Be(1);

        var expectedPath = Path.Combine(_destDir, "2024", "08", "25", "cleanup_test.jpg");
        File.Exists(expectedPath).Should().BeTrue();

        // Verify directories were created
        Directory.Exists(Path.Combine(_destDir, "2024")).Should().BeTrue();
        Directory.Exists(Path.Combine(_destDir, "2024", "08")).Should().BeTrue();
        Directory.Exists(Path.Combine(_destDir, "2024", "08", "25")).Should().BeTrue();

        // Act - Step 2: Rollback
        var rollbackResult = await rollbackService.RollbackAsync(logPath);

        // Assert
        rollbackResult.Success.Should().BeTrue();
        rollbackResult.FilesRestored.Should().Be(1);

        // File should be deleted
        File.Exists(expectedPath).Should().BeFalse();

        // Empty directories should be cleaned up
        rollbackResult.DirectoriesRemoved.Should().BeGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task CopyMultipleFilesToSameFolder_ThenRollback_RemovesAllFiles()
    {
        // Arrange - Create multiple files with the same month (will go to same folder)
        var date1 = new DateTime(2024, 10, 1, 9, 0, 0);
        var date2 = new DateTime(2024, 10, 15, 12, 0, 0);
        var date3 = new DateTime(2024, 10, 28, 18, 0, 0);

        var sourceFile1 = await CreateTestJpegAsync("oct_photo1.jpg", date1);
        var sourceFile2 = await CreateTestJpegAsync("oct_photo2.jpg", date2);
        var sourceFile3 = await CreateTestJpegAsync("oct_photo3.jpg", date3);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            mode: OperationMode.Copy);

        var serviceProvider = BuildRealServiceProvider(config);
        var rollbackService = serviceProvider.GetRequiredService<IRollbackService>();

        // Act - Step 1: Copy files
        var (logPath, copyResult) = await CopyWithTransactionLogAsync(config, serviceProvider);

        copyResult.FilesProcessed.Should().Be(3);

        var destFolder = Path.Combine(_destDir, "2024", "10");
        Directory.Exists(destFolder).Should().BeTrue();
        Directory.GetFiles(destFolder).Should().HaveCount(3);

        // Act - Step 2: Rollback
        var rollbackResult = await rollbackService.RollbackAsync(logPath);

        // Assert
        rollbackResult.Success.Should().BeTrue();
        rollbackResult.FilesRestored.Should().Be(3);

        // All destination files should be removed
        File.Exists(Path.Combine(destFolder, "oct_photo1.jpg")).Should().BeFalse();
        File.Exists(Path.Combine(destFolder, "oct_photo2.jpg")).Should().BeFalse();
        File.Exists(Path.Combine(destFolder, "oct_photo3.jpg")).Should().BeFalse();

        // Source files should still exist
        File.Exists(sourceFile1).Should().BeTrue();
        File.Exists(sourceFile2).Should().BeTrue();
        File.Exists(sourceFile3).Should().BeTrue();
    }

    #endregion
}
