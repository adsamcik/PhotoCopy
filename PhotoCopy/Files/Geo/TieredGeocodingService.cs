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
                _loader = CellLoader.Open(dataPath, _index.CountryNames);
                _cache = new CellCache(DefaultCacheMemoryBytes);
                _initialized = true;

                var loadTime = DateTime.UtcNow - startTime;
                _logger.LogInformation(
                    "Geo-index loaded in {LoadTime:F2}s: {CellCount} cells, {LocationCount} locations, " +
                    "data size {DataSize:F2}MB, index size ~{IndexSize:F2}MB, {CountryCount} countries",
                    loadTime.TotalSeconds,
                    _index.CellCount,
                    _index.TotalLocationCount,
                    _index.DataFileSize / 1024.0 / 1024.0,
                    _index.EstimatedMemoryBytes / 1024.0 / 1024.0,
                    _index.CountryNames.Count);
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
            // Find nearest place of any type (district, village, town, city, etc.)
            var nearestResult = FindNearest(latitude, longitude, DefaultMaxDistanceKm, citiesOnly: false);
            
            // Find nearest city (PlaceType >= Town)
            var cityResult = FindNearest(latitude, longitude, DefaultMaxDistanceKm, citiesOnly: true);

            if (nearestResult == null && cityResult == null)
                return null;

            return new LocationData(
                District: nearestResult?.Location.Name ?? cityResult?.Location.Name ?? string.Empty,
                City: cityResult?.Location.Name,
                County: null, // Not stored in our format
                State: nearestResult?.Location.State ?? cityResult?.Location.State,
                Country: nearestResult?.Location.Country ?? cityResult?.Location.Country ?? string.Empty,
                Population: null // Population not stored in new format
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
    /// <param name="citiesOnly">If true, only consider city-level places (PlaceType >= Town).</param>
    /// <param name="countryFilter">If specified, only consider places in this country (ISO 3166-1 alpha-2).</param>
    /// <returns>Lookup result or null if no match within distance.</returns>
    public GeoLookupResult? FindNearest(double latitude, double longitude, double maxDistanceKm = DefaultMaxDistanceKm, bool citiesOnly = false, string? countryFilter = null)
    {
        if (_index == null || _loader == null || _cache == null)
            return null;

        // Encode coordinates to geohash at our precision level
        string geohash = Geohash.Encode(latitude, longitude, GeoIndexFormat.DefaultPrecision);

        // Collect all candidates from the cell and its neighbors
        LocationEntry? best = null;
        double bestDistance = maxDistanceKm;
        string? bestCellHash = null;

        foreach (var (cellHash, entry) in _index.GetCellAndNeighbors(geohash))
        {
            var cell = GetOrLoadCell(cellHash, entry);
            if (cell == null)
                continue;

            // Use the cell's efficient FindNearest with citiesOnly and country filter
            var match = cell.FindNearest(latitude, longitude, bestDistance, citiesOnly, countryFilter);
            if (match != null)
            {
                double distance = match.DistanceKm(latitude, longitude);
                if (distance < bestDistance)
                {
                    best = match;
                    bestDistance = distance;
                    bestCellHash = cellHash;
                }
            }
        }

        if (best == null || bestCellHash == null)
            return null;

        return new GeoLookupResult
        {
            Location = best,
            DistanceKm = bestDistance,
            CellGeohash = bestCellHash,
            IsFromNeighborCell = bestCellHash != geohash
        };
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
