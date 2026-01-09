using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;

namespace PhotoCopy.Files.Geo;

/// <summary>
/// High-performance reverse geocoding service using tiered LOD-style loading.
/// 
/// Architecture:
/// 1. Small always-in-memory spatial index (~3MB) for O(1) cell lookup
/// 2. LRU cache of loaded cells (configurable memory limit)
/// 3. Memory-mapped data file for fast on-demand cell loading
/// 4. Brotli compression for compact storage
/// 
/// Performance characteristics:
/// - First lookup in a region: ~1ms (cell load + search)
/// - Subsequent lookups in same region: ~0.01ms (cache hit)
/// - Memory usage: ~3MB base + cached cells (configurable)
/// </summary>
public sealed class TieredGeocodingService : IReverseGeocodingService, IDisposable
{
    private readonly ILogger<TieredGeocodingService> _logger;
    private readonly PhotoCopyConfig _config;
    private readonly object _lock = new();

    private SpatialIndex? _index;
    private CellLoader? _loader;
    private CellCache? _cache;
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Default memory limit for cell cache (100MB).
    /// </summary>
    public const long DefaultCacheMemoryBytes = 100 * 1024 * 1024;

    /// <summary>
    /// Default maximum distance in km for a match to be considered valid.
    /// </summary>
    public const double DefaultMaxDistanceKm = 50.0;

    /// <summary>
    /// Default priority threshold in km - within this distance, prefer populated places over parks.
    /// </summary>
    public const double DefaultPriorityThresholdKm = 15.0;

    /// <summary>
    /// Gets whether the service has been initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// Gets cache statistics (null if not initialized).
    /// </summary>
    public string? CacheStatistics => _cache?.GetStatistics();

    public TieredGeocodingService(ILogger<TieredGeocodingService> logger, PhotoCopyConfig config)
    {
        _logger = logger;
        _config = config;
    }

    /// <summary>
    /// Initializes the geocoding service by loading index files.
    /// </summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return Task.CompletedTask;

