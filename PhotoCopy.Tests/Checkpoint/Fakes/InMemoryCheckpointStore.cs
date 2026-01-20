using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using PhotoCopy.Checkpoint;
using PhotoCopy.Checkpoint.Models;

namespace PhotoCopy.Tests.Checkpoint.Fakes;

/// <summary>
/// In-memory checkpoint store for testing.
/// </summary>
public class InMemoryCheckpointStore : ICheckpointStore
{
    private readonly Dictionary<string, CheckpointState> _checkpoints = new();
    private readonly Dictionary<string, FakeCheckpointWriter> _writers = new();
    private readonly List<CheckpointState> _createdCheckpoints = new();
    private readonly object _lock = new();
    private string _checkpointDirectory = ".photocopy-checkpoints";

    /// <summary>
    /// Gets all checkpoints stored in this instance.
    /// </summary>
    public IReadOnlyDictionary<string, CheckpointState> Checkpoints
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<string, CheckpointState>(_checkpoints);
            }
        }
    }

    /// <summary>
    /// Gets all writers created by this store.
    /// </summary>
    public IReadOnlyDictionary<string, FakeCheckpointWriter> Writers
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<string, FakeCheckpointWriter>(_writers);
            }
        }
    }

    /// <summary>
    /// Gets all checkpoints that have been created (for test assertions).
    /// </summary>
    public IReadOnlyList<CheckpointState> CreatedCheckpoints
    {
        get
        {
            lock (_lock)
            {
                return _createdCheckpoints.ToList();
            }
        }
    }

    /// <summary>
    /// Adds a checkpoint directly for testing.
    /// </summary>
    public void AddCheckpoint(CheckpointState state)
    {
        lock (_lock)
        {
            var path = state.FilePath ?? GeneratePath(state.SessionId);
            state.FilePath = path;
            _checkpoints[path] = state;
        }
    }

    public Task<CheckpointState?> FindLatestAsync(
        string sourceDirectory,
        string destinationPattern,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            var matching = _checkpoints.Values
                .Where(c => c.SourceDirectory == sourceDirectory &&
                           c.DestinationPattern == destinationPattern)
                .OrderByDescending(c => c.StartedUtc)
                .FirstOrDefault();

            return Task.FromResult(matching);
        }
    }

    public Task<CheckpointState?> LoadAsync(string path, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _checkpoints.TryGetValue(path, out var state);
            return Task.FromResult(state);
        }
    }

    public Task<ICheckpointWriter> CreateWriterAsync(CheckpointState state, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var path = GeneratePath(state.SessionId);
            state.FilePath = path;
            _checkpoints[path] = state;
            _createdCheckpoints.Add(state);

            var writer = new FakeCheckpointWriter(state.TotalFiles, state.StartedUtc);
            _writers[path] = writer;

            return Task.FromResult<ICheckpointWriter>(writer);
        }
    }

    public Task<ICheckpointWriter> ResumeWriterAsync(CheckpointState state, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var path = state.FilePath ?? GeneratePath(state.SessionId);
            state.FilePath = path;
            _checkpoints[path] = state;

            var writer = new FakeCheckpointWriter(state.TotalFiles, state.StartedUtc);

            // Pre-populate the writer with existing completed files
            for (var i = 0; i < state.Completed.Length; i++)
            {
                if (state.Completed[i])
                {
                    writer.RecordCompletion(i, OperationResult.Completed, 0);
                }
            }

            _writers[path] = writer;

            return Task.FromResult<ICheckpointWriter>(writer);
        }
    }

    public string GetCheckpointDirectory(string destinationPattern)
    {
        return _checkpointDirectory;
    }

    /// <summary>
    /// Sets the checkpoint directory for testing.
    /// </summary>
    public void SetCheckpointDirectory(string directory)
    {
        _checkpointDirectory = directory;
    }

    public async IAsyncEnumerable<CheckpointInfo> ListCheckpointsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        List<CheckpointState> checkpoints;
        lock (_lock)
        {
            checkpoints = _checkpoints.Values.ToList();
        }

        foreach (var state in checkpoints)
        {
            if (ct.IsCancellationRequested)
            {
                yield break;
            }

            yield return new CheckpointInfo(
                state.FilePath ?? GeneratePath(state.SessionId),
                state.SessionId,
                state.SourceDirectory,
                state.DestinationPattern,
                state.StartedUtc,
                state.TotalFiles,
                state.CompletedCount,
                CheckpointStatus.InProgress);

            await Task.Yield();
        }
    }

    public Task CleanupAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - maxAge;

        lock (_lock)
        {
            var toRemove = _checkpoints
                .Where(kvp => kvp.Value.StartedUtc < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var path in toRemove)
            {
                _checkpoints.Remove(path);
                _writers.Remove(path);
            }
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _checkpoints.Remove(path);
            _writers.Remove(path);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Clears all stored checkpoints (for test cleanup).
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _checkpoints.Clear();
            _writers.Clear();
            _createdCheckpoints.Clear();
        }
    }

    private static string GeneratePath(string sessionId)
    {
        return $".photocopy-checkpoints/{sessionId}.checkpoint";
    }
}
