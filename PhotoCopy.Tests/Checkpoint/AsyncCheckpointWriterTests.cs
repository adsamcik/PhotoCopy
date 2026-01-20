using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PhotoCopy.Checkpoint;
using PhotoCopy.Checkpoint.Models;
using PhotoCopy.Tests.Checkpoint.Fakes;

namespace PhotoCopy.Tests.Checkpoint;

public class AsyncCheckpointWriterTests : IAsyncDisposable
{
    private string _testDirectory = null!;
    private string _checkpointPath = null!;
    private FakeClock _clock = null!;

    [Before(Test)]
    public void Setup()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "AsyncCheckpointWriterTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDirectory);
        _checkpointPath = Path.Combine(_testDirectory, "test.checkpoint");
        _clock = new FakeClock(new DateTime(2026, 1, 20, 12, 0, 0, DateTimeKind.Utc));
        
        // Pre-create checkpoint file with minimal header
        CreateMinimalCheckpointFile(_checkpointPath);
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

    #region RecordCompletion Tests

    [Test]
    public async Task RecordCompletion_MarksFileAsCompletedInBitArray()
    {
        // Arrange
        const int totalFiles = 100;
        const int recordsOffset = 256;
        const int fileIndex = 42;

        await using var writer = new AsyncCheckpointWriter(
            _checkpointPath, totalFiles, recordsOffset, _clock);

        // Act
        writer.RecordCompletion(fileIndex, OperationResult.Completed, 1024);
        await Task.Delay(50); // Allow async processing

        // Assert
        await Assert.That(writer.IsCompleted(fileIndex)).IsTrue();
    }

    [Test]
    public async Task RecordCompletion_UpdatesStatistics()
    {
        // Arrange
        const int totalFiles = 100;
        const int recordsOffset = 256;
        const long fileSize = 2048;

        await using var writer = new AsyncCheckpointWriter(
            _checkpointPath, totalFiles, recordsOffset, _clock);

        // Act
        writer.RecordCompletion(0, OperationResult.Completed, fileSize);
        writer.RecordCompletion(1, OperationResult.Skipped, fileSize);
        writer.RecordCompletion(2, OperationResult.Failed, fileSize);
        await Task.Delay(50); // Allow async processing

        // Assert
        var stats = writer.GetStatistics();
        await Assert.That(stats.FilesCompleted).IsEqualTo(1);
        await Assert.That(stats.FilesSkipped).IsEqualTo(1);
        await Assert.That(stats.FilesFailed).IsEqualTo(1);
        await Assert.That(stats.BytesCompleted).IsEqualTo(fileSize * 3);
    }

    #endregion

    #region IsCompleted Tests

    [Test]
    public async Task IsCompleted_ReturnsTrue_ForCompletedFiles()
    {
        // Arrange
        const int totalFiles = 50;
        const int recordsOffset = 256;
        const int fileIndex = 25;

        await using var writer = new AsyncCheckpointWriter(
            _checkpointPath, totalFiles, recordsOffset, _clock);

        writer.RecordCompletion(fileIndex, OperationResult.Completed, 512);

        // Act
        var isCompleted = writer.IsCompleted(fileIndex);

        // Assert
        await Assert.That(isCompleted).IsTrue();
    }

    [Test]
    public async Task IsCompleted_ReturnsFalse_ForIncompleteFiles()
    {
        // Arrange
        const int totalFiles = 50;
        const int recordsOffset = 256;
        const int fileIndex = 25;

        await using var writer = new AsyncCheckpointWriter(
            _checkpointPath, totalFiles, recordsOffset, _clock);

        // Act - don't mark file as completed
        var isCompleted = writer.IsCompleted(fileIndex);

        // Assert
        await Assert.That(isCompleted).IsFalse();
    }

    [Test]
    public async Task IsCompleted_ReturnsFalse_ForOutOfRangeIndex()
    {
        // Arrange
        const int totalFiles = 50;
        const int recordsOffset = 256;

        await using var writer = new AsyncCheckpointWriter(
            _checkpointPath, totalFiles, recordsOffset, _clock);

        // Act
        var isCompletedNegative = writer.IsCompleted(-1);
        var isCompletedTooLarge = writer.IsCompleted(100);

        // Assert
        await Assert.That(isCompletedNegative).IsFalse();
        await Assert.That(isCompletedTooLarge).IsFalse();
    }

