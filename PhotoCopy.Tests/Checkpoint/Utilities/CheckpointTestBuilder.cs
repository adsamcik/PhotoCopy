using System;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using PhotoCopy.Checkpoint.Models;

namespace PhotoCopy.Tests.Checkpoint.Utilities;

/// <summary>
/// Fluent builder for creating CheckpointState instances for tests.
/// </summary>
public class CheckpointTestBuilder
{
    private string _sessionId = $"test-{Guid.NewGuid():N}";
    private string _sourceDirectory = @"C:\TestSource";
    private string _destinationPattern = @"C:\TestDest\{Year}\{Month}";
    private int _totalFiles = 100;
    private long _totalBytes = 1024 * 1024 * 100; // 100 MB
    private DateTime _startTime = new(2026, 1, 20, 12, 0, 0, DateTimeKind.Utc);
    private int _version = 1;
    private BitArray? _completed;
    private HashSet<int>? _pendingDeletion;
    private Dictionary<int, string>? _failed;
    private CheckpointStatistics? _statistics;
    private byte[]? _configHash;
    private byte[]? _planHash;
    private string? _filePath;

    /// <summary>
    /// Creates a new builder instance.
    /// </summary>
    public static CheckpointTestBuilder Create() => new();

    /// <summary>
    /// Sets the session ID.
    /// </summary>
    public CheckpointTestBuilder WithSessionId(string sessionId)
    {
        _sessionId = sessionId;
        return this;
    }

    /// <summary>
    /// Sets the source directory.
    /// </summary>
    public CheckpointTestBuilder WithSource(string sourceDirectory)
    {
        _sourceDirectory = sourceDirectory;
        return this;
    }

    /// <summary>
    /// Sets the destination pattern.
    /// </summary>
    public CheckpointTestBuilder WithDestination(string destinationPattern)
    {
        _destinationPattern = destinationPattern;
        return this;
    }

    /// <summary>
    /// Sets the total file count.
    /// </summary>
    public CheckpointTestBuilder WithTotalFiles(int totalFiles)
    {
        _totalFiles = totalFiles;
        return this;
    }

    /// <summary>
    /// Sets the total bytes.
    /// </summary>
    public CheckpointTestBuilder WithTotalBytes(long totalBytes)
    {
        _totalBytes = totalBytes;
        return this;
    }

    /// <summary>
    /// Sets the start time.
    /// </summary>
    public CheckpointTestBuilder WithStartTime(DateTime startTime)
    {
        _startTime = startTime;
        return this;
    }

    /// <summary>
    /// Sets the schema version.
    /// </summary>
    public CheckpointTestBuilder WithVersion(int version)
    {
        _version = version;
        return this;
    }

    /// <summary>
    /// Sets the completed files using specific indices.
    /// </summary>
    public CheckpointTestBuilder WithCompletedFiles(params int[] fileIndices)
    {
        _completed = new BitArray(_totalFiles);
        foreach (var index in fileIndices)
        {
            if (index >= 0 && index < _totalFiles)
            {
                _completed[index] = true;
            }
        }
        return this;
    }

    /// <summary>
    /// Sets the completed files using a range (0 to count-1).
    /// </summary>
    public CheckpointTestBuilder WithCompletedFileRange(int count)
    {
        _completed = new BitArray(_totalFiles);
        for (var i = 0; i < count && i < _totalFiles; i++)
        {
            _completed[i] = true;
        }
        return this;
    }

    /// <summary>
    /// Sets all files as completed.
    /// </summary>
    public CheckpointTestBuilder WithAllCompleted()
    {
        _completed = new BitArray(_totalFiles, true);
        return this;
    }

    /// <summary>
    /// Adds files pending source deletion (for Move operations).
    /// </summary>
    public CheckpointTestBuilder WithPendingDeletion(params int[] fileIndices)
    {
        _pendingDeletion = new HashSet<int>(fileIndices);
        return this;
    }

    /// <summary>
    /// Adds failed files with error messages.
    /// </summary>
    public CheckpointTestBuilder WithFailedFiles(params (int index, string error)[] failures)
    {
        _failed = new Dictionary<int, string>();
        foreach (var (index, error) in failures)
        {
            _failed[index] = error;
        }
        return this;
    }

    /// <summary>
    /// Sets custom statistics.
    /// </summary>
    public CheckpointTestBuilder WithStatistics(CheckpointStatistics statistics)
    {
        _statistics = statistics;
        return this;
    }

    /// <summary>
    /// Sets custom statistics using individual values.
    /// </summary>
    public CheckpointTestBuilder WithStatistics(
        int filesCompleted = 0,
        int filesFailed = 0,
        int filesSkipped = 0,
        long bytesCompleted = 0)
    {
        _statistics = new CheckpointStatistics
        {
            FilesCompleted = filesCompleted,
            FilesFailed = filesFailed,
            FilesSkipped = filesSkipped,
            BytesCompleted = bytesCompleted,
            LastUpdatedUtc = _startTime
        };
        return this;
    }

    /// <summary>
    /// Sets a custom config hash.
    /// </summary>
    public CheckpointTestBuilder WithConfigHash(byte[] hash)
    {
        _configHash = hash;
        return this;
    }

    /// <summary>
    /// Sets a custom plan hash.
    /// </summary>
    public CheckpointTestBuilder WithPlanHash(byte[] hash)
    {
        _planHash = hash;
        return this;
    }

    /// <summary>
    /// Sets the file path.
    /// </summary>
    public CheckpointTestBuilder WithFilePath(string filePath)
    {
        _filePath = filePath;
        return this;
    }

    /// <summary>
    /// Builds the CheckpointState instance.
    /// </summary>
    public CheckpointState Build()
    {
        var state = new CheckpointState
        {
            SessionId = _sessionId,
            Version = _version,
            StartedUtc = _startTime,
            SourceDirectory = _sourceDirectory,
            DestinationPattern = _destinationPattern,
            ConfigHash = _configHash ?? ComputeMockHash($"config:{_sourceDirectory}:{_destinationPattern}"),
            PlanHash = _planHash ?? ComputeMockHash($"plan:{_totalFiles}:{_totalBytes}"),
            TotalFiles = _totalFiles,
            TotalBytes = _totalBytes,
            Completed = _completed ?? new BitArray(_totalFiles),
            PendingSourceDeletion = _pendingDeletion ?? new HashSet<int>(),
            Failed = _failed ?? new Dictionary<int, string>(),
            Statistics = _statistics ?? new CheckpointStatistics
            {
                FilesCompleted = _completed?.Cast<bool>().Count(b => b) ?? 0,
                LastUpdatedUtc = _startTime
            },
            FilePath = _filePath
        };

        return state;
    }

    /// <summary>
    /// Computes a mock hash for test data.
    /// </summary>
    private static byte[] ComputeMockHash(string input)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(input));
    }

    /// <summary>
    /// Creates a mock hash from a string (for use in assertions).
    /// </summary>
    public static byte[] MockHash(string input) => ComputeMockHash(input);
}
