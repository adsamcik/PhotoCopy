using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NSubstitute;
using PhotoCopy.Checkpoint;
using PhotoCopy.Checkpoint.Models;
using PhotoCopy.Configuration;
using PhotoCopy.Files;
using PhotoCopy.Tests.Checkpoint.Fakes;

namespace PhotoCopy.Tests.Checkpoint;

public class CheckpointValidatorTests
{
    private FakeClock _clock = null!;
    private CheckpointValidator _validator = null!;

    [Before(Test)]
    public void Setup()
    {
        _clock = new FakeClock(new DateTime(2026, 1, 20, 12, 0, 0, DateTimeKind.Utc));
        _validator = new CheckpointValidator(_clock);
    }

    #region ComputeConfigHash Tests

    [Test]
    public async Task ComputeConfigHash_ReturnsConsistentHash_ForSameConfig()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act
        var hash1 = _validator.ComputeConfigHash(config);
        var hash2 = _validator.ComputeConfigHash(config);

        // Assert
        await Assert.That(hash1).IsEquivalentTo(hash2);
    }

    [Test]
    public async Task ComputeConfigHash_ReturnsDifferentHash_WhenDestinationChanges()
    {
        // Arrange
        var config1 = CreateDefaultConfig();
        config1.Destination = @"C:\Photos\{year}\{month}";

        var config2 = CreateDefaultConfig();
        config2.Destination = @"D:\Backup\{year}\{month}\{day}";

        // Act
        var hash1 = _validator.ComputeConfigHash(config1);
        var hash2 = _validator.ComputeConfigHash(config2);

        // Assert
        await Assert.That(hash1).IsNotEquivalentTo(hash2);
    }

    [Test]
    public async Task ComputeConfigHash_ReturnsDifferentHash_WhenModeChanges()
    {
        // Arrange
        var config1 = CreateDefaultConfig();
        config1.Mode = OperationMode.Copy;

        var config2 = CreateDefaultConfig();
        config2.Mode = OperationMode.Move;

        // Act
        var hash1 = _validator.ComputeConfigHash(config1);
        var hash2 = _validator.ComputeConfigHash(config2);

        // Assert
        await Assert.That(hash1).IsNotEquivalentTo(hash2);
    }

    [Test]
    public async Task ComputeConfigHash_ReturnsDifferentHash_WhenDuplicatesFormatChanges()
    {
        // Arrange
        var config1 = CreateDefaultConfig();
        config1.DuplicatesFormat = "-{number}";

        var config2 = CreateDefaultConfig();
        config2.DuplicatesFormat = "_{number}";

        // Act
        var hash1 = _validator.ComputeConfigHash(config1);
        var hash2 = _validator.ComputeConfigHash(config2);

        // Assert
        await Assert.That(hash1).IsNotEquivalentTo(hash2);
    }

    [Test]
    public async Task ComputeConfigHash_ReturnsDifferentHash_WhenPathCasingChanges()
    {
        // Arrange
        var config1 = CreateDefaultConfig();
        config1.PathCasing = PathCasing.Original;

        var config2 = CreateDefaultConfig();
        config2.PathCasing = PathCasing.Lowercase;

        // Act
        var hash1 = _validator.ComputeConfigHash(config1);
        var hash2 = _validator.ComputeConfigHash(config2);

        // Assert
        await Assert.That(hash1).IsNotEquivalentTo(hash2);
    }

    [Test]
    public async Task ComputeConfigHash_ReturnsDifferentHash_WhenUseFullCountryNamesChanges()
    {
        // Arrange
        var config1 = CreateDefaultConfig();
        config1.UseFullCountryNames = false;

        var config2 = CreateDefaultConfig();
        config2.UseFullCountryNames = true;

        // Act
        var hash1 = _validator.ComputeConfigHash(config1);
        var hash2 = _validator.ComputeConfigHash(config2);

        // Assert
        await Assert.That(hash1).IsNotEquivalentTo(hash2);
    }

    #endregion

    #region ComputePlanHash Tests

    [Test]
    public async Task ComputePlanHash_ReturnsConsistentHash_ForSameFileList()
    {
        // Arrange
        var files = CreateMockFileList(new[]
        {
            (@"C:\Photos\photo1.jpg", 1024L),
            (@"C:\Photos\photo2.jpg", 2048L),
            (@"C:\Photos\photo3.jpg", 512L)
        });

        // Act
        var hash1 = _validator.ComputePlanHash(files);
        var hash2 = _validator.ComputePlanHash(files);

        // Assert
        await Assert.That(hash1).IsEquivalentTo(hash2);
    }

    [Test]
    public async Task ComputePlanHash_ReturnsDifferentHash_WhenFileListDiffers()
    {
        // Arrange
        var files1 = CreateMockFileList(new[]
        {
            (@"C:\Photos\photo1.jpg", 1024L),
            (@"C:\Photos\photo2.jpg", 2048L)
        });

        var files2 = CreateMockFileList(new[]
        {
            (@"C:\Photos\photo1.jpg", 1024L),
            (@"C:\Photos\photo3.jpg", 2048L)
        });

        // Act
        var hash1 = _validator.ComputePlanHash(files1);
        var hash2 = _validator.ComputePlanHash(files2);

        // Assert
        await Assert.That(hash1).IsNotEquivalentTo(hash2);
    }

    [Test]
    public async Task ComputePlanHash_ReturnsDifferentHash_WhenFileSizeDiffers()
    {
        // Arrange
        var files1 = CreateMockFileList(new[]
        {
            (@"C:\Photos\photo1.jpg", 1024L)
        });

        var files2 = CreateMockFileList(new[]
        {
            (@"C:\Photos\photo1.jpg", 2048L)
        });

        // Act
        var hash1 = _validator.ComputePlanHash(files1);
        var hash2 = _validator.ComputePlanHash(files2);

        // Assert
        await Assert.That(hash1).IsNotEquivalentTo(hash2);
    }

    #endregion

    #region ValidateAsync Tests

    [Test]
    public async Task ValidateAsync_ReturnsValid_ForMatchingCheckpoint()
    {
        // Arrange
        var config = CreateDefaultConfig();
        var configHash = _validator.ComputeConfigHash(config);
        var checkpoint = CreateCheckpointState(config, configHash, completedCount: 50, totalFiles: 100);

        // Act
        var result = await _validator.ValidateAsync(checkpoint, config);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.TotalOperations).IsEqualTo(100);
        await Assert.That(result.CompletedOperations).IsEqualTo(50);
        await Assert.That(result.PendingOperations).IsEqualTo(50);
    }

    [Test]
    public async Task ValidateAsync_ReturnsInvalid_WhenSourceDirectoryDiffers()
    {
        // Arrange
        var config = CreateDefaultConfig();
        config.Source = @"C:\DifferentSource";

        var originalConfig = CreateDefaultConfig();
        originalConfig.Source = @"C:\OriginalSource";
        var configHash = _validator.ComputeConfigHash(originalConfig);

        var checkpoint = CreateCheckpointState(originalConfig, configHash);

        // Act
        var result = await _validator.ValidateAsync(checkpoint, config);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.InvalidReason).IsNotNull();
        await Assert.That(result.InvalidReason!.Contains("Source directory mismatch")).IsTrue();
    }

    [Test]
    public async Task ValidateAsync_ReturnsInvalid_WhenDestinationPatternDiffers()
    {
        // Arrange
        var config = CreateDefaultConfig();
        config.Destination = @"D:\NewDestination\{year}";

        var originalConfig = CreateDefaultConfig();
        var configHash = _validator.ComputeConfigHash(originalConfig);

        var checkpoint = CreateCheckpointState(originalConfig, configHash);

        // Act
        var result = await _validator.ValidateAsync(checkpoint, config);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.InvalidReason).IsNotNull();
        await Assert.That(result.InvalidReason!.Contains("Destination pattern mismatch")).IsTrue();
    }

    [Test]
    public async Task ValidateAsync_ReturnsInvalid_WhenConfigHashDiffers()
    {
        // Arrange
        var config = CreateDefaultConfig();
        config.Mode = OperationMode.Move; // Different from checkpoint

        var originalConfig = CreateDefaultConfig();
        originalConfig.Mode = OperationMode.Copy;
        var originalHash = _validator.ComputeConfigHash(originalConfig);

        // Create checkpoint with matching source/dest but different config hash
        var checkpoint = new CheckpointState
        {
            SessionId = "test-session",
            Version = 1,
            StartedUtc = _clock.UtcNow.AddDays(-1),
            SourceDirectory = config.Source, // Same source
            DestinationPattern = config.Destination, // Same destination
            ConfigHash = originalHash, // Original config hash (Copy mode)
            PlanHash = new byte[32],
            TotalFiles = 100,
            TotalBytes = 1000000,
            Completed = new BitArray(100)
        };

        // Act
        var result = await _validator.ValidateAsync(checkpoint, config);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.InvalidReason).IsNotNull();
        await Assert.That(result.InvalidReason!.Contains("Configuration has changed")).IsTrue();
    }

    [Test]
    public async Task ValidateAsync_AddsWarning_ForOldCheckpoints()
    {
        // Arrange
        var config = CreateDefaultConfig();
        var configHash = _validator.ComputeConfigHash(config);

        // Create checkpoint that is 35 days old
        var oldStartTime = _clock.UtcNow.AddDays(-35);
        var checkpoint = new CheckpointState
        {
            SessionId = "test-session",
            Version = 1,
            StartedUtc = oldStartTime,
            SourceDirectory = config.Source,
            DestinationPattern = config.Destination,
            ConfigHash = configHash,
            PlanHash = new byte[32],
            TotalFiles = 100,
            TotalBytes = 1000000,
            Completed = new BitArray(100)
        };

        // Act
        var result = await _validator.ValidateAsync(checkpoint, config);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Warnings.Count).IsGreaterThan(0);
        await Assert.That(result.Warnings[0].Contains("35 days old")).IsTrue();
    }

    [Test]
    public async Task ValidateAsync_NoWarning_ForRecentCheckpoints()
    {
        // Arrange
        var config = CreateDefaultConfig();
        var configHash = _validator.ComputeConfigHash(config);

        // Create checkpoint that is only 5 days old
        var recentStartTime = _clock.UtcNow.AddDays(-5);
        var checkpoint = new CheckpointState
        {
            SessionId = "test-session",
            Version = 1,
            StartedUtc = recentStartTime,
            SourceDirectory = config.Source,
            DestinationPattern = config.Destination,
            ConfigHash = configHash,
            PlanHash = new byte[32],
            TotalFiles = 100,
            TotalBytes = 1000000,
            Completed = new BitArray(100)
        };

        // Act
        var result = await _validator.ValidateAsync(checkpoint, config);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Warnings.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ValidateAsync_ReturnsInvalid_WhenAllFilesCompleted()
    {
        // Arrange
        var config = CreateDefaultConfig();
        var configHash = _validator.ComputeConfigHash(config);

        var completed = new BitArray(100);
        for (var i = 0; i < 100; i++)
        {
            completed[i] = true;
        }

        var checkpoint = new CheckpointState
        {
            SessionId = "test-session",
            Version = 1,
            StartedUtc = _clock.UtcNow.AddDays(-1),
            SourceDirectory = config.Source,
            DestinationPattern = config.Destination,
            ConfigHash = configHash,
            PlanHash = new byte[32],
            TotalFiles = 100,
            TotalBytes = 1000000,
            Completed = completed
        };

        // Act
        var result = await _validator.ValidateAsync(checkpoint, config);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.InvalidReason!.Contains("already completed")).IsTrue();
    }

    #endregion

    #region Path Normalization Tests

    [Test]
    public async Task ValidateAsync_HandlesPathNormalization_ForwardSlashes()
    {
        // Arrange
        var config = CreateDefaultConfig();
        config.Source = @"C:/Photos/Source";

        var configHash = _validator.ComputeConfigHash(config);

        var checkpoint = new CheckpointState
        {
            SessionId = "test-session",
            Version = 1,
            StartedUtc = _clock.UtcNow.AddDays(-1),
            SourceDirectory = @"C:\Photos\Source", // Backslashes
            DestinationPattern = config.Destination,
            ConfigHash = configHash,
            PlanHash = new byte[32],
            TotalFiles = 100,
            TotalBytes = 1000000,
            Completed = new BitArray(100)
        };

        // Act
        var result = await _validator.ValidateAsync(checkpoint, config);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task ValidateAsync_HandlesPathNormalization_TrailingSlashes()
    {
        // Arrange
        var config = CreateDefaultConfig();
        config.Source = @"C:\Photos\Source\";

        var configHash = _validator.ComputeConfigHash(config);

        var checkpoint = new CheckpointState
        {
            SessionId = "test-session",
            Version = 1,
            StartedUtc = _clock.UtcNow.AddDays(-1),
            SourceDirectory = @"C:\Photos\Source", // No trailing slash
            DestinationPattern = config.Destination,
            ConfigHash = configHash,
            PlanHash = new byte[32],
            TotalFiles = 100,
            TotalBytes = 1000000,
            Completed = new BitArray(100)
        };

        // Act
        var result = await _validator.ValidateAsync(checkpoint, config);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    #endregion

    #region Helper Methods

    private static PhotoCopyConfig CreateDefaultConfig()
    {
        return new PhotoCopyConfig
        {
            Source = @"C:\Photos\Source",
            Destination = @"C:\Photos\Organized\{year}\{month}",
            Mode = OperationMode.Copy,
            DuplicatesFormat = "-{number}",
            PathCasing = PathCasing.Original,
            UseFullCountryNames = false,
            LocationGranularity = LocationGranularity.City,
            UnknownLocationFallback = "Unknown"
        };
    }

    private CheckpointState CreateCheckpointState(
        PhotoCopyConfig config,
        byte[] configHash,
        int completedCount = 0,
        int totalFiles = 100)
    {
        var completed = new BitArray(totalFiles);
        for (var i = 0; i < completedCount && i < totalFiles; i++)
        {
            completed[i] = true;
        }

        return new CheckpointState
        {
            SessionId = "test-session",
            Version = 1,
            StartedUtc = _clock.UtcNow.AddDays(-1),
            SourceDirectory = config.Source,
            DestinationPattern = config.Destination,
            ConfigHash = configHash,
            PlanHash = new byte[32],
            TotalFiles = totalFiles,
            TotalBytes = totalFiles * 10000L,
            Completed = completed
        };
    }

    private static IReadOnlyList<IFile> CreateMockFileList(
        IEnumerable<(string FullName, long Length)> fileSpecs)
    {
        var files = new List<IFile>();

        foreach (var (fullName, length) in fileSpecs)
        {
            var mockFile = Substitute.For<IFile>();
            var fileInfo = Substitute.For<FileInfo>(fullName);
            
            // Mock FileInfo properties
            mockFile.File.Returns(fileInfo);
            mockFile.File.FullName.Returns(fullName);
            mockFile.File.Length.Returns(length);

            files.Add(mockFile);
        }

        return files;
    }

    #endregion
}
