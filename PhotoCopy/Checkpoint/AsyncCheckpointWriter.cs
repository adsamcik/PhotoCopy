using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using PhotoCopy.Checkpoint.Models;

namespace PhotoCopy.Checkpoint;

/// <summary>
/// High-performance async checkpoint writer using Channel for lock-free writes.
/// Workers call RecordCompletion without blocking; background task batches writes.
/// </summary>
public sealed class AsyncCheckpointWriter : ICheckpointWriter
{
    private const int ChannelCapacity = 10_000;
    private const int BatchSize = 170; // ~4KB batches (170 * 24 bytes = 4080 bytes)
    private const int WriteBufferSize = BatchSize * OperationRecord.RecordSize;

    private readonly string _checkpointPath;
    private readonly int _totalFiles;
    private readonly int _recordsOffset;
    private readonly ISystemClock _clock;

    // Lock-free channel for incoming records
    private readonly Channel<OperationRecord> _channel;
    private readonly ChannelWriter<OperationRecord> _writer;
    private readonly ChannelReader<OperationRecord> _reader;

    // O(1) completion tracking - 125KB for 1M files
    private readonly BitArray _completed;
    private readonly object _completedLock = new();

    // Error tracking for failed files
    private readonly ConcurrentDictionary<int, string> _errors = new();

    // Thread-safe statistics using Interlocked
    private long _filesCompleted;
    private long _filesFailed;
    private long _filesSkipped;
    private long _bytesCompleted;
    private DateTime _lastUpdatedUtc;
    private readonly object _lastUpdatedLock = new();

    // Background writer task
    private readonly Task _writerTask;
    private readonly CancellationTokenSource _cts = new();
    private readonly FileStream _fileStream;
    private int _currentRecordOffset;
    private bool _disposed;

