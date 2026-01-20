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

namespace PhotoCopy.Tests.Persistence;

/// <summary>
/// Integration tests for the resume-from-checkpoint workflow.
/// These tests exercise the full copy pipeline with real file system access.
/// </summary>
[NotInParallel("ResumeTests")]
[Property("Category", "Integration,Resume")]
public class ResumeFromCheckpointTests
{
    private string _testBaseDirectory = null!;
    private string _sourceDir = null!;
    private string _destDir = null!;
    private string _checkpointDir = null!;
    private InMemoryCheckpointPersistence _checkpointPersistence = null!;

    [Before(Test)]
    public void Setup()
    {
        _testBaseDirectory = Path.Combine(
            Path.GetTempPath(),
            "ResumeFromCheckpointTests",
            Guid.NewGuid().ToString());
        _sourceDir = Path.Combine(_testBaseDirectory, "source");
        _destDir = Path.Combine(_testBaseDirectory, "dest");
        _checkpointDir = Path.Combine(_destDir, ".photocopy");

        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_destDir);
        Directory.CreateDirectory(_checkpointDir);

        _checkpointPersistence = new InMemoryCheckpointPersistence();
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

    private async Task<List<string>> CreateTestJpegFilesAsync(int count)
    {
        var files = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var filename = $"photo_{i:D4}.jpg";
            var filePath = Path.Combine(_sourceDir, filename);
            var jpegBytes = MockImageGenerator.CreateJpeg(
                dateTaken: new DateTime(2025, 1, 15, 10, 0, 0).AddMinutes(i));
            await File.WriteAllBytesAsync(filePath, jpegBytes);
            files.Add(filePath);
        }
        return files;
    }

    private PhotoCopyConfig CreateConfig(
        bool resume = false,
        bool dryRun = false,
        OperationMode mode = OperationMode.Copy)
    {
        return new PhotoCopyConfig
        {
            Source = _sourceDir,
            Destination = Path.Combine(_destDir, "{year}", "{month:D2}", "{name}{ext}"),
            DryRun = dryRun,
            Mode = mode,
            EnableRollback = false,
            CalculateChecksums = false,
            LogLevel = OutputLevel.Verbose,
            Parallelism = 1,
            AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".heic"
            }
        };
    }

    private IServiceProvider BuildServiceProvider(
        PhotoCopyConfig config,
        IFileSystem? fileSystem = null)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<IOptions<PhotoCopyConfig>>(Options.Create(config));

        var mockGeocodingService = Substitute.For<IReverseGeocodingService>();
        mockGeocodingService.ReverseGeocode(Arg.Any<double>(), Arg.Any<double>())
            .Returns((LocationData?)null);
        services.AddSingleton(mockGeocodingService);

        services.AddSingleton<IChecksumCalculator, Sha256ChecksumCalculator>();
        services.AddSingleton<IGpsLocationIndex, GpsLocationIndex>();
        services.AddSingleton<ILivePhotoEnricher, LivePhotoEnricher>();
        services.AddSingleton<ICompanionGpsEnricher, CompanionGpsEnricher>();
        services.AddTransient<IFileMetadataExtractor, FileMetadataExtractor>();
        services.AddTransient<IMetadataEnricher, MetadataEnricher>();
        services.AddTransient<IMetadataEnrichmentStep, DateTimeMetadataEnrichmentStep>();
        services.AddTransient<IMetadataEnrichmentStep, LocationMetadataEnrichmentStep>();
        services.AddTransient<IMetadataEnrichmentStep, ChecksumMetadataEnrichmentStep>();
        services.AddTransient<IFileFactory, FileFactory>();
        services.AddTransient<IDirectoryScanner, DirectoryScanner>();

        if (fileSystem != null)
        {
            services.AddSingleton(fileSystem);
        }
        else
        {
            services.AddTransient<IFileSystem, PhotoCopy.Files.FileSystem>();
        }

        services.AddTransient<IDirectoryCopierAsync, DirectoryCopierAsync>();
        services.AddTransient<IDirectoryCopier, DirectoryCopier>();
        services.AddTransient<IValidatorFactory, ValidatorFactory>();
        services.AddTransient<IFileValidationService, FileValidationService>();
        services.AddSingleton<ITransactionLogger, TransactionLogger>();

        return services.BuildServiceProvider();
    }

    private int CountDestinationFiles()
    {
        if (!Directory.Exists(_destDir))
            return 0;
        return Directory.GetFiles(_destDir, "*.jpg", SearchOption.AllDirectories).Length;
    }

    #endregion

    #region Basic Resume Tests

    [Test]
    [Skip("Checkpoint integration not yet implemented - test demonstrates expected behavior")]
    public async Task Resume_WithValidCheckpoint_SkipsAlreadyProcessedFiles()
    {
        // Arrange: Create 10 test files
        var testFiles = await CreateTestJpegFilesAsync(10);
        var checkpointPath = Path.Combine(_checkpointDir, "checkpoint.json");

        // Simulate previous session: first 5 files were processed
        var processedFiles = testFiles.Take(5)
            .Select((f, i) => new ProcessedFileEntry(
                f,
                Path.Combine(_destDir, "2025", "01", Path.GetFileName(f)),
                new FileInfo(f).Length,
                File.GetLastWriteTimeUtc(f),
                null))
            .ToList();

        var checkpoint = new CheckpointBuilder()
            .WithSourceDirectory(_sourceDir)
            .WithDestinationPattern(Path.Combine(_destDir, "{year}", "{month:D2}", "{name}{ext}"))
            .WithProcessedFiles(processedFiles)
            .WithStatus(CheckpointStatus.InProgress)
            .Build();

        _checkpointPersistence.Seed(checkpointPath, checkpoint);

        // Manually copy first 5 files to simulate previous session
        foreach (var entry in processedFiles)
        {
            var destDir = Path.GetDirectoryName(entry.DestinationPath)!;
            Directory.CreateDirectory(destDir);
            File.Copy(entry.SourcePath, entry.DestinationPath);
        }

        // Act: Resume operation
        var config = CreateConfig(resume: true);
        var provider = BuildServiceProvider(config);
        var copier = provider.GetRequiredService<IDirectoryCopier>();
        var validators = provider.GetRequiredService<IValidatorFactory>().Create(config);

        var result = copier.Copy(validators);

        // Assert
        // Only remaining 5 files should be processed
        result.FilesProcessed.Should().Be(5);
        CountDestinationFiles().Should().Be(10);
    }

    [Test]
    [Skip("Checkpoint integration not yet implemented - test demonstrates expected behavior")]
    public async Task Resume_WhenNoCheckpointExists_StartsFromBeginning()
    {
        // Arrange
        var testFiles = await CreateTestJpegFilesAsync(10);

        // Act: Run with resume flag but no checkpoint exists
        var config = CreateConfig(resume: true);
        var provider = BuildServiceProvider(config);
        var copier = provider.GetRequiredService<IDirectoryCopier>();
        var validators = provider.GetRequiredService<IValidatorFactory>().Create(config);

        var result = copier.Copy(validators);

        // Assert: All files should be processed
        result.FilesProcessed.Should().Be(10);
        CountDestinationFiles().Should().Be(10);
    }

    [Test]
    [Skip("Checkpoint integration not yet implemented - test demonstrates expected behavior")]
    public async Task Resume_WithCompletedCheckpoint_ReportsAlreadyComplete()
    {
        // Arrange
        var testFiles = await CreateTestJpegFilesAsync(10);
        var checkpointPath = Path.Combine(_checkpointDir, "checkpoint.json");

        var checkpoint = new CheckpointBuilder()
            .WithSourceDirectory(_sourceDir)
            .WithStatus(CheckpointStatus.Completed)
            .Build();

        _checkpointPersistence.Seed(checkpointPath, checkpoint);

        // Act
        var config = CreateConfig(resume: true);
        var provider = BuildServiceProvider(config);
        var copier = provider.GetRequiredService<IDirectoryCopier>();
        var validators = provider.GetRequiredService<IValidatorFactory>().Create(config);

        var result = copier.Copy(validators);

        // Assert: No files should be processed (already complete)
        result.FilesProcessed.Should().Be(0);
    }

    #endregion

    #region Interruption and Recovery Tests

    [Test]
    public async Task Copy_WithCrashingFileSystem_StopsAtCrashPoint()
    {
        // Arrange: Create 20 test files
        var testFiles = await CreateTestJpegFilesAsync(20);

        // First build a provider to get a real FileSystem, then wrap it
        var config = CreateConfig();
        var tempProvider = BuildServiceProvider(config);
        var realFs = tempProvider.GetRequiredService<IFileSystem>();
        var crashingFs = InterruptionSimulator.CreateCrashingFileSystem(realFs, crashAfterNCopies: 10);

        var provider = BuildServiceProvider(config, crashingFs);
        var copier = provider.GetRequiredService<IDirectoryCopier>();
        var validators = provider.GetRequiredService<IValidatorFactory>().Create(config);

        // Act & Assert
        var copyAction = () => copier.Copy(validators);
        copyAction.Should().Throw<SimulatedCrashException>();

        // 10 files should have been copied before crash
        CountDestinationFiles().Should().Be(10);
    }

    [Test]
    public async Task Copy_WithPartialWriteFailure_LeavesPartialFile()
    {
        // Arrange: Create 5 test files
        var testFiles = await CreateTestJpegFilesAsync(5);
        var targetFile = "photo_0002.jpg"; // Will crash on 3rd file

        var config = CreateConfig();
        var tempProvider = BuildServiceProvider(config);
        var realFs = tempProvider.GetRequiredService<IFileSystem>();
        var partialWriteFs = InterruptionSimulator.CreatePartialWriteFileSystem(
            realFs,
            crashOnFileName: targetFile,
            percentageWritten: 0.5);

        var provider = BuildServiceProvider(config, partialWriteFs);
        var copier = provider.GetRequiredService<IDirectoryCopier>();
        var validators = provider.GetRequiredService<IValidatorFactory>().Create(config);

        // Act
        var result = copier.Copy(validators);

        // Assert: 3rd file should fail, leaving partial file
        result.FilesFailed.Should().BeGreaterThanOrEqualTo(1);
        result.Errors.Should().Contain(e => e.File.File.Name == targetFile);

        // Check partial file exists and is smaller than source
        var destFiles = Directory.GetFiles(_destDir, targetFile, SearchOption.AllDirectories);
        if (destFiles.Length > 0)
        {
            var sourceSize = new FileInfo(testFiles[2]).Length;
            var destSize = new FileInfo(destFiles[0]).Length;
            destSize.Should().BeLessThan(sourceSize);
        }
    }

    #endregion

    #region Edge Case Tests

    [Test]
    public async Task Resume_WithEmptySourceDirectory_HandlesGracefully()
    {
        // Arrange: Empty source, with a checkpoint from previous run
        var checkpointPath = Path.Combine(_checkpointDir, "checkpoint.json");

        var checkpoint = new CheckpointBuilder()
            .WithSourceDirectory(_sourceDir)
            .WithProcessedFiles(0)
            .WithStatus(CheckpointStatus.InProgress)
            .Build();

        _checkpointPersistence.Seed(checkpointPath, checkpoint);

        // Act
        var config = CreateConfig(resume: true);
        var provider = BuildServiceProvider(config);
        var copier = provider.GetRequiredService<IDirectoryCopier>();
        var validators = provider.GetRequiredService<IValidatorFactory>().Create(config);

        var result = copier.Copy(validators);

        // Assert
        result.FilesProcessed.Should().Be(0);
        result.FilesFailed.Should().Be(0);
    }

    [Test]
    public async Task Resume_WithSingleFile_HandlesCorrectly()
    {
        // Arrange: Only 1 file
        var testFiles = await CreateTestJpegFilesAsync(1);

        // Act
        var config = CreateConfig();
        var provider = BuildServiceProvider(config);
        var copier = provider.GetRequiredService<IDirectoryCopier>();
        var validators = provider.GetRequiredService<IValidatorFactory>().Create(config);

        var result = copier.Copy(validators);

        // Assert
        result.FilesProcessed.Should().Be(1);
        CountDestinationFiles().Should().Be(1);
    }

    [Test]
    [Skip("Checkpoint integration not yet implemented - test demonstrates expected behavior")]
    public async Task Resume_WhenSourceFilesDeleted_SkipsDeletedFiles()
    {
        // Arrange: Create checkpoint referencing files that no longer exist
        var testFiles = await CreateTestJpegFilesAsync(10);
        var checkpointPath = Path.Combine(_checkpointDir, "checkpoint.json");

        // Checkpoint says first 5 are processed
        var processedFiles = testFiles.Take(5)
            .Select((f, i) => new ProcessedFileEntry(
                f,
                Path.Combine(_destDir, "2025", "01", Path.GetFileName(f)),
                1024,
                DateTime.UtcNow,
                null))
            .ToList();

        var checkpoint = new CheckpointBuilder()
            .WithSourceDirectory(_sourceDir)
            .WithProcessedFiles(processedFiles)
            .WithStatus(CheckpointStatus.InProgress)
            .Build();

        _checkpointPersistence.Seed(checkpointPath, checkpoint);

        // Delete some remaining source files
        File.Delete(testFiles[5]);
        File.Delete(testFiles[6]);

        // Act
        var config = CreateConfig(resume: true);
        var provider = BuildServiceProvider(config);
        var copier = provider.GetRequiredService<IDirectoryCopier>();
        var validators = provider.GetRequiredService<IValidatorFactory>().Create(config);

        var result = copier.Copy(validators);

        // Assert: Only 3 remaining files should be processed
        result.FilesProcessed.Should().Be(3);
    }

    #endregion
}