    #endregion

    #region GetStatistics Tests

    [Test]
    public async Task GetStatistics_ReturnsAccurateCounts()
    {
        // Arrange
        const int totalFiles = 100;
        const int recordsOffset = 256;

        await using var writer = new AsyncCheckpointWriter(
            _checkpointPath, totalFiles, recordsOffset, _clock);

        // Act - record various operations
        writer.RecordCompletion(0, OperationResult.Completed, 1000);
        writer.RecordCompletion(1, OperationResult.Completed, 2000);
        writer.RecordCompletion(2, OperationResult.Completed, 3000);
        writer.RecordCompletion(3, OperationResult.Skipped, 500);
        writer.RecordCompletion(4, OperationResult.Skipped, 600);
        writer.RecordCompletion(5, OperationResult.Failed, 100);
        await Task.Delay(50); // Allow async processing

        var stats = writer.GetStatistics();

        // Assert
        await Assert.That(stats.FilesCompleted).IsEqualTo(3);
        await Assert.That(stats.FilesSkipped).IsEqualTo(2);
        await Assert.That(stats.FilesFailed).IsEqualTo(1);
        await Assert.That(stats.BytesCompleted).IsEqualTo(7200L);
    }

    [Test]
    public async Task GetStatistics_UpdatesLastUpdatedUtc()
    {
        // Arrange
        const int totalFiles = 100;
        const int recordsOffset = 256;
        var initialTime = new DateTime(2026, 1, 20, 12, 0, 0, DateTimeKind.Utc);

        await using var writer = new AsyncCheckpointWriter(
            _checkpointPath, totalFiles, recordsOffset, _clock);

        // Act
        _clock.Advance(TimeSpan.FromMinutes(5));
        writer.RecordCompletion(0, OperationResult.Completed, 1000);
        await Task.Delay(50);

        var stats = writer.GetStatistics();

        // Assert
        await Assert.That(stats.LastUpdatedUtc).IsEqualTo(initialTime.AddMinutes(5));
    }

    #endregion

    #region Parallel Thread Safety Tests

    [Test]
    public async Task MultipleCompletions_FromParallelThreads_AreHandledCorrectly()
    {
        // Arrange
        const int totalFiles = 1000;
        const int recordsOffset = 256;
        const int parallelOperations = 100;

        await using var writer = new AsyncCheckpointWriter(
            _checkpointPath, totalFiles, recordsOffset, _clock);

        // Act - simulate parallel completions
        var tasks = new List<Task>();
        for (var i = 0; i < parallelOperations; i++)
        {
            var fileIndex = i;
            tasks.Add(Task.Run(() => 
                writer.RecordCompletion(fileIndex, OperationResult.Completed, 1024)));
        }
        await Task.WhenAll(tasks);
        await Task.Delay(100); // Allow async processing

        // Assert - all files should be marked as completed
        var stats = writer.GetStatistics();
        await Assert.That(stats.FilesCompleted).IsEqualTo(parallelOperations);
        await Assert.That(stats.BytesCompleted).IsEqualTo(parallelOperations * 1024L);

        // Verify each file is marked as completed
        for (var i = 0; i < parallelOperations; i++)
        {
            await Assert.That(writer.IsCompleted(i)).IsTrue();
        }
    }