    /// <summary>
    /// Creates a new AsyncCheckpointWriter.
    /// </summary>
    /// <param name="checkpointPath">Path to the .checkpoint file.</param>
    /// <param name="totalFiles">Total number of files for BitArray sizing.</param>
    /// <param name="recordsOffset">Byte offset where records begin in the file.</param>
    /// <param name="clock">System clock for timestamps (defaults to SystemClock.Instance).</param>
    public AsyncCheckpointWriter(
        string checkpointPath,
        int totalFiles,
        int recordsOffset,
        ISystemClock? clock = null)
    {
        _checkpointPath = checkpointPath ?? throw new ArgumentNullException(nameof(checkpointPath));
        _totalFiles = totalFiles;
        _recordsOffset = recordsOffset;
        _currentRecordOffset = recordsOffset;
        _clock = clock ?? SystemClock.Instance;
        _lastUpdatedUtc = _clock.UtcNow;

        // Initialize BitArray for completion tracking
        _completed = new BitArray(totalFiles);

        // Create bounded channel for backpressure
        var options = new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<OperationRecord>(options);
        _writer = _channel.Writer;
        _reader = _channel.Reader;

        // Open file stream for appending records
        _fileStream = new FileStream(
            checkpointPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        // Seek to the records offset
        _fileStream.Seek(recordsOffset, SeekOrigin.Begin);

        // Start background writer task
        _writerTask = Task.Run(WriteLoopAsync);
    }

    /// <summary>
    /// Creates an AsyncCheckpointWriter for resuming from an existing checkpoint.
    /// </summary>
    /// <param name="checkpointPath">Path to the existing .checkpoint file.</param>
    /// <param name="totalFiles">Total number of files.</param>
    /// <param name="recordsOffset">Byte offset where records begin.</param>
    /// <param name="existingCompleted">BitArray of already completed files.</param>
    /// <param name="existingErrors">Dictionary of existing errors.</param>
    /// <param name="existingStats">Existing statistics to continue from.</param>
    /// <param name="currentFileOffset">Current file offset for appending.</param>
    /// <param name="clock">System clock for timestamps.</param>
    public AsyncCheckpointWriter(
        string checkpointPath,
        int totalFiles,
        int recordsOffset,
        BitArray existingCompleted,
        Dictionary<int, string> existingErrors,
        CheckpointStatistics existingStats,
        int currentFileOffset,
        ISystemClock? clock = null)
        : this(checkpointPath, totalFiles, recordsOffset, clock)
    {
        // Restore existing state
        lock (_completedLock)
        {
            for (var i = 0; i < existingCompleted.Length && i < _completed.Length; i++)
            {
                _completed[i] = existingCompleted[i];
            }
        }

        foreach (var kvp in existingErrors)
        {
            _errors[kvp.Key] = kvp.Value;
        }

        _filesCompleted = existingStats.FilesCompleted;
        _filesFailed = existingStats.FilesFailed;
        _filesSkipped = existingStats.FilesSkipped;
        _bytesCompleted = existingStats.BytesCompleted;
        _lastUpdatedUtc = existingStats.LastUpdatedUtc;

        // Seek to correct position for appending
        _currentRecordOffset = currentFileOffset;
        _fileStream.Seek(currentFileOffset, SeekOrigin.Begin);
    }

    /// <inheritdoc/>
    public void RecordCompletion(int fileIndex, OperationResult result, long fileSize)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AsyncCheckpointWriter));

        if (fileIndex < 0 || fileIndex >= _totalFiles)
            throw new ArgumentOutOfRangeException(nameof(fileIndex));

        var now = _clock.UtcNow;
        var record = new OperationRecord(fileIndex, result, fileSize, now);

        // Mark as completed in BitArray (thread-safe)
        lock (_completedLock)
        {
            _completed[fileIndex] = true;
        }

        // Update statistics using Interlocked
        UpdateStatistics(result, fileSize, now);

        // Write to channel (non-blocking unless full)
        if (!_writer.TryWrite(record))
        {
            // Channel is full, block until space is available
            // This provides backpressure to prevent memory issues
            _writer.WriteAsync(record).AsTask().GetAwaiter().GetResult();
        }
    }

    /// <inheritdoc/>
    public void RecordFailure(int fileIndex, long fileSize, string errorMessage)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AsyncCheckpointWriter));

        if (fileIndex < 0 || fileIndex >= _totalFiles)
            throw new ArgumentOutOfRangeException(nameof(fileIndex));

        // Store error message
        _errors[fileIndex] = errorMessage ?? string.Empty;

        // Record as failed operation
        RecordCompletion(fileIndex, OperationResult.Failed, fileSize);
    }

    /// <inheritdoc/>
    public bool IsCompleted(int fileIndex)
    {
        if (fileIndex < 0 || fileIndex >= _totalFiles)
            return false;

        lock (_completedLock)
        {
            return _completed[fileIndex];
        }
    }

    /// <inheritdoc/>
    public CheckpointStatistics GetStatistics()
    {
        DateTime lastUpdated;
        lock (_lastUpdatedLock)
        {
            lastUpdated = _lastUpdatedUtc;
        }

        return new CheckpointStatistics
        {
            FilesCompleted = (int)Interlocked.Read(ref _filesCompleted),
            FilesFailed = (int)Interlocked.Read(ref _filesFailed),
            FilesSkipped = (int)Interlocked.Read(ref _filesSkipped),
            BytesCompleted = Interlocked.Read(ref _bytesCompleted),
            LastUpdatedUtc = lastUpdated
        };
    }

    /// <inheritdoc/>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AsyncCheckpointWriter));

        // Complete the channel to signal writer to finish
        _writer.Complete();

        // Wait for the writer task to drain the channel
        await _writerTask.WaitAsync(ct).ConfigureAwait(false);

        // Flush the file stream
        await _fileStream.FlushAsync(ct).ConfigureAwait(false);

        // Update header with current statistics
        await UpdateHeaderAsync(CheckpointStatus.InProgress, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task CompleteAsync(CancellationToken ct = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AsyncCheckpointWriter));

        // Ensure all pending writes are flushed
        if (!_writer.TryComplete())
        {
            // Already completed, just wait
        }
        else
        {
            await _writerTask.WaitAsync(ct).ConfigureAwait(false);
        }

        await _fileStream.FlushAsync(ct).ConfigureAwait(false);

        // Update header to mark as completed
        await UpdateHeaderAsync(CheckpointStatus.Completed, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task FailAsync(string errorMessage, CancellationToken ct = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AsyncCheckpointWriter));

        // Complete the channel
        _writer.TryComplete();

        try
        {
            // Wait for writer task to finish
            await _writerTask.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation during fail
        }

        await _fileStream.FlushAsync(ct).ConfigureAwait(false);

        // Update header to mark as failed
        await UpdateHeaderAsync(CheckpointStatus.Failed, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Signal cancellation
        await _cts.CancelAsync().ConfigureAwait(false);

        // Complete the channel
        _writer.TryComplete();

        try
        {
            // Wait for writer task to finish with timeout
            await _writerTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Writer took too long, continue with disposal
        }
        catch (OperationCanceledException)
        {
            // Expected during cancellation
        }

        // Dispose resources
        await _fileStream.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }

    private void UpdateStatistics(OperationResult result, long fileSize, DateTime timestamp)
    {
        switch (result)
        {
            case OperationResult.Completed:
            case OperationResult.CopyDonePendingDelete:
                Interlocked.Increment(ref _filesCompleted);
                break;
            case OperationResult.Skipped:
                Interlocked.Increment(ref _filesSkipped);
                break;
            case OperationResult.Failed:
                Interlocked.Increment(ref _filesFailed);
                break;
        }

        Interlocked.Add(ref _bytesCompleted, fileSize);

        lock (_lastUpdatedLock)
        {
            if (timestamp > _lastUpdatedUtc)
                _lastUpdatedUtc = timestamp;
        }
    }

    private async Task WriteLoopAsync()
    {
        var buffer = new byte[WriteBufferSize];
        var records = new List<OperationRecord>(BatchSize);

        try
        {
            while (await _reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                records.Clear();

                // Read up to BatchSize records
                while (records.Count < BatchSize && _reader.TryRead(out var record))
                {
                    records.Add(record);
                }

                if (records.Count > 0)
                {
                    await WriteBatchAsync(records, buffer).ConfigureAwait(false);
                }
            }

            // Drain any remaining records after channel is closed
            while (_reader.TryRead(out var record))
            {
                records.Clear();
                records.Add(record);

                while (records.Count < BatchSize && _reader.TryRead(out var nextRecord))
                {
                    records.Add(nextRecord);
                }

                await WriteBatchAsync(records, buffer).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (ChannelClosedException)
        {
            // Channel was closed, normal termination
        }
    }

    private async Task WriteBatchAsync(List<OperationRecord> records, byte[] buffer)
    {
        var bytesToWrite = records.Count * OperationRecord.RecordSize;
        var span = buffer.AsSpan(0, bytesToWrite);

        // Serialize all records to buffer
        for (var i = 0; i < records.Count; i++)
        {
            records[i].WriteTo(span.Slice(i * OperationRecord.RecordSize, OperationRecord.RecordSize));
        }

        // Write batch to file
        await _fileStream.WriteAsync(buffer.AsMemory(0, bytesToWrite), _cts.Token).ConfigureAwait(false);
        _currentRecordOffset += bytesToWrite;
    }

    private async Task UpdateHeaderAsync(CheckpointStatus status, CancellationToken ct)
    {
        var stats = GetStatistics();
        var headerBuffer = new byte[CheckpointHeader.HeaderSize];

        // Read current header
        _fileStream.Seek(0, SeekOrigin.Begin);
        var bytesRead = await _fileStream.ReadAsync(headerBuffer, ct).ConfigureAwait(false);

        if (bytesRead < CheckpointHeader.HeaderSize)
        {
            // Header not fully written, skip update
            return;
        }

        var header = CheckpointHeader.ReadFrom(headerBuffer);

        // Update header fields
        header.Status = (int)status;
        header.LastUpdateUtcTicks = _clock.UtcNow.Ticks;
        header.CompletedCount = stats.FilesCompleted + stats.FilesSkipped;
        header.CompletedBytes = stats.BytesCompleted;

        // Write updated header back
        header.WriteTo(headerBuffer);
        _fileStream.Seek(0, SeekOrigin.Begin);
        await _fileStream.WriteAsync(headerBuffer, ct).ConfigureAwait(false);
        await _fileStream.FlushAsync(ct).ConfigureAwait(false);

        // Seek back to records position for future writes
        _fileStream.Seek(_currentRecordOffset, SeekOrigin.Begin);
    }
}
