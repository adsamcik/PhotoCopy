using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PhotoCopy.Checkpoint;
using PhotoCopy.Checkpoint.Models;
using PhotoCopy.Tests.Checkpoint.Fakes;
using TUnit.Core;

namespace PhotoCopy.Tests.Checkpoint;

/// <summary>
/// Unit tests for BinaryCheckpointStore class.
/// </summary>
[NotInParallel]
public class BinaryCheckpointStoreTests : IAsyncDisposable
{
    private string _testDirectory = null!;
    private FakeClock _clock = null!;

    [Before(Test)]
    public void Setup()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "BinaryCheckpointStoreTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDirectory);
        // Use a unique timestamp based on current time to avoid session ID collisions
        _clock = new FakeClock(DateTime.UtcNow);
    }

    [After(Test)]
    public async Task Cleanup()
    {
        await Task.Delay(50); // Allow file handles to be released

        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Cleanup();
    }

    #region GetCheckpointDirectory Tests

    [Test]
    public async Task GetCheckpointDirectory_UsesCustomDirectory_WhenProvided()
    {
        // Arrange
        var customDir = Path.Combine(_testDirectory, "custom-checkpoints");
        var store = new BinaryCheckpointStore(_clock, customDir);

        // Act
        var result = store.GetCheckpointDirectory(@"D:\Photos\{Year}\{Month}");

        // Assert
        await Assert.That(result).IsEqualTo(customDir);
    }

    [Test]
    public async Task GetCheckpointDirectory_ExtractsRootFromPattern_WhenNoCustomDirectory()
    {
        // Arrange
        var store = new BinaryCheckpointStore(_clock);

        // Act
        var result = store.GetCheckpointDirectory(@"D:\Photos\{Year}\{Month}");

        // Assert
        await Assert.That(result).IsEqualTo(@"D:\Photos\.photocopy");
    }

    [Test]
    public async Task GetCheckpointDirectory_HandlesPatternWithNoVariables()
    {
        // Arrange
        var store = new BinaryCheckpointStore(_clock);

        // Act
        var result = store.GetCheckpointDirectory(@"D:\Photos\Organized");

        // Assert  
        await Assert.That(result).Contains(".photocopy");
    }

    #endregion

    #region CreateWriterAsync Tests

    [Test]
    public async Task CreateWriterAsync_CreatesCheckpointFile()
    {
        // Arrange
        var checkpointDir = Path.Combine(_testDirectory, ".photocopy");
        var store = new BinaryCheckpointStore(_clock, checkpointDir);
        var state = CheckpointState.CreateNew(
            sourceDirectory: @"C:\Source",
            destinationPattern: @"D:\Dest\{Year}",
            totalFiles: 100,
            totalBytes: 1024000,
            configHash: new byte[16],
            planHash: new byte[16],
            startedUtc: _clock.UtcNow);

        // Act
        await using var writer = await store.CreateWriterAsync(state);

        // Assert
        await Assert.That(state.FilePath).IsNotNull();
        await Assert.That(File.Exists(state.FilePath!)).IsTrue();
    }

    [Test]
    public async Task CreateWriterAsync_WriterCanRecordCompletions()
    {
        // Arrange
        var checkpointDir = Path.Combine(_testDirectory, ".photocopy");
        var store = new BinaryCheckpointStore(_clock, checkpointDir);
        var state = CheckpointState.CreateNew(
            sourceDirectory: @"C:\Source",
            destinationPattern: @"D:\Dest\{Year}",
            totalFiles: 10,
            totalBytes: 10240,
            configHash: new byte[16],
            planHash: new byte[16],
            startedUtc: _clock.UtcNow);

        // Act
        await using var writer = await store.CreateWriterAsync(state);
        writer.RecordCompletion(0, OperationResult.Completed, 1024);
        writer.RecordCompletion(1, OperationResult.Skipped, 512);
        await Task.Delay(100); // Allow async processing

        // Assert
        var stats = writer.GetStatistics();
        await Assert.That(stats.FilesCompleted).IsEqualTo(1);
        await Assert.That(stats.FilesSkipped).IsEqualTo(1);
    }

    #endregion

    #region LoadAsync Tests

    [Test]
    public async Task LoadAsync_ReturnsNull_WhenFileDoesNotExist()
    {
        // Arrange
        var store = new BinaryCheckpointStore(_clock, _testDirectory);
        var nonExistentPath = Path.Combine(_testDirectory, "nonexistent.checkpoint");

        // Act
        var result = await store.LoadAsync(nonExistentPath);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task LoadAsync_LoadsCheckpointCreatedByWriter()
    {
        // Arrange
        var checkpointDir = Path.Combine(_testDirectory, ".photocopy");
        var store = new BinaryCheckpointStore(_clock, checkpointDir);
        var originalState = CheckpointState.CreateNew(
            sourceDirectory: @"C:\TestSource",
            destinationPattern: @"D:\TestDest\{Year}",
            totalFiles: 50,
            totalBytes: 512000,
            configHash: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 },
            planHash: new byte[] { 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 },
            startedUtc: _clock.UtcNow);

        await using (var writer = await store.CreateWriterAsync(originalState))
        {
            writer.RecordCompletion(0, OperationResult.Completed, 1024);
            writer.RecordCompletion(1, OperationResult.Completed, 2048);
            writer.RecordCompletion(5, OperationResult.Skipped, 512);
            await Task.Delay(100); // Allow async processing
        }

        // Act
        var loadedState = await store.LoadAsync(originalState.FilePath!);

        // Assert
        await Assert.That(loadedState).IsNotNull();
        await Assert.That(loadedState!.SourceDirectory).IsEqualTo(@"C:\TestSource");
        await Assert.That(loadedState.DestinationPattern).IsEqualTo(@"D:\TestDest\{Year}");
        await Assert.That(loadedState.TotalFiles).IsEqualTo(50);
        await Assert.That(loadedState.TotalBytes).IsEqualTo(512000);
        await Assert.That(loadedState.Statistics.FilesCompleted).IsEqualTo(2);
        await Assert.That(loadedState.Statistics.FilesSkipped).IsEqualTo(1);
    }

    [Test]
    public async Task LoadAsync_RestoresCompletedBitArray()
    {
        // Arrange
        var checkpointDir = Path.Combine(_testDirectory, ".photocopy");
        var store = new BinaryCheckpointStore(_clock, checkpointDir);
        var state = CheckpointState.CreateNew(
            sourceDirectory: @"C:\Source",
            destinationPattern: @"D:\Dest",
            totalFiles: 20,
            totalBytes: 20480,
            configHash: new byte[16],
            planHash: new byte[16],
            startedUtc: _clock.UtcNow);

        var completedIndices = new[] { 0, 3, 7, 15, 19 };

        await using (var writer = await store.CreateWriterAsync(state))
        {
            foreach (var index in completedIndices)
            {
                writer.RecordCompletion(index, OperationResult.Completed, 1024);
            }
            await Task.Delay(100);
        }

        // Act
        var loadedState = await store.LoadAsync(state.FilePath!);

        // Assert
        await Assert.That(loadedState).IsNotNull();
        foreach (var index in completedIndices)
        {
            await Assert.That(loadedState!.Completed[index]).IsTrue();
        }
        // Check that other indices are not marked as completed
        await Assert.That(loadedState!.Completed[1]).IsFalse();
        await Assert.That(loadedState.Completed[10]).IsFalse();
    }

    #endregion

    #region FindLatestAsync Tests

    [Test]
    public async Task FindLatestAsync_ReturnsNull_WhenNoCheckpointsExist()
    {
        // Arrange
        var checkpointDir = Path.Combine(_testDirectory, ".photocopy");
        var store = new BinaryCheckpointStore(_clock, checkpointDir);

        // Act
        var result = await store.FindLatestAsync(@"C:\Source", @"D:\Dest\{Year}");

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task FindLatestAsync_ReturnsMatchingCheckpoint()
    {
        // Arrange
        var checkpointDir = Path.Combine(_testDirectory, ".photocopy");
        var store = new BinaryCheckpointStore(_clock, checkpointDir);
        var state = CheckpointState.CreateNew(
            sourceDirectory: @"C:\MySource",
            destinationPattern: @"D:\MyDest\{Year}",
            totalFiles: 10,
            totalBytes: 10240,
            configHash: new byte[16],
            planHash: new byte[16],
            startedUtc: _clock.UtcNow);

        await using (var writer = await store.CreateWriterAsync(state))
        {
            writer.RecordCompletion(0, OperationResult.Completed, 1024);
            await Task.Delay(50);
        }

        // Act
        var found = await store.FindLatestAsync(@"C:\MySource", @"D:\MyDest\{Year}");

        // Assert
        await Assert.That(found).IsNotNull();
        await Assert.That(found!.SessionId).IsEqualTo(state.SessionId);
    }

    [Test]
    public async Task FindLatestAsync_ReturnsNull_WhenSourceDoesNotMatch()
    {
        // Arrange
        var checkpointDir = Path.Combine(_testDirectory, ".photocopy");
        var store = new BinaryCheckpointStore(_clock, checkpointDir);
        var state = CheckpointState.CreateNew(
            sourceDirectory: @"C:\Source",
            destinationPattern: @"D:\Dest",
            totalFiles: 10,
            totalBytes: 10240,
            configHash: new byte[16],
            planHash: new byte[16],
            startedUtc: _clock.UtcNow);

        await using (var writer = await store.CreateWriterAsync(state))
        {
            await Task.Delay(50);
        }

        // Act - search with different source
        var found = await store.FindLatestAsync(@"C:\DifferentSource", @"D:\Dest");

        // Assert
        await Assert.That(found).IsNull();
    }

    [Test]
    public async Task FindLatestAsync_ReturnsLatest_WhenMultipleCheckpointsExist()
    {
        // Arrange
        var checkpointDir = Path.Combine(_testDirectory, ".photocopy");
        var store = new BinaryCheckpointStore(_clock, checkpointDir);

        // Create first checkpoint
        var state1 = CheckpointState.CreateNew(
            sourceDirectory: @"C:\Source",
            destinationPattern: @"D:\Dest",
            totalFiles: 10,
            totalBytes: 10240,
            configHash: new byte[16],
            planHash: new byte[16],
            startedUtc: _clock.UtcNow);

        await using (var writer = await store.CreateWriterAsync(state1))
        {
            await Task.Delay(50);
        }

        // Advance time and create second checkpoint
        _clock.Advance(TimeSpan.FromHours(1));

        var state2 = CheckpointState.CreateNew(
            sourceDirectory: @"C:\Source",
            destinationPattern: @"D:\Dest",
            totalFiles: 20,
            totalBytes: 20480,
            configHash: new byte[16],
            planHash: new byte[16],
            startedUtc: _clock.UtcNow);

        await using (var writer = await store.CreateWriterAsync(state2))
        {
            await Task.Delay(50);
        }

        // Act
        var found = await store.FindLatestAsync(@"C:\Source", @"D:\Dest");

        // Assert
        await Assert.That(found).IsNotNull();
        await Assert.That(found!.SessionId).IsEqualTo(state2.SessionId);
        await Assert.That(found.TotalFiles).IsEqualTo(20);
    }

    #endregion

    #region ResumeWriterAsync Tests

    [Test]
    public async Task ResumeWriterAsync_ThrowsArgumentException_WhenFilePathIsNull()
    {
        // Arrange
        var store = new BinaryCheckpointStore(_clock, _testDirectory);
        var state = CreateCheckpointStateWithFilePath(null);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.ResumeWriterAsync(state));
    }

    [Test]
    public async Task ResumeWriterAsync_ThrowsFileNotFoundException_WhenFileDoesNotExist()
    {
        // Arrange
        var store = new BinaryCheckpointStore(_clock, _testDirectory);
        var state = CreateCheckpointStateWithFilePath(Path.Combine(_testDirectory, "nonexistent.checkpoint"));

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await store.ResumeWriterAsync(state));
    }

    [Test]
    public async Task ResumeWriterAsync_ContinuesFromExistingState()
    {
        // Arrange
        var checkpointDir = Path.Combine(_testDirectory, ".photocopy");
        var store = new BinaryCheckpointStore(_clock, checkpointDir);
        var state = CheckpointState.CreateNew(
            sourceDirectory: @"C:\Source",
            destinationPattern: @"D:\Dest",
            totalFiles: 10,
            totalBytes: 10240,
            configHash: new byte[16],
            planHash: new byte[16],
            startedUtc: _clock.UtcNow);

        // Create initial checkpoint with some completions
        await using (var writer = await store.CreateWriterAsync(state))
        {
            writer.RecordCompletion(0, OperationResult.Completed, 1024);
            writer.RecordCompletion(1, OperationResult.Completed, 1024);
            await Task.Delay(100);
        }

        // Load the state for resume
        var loadedState = await store.LoadAsync(state.FilePath!);
        await Assert.That(loadedState).IsNotNull();

        // Act - Resume and add more completions
        await using (var resumedWriter = await store.ResumeWriterAsync(loadedState!))
        {
            resumedWriter.RecordCompletion(2, OperationResult.Completed, 1024);
            resumedWriter.RecordCompletion(3, OperationResult.Skipped, 512);
            await Task.Delay(100);

            // Assert
            var stats = resumedWriter.GetStatistics();
            await Assert.That(stats.FilesCompleted).IsEqualTo(3); // 2 original + 1 new
            await Assert.That(stats.FilesSkipped).IsEqualTo(1);
            await Assert.That(resumedWriter.IsCompleted(0)).IsTrue();
            await Assert.That(resumedWriter.IsCompleted(1)).IsTrue();
            await Assert.That(resumedWriter.IsCompleted(2)).IsTrue();
        }
    }

    #endregion

    #region DeleteAsync Tests

    [Test]
    public async Task DeleteAsync_DeletesExistingFile()
    {
        // Arrange
        var checkpointDir = Path.Combine(_testDirectory, ".photocopy");
        var store = new BinaryCheckpointStore(_clock, checkpointDir);
        var state = CheckpointState.CreateNew(
            sourceDirectory: @"C:\Source",
            destinationPattern: @"D:\Dest",
            totalFiles: 10,
            totalBytes: 10240,
            configHash: new byte[16],
            planHash: new byte[16],
            startedUtc: _clock.UtcNow);

        await using (var writer = await store.CreateWriterAsync(state))
        {
            await Task.Delay(50);
        }

        await Assert.That(File.Exists(state.FilePath!)).IsTrue();

        // Act
        await store.DeleteAsync(state.FilePath!);

        // Assert
        await Assert.That(File.Exists(state.FilePath!)).IsFalse();
    }

    [Test]
    public async Task DeleteAsync_DoesNotThrow_WhenFileDoesNotExist()
    {
        // Arrange
        var store = new BinaryCheckpointStore(_clock, _testDirectory);
        var nonExistentPath = Path.Combine(_testDirectory, "nonexistent.checkpoint");

        // Act & Assert - should not throw
        await store.DeleteAsync(nonExistentPath);
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task LoadAsync_ReturnsNull_ForCorruptedFile()
    {
        // Arrange
        var store = new BinaryCheckpointStore(_clock, _testDirectory);
        var corruptedPath = Path.Combine(_testDirectory, "corrupted.checkpoint");
        await File.WriteAllBytesAsync(corruptedPath, new byte[] { 1, 2, 3, 4, 5 }); // Too small to be valid

        // Act
        var result = await store.LoadAsync(corruptedPath);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task CreateWriterAsync_HandlesUnicodePathsCorrectly()
    {
        // Arrange
        var checkpointDir = Path.Combine(_testDirectory, ".photocopy");
        var store = new BinaryCheckpointStore(_clock, checkpointDir);
        var state = CheckpointState.CreateNew(
            sourceDirectory: @"C:\Фотографии\日本語",
            destinationPattern: @"D:\照片\{Year}\{Month}",
            totalFiles: 10,
            totalBytes: 10240,
            configHash: new byte[16],
            planHash: new byte[16],
            startedUtc: _clock.UtcNow);

        // Act
        await using (var writer = await store.CreateWriterAsync(state))
        {
            writer.RecordCompletion(0, OperationResult.Completed, 1024);
            await Task.Delay(100);
        }

        var loadedState = await store.LoadAsync(state.FilePath!);

        // Assert
        await Assert.That(loadedState).IsNotNull();
        await Assert.That(loadedState!.SourceDirectory).IsEqualTo(@"C:\Фотографии\日本語");
        await Assert.That(loadedState.DestinationPattern).IsEqualTo(@"D:\照片\{Year}\{Month}");
    }

    [Test]
    public async Task CreateWriterAsync_HandlesLongPaths()
    {
        // Arrange
        var checkpointDir = Path.Combine(_testDirectory, ".photocopy");
        var store = new BinaryCheckpointStore(_clock, checkpointDir);
        var longSourcePath = @"C:\Source\" + new string('a', 200);
        var state = CheckpointState.CreateNew(
            sourceDirectory: longSourcePath,
            destinationPattern: @"D:\Dest\{Year}",
            totalFiles: 5,
            totalBytes: 5120,
            configHash: new byte[16],
            planHash: new byte[16],
            startedUtc: _clock.UtcNow);

        // Act
        await using (var writer = await store.CreateWriterAsync(state))
        {
            writer.RecordCompletion(0, OperationResult.Completed, 1024);
            await Task.Delay(100);
        }

        var loadedState = await store.LoadAsync(state.FilePath!);

        // Assert
        await Assert.That(loadedState).IsNotNull();
        await Assert.That(loadedState!.SourceDirectory).IsEqualTo(longSourcePath);
    }

    #endregion

    #region Helper Methods

    private CheckpointState CreateCheckpointStateWithFilePath(string? filePath)
    {
        var state = CheckpointState.CreateNew(
            sourceDirectory: @"C:\Source",
            destinationPattern: @"D:\Dest",
            totalFiles: 10,
            totalBytes: 10240,
            configHash: new byte[16],
            planHash: new byte[16],
            startedUtc: _clock.UtcNow);
        state.FilePath = filePath;
        return state;
    }

    #endregion
}
