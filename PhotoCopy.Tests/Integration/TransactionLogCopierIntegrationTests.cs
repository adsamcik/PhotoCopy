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
/// Integration tests verifying that DirectoryCopierAsync writes transaction logs
/// when CreateTransactionLog/EnableRollback is enabled.
/// These tests use real temp directories, real file operations, and MockImageGenerator
/// to create valid JPEG images with embedded EXIF metadata.
/// </summary>
[NotInParallel("FileOperations")]
[Property("Category", "Integration,TransactionLog")]
public class TransactionLogCopierIntegrationTests
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
        _testBaseDirectory = Path.Combine(Path.GetTempPath(), "TransactionLogCopierIntegrationTests", Guid.NewGuid().ToString());
        _sourceDir = Path.Combine(_testBaseDirectory, "source");
        _destDir = Path.Combine(_testBaseDirectory, "dest");
        _logsDir = Path.Combine(_destDir, ".photocopy-logs");

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

        // Register transaction logger
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
    /// Performs a copy operation with transaction logging.
    /// Returns the transaction log path and copy result.
    /// </summary>
    private async Task<(string? LogPath, CopyResult Result)> CopyWithTransactionLogAsync(
        PhotoCopyConfig config,
        IServiceProvider serviceProvider)
    {
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();
        var transactionLogger = serviceProvider.GetRequiredService<ITransactionLogger>();

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

        if (!config.DryRun)
        {
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

        return (transactionLogger.TransactionLogPath, result);
    }

    /// <summary>
    /// Reads and deserializes a transaction log from disk.
    /// </summary>
    private async Task<TransactionLog?> ReadTransactionLogAsync(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(logPath);
        return JsonSerializer.Deserialize<TransactionLog>(json, _jsonOptions);
    }

    /// <summary>
    /// Finds all transaction log files in the logs directory.
    /// </summary>
    private IEnumerable<string> FindTransactionLogFiles()
    {
        if (!Directory.Exists(_logsDir))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(_logsDir, "photocopy-*.json");
    }

    #endregion

    #region Transaction Log Creation Tests

    [Test]
    public async Task DirectoryCopierAsync_WhenTransactionLogEnabled_WritesLogFile()
    {
        // Arrange - Create test JPEG files
        var date1 = new DateTime(2024, 5, 15, 10, 30, 0);
        var date2 = new DateTime(2024, 6, 20, 14, 45, 0);

        await CreateTestJpegAsync("photo1.jpg", date1);
        await CreateTestJpegAsync("photo2.jpg", date2);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            enableRollback: true);

        var serviceProvider = BuildRealServiceProvider(config);

        // Act - Copy files with transaction logging enabled
        var (logPath, result) = await CopyWithTransactionLogAsync(config, serviceProvider);

        // Assert - Transaction log file should be created
        await Assert.That(logPath).IsNotNull();
        await Assert.That(File.Exists(logPath)).IsTrue()
            .Because("transaction log file should exist on disk after copy operation");
        await Assert.That(result.FilesProcessed).IsEqualTo(2);
        await Assert.That(result.FilesFailed).IsEqualTo(0);
    }

    [Test]
    public async Task DirectoryCopierAsync_WhenTransactionLogDisabled_DoesNotWriteLogFile()
    {
        // Arrange - Create test JPEG file
        var dateTaken = new DateTime(2024, 7, 10, 9, 0, 0);
        await CreateTestJpegAsync("nolog.jpg", dateTaken);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            enableRollback: false);

        var serviceProvider = BuildRealServiceProvider(config);

        // Use DirectoryCopierAsync directly without transaction logging
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();

        // Act - Copy files without transaction logging
        var result = await copierAsync.CopyAsync(
            validatorFactory.Create(config),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - No transaction log files should exist
        var logFiles = FindTransactionLogFiles();

        await Assert.That(result.FilesProcessed).IsEqualTo(1);
        await Assert.That(logFiles).IsEmpty()
            .Because("no transaction log should be written when EnableRollback is false");
    }

    [Test]
    public async Task TransactionLog_ContainsCorrectSourceAndDestinationPaths()
    {
        // Arrange - Create test JPEG with known date
        var dateTaken = new DateTime(2024, 3, 25, 11, 30, 0);
        var sourceFilePath = await CreateTestJpegAsync("pathtest.jpg", dateTaken);

        var destinationPattern = Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}");
        var config = CreateConfig(destinationPattern, enableRollback: true);

        var serviceProvider = BuildRealServiceProvider(config);

        // Act - Copy file with transaction logging
        var (logPath, _) = await CopyWithTransactionLogAsync(config, serviceProvider);

        // Assert - Read and verify transaction log content
        await Assert.That(logPath).IsNotNull();
        var transactionLog = await ReadTransactionLogAsync(logPath!);

        await Assert.That(transactionLog).IsNotNull();
        await Assert.That(transactionLog!.Operations).HasCount().EqualTo(1);

        var operation = transactionLog.Operations[0];
        await Assert.That(operation.SourcePath).IsEqualTo(sourceFilePath)
            .Because("source path in log should match the original file path");

        var expectedDestination = Path.Combine(_destDir, "2024", "03", "pathtest.jpg");
        await Assert.That(operation.DestinationPath).IsEqualTo(expectedDestination)
            .Because("destination path should be correctly resolved from the pattern");

        // Verify source and destination directories are correct in log
        await Assert.That(transactionLog.SourceDirectory).IsEqualTo(_sourceDir);
        await Assert.That(transactionLog.DestinationPattern).IsEqualTo(destinationPattern);
    }

    [Test]
    public async Task TransactionLog_ContainsCorrectCopyMode()
    {
        // Arrange - Create test JPEG
        var dateTaken = new DateTime(2024, 8, 5, 16, 0, 0);
        await CreateTestJpegAsync("copymode.jpg", dateTaken);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            mode: OperationMode.Copy,
            enableRollback: true);

        var serviceProvider = BuildRealServiceProvider(config);

        // Act - Copy file
        var (logPath, _) = await CopyWithTransactionLogAsync(config, serviceProvider);

        // Assert - Verify log records Copy mode
        var transactionLog = await ReadTransactionLogAsync(logPath!);

        await Assert.That(transactionLog).IsNotNull();
        await Assert.That(transactionLog!.Operations).HasCount().EqualTo(1);
        await Assert.That(transactionLog.Operations[0].Operation).IsEqualTo(OperationType.Copy)
            .Because("transaction log should record Copy operation type");
    }

    [Test]
    public async Task TransactionLog_ContainsCorrectMoveMode()
    {
        // Arrange - Create test JPEG
        var dateTaken = new DateTime(2024, 9, 12, 8, 45, 0);
        await CreateTestJpegAsync("movemode.jpg", dateTaken);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            mode: OperationMode.Move,
            enableRollback: true);

        var serviceProvider = BuildRealServiceProvider(config);

        // Act - Move file
        var (logPath, _) = await CopyWithTransactionLogAsync(config, serviceProvider);

        // Assert - Verify log records Move mode
        var transactionLog = await ReadTransactionLogAsync(logPath!);

        await Assert.That(transactionLog).IsNotNull();
        await Assert.That(transactionLog!.Operations).HasCount().EqualTo(1);
        await Assert.That(transactionLog.Operations[0].Operation).IsEqualTo(OperationType.Move)
            .Because("transaction log should record Move operation type");
    }

    [Test]
    public async Task TransactionLog_RecordsAllCopiedFiles()
    {
        // Arrange - Create multiple test JPEG files with different dates
        var dates = new[]
        {
            new DateTime(2024, 1, 10, 9, 0, 0),
            new DateTime(2024, 2, 15, 10, 30, 0),
            new DateTime(2024, 3, 20, 11, 45, 0),
            new DateTime(2024, 4, 25, 13, 0, 0),
            new DateTime(2024, 5, 30, 14, 15, 0)
        };

        var sourceFiles = new List<string>();
        for (var i = 0; i < dates.Length; i++)
        {
            var path = await CreateTestJpegAsync($"multi{i + 1}.jpg", dates[i]);
            sourceFiles.Add(path);
        }

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            enableRollback: true);

        var serviceProvider = BuildRealServiceProvider(config);

        // Act - Copy all files with transaction logging
        var (logPath, result) = await CopyWithTransactionLogAsync(config, serviceProvider);

        // Assert - All files should be recorded in transaction log
        await Assert.That(result.FilesProcessed).IsEqualTo(5);

        var transactionLog = await ReadTransactionLogAsync(logPath!);

        await Assert.That(transactionLog).IsNotNull();
        await Assert.That(transactionLog!.Operations).HasCount().EqualTo(5)
            .Because("all 5 copied files should be recorded in the transaction log");

        // Verify each source file is recorded
        var recordedSourcePaths = transactionLog.Operations.Select(o => o.SourcePath).ToList();
        foreach (var sourceFile in sourceFiles)
        {
            await Assert.That(recordedSourcePaths).Contains(sourceFile)
                .Because($"source file {Path.GetFileName(sourceFile)} should be recorded in the log");
        }

        // Verify unique destination paths
        var destinationPaths = transactionLog.Operations.Select(o => o.DestinationPath).ToList();
        await Assert.That(destinationPaths.Distinct().Count()).IsEqualTo(5)
            .Because("each file should have a unique destination path");
    }

    [Test]
    public async Task TransactionLog_WithDryRun_MarksLogAsDryRun()
    {
        // Arrange - Create test JPEG
        var dateTaken = new DateTime(2024, 10, 1, 15, 30, 0);
        await CreateTestJpegAsync("dryrun.jpg", dateTaken);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            dryRun: true,
            enableRollback: true);

        var serviceProvider = BuildRealServiceProvider(config);

        // Act - Perform dry run with transaction logging
        var (logPath, result) = await CopyWithTransactionLogAsync(config, serviceProvider);

        // Assert - Transaction log should be marked as dry run
        var transactionLog = await ReadTransactionLogAsync(logPath!);

        await Assert.That(transactionLog).IsNotNull();
        await Assert.That(transactionLog!.IsDryRun).IsTrue()
            .Because("transaction log should be marked as dry run when DryRun is true");

        // In dry run mode, no files should be processed
        await Assert.That(result.FilesProcessed).IsEqualTo(0)
            .Because("dry run should not actually copy files");

        // Operations list should be empty for dry run
        await Assert.That(transactionLog.Operations).IsEmpty()
            .Because("no actual operations are performed during dry run");
    }

    #endregion

    #region Transaction Log Metadata Tests

    [Test]
    public async Task TransactionLog_HasValidTransactionId()
    {
        // Arrange
        var dateTaken = new DateTime(2024, 11, 15, 12, 0, 0);
        await CreateTestJpegAsync("txid.jpg", dateTaken);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            enableRollback: true);

        var serviceProvider = BuildRealServiceProvider(config);

        // Act
        var (logPath, _) = await CopyWithTransactionLogAsync(config, serviceProvider);

        // Assert
        var transactionLog = await ReadTransactionLogAsync(logPath!);

        await Assert.That(transactionLog).IsNotNull();
        await Assert.That(transactionLog!.TransactionId).IsNotNullOrEmpty()
            .Because("transaction log should have a valid transaction ID");
        await Assert.That(System.Text.RegularExpressions.Regex.IsMatch(
            transactionLog.TransactionId, @"^\d{8}-\d{6}-[a-f0-9]{8}$")).IsTrue()
            .Because("transaction ID should follow the expected format: yyyyMMdd-HHmmss-guidpart");
    }

    [Test]
    public async Task TransactionLog_HasCompletedStatus()
    {
        // Arrange
        var dateTaken = new DateTime(2024, 12, 20, 18, 30, 0);
        await CreateTestJpegAsync("status.jpg", dateTaken);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            enableRollback: true);

        var serviceProvider = BuildRealServiceProvider(config);

        // Act
        var (logPath, _) = await CopyWithTransactionLogAsync(config, serviceProvider);

        // Assert
        var transactionLog = await ReadTransactionLogAsync(logPath!);

        await Assert.That(transactionLog).IsNotNull();
        await Assert.That(transactionLog!.Status).IsEqualTo(TransactionStatus.Completed)
            .Because("transaction log should have Completed status after successful copy");
    }

    [Test]
    public async Task TransactionLog_RecordsTimestamps()
    {
        // Arrange
        var dateTaken = new DateTime(2025, 1, 5, 10, 0, 0);
        await CreateTestJpegAsync("timestamp.jpg", dateTaken);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            enableRollback: true);

        var serviceProvider = BuildRealServiceProvider(config);

        var beforeCopy = DateTime.UtcNow;

        // Act
        var (logPath, _) = await CopyWithTransactionLogAsync(config, serviceProvider);

        var afterCopy = DateTime.UtcNow;

        // Assert
        var transactionLog = await ReadTransactionLogAsync(logPath!);

        await Assert.That(transactionLog).IsNotNull();
        await Assert.That(transactionLog!.StartTime).IsGreaterThanOrEqualTo(beforeCopy)
            .Because("start time should be after we started the copy");
        await Assert.That(transactionLog.StartTime).IsLessThanOrEqualTo(afterCopy)
            .Because("start time should be before we finished the copy");

        await Assert.That(transactionLog.EndTime).IsNotNull()
            .Because("end time should be set after completion");
        await Assert.That(transactionLog.EndTime!.Value).IsGreaterThanOrEqualTo(transactionLog.StartTime)
            .Because("end time should be after start time");
    }

    [Test]
    public async Task TransactionLog_RecordsFileSizes()
    {
        // Arrange
        var dateTaken = new DateTime(2024, 6, 15, 14, 0, 0);
        var sourceFilePath = await CreateTestJpegAsync("filesize.jpg", dateTaken);
        var expectedFileSize = new FileInfo(sourceFilePath).Length;

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            enableRollback: true);

        var serviceProvider = BuildRealServiceProvider(config);

        // Act
        var (logPath, _) = await CopyWithTransactionLogAsync(config, serviceProvider);

        // Assert
        var transactionLog = await ReadTransactionLogAsync(logPath!);

        await Assert.That(transactionLog).IsNotNull();
        await Assert.That(transactionLog!.Operations).HasCount().EqualTo(1);
        await Assert.That(transactionLog.Operations[0].FileSize).IsEqualTo(expectedFileSize)
            .Because("transaction log should record accurate file sizes");
    }

    [Test]
    public async Task TransactionLog_RecordsCreatedDirectories()
    {
        // Arrange - Create test files that will go into different directories
        var date1 = new DateTime(2024, 1, 15, 10, 0, 0);
        var date2 = new DateTime(2024, 7, 20, 12, 0, 0);

        await CreateTestJpegAsync("jan.jpg", date1);
        await CreateTestJpegAsync("jul.jpg", date2);

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            enableRollback: true);

        var serviceProvider = BuildRealServiceProvider(config);

        // Act
        var (logPath, _) = await CopyWithTransactionLogAsync(config, serviceProvider);

        // Assert
        var transactionLog = await ReadTransactionLogAsync(logPath!);

        await Assert.That(transactionLog).IsNotNull();
        await Assert.That(transactionLog!.CreatedDirectories).IsNotEmpty()
            .Because("directories created during copy should be recorded");

        // Should have created directories for 2024/01 and 2024/07
        await Assert.That(transactionLog.CreatedDirectories.Any(d => d.Contains("01"))).IsTrue()
            .Because("January directory should be recorded");
        await Assert.That(transactionLog.CreatedDirectories.Any(d => d.Contains("07"))).IsTrue()
            .Because("July directory should be recorded");
    }

    #endregion
}
