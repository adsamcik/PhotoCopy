using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PhotoCopy.Configuration;

namespace PhotoCopy.Files.Geo.Boundaries;

/// <summary>
/// Result of a country lookup operation.
/// </summary>
/// <param name="CountryCode">ISO 3166-1 alpha-2 country code, or null if not in any country (ocean, etc.).</param>
/// <param name="IsOcean">True if the point is in international waters.</param>
/// <param name="IsBorderArea">True if the point is in a cell that spans multiple countries.</param>
/// <param name="CandidateCountries">For border areas, the list of possible countries.</param>
public sealed record CountryLookupResult(
    string? CountryCode,
    bool IsOcean = false,
    bool IsBorderArea = false,
    string[]? CandidateCountries = null);

/// <summary>
/// Interface for country boundary lookup service.
/// </summary>
public interface IBoundaryService
{
    /// <summary>
    /// Gets whether the service has been initialized.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Initializes the boundary service by loading boundary data.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines which country a GPS coordinate is in.
    /// </summary>
    /// <param name="latitude">Latitude in degrees.</param>
    /// <param name="longitude">Longitude in degrees.</param>
    /// <returns>Country lookup result with country code and metadata.</returns>
    CountryLookupResult GetCountry(double latitude, double longitude);

    /// <summary>
    /// Tests if a specific point is within a specific country.
    /// </summary>
    /// <param name="latitude">Latitude in degrees.</param>
    /// <param name="longitude">Longitude in degrees.</param>
    /// <param name="countryCode">ISO 3166-1 alpha-2 country code.</param>
    /// <returns>True if the point is within the specified country.</returns>
    bool IsPointInCountry(double latitude, double longitude, string countryCode);

    /// <summary>
    /// Gets all countries that might contain a point (for border areas).
    /// </summary>
    /// <param name="latitude">Latitude in degrees.</param>
    /// <param name="longitude">Longitude in degrees.</param>
    /// <returns>Array of candidate country codes.</returns>
    string[] GetCandidateCountries(double latitude, double longitude);
}

/// <summary>
/// High-performance country boundary lookup service using geohash caching
/// and point-in-polygon testing.
/// 
/// Architecture:
/// 1. Geohash cache: For most lookups, the geohash cell is entirely within one country.
///    These are resolved in O(1) with a simple dictionary lookup.
/// 2. Border cells: For cells that span multiple countries, we perform point-in-polygon
///    tests against the candidate countries.
/// 3. Bounding box pre-filtering: Before expensive polygon tests, we reject countries
///    whose bounding box doesn't contain the point.
/// </summary>
public sealed class BoundaryIndex : IBoundaryService, IDisposable
{
    private readonly ILogger<BoundaryIndex> _logger;
    private readonly PhotoCopyConfig _config;
    private readonly object _lock = new();

    private Dictionary<string, CountryBoundary>? _countries;
    private Dictionary<string, string>? _geohashCache; // geohash -> country code (for single-country cells)
    private Dictionary<string, string[]>? _borderCells; // geohash -> candidate country codes
    private readonly ConcurrentDictionary<string, CountryLookupResult> _lookupCache = new();

    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Geohash precision for cell caching (4 = ~20-40km cells).
    /// </summary>
    public const int CachePrecision = 4;

    /// <summary>
    /// Maximum number of cached lookup results.
    /// </summary>
    public const int MaxLookupCacheSize = 10000;

    /// <summary>
    /// File extension for boundary data files.
    /// </summary>
    public const string BoundaryFileExtension = ".geobounds";

    /// <inheritdoc/>
    public bool IsInitialized => _initialized;

    public BoundaryIndex(ILogger<BoundaryIndex> logger, PhotoCopyConfig config)
    {
        _logger = logger;
        _config = config;
    }

    /// <inheritdoc/>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return Task.CompletedTask;