        lock (_lock)
        {
            if (_initialized)
                return Task.CompletedTask;

            try
            {
                var (indexPath, dataPath) = FindIndexFiles();
                if (indexPath == null || dataPath == null)
                {
                    _logger.LogWarning("Geo-index files not found. Reverse geocoding will be disabled. " +
                        "Run the GeoIndexGenerator tool to create index files.");
                    return Task.CompletedTask;
                }

                _logger.LogInformation("Loading geo-index from {IndexPath}", indexPath);
                var startTime = DateTime.UtcNow;

                _index = SpatialIndex.Load(indexPath);
                _loader = CellLoader.Open(dataPath);
                _cache = new CellCache(DefaultCacheMemoryBytes);
                _initialized = true;

                var loadTime = DateTime.UtcNow - startTime;
                _logger.LogInformation(
                    "Geo-index loaded in {LoadTime:F2}s: {CellCount} cells, {LocationCount} locations, " +
                    "data size {DataSize:F2}MB, index size ~{IndexSize:F2}MB",
                    loadTime.TotalSeconds,
                    _index.CellCount,
                    _index.TotalLocationCount,
                    _index.DataFileSize / 1024.0 / 1024.0,
                    _index.EstimatedMemoryBytes / 1024.0 / 1024.0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load geo-index. Reverse geocoding will be disabled.");
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs reverse geocoding for the given coordinates.
    /// </summary>
    /// <param name="latitude">Latitude in degrees.</param>
    /// <param name="longitude">Longitude in degrees.</param>
    /// <returns>Location data or null if no match found.</returns>
    public LocationData? ReverseGeocode(double latitude, double longitude)
    {
        if (!_initialized || _index == null || _loader == null || _cache == null)
            return null;

        try
        {
            var result = FindNearest(latitude, longitude, DefaultMaxDistanceKm);
            if (result == null)
                return null;

            return new LocationData(
                City: result.Location.City,
                County: null, // Not stored in our format currently
                State: string.IsNullOrEmpty(result.Location.State) ? null : result.Location.State,
                Country: result.Location.Country,
                Population: result.Location.PopulationK > 0 ? result.Location.PopulationK * 1000L : null
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during reverse geocoding for ({Lat}, {Lon})", latitude, longitude);
            return null;
        }
    }

    /// <summary>
    /// Finds the nearest location to the specified coordinates.
    /// </summary>
    /// <param name="latitude">Latitude in degrees.</param>
    /// <param name="longitude">Longitude in degrees.</param>
    /// <param name="maxDistanceKm">Maximum distance in km.</param>
    /// <returns>Lookup result or null if no match within distance.</returns>
    public GeoLookupResult? FindNearest(double latitude, double longitude, double maxDistanceKm = DefaultMaxDistanceKm)
    {
        if (_index == null || _loader == null || _cache == null)
            return null;

        // Encode coordinates to geohash at our precision level
        string geohash = Geohash.Encode(latitude, longitude, GeoIndexFormat.DefaultPrecision);

        // Collect all candidates from the cell and its neighbors
        List<(LocationEntryMemory Location, string CellHash, double Distance)> candidates = [];

        foreach (var (cellHash, entry) in _index.GetCellAndNeighbors(geohash))
        {
            var cell = GetOrLoadCell(cellHash, entry);
            if (cell == null)
                continue;

            // Get the best from this cell (already applies priority logic within cell)
            var nearest = cell.FindNearest(latitude, longitude, maxDistanceKm, DefaultPriorityThresholdKm);
            if (nearest != null)
            {
                double distance = nearest.DistanceKm(latitude, longitude);
                candidates.Add((nearest, cellHash, distance));
            }
        }

        if (candidates.Count == 0)
            return null;

        // Apply the same priority logic across all candidates
        var best = SelectBestCandidate(candidates, DefaultPriorityThresholdKm);
        
        return new GeoLookupResult
        {
            Location = best.Location,
            DistanceKm = best.Distance,
            CellGeohash = best.CellHash,
            IsFromNeighborCell = best.CellHash != geohash
        };
    }

    /// <summary>
    /// Selects the best candidate using priority logic:
    /// - Within priority threshold: prefer better feature class (P > A > L), then closer
    /// - Beyond threshold: prefer closer
    /// </summary>
    private static (LocationEntryMemory Location, string CellHash, double Distance) SelectBestCandidate(
        List<(LocationEntryMemory Location, string CellHash, double Distance)> candidates,
        double priorityThresholdKm)
    {
        var best = candidates[0];
        int bestPriority = GeoFeatureClass.GetPriority(best.Location.FeatureClass);

        for (int i = 1; i < candidates.Count; i++)
        {
            var current = candidates[i];
            int currentPriority = GeoFeatureClass.GetPriority(current.Location.FeatureClass);

            bool isBetter;
            if (current.Distance <= priorityThresholdKm && best.Distance <= priorityThresholdKm)
            {
                // Both within threshold: prefer better priority, then closer
                isBetter = currentPriority < bestPriority ||
                           (currentPriority == bestPriority && current.Distance < best.Distance);
            }
            else if (current.Distance <= priorityThresholdKm)
            {
                // Current is within threshold, best isn't: prefer current
                isBetter = true;
            }
            else if (best.Distance <= priorityThresholdKm)
            {
                // Best is within threshold, current isn't: keep best
                isBetter = false;
            }
            else
            {
                // Both outside threshold: prefer closer
                isBetter = current.Distance < best.Distance;
            }

            if (isBetter)
            {
                best = current;
                bestPriority = currentPriority;
            }
        }

        return best;
    }

    /// <summary>
    /// Gets or loads a cell, using the cache.
    /// </summary>
    private GeoCell? GetOrLoadCell(string geohash, CellIndexEntry entry)
    {
        if (_cache!.TryGet(geohash, out var cachedCell))
            return cachedCell;

        // Load from disk
        try
        {
            var cell = _loader!.LoadCell(entry, geohash);
            _cache.Put(geohash, cell);
            return cell;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load cell {Geohash}", geohash);
            return null;
        }
    }

    /// <summary>
    /// Finds the geo-index files in known locations.
    /// Returns (indexPath, dataPath).
    /// </summary>
    private (string? IndexPath, string? DataPath) FindIndexFiles()
    {
        // Check locations in priority order:
        // 1. Configured path (if any)
        // 2. Application directory
        // 3. Application data subdirectory
        // 4. User profile directory

        var searchPaths = new[]
        {
            Path.GetDirectoryName(_config.GeonamesPath), // Same directory as GeoNames config
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "data"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".photocopy"),
        };

        foreach (var basePath in searchPaths)
        {
            if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath))
                continue;

            string indexPath = Path.Combine(basePath, "geo.geoindex");
            string dataPath = Path.Combine(basePath, "geo.geodata");
            
            if (File.Exists(indexPath) && File.Exists(dataPath))
            {
                return (indexPath, dataPath);
            }
        }

        return (null, null);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _cache?.Dispose();
        _loader?.Dispose();
        _index?.Dispose();

        if (_cache != null)
        {
            _logger.LogDebug("TieredGeocodingService disposed. Final stats: {Stats}", _cache.GetStatistics());
        }
    }
}
