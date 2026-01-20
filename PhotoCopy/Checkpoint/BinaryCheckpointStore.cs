using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PhotoCopy.Checkpoint.Models;

namespace PhotoCopy.Checkpoint;

/// <summary>
/// Binary file-based checkpoint store for efficient persistence.
/// </summary>
/// <remarks>
/// Binary format:
/// 1. Header (128 bytes) - CheckpointHeader struct
/// 2. Source directory (UTF-8 string, length from header)
/// 3. Destination pattern (UTF-8 string, length from header)
/// 4. Padding to 8-byte alignment
/// 5. Operation records (24 bytes each, appended)
/// </remarks>
public sealed class BinaryCheckpointStore : ICheckpointStore
{
    private const string CheckpointFileExtension = ".checkpoint";
    private const string CheckpointFilePrefix = "photocopy-";
    private const string CheckpointDirectoryName = ".photocopy";

    private readonly ISystemClock _clock;
    private readonly string? _customCheckpointDirectory;

    /// <summary>
    /// Creates a new BinaryCheckpointStore.
    /// </summary>
    /// <param name="clock">System clock for timestamps (defaults to SystemClock.Instance).</param>
    /// <param name="customCheckpointDirectory">Optional custom checkpoint directory override.</param>
    public BinaryCheckpointStore(
        ISystemClock? clock = null,
        string? customCheckpointDirectory = null)
    {
        _clock = clock ?? SystemClock.Instance;
        _customCheckpointDirectory = customCheckpointDirectory;
    }

    /// <inheritdoc/>
    public string GetCheckpointDirectory(string destinationPattern)
    {
        if (_customCheckpointDirectory != null)
        {
            return _customCheckpointDirectory;
        }

        // Extract root directory from destination pattern
        // Pattern might be like: D:\Photos\{Year}\{Month}\{Filename}
        // We want just: D:\Photos
        var root = ExtractDestinationRoot(destinationPattern);
        return Path.Combine(root, CheckpointDirectoryName);
    }

