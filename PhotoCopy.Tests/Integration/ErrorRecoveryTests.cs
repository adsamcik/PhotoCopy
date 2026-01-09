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
using NSubstitute.ExceptionExtensions;
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
/// Integration tests for error recovery during batch copy operations.
/// These tests verify that:
/// 1. When a file fails to copy, remaining files are still processed
/// 2. Errors are properly reported and counted
/// 3. Failed files are not included in transaction logs
/// 4. The copier handles various failure scenarios gracefully
/// </summary>
[NotInParallel("FileOperations")]
[Property("Category", "Integration,ErrorRecovery")]
public class ErrorRecoveryTests
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
        _testBaseDirectory = Path.Combine(Path.GetTempPath(), "ErrorRecoveryTests", Guid.NewGuid().ToString());
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
                Thread.Sleep(50);
                Directory.Delete(path, true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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
    private IServiceProvider BuildRealServiceProvider(PhotoCopyConfig config, IFileSystem? fileSystem = null)
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

        // Use provided mock or real file system
        if (fileSystem != null)
        {
            services.AddSingleton(fileSystem);
        }
        else
        {
            services.AddTransient<IFileSystem, FileSystem>();
        }

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
        bool enableRollback = false,
        DuplicateHandling duplicateHandling = DuplicateHandling.None)
    {
        return new PhotoCopyConfig
        {
            Source = _sourceDir,
            Destination = destinationTemplate,
            DryRun = dryRun,
            Mode = mode,
            EnableRollback = enableRollback,
            CalculateChecksums = false,
            DuplicatesFormat = "_{number}",
            LogLevel = OutputLevel.Verbose,
            Parallelism = 1, // Single-threaded for predictable test behavior
            AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".heic", ".mov", ".mp4"
            }
        };
    }

    /// <summary>
    /// Creates a mock IFileSystem that fails for specific files.
    /// </summary>
    private IFileSystem CreateFailingFileSystem(
        IDirectoryScanner scanner,
        HashSet<string> failingFiles,
        string errorMessage = "Simulated IO error")
    {
        var mock = Substitute.For<IFileSystem>();

        mock.EnumerateFiles(Arg.Any<string>())
            .Returns(c => scanner.EnumerateFiles(c.ArgAt<string>(0)));

        mock.DirectoryExists(Arg.Any<string>())
            .Returns(c => Directory.Exists(c.ArgAt<string>(0)));

        mock.FileExists(Arg.Any<string>())
            .Returns(c => File.Exists(c.ArgAt<string>(0)));

        mock.When(x => x.CreateDirectory(Arg.Any<string>()))
            .Do(c => Directory.CreateDirectory(c.ArgAt<string>(0)));

        mock.When(x => x.CopyFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()))
            .Do(c =>
            {
                var source = c.ArgAt<string>(0);
                var dest = c.ArgAt<string>(1);
                var overwrite = c.ArgAt<bool>(2);

                if (failingFiles.Contains(Path.GetFileName(source)))
                {
                    throw new IOException(errorMessage);
                }

                File.Copy(source, dest, overwrite);
            });

        mock.When(x => x.MoveFile(Arg.Any<string>(), Arg.Any<string>()))
            .Do(c =>
            {
                var source = c.ArgAt<string>(0);
                var dest = c.ArgAt<string>(1);

                if (failingFiles.Contains(Path.GetFileName(source)))
                {
                    throw new IOException(errorMessage);
                }

                File.Move(source, dest);
            });

        mock.GetFileInfo(Arg.Any<string>())
            .Returns(c => new FileInfo(c.ArgAt<string>(0)));

        mock.GetDirectoryInfo(Arg.Any<string>())
            .Returns(c => new DirectoryInfo(c.ArgAt<string>(0)));

        return mock;
    }

    /// <summary>
    /// Creates a mock IFileSystem with custom CopyFile behavior.
    /// </summary>
    private IFileSystem CreateMockFileSystem(
        IDirectoryScanner scanner,
        Action<string, string, bool> copyAction)
    {
        var mock = Substitute.For<IFileSystem>();

        mock.EnumerateFiles(Arg.Any<string>())
            .Returns(c => scanner.EnumerateFiles(c.ArgAt<string>(0)));

        mock.DirectoryExists(Arg.Any<string>())
            .Returns(c => Directory.Exists(c.ArgAt<string>(0)));

        mock.FileExists(Arg.Any<string>())
            .Returns(c => File.Exists(c.ArgAt<string>(0)));

        mock.When(x => x.CreateDirectory(Arg.Any<string>()))
            .Do(c => Directory.CreateDirectory(c.ArgAt<string>(0)));

        mock.When(x => x.CopyFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()))
            .Do(c => copyAction(c.ArgAt<string>(0), c.ArgAt<string>(1), c.ArgAt<bool>(2)));

        mock.GetFileInfo(Arg.Any<string>())
            .Returns(c => new FileInfo(c.ArgAt<string>(0)));

        mock.GetDirectoryInfo(Arg.Any<string>())
            .Returns(c => new DirectoryInfo(c.ArgAt<string>(0)));

        return mock;
    }

    /// <summary>
    /// Creates a DirectoryScanner with proper configuration.
    /// </summary>
    private IDirectoryScanner CreateDirectoryScanner(PhotoCopyConfig config)
    {
        var options = Microsoft.Extensions.Options.Options.Create(config);
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var metadataExtractor = new FileMetadataExtractor(logger, options);
        var checksumCalculator = new Sha256ChecksumCalculator();

        var mockGeocodingService = Substitute.For<IReverseGeocodingService>();
        mockGeocodingService.ReverseGeocode(Arg.Any<double>(), Arg.Any<double>())
            .Returns((LocationData?)null);

        var enricher = new MetadataEnricher(new IMetadataEnrichmentStep[]
        {
            new DateTimeMetadataEnrichmentStep(metadataExtractor),
            new LocationMetadataEnrichmentStep(metadataExtractor, mockGeocodingService),
            new ChecksumMetadataEnrichmentStep(checksumCalculator, options)
        });

        var fileFactory = new FileFactory(enricher, Substitute.For<ILogger<FileWithMetadata>>(), options);
        return new DirectoryScanner(Substitute.For<ILogger<DirectoryScanner>>(), options, fileFactory);
    }

    #endregion

    #region Error Recovery Tests

    [Test]
    public async Task FirstFileFails_RemainingFilesStillCopied()
    {
        // Arrange: Create 3 files, first will fail
        await CreateTestJpegAsync("aaa_first.jpg", new DateTime(2024, 1, 15));
        await CreateTestJpegAsync("bbb_second.jpg", new DateTime(2024, 2, 20));
        await CreateTestJpegAsync("ccc_third.jpg", new DateTime(2024, 3, 25));

        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var scanner = CreateDirectoryScanner(config);
        var failingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "aaa_first.jpg" };
        var mockFileSystem = CreateFailingFileSystem(scanner, failingFiles);

        var copierAsync = new DirectoryCopierAsync(
            Substitute.For<ILogger<DirectoryCopierAsync>>(),
            mockFileSystem,
            Microsoft.Extensions.Options.Options.Create(config),
            Substitute.For<ITransactionLogger>(), new FileValidationService());

        // Act
        var result = await copierAsync.CopyAsync(
            Array.Empty<IValidator>(),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(result.FilesFailed).IsEqualTo(1);
        await Assert.That(result.FilesProcessed).IsEqualTo(2);
        await Assert.That(result.Errors).HasCount().EqualTo(1);
        await Assert.That(result.Errors[0].ErrorMessage).Contains("Simulated IO error");

        // Verify that the other files were copied
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "02", "bbb_second.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "03", "ccc_third.jpg"))).IsTrue();
    }

    [Test]
    public async Task MiddleFileFails_OtherFilesStillCopied()
    {
        // Arrange: Create 3 files, middle one will fail
        await CreateTestJpegAsync("aaa_first.jpg", new DateTime(2024, 1, 15));
        await CreateTestJpegAsync("bbb_middle.jpg", new DateTime(2024, 2, 20));
        await CreateTestJpegAsync("ccc_last.jpg", new DateTime(2024, 3, 25));

        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var scanner = CreateDirectoryScanner(config);
        var failingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bbb_middle.jpg" };
        var mockFileSystem = CreateFailingFileSystem(scanner, failingFiles);

        var copierAsync = new DirectoryCopierAsync(
            Substitute.For<ILogger<DirectoryCopierAsync>>(),
            mockFileSystem,
            Microsoft.Extensions.Options.Options.Create(config),
            Substitute.For<ITransactionLogger>(), new FileValidationService());

        // Act
        var result = await copierAsync.CopyAsync(
            Array.Empty<IValidator>(),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(result.FilesFailed).IsEqualTo(1);
        await Assert.That(result.FilesProcessed).IsEqualTo(2);

        // First and last files should be copied
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "01", "aaa_first.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "03", "ccc_last.jpg"))).IsTrue();
    }

    [Test]
    public async Task LastFileFails_PreviousFilesAlreadyCopied()
    {
        // Arrange: Create 3 files, last one will fail
        await CreateTestJpegAsync("aaa_first.jpg", new DateTime(2024, 1, 15));
        await CreateTestJpegAsync("bbb_second.jpg", new DateTime(2024, 2, 20));
        await CreateTestJpegAsync("zzz_last.jpg", new DateTime(2024, 3, 25));

        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var scanner = CreateDirectoryScanner(config);
        var failingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "zzz_last.jpg" };
        var mockFileSystem = CreateFailingFileSystem(scanner, failingFiles);

        var copierAsync = new DirectoryCopierAsync(
            Substitute.For<ILogger<DirectoryCopierAsync>>(),
            mockFileSystem,
            Microsoft.Extensions.Options.Options.Create(config),
            Substitute.For<ITransactionLogger>(), new FileValidationService());

        // Act
        var result = await copierAsync.CopyAsync(
            Array.Empty<IValidator>(),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(result.FilesFailed).IsEqualTo(1);
        await Assert.That(result.FilesProcessed).IsEqualTo(2);

        // Previous files should have been copied
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "01", "aaa_first.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "02", "bbb_second.jpg"))).IsTrue();
    }

    [Test]
    public async Task SourceFileDeleted_DuringCopy_HandlesGracefully()
    {
        // Arrange: Create files, but one will be deleted before copy
        await CreateTestJpegAsync("keep.jpg", new DateTime(2024, 1, 15));
        var deletedFilePath = await CreateTestJpegAsync("deleted.jpg", new DateTime(2024, 2, 20));
        await CreateTestJpegAsync("also_keep.jpg", new DateTime(2024, 3, 25));

        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var scanner = CreateDirectoryScanner(config);

        // Create a mock that simulates file deletion by throwing FileNotFoundException
        var mock = CreateMockFileSystem(scanner, (source, dest, overwrite) =>
        {
            if (Path.GetFileName(source) == "deleted.jpg")
            {
                throw new FileNotFoundException("Source file no longer exists", source);
            }
            File.Copy(source, dest, overwrite);
        });

        var copierAsync = new DirectoryCopierAsync(
            Substitute.For<ILogger<DirectoryCopierAsync>>(),
            mock,
            Microsoft.Extensions.Options.Options.Create(config),
            Substitute.For<ITransactionLogger>(), new FileValidationService());

        // Act
        var result = await copierAsync.CopyAsync(
            Array.Empty<IValidator>(),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(result.FilesFailed).IsEqualTo(1);
        await Assert.That(result.FilesProcessed).IsEqualTo(2);
        await Assert.That(result.Errors[0].ErrorMessage).Contains("no longer exists");

        // Other files should still be copied
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "01", "keep.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "03", "also_keep.jpg"))).IsTrue();
    }

    [Test]
    public async Task DestinationReadOnly_ReportsError()
    {
        // Arrange
        await CreateTestJpegAsync("photo.jpg", new DateTime(2024, 5, 10));
        await CreateTestJpegAsync("another.jpg", new DateTime(2024, 6, 15));

        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var scanner = CreateDirectoryScanner(config);

        var mock = CreateMockFileSystem(scanner, (source, dest, overwrite) =>
        {
            if (Path.GetFileName(source) == "photo.jpg")
            {
                throw new UnauthorizedAccessException("Access to the path is denied. Destination is read-only.");
            }
            File.Copy(source, dest, overwrite);
        });

        var copierAsync = new DirectoryCopierAsync(
            Substitute.For<ILogger<DirectoryCopierAsync>>(),
            mock,
            Microsoft.Extensions.Options.Options.Create(config),
            Substitute.For<ITransactionLogger>(), new FileValidationService());

        // Act
        var result = await copierAsync.CopyAsync(
            Array.Empty<IValidator>(),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(result.FilesFailed).IsEqualTo(1);
        await Assert.That(result.FilesProcessed).IsEqualTo(1);
        await Assert.That(result.Errors[0].ErrorMessage).Contains("Access to the path is denied");

        // The other file should still be copied
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "06", "another.jpg"))).IsTrue();
    }

    [Test]
    public async Task FileLocked_ByAnotherProcess_ReportsError()
    {
        // Arrange
        await CreateTestJpegAsync("locked.jpg", new DateTime(2024, 4, 10));
        await CreateTestJpegAsync("unlocked.jpg", new DateTime(2024, 5, 15));

        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var scanner = CreateDirectoryScanner(config);

        var mock = CreateMockFileSystem(scanner, (source, dest, overwrite) =>
        {
            if (Path.GetFileName(source) == "locked.jpg")
            {
                throw new IOException("The process cannot access the file because it is being used by another process.");
            }
            File.Copy(source, dest, overwrite);
        });

        var copierAsync = new DirectoryCopierAsync(
            Substitute.For<ILogger<DirectoryCopierAsync>>(),
            mock,
            Microsoft.Extensions.Options.Options.Create(config),
            Substitute.For<ITransactionLogger>(), new FileValidationService());

        // Act
        var result = await copierAsync.CopyAsync(
            Array.Empty<IValidator>(),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(result.FilesFailed).IsEqualTo(1);
        await Assert.That(result.FilesProcessed).IsEqualTo(1);
        await Assert.That(result.Errors[0].ErrorMessage).Contains("being used by another process");

        // The unlocked file should still be copied
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "05", "unlocked.jpg"))).IsTrue();
    }

    [Test]
    public async Task AllFilesFail_ReportsAllErrors()
    {
        // Arrange: Create 3 files, all will fail
        await CreateTestJpegAsync("file1.jpg", new DateTime(2024, 1, 15));
        await CreateTestJpegAsync("file2.jpg", new DateTime(2024, 2, 20));
        await CreateTestJpegAsync("file3.jpg", new DateTime(2024, 3, 25));

        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var scanner = CreateDirectoryScanner(config);
        var failingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "file1.jpg", "file2.jpg", "file3.jpg"
        };
        var mockFileSystem = CreateFailingFileSystem(scanner, failingFiles, "General failure");

        var copierAsync = new DirectoryCopierAsync(
            Substitute.For<ILogger<DirectoryCopierAsync>>(),
            mockFileSystem,
            Microsoft.Extensions.Options.Options.Create(config),
            Substitute.For<ITransactionLogger>(), new FileValidationService());

        // Act
        var result = await copierAsync.CopyAsync(
            Array.Empty<IValidator>(),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(result.FilesFailed).IsEqualTo(3);
        await Assert.That(result.FilesProcessed).IsEqualTo(0);
        await Assert.That(result.Errors).HasCount().EqualTo(3);

        // Verify all errors contain the error message
        foreach (var error in result.Errors)
        {
            await Assert.That(error.ErrorMessage).Contains("General failure");
        }

        // No files should exist in destination
        var destFiles = Directory.GetFiles(_destDir, "*.*", SearchOption.AllDirectories);
        await Assert.That(destFiles.Length).IsEqualTo(0);
    }

    [Test]
    public async Task PartialFailure_ReportsSuccessAndFailureCounts()
    {
        // Arrange: Create 5 files, 2 will fail
        await CreateTestJpegAsync("success1.jpg", new DateTime(2024, 1, 10));
        await CreateTestJpegAsync("fail1.jpg", new DateTime(2024, 2, 15));
        await CreateTestJpegAsync("success2.jpg", new DateTime(2024, 3, 20));
        await CreateTestJpegAsync("fail2.jpg", new DateTime(2024, 4, 25));
        await CreateTestJpegAsync("success3.jpg", new DateTime(2024, 5, 30));

        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var scanner = CreateDirectoryScanner(config);
        var failingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fail1.jpg", "fail2.jpg" };
        var mockFileSystem = CreateFailingFileSystem(scanner, failingFiles);

        var copierAsync = new DirectoryCopierAsync(
            Substitute.For<ILogger<DirectoryCopierAsync>>(),
            mockFileSystem,
            Microsoft.Extensions.Options.Options.Create(config),
            Substitute.For<ITransactionLogger>(), new FileValidationService());

        // Act
        var result = await copierAsync.CopyAsync(
            Array.Empty<IValidator>(),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(result.FilesProcessed).IsEqualTo(3);
        await Assert.That(result.FilesFailed).IsEqualTo(2);
        await Assert.That(result.Errors).HasCount().EqualTo(2);

        // Verify successful files exist
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "01", "success1.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "03", "success2.jpg"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "05", "success3.jpg"))).IsTrue();
    }

    [Test]
    public async Task FailedFile_NotIncludedInTransactionLog()
    {
        // Arrange: Create 3 files, middle one will fail
        await CreateTestJpegAsync("success1.jpg", new DateTime(2024, 1, 15));
        await CreateTestJpegAsync("failure.jpg", new DateTime(2024, 2, 20));
        await CreateTestJpegAsync("success2.jpg", new DateTime(2024, 3, 25));

        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            enableRollback: true);

        var scanner = CreateDirectoryScanner(config);
        var failingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "failure.jpg" };
        var mockFileSystem = CreateFailingFileSystem(scanner, failingFiles);

        var transactionLogger = new TransactionLogger(
            Substitute.For<ILogger<TransactionLogger>>(),
            Microsoft.Extensions.Options.Options.Create(config));

        // Begin transaction
        transactionLogger.BeginTransaction(config.Source, config.Destination, config.DryRun);

        // Build copy plan using a real copier
        var copierAsync = new DirectoryCopierAsync(
            Substitute.For<ILogger<DirectoryCopierAsync>>(),
            mockFileSystem,
            Microsoft.Extensions.Options.Options.Create(config),
            Substitute.For<ITransactionLogger>(), new FileValidationService());

        var plan = await copierAsync.BuildPlanAsync(Array.Empty<IValidator>(), CancellationToken.None);

        // Execute with manual transaction logging (simulating what the real workflow does)
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

                mockFileSystem.CopyFile(operation.File.File.FullName, operation.DestinationPath, true);

                // Only log successful operations
                transactionLogger.LogOperation(
                    operation.File.File.FullName,
                    operation.DestinationPath,
                    OperationType.Copy,
                    operation.File.File.Length);

                processedCount++;
            }
            catch (Exception ex)
            {
                errors.Add(new CopyError(operation.File, operation.DestinationPath, ex.Message));
                // Deliberately NOT logging failed operations
            }
        }

        transactionLogger.CompleteTransaction();
        await transactionLogger.SaveAsync(CancellationToken.None);

        // Assert: Read the transaction log
        var logPath = transactionLogger.TransactionLogPath;
        await Assert.That(logPath).IsNotNull();
        await Assert.That(File.Exists(logPath!)).IsTrue();

        var logJson = await File.ReadAllTextAsync(logPath!);
        var log = JsonSerializer.Deserialize<TransactionLog>(logJson, _jsonOptions);

        await Assert.That(log).IsNotNull();
        await Assert.That(log!.Operations).HasCount().EqualTo(2);

        // Verify failed file is NOT in the log
        var loggedFiles = log.Operations.Select(o => Path.GetFileName(o.SourcePath)).ToList();
        await Assert.That(loggedFiles).Contains("success1.jpg");
        await Assert.That(loggedFiles).Contains("success2.jpg");
        await Assert.That(loggedFiles).DoesNotContain("failure.jpg");
    }

    [Test]
    public async Task IOError_InDryRun_StillReportsWhatWouldHappen()
    {
        // Arrange: Create test files
        await CreateTestJpegAsync("file1.jpg", new DateTime(2024, 1, 15));
        await CreateTestJpegAsync("file2.jpg", new DateTime(2024, 2, 20));
        await CreateTestJpegAsync("file3.jpg", new DateTime(2024, 3, 25));

        // Use DryRun mode - no actual IO should occur
        var config = CreateConfig(
            Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            dryRun: true);

        // Use real service provider since DryRun doesn't actually copy
        var serviceProvider = BuildRealServiceProvider(config);
        var copierAsync = serviceProvider.GetRequiredService<IDirectoryCopierAsync>();

        // Act
        var result = await copierAsync.CopyAsync(
            Array.Empty<IValidator>(),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert: In dry run, no files fail since no IO is performed
        await Assert.That(result.FilesFailed).IsEqualTo(0);
        await Assert.That(result.FilesProcessed).IsEqualTo(3);
        await Assert.That(result.Errors).IsEmpty();

        // Verify no files were actually copied (it's a dry run)
        var destFiles = Directory.GetFiles(_destDir, "*.*", SearchOption.AllDirectories);
        await Assert.That(destFiles.Length).IsEqualTo(0);
    }

    #endregion

    #region Progress Reporter Error Handling Tests

    [Test]
    public async Task ErrorsAreReportedToProgressReporter()
    {
        // Arrange
        await CreateTestJpegAsync("good.jpg", new DateTime(2024, 1, 15));
        await CreateTestJpegAsync("bad.jpg", new DateTime(2024, 2, 20));

        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var scanner = CreateDirectoryScanner(config);
        var failingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bad.jpg" };
        var mockFileSystem = CreateFailingFileSystem(scanner, failingFiles, "Test error message");

        var mockProgressReporter = Substitute.For<IProgressReporter>();
        var reportedErrors = new List<(string fileName, Exception ex)>();
        mockProgressReporter
            .When(x => x.ReportError(Arg.Any<string>(), Arg.Any<Exception>()))
            .Do(c => reportedErrors.Add((c.Arg<string>(), c.Arg<Exception>())));

        var copierAsync = new DirectoryCopierAsync(
            Substitute.For<ILogger<DirectoryCopierAsync>>(),
            mockFileSystem,
            Microsoft.Extensions.Options.Options.Create(config),
            Substitute.For<ITransactionLogger>(), new FileValidationService());

        // Act
        await copierAsync.CopyAsync(
            Array.Empty<IValidator>(),
            mockProgressReporter,
            CancellationToken.None);

        // Assert
        await Assert.That(reportedErrors).HasCount().EqualTo(1);
        await Assert.That(reportedErrors[0].fileName).IsEqualTo("bad.jpg");
        await Assert.That(reportedErrors[0].ex.Message).Contains("Test error message");
    }

    [Test]
    public async Task MultipleErrorTypes_AllReportedCorrectly()
    {
        // Arrange: Create files that will fail with different error types
        await CreateTestJpegAsync("io_error.jpg", new DateTime(2024, 1, 15));
        await CreateTestJpegAsync("access_denied.jpg", new DateTime(2024, 2, 20));
        await CreateTestJpegAsync("not_found.jpg", new DateTime(2024, 3, 25));
        await CreateTestJpegAsync("success.jpg", new DateTime(2024, 4, 30));

        var config = CreateConfig(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
        var scanner = CreateDirectoryScanner(config);

        var mock = CreateMockFileSystem(scanner, (source, dest, overwrite) =>
        {
            var filename = Path.GetFileName(source);
            switch (filename)
            {
                case "io_error.jpg":
                    throw new IOException("Disk read error");
                case "access_denied.jpg":
                    throw new UnauthorizedAccessException("Access denied");
                case "not_found.jpg":
                    throw new FileNotFoundException("File not found", source);
                default:
                    File.Copy(source, dest, overwrite);
                    break;
            }
        });

        var copierAsync = new DirectoryCopierAsync(
            Substitute.For<ILogger<DirectoryCopierAsync>>(),
            mock,
            Microsoft.Extensions.Options.Options.Create(config),
            Substitute.For<ITransactionLogger>(), new FileValidationService());

        // Act
        var result = await copierAsync.CopyAsync(
            Array.Empty<IValidator>(),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(result.FilesFailed).IsEqualTo(3);
        await Assert.That(result.FilesProcessed).IsEqualTo(1);
        await Assert.That(result.Errors).HasCount().EqualTo(3);

        // Verify different error types are captured
        var errorMessages = result.Errors.Select(e => e.ErrorMessage).ToList();
        await Assert.That(errorMessages.Any(e => e.Contains("Disk read error"))).IsTrue();
        await Assert.That(errorMessages.Any(e => e.Contains("Access denied"))).IsTrue();
        await Assert.That(errorMessages.Any(e => e.Contains("File not found"))).IsTrue();

        // Success file should be copied
        await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "04", "success.jpg"))).IsTrue();
    }

    #endregion
}
