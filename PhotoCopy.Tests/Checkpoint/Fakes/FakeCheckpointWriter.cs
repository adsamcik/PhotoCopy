using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhotoCopy.Checkpoint;
using PhotoCopy.Checkpoint.Models;

namespace PhotoCopy.Tests.Checkpoint.Fakes;

/// <summary>
/// Fake checkpoint writer for testing. Uses in-memory storage for tracking completions.
/// </summary>
public class FakeCheckpointWriter : ICheckpointWriter
{
    private readonly BitArray _completed;
    private readonly ConcurrentBag<CompletionRecord> _recordedCompletions = new();
    private readonly ConcurrentDictionary<int, string> _recordedFailures = new();
    private readonly object _statsLock = new();

    private int _filesCompleted;
    private int _filesFailed;
    private int _filesSkipped;
    private long _bytesCompleted;
    private DateTime _lastUpdated;
    private bool _isCompleted;
    private bool _isFailed;
    private string? _failureMessage;

    public FakeCheckpointWriter(int totalFiles, DateTime? startTime = null)
    {
        _completed = new BitArray(totalFiles);
        _lastUpdated = startTime ?? DateTime.UtcNow;
    }

    /// <summary>
    /// Gets all recorded completions for test assertions.
    /// </summary>
    public IReadOnlyList<CompletionRecord> RecordedCompletions => _recordedCompletions.ToArray();

    /// <summary>
    /// Gets all recorded failures for test assertions.
    /// </summary>
    public IReadOnlyDictionary<int, string> RecordedFailures => _recordedFailures;

    /// <summary>
    /// Gets whether the checkpoint was marked as completed.
    /// </summary>
    public bool IsMarkedComplete => _isCompleted;

    /// <summary>
    /// Gets whether the checkpoint was marked as failed.
    /// </summary>
    public bool IsMarkedFailed => _isFailed;

    /// <summary>
    /// Gets the failure message if the checkpoint was marked as failed.
    /// </summary>
    public string? FailureMessage => _failureMessage;

    /// <summary>
    /// Gets the number of flush operations performed.
    /// </summary>
    public int FlushCount { get; private set; }

    public void RecordCompletion(int fileIndex, OperationResult result, long fileSize)
    {
        if (fileIndex < 0 || fileIndex >= _completed.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(fileIndex));
        }

        lock (_statsLock)
        {
            _completed[fileIndex] = true;
            _lastUpdated = DateTime.UtcNow;

            switch (result)
            {
                case OperationResult.Completed:
                case OperationResult.CopyDonePendingDelete:
                    _filesCompleted++;
                    _bytesCompleted += fileSize;
                    break;
                case OperationResult.Skipped:
                    _filesSkipped++;
                    break;
                case OperationResult.Failed:
                    _filesFailed++;
                    break;
            }
        }

        _recordedCompletions.Add(new CompletionRecord(fileIndex, result, fileSize, DateTime.UtcNow));
    }

    public void RecordFailure(int fileIndex, long fileSize, string errorMessage)
    {
        if (fileIndex < 0 || fileIndex >= _completed.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(fileIndex));
        }

        lock (_statsLock)
        {
            _completed[fileIndex] = true;
            _filesFailed++;
            _lastUpdated = DateTime.UtcNow;
        }

        _recordedFailures[fileIndex] = errorMessage;
        _recordedCompletions.Add(new CompletionRecord(fileIndex, OperationResult.Failed, fileSize, DateTime.UtcNow));
    }

    public bool IsCompleted(int fileIndex)
    {
        if (fileIndex < 0 || fileIndex >= _completed.Length)
        {
            return false;
        }

        lock (_statsLock)
        {
            return _completed[fileIndex];
        }
    }

    public CheckpointStatistics GetStatistics()
    {
        lock (_statsLock)
        {
            return new CheckpointStatistics
            {
                FilesCompleted = _filesCompleted,
                FilesFailed = _filesFailed,
                FilesSkipped = _filesSkipped,
                BytesCompleted = _bytesCompleted,
                LastUpdatedUtc = _lastUpdated
            };
        }
    }

    public Task FlushAsync(CancellationToken ct = default)
    {
        FlushCount++;
        return Task.CompletedTask;
    }

    public Task CompleteAsync(CancellationToken ct = default)
    {
        _isCompleted = true;
        return Task.CompletedTask;
    }

    public Task FailAsync(string errorMessage, CancellationToken ct = default)
    {
        _isFailed = true;
        _failureMessage = errorMessage;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Record of a completion for test assertions.
    /// </summary>
    public sealed record CompletionRecord(
        int FileIndex,
        OperationResult Result,
        long FileSize,
        DateTime Timestamp);
}