        return Task.Run(() => Initialize(), cancellationToken);
    }

    private void Initialize()
    {
        if (_initialized)
            return;

        lock (_lock)
        {
            if (_initialized)
                return;

            try
            {
                var boundaryPath = FindBoundaryFile();
                if (boundaryPath == null)
                {
                    _logger.LogWarning("Country boundary file (.geobounds) not found. Country filtering will be disabled.");
                    return;
                }

                _logger.LogInformation("Loading country boundaries from {Path}", boundaryPath);
                var startTime = DateTime.UtcNow;

                LoadBoundaryFile(boundaryPath);

                var loadTime = DateTime.UtcNow - startTime;
                _logger.LogInformation(
                    "Country boundaries loaded in {LoadTime:F2}s: {CountryCount} countries, {CacheSize} cached cells",
                    loadTime.TotalSeconds, _countries?.Count ?? 0, _geohashCache?.Count ?? 0);

                _initialized = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load country boundaries. Country filtering will be disabled.");
            }
        }
    }

    /// <inheritdoc/>
    public CountryLookupResult GetCountry(double latitude, double longitude)
    {
        if (!_initialized || _countries == null)
            return new CountryLookupResult(null);

        // Normalize coordinates
        latitude = PointInPolygon.ClampLatitude(latitude);
        longitude = PointInPolygon.NormalizeLongitude(longitude);

        // Check lookup cache first (for recent repeated lookups)
        string cacheKey = $"{latitude:F4},{longitude:F4}";
        if (_lookupCache.TryGetValue(cacheKey, out var cached))
            return cached;

        // Get geohash for this location
        string geohash = Geohash.Encode(latitude, longitude, CachePrecision);

        // Fast path: Check if this cell is entirely within one country
        if (_geohashCache != null && _geohashCache.TryGetValue(geohash, out var cachedCountry))
        {
            var result = new CountryLookupResult(cachedCountry);
            CacheLookupResult(cacheKey, result);
            return result;
        }

        // Check if this is a known border cell
        string[]? candidates = null;
        if (_borderCells != null && _borderCells.TryGetValue(geohash, out candidates))
        {
            // Perform point-in-polygon tests for candidate countries
            foreach (var countryCode in candidates)
            {
                if (_countries.TryGetValue(countryCode, out var boundary) &&
                    Boundaries.PointInPolygon.IsPointInCountry(latitude, longitude, boundary))
                {
                    var result = new CountryLookupResult(countryCode, IsBorderArea: true, CandidateCountries: candidates);
                    CacheLookupResult(cacheKey, result);
                    return result;
                }
            }
        }

        // Slow path: Check all countries (fallback for unknown cells)
        foreach (var (countryCode, boundary) in _countries)
        {
            if (Boundaries.PointInPolygon.IsPointInCountry(latitude, longitude, boundary))
            {
                var result = new CountryLookupResult(countryCode);
                CacheLookupResult(cacheKey, result);
                return result;
            }
        }

        // Not in any country (ocean, Antarctica, etc.)
        var oceanResult = new CountryLookupResult(null, IsOcean: true);
        CacheLookupResult(cacheKey, oceanResult);
        return oceanResult;
    }

    /// <inheritdoc/>
    public bool IsPointInCountry(double latitude, double longitude, string countryCode)
    {
        if (!_initialized || _countries == null)
            return false;

        if (!_countries.TryGetValue(countryCode, out var boundary))
            return false;

        latitude = PointInPolygon.ClampLatitude(latitude);
        longitude = PointInPolygon.NormalizeLongitude(longitude);

        return Boundaries.PointInPolygon.IsPointInCountry(latitude, longitude, boundary);
    }

    /// <inheritdoc/>
    public string[] GetCandidateCountries(double latitude, double longitude)
    {
        if (!_initialized || _countries == null)
            return Array.Empty<string>();

        latitude = PointInPolygon.ClampLatitude(latitude);
        longitude = PointInPolygon.NormalizeLongitude(longitude);

        string geohash = Geohash.Encode(latitude, longitude, CachePrecision);

        // Check border cells
        if (_borderCells != null && _borderCells.TryGetValue(geohash, out var candidates))
            return candidates;

        // Check single-country cache
        if (_geohashCache != null && _geohashCache.TryGetValue(geohash, out var country))
            return new[] { country };

        // Fallback: find all countries whose bounding box contains this point
        var result = new List<string>();
        foreach (var (code, boundary) in _countries)
        {
            if (boundary.BoundingBox.Contains(latitude, longitude))
                result.Add(code);
        }

        return result.ToArray();
    }

    private void CacheLookupResult(string key, CountryLookupResult result)
    {
        // Simple cache eviction: clear if too large
        if (_lookupCache.Count >= MaxLookupCacheSize)
            _lookupCache.Clear();

        _lookupCache.TryAdd(key, result);
    }

    private string? FindBoundaryFile()
    {
        var searchPaths = new[]
        {
            // Check configured path first
            string.IsNullOrEmpty(_config.GeonamesPath) ? null : 
                Path.Combine(Path.GetDirectoryName(_config.GeonamesPath) ?? "", "geo" + BoundaryFileExtension),
            
            // Check standard locations
            Path.Combine(AppContext.BaseDirectory, "data", "geo" + BoundaryFileExtension),
            Path.Combine(AppContext.BaseDirectory, "geo" + BoundaryFileExtension),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                ".photocopy", "geo" + BoundaryFileExtension),
        };

        foreach (var path in searchPaths)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return path;
        }

        return null;
    }

    private void LoadBoundaryFile(string path)
    {
        try
        {
            var data = BoundaryFileFormat.Read(path);

            _countries = new Dictionary<string, CountryBoundary>(StringComparer.OrdinalIgnoreCase);
            foreach (var country in data.Countries)
            {
                _countries[country.CountryCode] = country;
            }

            _geohashCache = new Dictionary<string, string>(data.GeohashCache, StringComparer.OrdinalIgnoreCase);
            _borderCells = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in data.BorderCells)
            {
                _borderCells[key] = value;
            }

            _logger.LogDebug("Loaded {CountryCount} countries, {CacheCount} cached cells, {BorderCount} border cells",
                _countries.Count, _geohashCache.Count, _borderCells.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse boundary file {Path}", path);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _countries?.Clear();
        _geohashCache?.Clear();
        _borderCells?.Clear();
        _lookupCache.Clear();
    }
}
