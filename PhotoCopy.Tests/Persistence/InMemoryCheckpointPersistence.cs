using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoCopy.Tests.Persistence;

/// <summary>
/// Interface for checkpoint persistence operations.
/// Abstracting this allows easy testing without real file system access.
/// </summary>
public interface ICheckpointPersistence
{
    /// <summary>
    /// Loads a checkpoint from the specified path.
    /// Returns null if no checkpoint exists.
    /// </summary>
    Task<Checkpoint?> LoadAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Saves a checkpoint to the specified path atomically.
    /// Uses write-to-temp-then-rename pattern for crash safety.
    /// </summary>
    Task SaveAsync(string path, Checkpoint checkpoint, CancellationToken ct = default);

    /// <summary>
    /// Checks if a checkpoint exists at the specified path.
    /// </summary>
    bool Exists(string path);

    /// <summary>
    /// Deletes the checkpoint at the specified path.
    /// </summary>
    void Delete(string path);
}

/// <summary>
/// In-memory checkpoint persistence for unit testing.
/// Tracks all save operations for verification.
/// </summary>
public class InMemoryCheckpointPersistence : ICheckpointPersistence
{
    private readonly Dictionary<string, Checkpoint> _checkpoints = new(System.StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>
    /// Number of times SaveAsync was called.
    /// </summary>
    public int SaveCount { get; private set; }

    /// <summary>
    /// Number of times LoadAsync was called.
    /// </summary>
    public int LoadCount { get; private set; }

    /// <summary>
    /// History of all checkpoints saved (for verifying checkpoint progression).
    /// </summary>
    public List<Checkpoint> SaveHistory { get; } = new();

    /// <summary>
    /// If set, LoadAsync will throw this exception.
    /// </summary>
    public System.Exception? LoadException { get; set; }

    /// <summary>
    /// If set, SaveAsync will throw this exception on the Nth save (1-based).
    /// </summary>
    public int? FailOnSaveNumber { get; set; }

    /// <summary>
    /// Delay to introduce for SaveAsync (simulating slow disk).
    /// </summary>
    public System.TimeSpan SaveDelay { get; set; } = System.TimeSpan.Zero;

    public Task<Checkpoint?> LoadAsync(string path, CancellationToken ct = default)
    {
        lock (_lock)
        {
            LoadCount++;

            if (LoadException != null)
            {
                throw LoadException;
            }

            _checkpoints.TryGetValue(NormalizePath(path), out var checkpoint);
            return Task.FromResult(checkpoint);
        }
    }

    public async Task SaveAsync(string path, Checkpoint checkpoint, CancellationToken ct = default)
    {
        if (SaveDelay > System.TimeSpan.Zero)
        {
            await Task.Delay(SaveDelay, ct);
        }

        lock (_lock)
        {
            SaveCount++;

            if (FailOnSaveNumber.HasValue && SaveCount == FailOnSaveNumber.Value)
            {
                throw new System.IO.IOException($"Simulated save failure on attempt {SaveCount}");
            }

            // Clone the checkpoint to preserve history (records are immutable but contain mutable lists)
            var clone = checkpoint with
            {
                ProcessedFiles = checkpoint.ProcessedFiles.ToList()
            };

            _checkpoints[NormalizePath(path)] = clone;
            SaveHistory.Add(clone);
        }
    }

    public bool Exists(string path)
    {
        lock (_lock)
        {
            return _checkpoints.ContainsKey(NormalizePath(path));
        }
    }

    public void Delete(string path)
    {
        lock (_lock)
        {
            _checkpoints.Remove(NormalizePath(path));
        }
    }

    /// <summary>
    /// Clears all stored checkpoints and resets counters.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _checkpoints.Clear();
            SaveHistory.Clear();
            SaveCount = 0;
            LoadCount = 0;
            LoadException = null;
            FailOnSaveNumber = null;
        }
    }

    /// <summary>
    /// Pre-loads a checkpoint (simulating existing checkpoint from previous session).
    /// </summary>
    public void Seed(string path, Checkpoint checkpoint)
    {
        lock (_lock)
        {
            _checkpoints[NormalizePath(path)] = checkpoint;
        }
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('/', '\\').TrimEnd('\\');
    }
}

/// <summary>
/// Checkpoint persistence that wraps another persistence and adds verification hooks.
/// </summary>
public class VerifyingCheckpointPersistence : ICheckpointPersistence
{
    private readonly ICheckpointPersistence _inner;
    private readonly List<(string Path, Checkpoint Checkpoint)> _savedCheckpoints = new();

    public VerifyingCheckpointPersistence(ICheckpointPersistence inner)
    {
        _inner = inner;
    }

    public IReadOnlyList<(string Path, Checkpoint Checkpoint)> SavedCheckpoints => _savedCheckpoints;

    public Task<Checkpoint?> LoadAsync(string path, CancellationToken ct = default)
        => _inner.LoadAsync(path, ct);

    public async Task SaveAsync(string path, Checkpoint checkpoint, CancellationToken ct = default)
    {
        await _inner.SaveAsync(path, checkpoint, ct);
        _savedCheckpoints.Add((path, checkpoint));
    }

    public bool Exists(string path) => _inner.Exists(path);
    public void Delete(string path) => _inner.Delete(path);

    /// <summary>
    /// Verifies that checkpoints were saved at expected intervals.
    /// </summary>
    public void VerifyCheckpointProgression()
    {
        if (_savedCheckpoints.Count < 2)
            return;

        for (var i = 1; i < _savedCheckpoints.Count; i++)
        {
            var prev = _savedCheckpoints[i - 1].Checkpoint;
            var curr = _savedCheckpoints[i].Checkpoint;

            // Progress should monotonically increase
            if (curr.ProcessedFiles.Count < prev.ProcessedFiles.Count)
            {
                throw new System.Exception(
                    $"Checkpoint regression: checkpoint {i} has {curr.ProcessedFiles.Count} files, " +
                    $"but previous had {prev.ProcessedFiles.Count}");
            }
        }
    }
}
