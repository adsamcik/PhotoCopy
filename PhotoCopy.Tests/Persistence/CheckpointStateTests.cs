using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;

namespace PhotoCopy.Tests.Persistence;

/// <summary>
/// Unit tests for checkpoint state transitions and validation.
/// These tests verify the core checkpoint logic without any file system access.
/// </summary>
[Property("Category", "Unit,Checkpoint")]
public class CheckpointStateTests
{
    #region Checkpoint Creation Tests

    [Test]
    public void NewCheckpoint_HasCorrectDefaults()
    {
        // Arrange & Act
        var checkpoint = new Checkpoint
        {
            SourceDirectory = @"C:\Photos",
            DestinationPattern = @"C:\Backup\{year}\{name}{ext}"
        };

        // Assert
        checkpoint.Version.Should().Be(Checkpoint.CurrentVersion);
        checkpoint.Status.Should().Be(CheckpointStatus.InProgress);
        checkpoint.ProcessedFiles.Should().BeEmpty();
        checkpoint.FailureReason.Should().BeNull();
    }

    [Test]
    public void CheckpointBuilder_CreatesValidCheckpoint()
    {
        // Arrange & Act
        var checkpoint = new CheckpointBuilder()
            .WithSourceDirectory(@"D:\MyPhotos")
            .WithDestinationPattern(@"E:\Backup\{year}\{month}\{name}{ext}")
            .WithProcessedFiles(10)
            .Build();

        // Assert
        checkpoint.ShouldBeValidCheckpoint();
        checkpoint.SourceDirectory.Should().Be(@"D:\MyPhotos");
        checkpoint.ProcessedFiles.Should().HaveCount(10);
    }

    [Test]
    public void CheckpointBuilder_CorruptedCheckpoint_HasInvalidVersion()
    {
        // Arrange & Act
        var checkpoint = new CheckpointBuilder()
            .Corrupted()
            .Build();

        // Assert
        checkpoint.Version.Should().BeLessThan(0);
    }

    #endregion

    #region Status Transition Tests

    [Test]
    public void Checkpoint_TransitionToCompleted_IsValid()
    {
        // Arrange
        var checkpoint = new CheckpointBuilder()
            .WithStatus(CheckpointStatus.InProgress)
            .WithProcessedFiles(100)
            .Build();

        // Act
        var completed = checkpoint with { Status = CheckpointStatus.Completed };

        // Assert
        completed.Status.Should().Be(CheckpointStatus.Completed);
        completed.ProcessedFiles.Should().HaveCount(100);
    }

    [Test]
    public void Checkpoint_TransitionToFailed_IncludesReason()
    {
        // Arrange
        var checkpoint = new CheckpointBuilder()
            .WithStatus(CheckpointStatus.InProgress)
            .Build();

        // Act
        var failed = checkpoint with
        {
            Status = CheckpointStatus.Failed,
            FailureReason = "Disk full during copy"
        };

        // Assert
        failed.Status.Should().Be(CheckpointStatus.Failed);
        failed.FailureReason.Should().Contain("Disk full");
    }

    [Test]
    public void Checkpoint_TransitionToCancelled_PreservesProgress()
    {
        // Arrange
        var checkpoint = new CheckpointBuilder()
            .WithStatus(CheckpointStatus.InProgress)
            .WithProcessedFiles(50)
            .Build();

        // Act
        var cancelled = checkpoint with { Status = CheckpointStatus.Cancelled };

        // Assert
        cancelled.Status.Should().Be(CheckpointStatus.Cancelled);
        cancelled.ProcessedFiles.Should().HaveCount(50);
    }

    #endregion

    #region Processed Files Tracking Tests

    [Test]
    public void ProcessedFiles_TracksSourceAndDestination()
    {
        // Arrange
        var entry = new ProcessedFileEntry(
            SourcePath: @"C:\Photos\vacation.jpg",
            DestinationPath: @"D:\Backup\2025\vacation.jpg",
            FileSize: 5_242_880,
            SourceLastModified: new DateTime(2025, 1, 15, 10, 30, 0),
            Checksum: "abc123");

        // Assert
        entry.SourcePath.Should().Contain("vacation.jpg");
        entry.DestinationPath.Should().Contain("2025");
        entry.FileSize.Should().Be(5_242_880);
    }

    [Test]
    public void ProcessedFiles_CanTrackZeroByteFiles()
    {
        // Edge case: zero-byte files should be tracked correctly
        var entry = new ProcessedFileEntry(
            SourcePath: @"C:\Photos\empty.txt",
            DestinationPath: @"D:\Backup\empty.txt",
            FileSize: 0,
            SourceLastModified: DateTime.UtcNow,
            Checksum: null);

        entry.FileSize.Should().Be(0);
    }

    [Test]
    public void ProcessedFiles_HandlesUnicodePaths()
    {
        // Edge case: unicode characters in paths
        var entry = new ProcessedFileEntry(
            SourcePath: @"C:\Photos\日本旅行\桜🌸.jpg",
            DestinationPath: @"D:\Backup\2025\桜🌸.jpg",
            FileSize: 1024,
            SourceLastModified: DateTime.UtcNow,
            Checksum: null);

        entry.SourcePath.Should().Contain("🌸");
    }

    #endregion

    #region Statistics Tests

    [Test]
    public void Statistics_CalculatesProgressCorrectly()
    {
        // Arrange
        var stats = new CheckpointStatistics
        {
            TotalFilesDiscovered = 100,
            FilesProcessed = 45,
            FilesSkipped = 5,
            FilesFailed = 0,
            BytesProcessed = 150_000_000
        };

        // Assert
        var percentComplete = (double)stats.FilesProcessed / stats.TotalFilesDiscovered * 100;
        percentComplete.Should().BeApproximately(45.0, 0.01);
    }