    /// <inheritdoc/>
    public async Task<CheckpointState?> FindLatestAsync(
        string sourceDirectory,
        string destinationPattern,
        CancellationToken ct = default)
    {
        var checkpointDir = GetCheckpointDirectory(destinationPattern);

        if (!Directory.Exists(checkpointDir))
        {
            return null;
        }

        CheckpointState? latest = null;
        DateTime latestTime = DateTime.MinValue;

        var pattern = $"{CheckpointFilePrefix}*{CheckpointFileExtension}";
        foreach (var file in Directory.EnumerateFiles(checkpointDir, pattern))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var state = await LoadAsync(file, ct).ConfigureAwait(false);
                if (state == null)
                {
                    continue;
                }

                // Check if this checkpoint matches our source/dest
                if (!NormalizePath(state.SourceDirectory).Equals(NormalizePath(sourceDirectory), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!NormalizePath(state.DestinationPattern).Equals(NormalizePath(destinationPattern), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Only consider InProgress checkpoints for resume
                if (state.FilePath != null)
                {
                    var header = await ReadHeaderAsync(state.FilePath, ct).ConfigureAwait(false);
                    if (header.HasValue && (CheckpointStatus)header.Value.Status != CheckpointStatus.InProgress)
                    {
                        continue;
                    }
                }

                // Track the latest by start time
                if (state.StartedUtc > latestTime)
                {
                    latestTime = state.StartedUtc;
                    latest = state;
                }
            }
            catch (Exception)
            {
                // Skip corrupted or unreadable files
            }
        }

        return latest;
    }

    /// <inheritdoc/>
    public async Task<CheckpointState?> LoadAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        // Read header
        var headerBuffer = ArrayPool<byte>.Shared.Rent(CheckpointHeader.HeaderSize);
        try
        {
            var bytesRead = await stream.ReadAsync(headerBuffer.AsMemory(0, CheckpointHeader.HeaderSize), ct).ConfigureAwait(false);
            if (bytesRead < CheckpointHeader.HeaderSize)
            {
                return null; // Incomplete header
            }

            var header = CheckpointHeader.ReadFrom(headerBuffer.AsSpan(0, CheckpointHeader.HeaderSize));
            if (!header.IsValid)
            {
                return null; // Invalid magic or version
            }

            // Read source directory
            var sourceBuffer = ArrayPool<byte>.Shared.Rent(header.SourceDirectoryLength);
            try
            {
                bytesRead = await stream.ReadAsync(sourceBuffer.AsMemory(0, header.SourceDirectoryLength), ct).ConfigureAwait(false);
                if (bytesRead < header.SourceDirectoryLength)
                {
                    return null;
                }
                var sourceDirectory = Encoding.UTF8.GetString(sourceBuffer, 0, header.SourceDirectoryLength);

                // Read destination pattern
                var destBuffer = ArrayPool<byte>.Shared.Rent(header.DestinationPatternLength);
                try
                {
                    bytesRead = await stream.ReadAsync(destBuffer.AsMemory(0, header.DestinationPatternLength), ct).ConfigureAwait(false);
                    if (bytesRead < header.DestinationPatternLength)
                    {
                        return null;
                    }
                    var destinationPattern = Encoding.UTF8.GetString(destBuffer, 0, header.DestinationPatternLength);

                    // Skip padding to get to records offset
                    stream.Seek(header.RecordsOffset, SeekOrigin.Begin);

                    // Rebuild BitArray from operation records
                    var completed = new BitArray(header.TotalFiles);
                    var failed = new Dictionary<int, string>();
                    var statistics = new CheckpointStatistics
                    {
                        LastUpdatedUtc = header.LastUpdateUtc
                    };

                    var recordBuffer = ArrayPool<byte>.Shared.Rent(OperationRecord.RecordSize);
                    try
                    {
                        while ((bytesRead = await stream.ReadAsync(recordBuffer.AsMemory(0, OperationRecord.RecordSize), ct).ConfigureAwait(false)) == OperationRecord.RecordSize)
                        {
                            var record = OperationRecord.ReadFrom(recordBuffer.AsSpan(0, OperationRecord.RecordSize));

                            if (record.FileIndex >= 0 && record.FileIndex < header.TotalFiles)
                            {
                                completed[record.FileIndex] = true;

                                switch (record.Result)
                                {
                                    case OperationResult.Completed:
                                        statistics.FilesCompleted++;
                                        statistics.BytesCompleted += record.FileSize;
                                        break;
                                    case OperationResult.Failed:
                                        statistics.FilesFailed++;
                                        break;
                                    case OperationResult.Skipped:
                                        statistics.FilesSkipped++;
                                        break;
                                    case OperationResult.CopyDonePendingDelete:
                                        statistics.FilesCompleted++;
                                        statistics.BytesCompleted += record.FileSize;
                                        break;
                                }

                                if (record.TimestampUtc > statistics.LastUpdatedUtc)
                                {
                                    statistics.LastUpdatedUtc = record.TimestampUtc;
                                }
                            }
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(recordBuffer);
                    }

                    // Extract session ID from filename
                    var fileName = Path.GetFileNameWithoutExtension(path);
                    var sessionId = fileName.StartsWith(CheckpointFilePrefix, StringComparison.Ordinal)
                        ? fileName[CheckpointFilePrefix.Length..]
                        : fileName;

                    return new CheckpointState
                    {
                        SessionId = sessionId,
                        Version = header.Version,
                        StartedUtc = header.StartTimeUtc,
                        SourceDirectory = sourceDirectory,
                        DestinationPattern = destinationPattern,
                        ConfigHash = header.ConfigHashPrefix.ToByteArray(),
                        PlanHash = header.PlanHashPrefix.ToByteArray(),
                        TotalFiles = header.TotalFiles,
                        TotalBytes = header.TotalBytes,
                        Completed = completed,
                        Failed = failed,
                        Statistics = statistics,
                        FilePath = path
                    };
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(destBuffer);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(sourceBuffer);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuffer);
        }
    }

    /// <inheritdoc/>
    public async Task<ICheckpointWriter> CreateWriterAsync(CheckpointState state, CancellationToken ct = default)
    {
        var checkpointDir = GetCheckpointDirectory(state.DestinationPattern);
        Directory.CreateDirectory(checkpointDir);

        var fileName = $"{CheckpointFilePrefix}{state.SessionId}{CheckpointFileExtension}";
        var filePath = Path.Combine(checkpointDir, fileName);

        // Prepare config and plan hashes (ensure 16 bytes minimum)
        var configHash = EnsureHashLength(state.ConfigHash, 16);
        var planHash = EnsureHashLength(state.PlanHash, 16);

        // Create header
        var header = CheckpointHeader.Create(
            state.TotalFiles,
            state.TotalBytes,
            configHash,
            planHash,
            state.StartedUtc);

        // Calculate string lengths and records offset
        var sourceBytes = Encoding.UTF8.GetBytes(state.SourceDirectory);
        var destBytes = Encoding.UTF8.GetBytes(state.DestinationPattern);

        header.SourceDirectoryLength = sourceBytes.Length;
        header.DestinationPatternLength = destBytes.Length;

        // Calculate records offset with 8-byte alignment
        var stringsEnd = CheckpointHeader.HeaderSize + sourceBytes.Length + destBytes.Length;
        var recordsOffset = AlignTo8(stringsEnd);
        header.RecordsOffset = recordsOffset;

        // Write initial file
        await using var stream = new FileStream(
            filePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);

        // Write header
        var headerBuffer = ArrayPool<byte>.Shared.Rent(CheckpointHeader.HeaderSize);
        try
        {
            header.WriteTo(headerBuffer.AsSpan(0, CheckpointHeader.HeaderSize));
            await stream.WriteAsync(headerBuffer.AsMemory(0, CheckpointHeader.HeaderSize), ct).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuffer);
        }

        // Write source directory
        await stream.WriteAsync(sourceBytes, ct).ConfigureAwait(false);

        // Write destination pattern
        await stream.WriteAsync(destBytes, ct).ConfigureAwait(false);

        // Write padding to align to 8 bytes
        var paddingSize = recordsOffset - stringsEnd;
        if (paddingSize > 0)
        {
            var padding = new byte[paddingSize];
            await stream.WriteAsync(padding, ct).ConfigureAwait(false);
        }

        await stream.FlushAsync(ct).ConfigureAwait(false);

        // Update state with file path
        state.FilePath = filePath;

        // Create and return the writer
        return new AsyncCheckpointWriter(
            filePath,
            state.TotalFiles,
            recordsOffset,
            _clock);
    }

    /// <inheritdoc/>
    public async Task<ICheckpointWriter> ResumeWriterAsync(CheckpointState state, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(state.FilePath))
        {
            throw new ArgumentException("State must have a file path for resume", nameof(state));
        }

        if (!File.Exists(state.FilePath))
        {
            throw new FileNotFoundException("Checkpoint file not found", state.FilePath);
        }

        // Read the header to get records offset
        var header = await ReadHeaderAsync(state.FilePath, ct).ConfigureAwait(false);
        if (!header.HasValue)
        {
            throw new InvalidOperationException("Cannot read checkpoint header");
        }

        // Calculate current file offset (end of file where we'll append)
        var fileInfo = new FileInfo(state.FilePath);
        var currentOffset = (int)fileInfo.Length;

        // Create writer with existing state
        return new AsyncCheckpointWriter(
            state.FilePath,
            state.TotalFiles,
            header.Value.RecordsOffset,
            state.Completed,
            state.Failed,
            state.Statistics,
            currentOffset,
            _clock);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<CheckpointInfo> ListCheckpointsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // If using custom directory, only scan that
        if (_customCheckpointDirectory != null)
        {
            if (Directory.Exists(_customCheckpointDirectory))
            {
                await foreach (var info in ScanDirectoryAsync(_customCheckpointDirectory, ct).ConfigureAwait(false))
                {
                    yield return info;
                }
            }
            yield break;
        }

        // Otherwise, we need to scan common locations
        // This is a limitation - we can only list checkpoints we know about
        // For now, yield nothing if no custom directory is set
        // Real implementation would need a registry or scan all drives
        yield break;
    }

