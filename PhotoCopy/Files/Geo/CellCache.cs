using System;
using System.Collections.Generic;
using System.Threading;

namespace PhotoCopy.Files.Geo;

/// <summary>
/// LRU (Least Recently Used) cache for loaded geo-cells.
/// Thread-safe with configurable memory limit.
/// </summary>
public sealed class CellCache : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _cache;
    private readonly LinkedList<CacheEntry> _lruList;
    private readonly long _maxMemoryBytes;
    private long _currentMemoryBytes;
    private bool _disposed;

    /// <summary>
    /// Number of cells currently in cache.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock) return _cache.Count;
        }
    }

    /// <summary>
    /// Current memory usage in bytes.
    /// </summary>
    public long CurrentMemoryBytes
    {
        get
        {
            lock (_lock) return _currentMemoryBytes;
        }
    }

    /// <summary>
    /// Maximum memory limit in bytes.
    /// </summary>
    public long MaxMemoryBytes => _maxMemoryBytes;

    /// <summary>
    /// Cache hit count (for statistics).
    /// </summary>
    public long HitCount => _hitCount;
    private long _hitCount;

    /// <summary>
    /// Cache miss count (for statistics).
    /// </summary>
    public long MissCount => _missCount;
    private long _missCount;

    /// <summary>
    /// Number of evictions performed.
    /// </summary>
    public long EvictionCount => _evictionCount;
    private long _evictionCount;

    /// <summary>
    /// Creates a new cell cache with specified memory limit.
    /// </summary>
    /// <param name="maxMemoryBytes">Maximum memory in bytes (default 100MB).</param>
    public CellCache(long maxMemoryBytes = 100 * 1024 * 1024)
    {
        _maxMemoryBytes = maxMemoryBytes;
        _cache = new Dictionary<string, LinkedListNode<CacheEntry>>(256);
        _lruList = new LinkedList<CacheEntry>();
    }

    /// <summary>
    /// Tries to get a cell from the cache.
    /// </summary>
    /// <param name="geohash">Geohash key.</param>
    /// <param name="cell">The cached cell if found.</param>
    /// <returns>True if the cell was in cache.</returns>
    public bool TryGet(string geohash, out GeoCell? cell)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(geohash, out var node))
            {
                // Move to front (most recently used)
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                cell = node.Value.Cell;
                Interlocked.Increment(ref _hitCount);
                return true;
            }

            cell = null;
            Interlocked.Increment(ref _missCount);
            return false;
        }
    }

    /// <summary>
    /// Adds or updates a cell in the cache.
    /// May evict old entries if memory limit is exceeded.
    /// </summary>
    /// <param name="geohash">Geohash key.</param>
    /// <param name="cell">The cell to cache.</param>
    public void Put(string geohash, GeoCell cell)
    {
        lock (_lock)
        {
            // If already exists, update it
            if (_cache.TryGetValue(geohash, out var existingNode))
            {
                _currentMemoryBytes -= existingNode.Value.Cell.EstimatedMemoryBytes;
                _lruList.Remove(existingNode);
            }

            // Create new entry
            var entry = new CacheEntry { Geohash = geohash, Cell = cell };
            var newNode = _lruList.AddFirst(entry);
            _cache[geohash] = newNode;
            _currentMemoryBytes += cell.EstimatedMemoryBytes;

            // Evict if over memory limit
            EvictIfNeeded();
        }
    }

    /// <summary>
    /// Removes a cell from the cache.
    /// </summary>
    /// <param name="geohash">Geohash key.</param>
    /// <returns>True if the cell was removed.</returns>
    public bool Remove(string geohash)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(geohash, out var node))
            {
                _currentMemoryBytes -= node.Value.Cell.EstimatedMemoryBytes;
                _lruList.Remove(node);
                _cache.Remove(geohash);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Clears all entries from the cache.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
            _lruList.Clear();
            _currentMemoryBytes = 0;
        }
    }

    /// <summary>
    /// Gets all cached geohashes (for debugging).
    /// </summary>
    public IEnumerable<string> GetCachedGeohashes()
    {
        lock (_lock)
        {
            return new List<string>(_cache.Keys);
        }
    }

    /// <summary>
    /// Gets cache statistics as a formatted string.
    /// </summary>
    public string GetStatistics()
    {
        lock (_lock)
        {
            long totalRequests = _hitCount + _missCount;
            double hitRate = totalRequests > 0 ? (double)_hitCount / totalRequests * 100 : 0;
            return $"CellCache: {_cache.Count} cells, {_currentMemoryBytes / 1024.0 / 1024.0:F2}MB / {_maxMemoryBytes / 1024.0 / 1024.0:F2}MB, " +
                   $"Hit rate: {hitRate:F1}% ({_hitCount} hits, {_missCount} misses), Evictions: {_evictionCount}";
        }
    }

    private void EvictIfNeeded()
    {
        // Must be called while holding _lock
        while (_currentMemoryBytes > _maxMemoryBytes && _lruList.Last != null)
        {
            var lruNode = _lruList.Last;
            var entry = lruNode.Value;
            _currentMemoryBytes -= entry.Cell.EstimatedMemoryBytes;
            _lruList.RemoveLast();
            _cache.Remove(entry.Geohash);
            Interlocked.Increment(ref _evictionCount);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
    }

    private sealed class CacheEntry
    {
        public required string Geohash { get; init; }
        public required GeoCell Cell { get; init; }
    }
}