    [Test]
    public async Task MixedOperations_FromParallelThreads_MaintainAccurateStatistics()
    {
        // Arrange
        const int totalFiles = 300;
        const int recordsOffset = 256;
        const int completedCount = 100;
        const int skippedCount = 50;
        const int failedCount = 25;

        await using var writer = new AsyncCheckpointWriter(
            _checkpointPath, totalFiles, recordsOffset, _clock);

        // Act - simulate parallel mixed operations
        var tasks = new List<Task>();
        
        // Completed operations (indices 0-99)
        for (var i = 0; i < completedCount; i++)
        {
            var fileIndex = i;
            tasks.Add(Task.Run(() => 
                writer.RecordCompletion(fileIndex, OperationResult.Completed, 1000)));
        }
        
        // Skipped operations (indices 100-149)
        for (var i = 0; i < skippedCount; i++)
        {
            var fileIndex = completedCount + i;
            tasks.Add(Task.Run(() => 
                writer.RecordCompletion(fileIndex, OperationResult.Skipped, 500)));
        }
        
        // Failed operations (indices 150-174)
        for (var i = 0; i < failedCount; i++)
        {
            var fileIndex = completedCount + skippedCount + i;
            tasks.Add(Task.Run(() => 
                writer.RecordCompletion(fileIndex, OperationResult.Failed, 200)));
        }

        await Task.WhenAll(tasks);
        await Task.Delay(100); // Allow async processing

        // Assert
        var stats = writer.GetStatistics();
        await Assert.That(stats.FilesCompleted).IsEqualTo(completedCount);
        await Assert.That(stats.FilesSkipped).IsEqualTo(skippedCount);
        await Assert.That(stats.FilesFailed).IsEqualTo(failedCount);

        var expectedBytes = (completedCount * 1000L) + (skippedCount * 500L) + (failedCount * 200L);
        await Assert.That(stats.BytesCompleted).IsEqualTo(expectedBytes);
    }

    #endregion

    #region FlushAsync Tests

    [Test]
    public async Task FlushAsync_WritesAllPendingRecords()
    {
        // Arrange
        const int totalFiles = 50;
        const int recordsOffset = 256;

        await using var writer = new AsyncCheckpointWriter(
            _checkpointPath, totalFiles, recordsOffset, _clock);

        // Record some completions
        for (var i = 0; i < 10; i++)
        {
            writer.RecordCompletion(i, OperationResult.Completed, 1024);
        }

        // Act
        await writer.FlushAsync();

        // Assert - verify file was written (file should be larger than header)
        var fileInfo = new FileInfo(_checkpointPath);
        var expectedMinSize = recordsOffset + (10 * OperationRecord.RecordSize);
        await Assert.That(fileInfo.Length).IsGreaterThanOrEqualTo(expectedMinSize);
    }

    [Test]
    public async Task FlushAsync_PreservesAllCompletionStates()
    {
        // Arrange
        const int totalFiles = 100;
        const int recordsOffset = 256;
        var completedIndices = new[] { 5, 15, 25, 35, 45 };

        await using var writer = new AsyncCheckpointWriter(
            _checkpointPath, totalFiles, recordsOffset, _clock);

        foreach (var index in completedIndices)
        {
            writer.RecordCompletion(index, OperationResult.Completed, 1024);
        }

        // Act
        await writer.FlushAsync();

        // Assert - all marked files should still be completed
        foreach (var index in completedIndices)
        {
            await Assert.That(writer.IsCompleted(index)).IsTrue();
        }

        // Non-completed files should still be incomplete
        await Assert.That(writer.IsCompleted(0)).IsFalse();
        await Assert.That(writer.IsCompleted(10)).IsFalse();
        await Assert.That(writer.IsCompleted(99)).IsFalse();
    }

    #endregion

    #region RecordFailure Tests

    [Test]
    public async Task RecordFailure_MarksFileAsCompletedWithFailedResult()
    {
        // Arrange
        const int totalFiles = 100;
        const int recordsOffset = 256;
        const int fileIndex = 50;

        await using var writer = new AsyncCheckpointWriter(
            _checkpointPath, totalFiles, recordsOffset, _clock);

        // Act
        writer.RecordFailure(fileIndex, 1024, "Test error message");
        await Task.Delay(50);

        // Assert
        await Assert.That(writer.IsCompleted(fileIndex)).IsTrue();
        
        var stats = writer.GetStatistics();
        await Assert.That(stats.FilesFailed).IsEqualTo(1);
    }

    #endregion

    #region Helper Methods

    private static void CreateMinimalCheckpointFile(string path)
    {
        // Create a minimal valid checkpoint file with just a header placeholder
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        
        // Write minimal header (256 bytes of zeros)
        var header = new byte[256];
        fs.Write(header, 0, header.Length);
    }

    #endregion
}