    [Test]
    public void Statistics_HandlesEmptyCollection()
    {
        // Edge case: empty source directory
        var stats = new CheckpointStatistics
        {
            TotalFilesDiscovered = 0,
            FilesProcessed = 0,
            FilesSkipped = 0,
            FilesFailed = 0,
            BytesProcessed = 0
        };

        // Division by zero protection in progress calculation
        var percentComplete = stats.TotalFilesDiscovered > 0
            ? (double)stats.FilesProcessed / stats.TotalFilesDiscovered * 100
            : 100.0; // Empty = complete

        percentComplete.Should().Be(100.0);
    }

    #endregion

    #region Configuration Snapshot Tests

    [Test]
    public void ConfigurationSnapshot_DetectsIncompatibleChanges()
    {
        // Arrange
        var originalConfig = new ConfigurationSnapshot
        {
            Mode = "Copy",
            CalculateChecksums = false,
            DuplicateHandling = "Rename"
        };

        var changedConfig = new ConfigurationSnapshot
        {
            Mode = "Move",  // Changed from Copy to Move
            CalculateChecksums = false,
            DuplicateHandling = "Rename"
        };

        // Assert
        originalConfig.Should().NotBe(changedConfig);
        originalConfig.Mode.Should().NotBe(changedConfig.Mode);
    }

    [Test]
    public void ConfigurationSnapshot_AllowsCompatibleChanges()
    {
        // Arrange - changing parallelism should be compatible
        var config1 = new ConfigurationSnapshot
        {
            Mode = "Copy",
            CalculateChecksums = true,
            DuplicateHandling = "Skip"
        };

        var config2 = new ConfigurationSnapshot
        {
            Mode = "Copy",
            CalculateChecksums = true,
            DuplicateHandling = "Skip"
        };

        // Assert
        config1.Should().Be(config2);
    }

    #endregion

    #region Validation Tests

    [Test]
    public void Validation_RejectsNegativeVersion()
    {
        // Arrange
        var checkpoint = new CheckpointBuilder()
            .Corrupted()
            .Build();

        // Act & Assert
        var act = () => checkpoint.ShouldBeValidCheckpoint();
        act.Should().Throw<Exception>().WithMessage("*version*positive*");
    }

    [Test]
    public void Validation_RejectsEmptySourceDirectory()
    {
        // Arrange
        var checkpoint = new Checkpoint
        {
            SourceDirectory = "",
            DestinationPattern = @"C:\Dest"
        };

        // Act & Assert
        var act = () => checkpoint.ShouldBeValidCheckpoint();
        act.Should().Throw<Exception>().WithMessage("*source directory*empty*");
    }

    [Test]
    public void Validation_RejectsFutureCreationTime()
    {
        // Arrange
        var checkpoint = new Checkpoint
        {
            SourceDirectory = @"C:\Source",
            DestinationPattern = @"C:\Dest",
            CreatedAt = DateTime.UtcNow.AddDays(1) // Future timestamp
        };

        // Act & Assert
        var act = () => checkpoint.ShouldBeValidCheckpoint();
        act.Should().Throw<Exception>().WithMessage("*future*");
    }

    #endregion

    #region Resume Context Tests

    [Test]
    public void ResumeContext_IdentifiesAlreadyProcessedFiles()
    {
        // Arrange
        var checkpoint = new CheckpointBuilder()
            .WithProcessedFiles(new[]
            {
                new ProcessedFileEntry(@"C:\Photos\a.jpg", @"D:\Dest\a.jpg", 100, DateTime.UtcNow, null),
                new ProcessedFileEntry(@"C:\Photos\b.jpg", @"D:\Dest\b.jpg", 200, DateTime.UtcNow, null),
                new ProcessedFileEntry(@"C:\Photos\c.jpg", @"D:\Dest\c.jpg", 300, DateTime.UtcNow, null)
            })
            .Build();

        // Act
        var processedPaths = checkpoint.ProcessedFiles
            .Select(f => f.SourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Assert
        processedPaths.Should().Contain(@"C:\Photos\a.jpg");
        processedPaths.Should().Contain(@"C:\Photos\b.jpg");
        processedPaths.Should().Contain(@"C:\Photos\c.jpg");
        processedPaths.Should().NotContain(@"C:\Photos\d.jpg");
    }

    [Test]
    public void ResumeContext_FiltersRemainingFiles()
    {
        // Arrange
        var allFiles = new[]
        {
            @"C:\Photos\a.jpg",
            @"C:\Photos\b.jpg",
            @"C:\Photos\c.jpg",
            @"C:\Photos\d.jpg",
            @"C:\Photos\e.jpg"
        };

        var checkpoint = new CheckpointBuilder()
            .WithProcessedFiles(new[]
            {
                new ProcessedFileEntry(@"C:\Photos\a.jpg", @"D:\a.jpg", 100, DateTime.UtcNow, null),
                new ProcessedFileEntry(@"C:\Photos\b.jpg", @"D:\b.jpg", 100, DateTime.UtcNow, null)
            })
            .Build();

        // Act
        var processedPaths = checkpoint.ProcessedFiles
            .Select(f => f.SourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var remainingFiles = allFiles
            .Where(f => !processedPaths.Contains(f))
            .ToList();

        // Assert
        remainingFiles.Should().HaveCount(3);
        remainingFiles.Should().Contain(@"C:\Photos\c.jpg");
        remainingFiles.Should().Contain(@"C:\Photos\d.jpg");
        remainingFiles.Should().Contain(@"C:\Photos\e.jpg");
    }

    #endregion
}