    /// <inheritdoc/>
    public async Task CleanupAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        var cutoff = _clock.UtcNow - maxAge;

        await foreach (var info in ListCheckpointsAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            // Delete completed/failed checkpoints older than maxAge
            if (info.Status != CheckpointStatus.InProgress && info.StartedUtc < cutoff)
            {
                await DeleteAsync(info.Path, ct).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    #region Private Helpers

    private static string ExtractDestinationRoot(string destinationPattern)
    {
        // Find the first variable placeholder or assume it's a directory
        var varIndex = destinationPattern.IndexOf('{');
        string path;

        if (varIndex > 0)
        {
            // Take everything before the first variable
            path = destinationPattern[..varIndex];
        }
        else if (varIndex == 0)
        {
            // Pattern starts with variable, use current directory
            return Directory.GetCurrentDirectory();
        }
        else
        {
            // No variables, assume it's a full path
            path = destinationPattern;
        }

        // Get the directory part
        var dirPath = Path.GetDirectoryName(path);

        // If empty or null, use the path itself if it looks like a directory
        if (string.IsNullOrEmpty(dirPath))
        {
            return Directory.Exists(path) ? path : Directory.GetCurrentDirectory();
        }

        // Walk up to find the first concrete directory (no path separator fragments)
        while (!string.IsNullOrEmpty(dirPath))
        {
            var dirName = Path.GetFileName(dirPath);
            if (!string.IsNullOrEmpty(dirName) && !dirName.Contains('{'))
            {
                // Check if this looks like a valid root
                var parent = Path.GetDirectoryName(dirPath);
                if (parent == null || IsRootPath(dirPath))
                {
                    return dirPath;
                }
                // Return the deepest concrete directory
                return dirPath;
            }
            dirPath = Path.GetDirectoryName(dirPath);
        }

        // Fallback
        var root = Path.GetPathRoot(destinationPattern);
        return string.IsNullOrEmpty(root) ? Directory.GetCurrentDirectory() : root;
    }

    private static bool IsRootPath(string path)
    {
        var root = Path.GetPathRoot(path);
        return !string.IsNullOrEmpty(root) && 
               (path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Equals(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), 
                            StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePath(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static int AlignTo8(int value)
    {
        return (value + 7) & ~7;
    }

    private static byte[] EnsureHashLength(byte[] hash, int minLength)
    {
        if (hash.Length >= minLength)
        {
            return hash;
        }

        var result = new byte[minLength];
        hash.CopyTo(result, 0);
        return result;
    }

    private async Task<CheckpointHeader?> ReadHeaderAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous);

        var buffer = ArrayPool<byte>.Shared.Rent(CheckpointHeader.HeaderSize);
        try
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, CheckpointHeader.HeaderSize), ct).ConfigureAwait(false);
            if (bytesRead < CheckpointHeader.HeaderSize)
            {
                return null;
            }

            var header = CheckpointHeader.ReadFrom(buffer.AsSpan(0, CheckpointHeader.HeaderSize));
            return header.IsValid ? header : null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async IAsyncEnumerable<CheckpointInfo> ScanDirectoryAsync(
        string directory,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var pattern = $"{CheckpointFilePrefix}*{CheckpointFileExtension}";

        foreach (var file in Directory.EnumerateFiles(directory, pattern))
        {
            ct.ThrowIfCancellationRequested();

            CheckpointInfo? info = null;
            try
            {
                var header = await ReadHeaderAsync(file, ct).ConfigureAwait(false);
                if (header.HasValue && header.Value.IsValid)
                {
                    // Read strings to get source/dest
                    await using var stream = new FileStream(
                        file,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 4096,
                        FileOptions.Asynchronous);

                    stream.Seek(CheckpointHeader.HeaderSize, SeekOrigin.Begin);

                    var sourceBuffer = ArrayPool<byte>.Shared.Rent(header.Value.SourceDirectoryLength);
                    var destBuffer = ArrayPool<byte>.Shared.Rent(header.Value.DestinationPatternLength);
                    try
                    {
                        await stream.ReadAsync(sourceBuffer.AsMemory(0, header.Value.SourceDirectoryLength), ct).ConfigureAwait(false);
                        await stream.ReadAsync(destBuffer.AsMemory(0, header.Value.DestinationPatternLength), ct).ConfigureAwait(false);

                        var sourceDirectory = Encoding.UTF8.GetString(sourceBuffer, 0, header.Value.SourceDirectoryLength);
                        var destinationPattern = Encoding.UTF8.GetString(destBuffer, 0, header.Value.DestinationPatternLength);

                        var fileName = Path.GetFileNameWithoutExtension(file);
                        var sessionId = fileName.StartsWith(CheckpointFilePrefix, StringComparison.Ordinal)
                            ? fileName[CheckpointFilePrefix.Length..]
                            : fileName;

                        info = new CheckpointInfo(
                            Path: file,
                            SessionId: sessionId,
                            SourceDirectory: sourceDirectory,
                            DestinationPattern: destinationPattern,
                            StartedUtc: header.Value.StartTimeUtc,
                            TotalFiles: header.Value.TotalFiles,
                            CompletedFiles: header.Value.CompletedCount,
                            Status: (CheckpointStatus)header.Value.Status);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(sourceBuffer);
                        ArrayPool<byte>.Shared.Return(destBuffer);
                    }
                }
            }
            catch (Exception)
            {
                // Skip unreadable files
            }

            if (info != null)
            {
                yield return info;
            }
        }
    }

    #endregion
}
