using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KdTree;
using KdTree.Math;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;

namespace PhotoCopy.Files;

public class ReverseGeocodingService : IReverseGeocodingService, IDisposable
{
    private readonly ILogger<ReverseGeocodingService> _logger;
    private readonly IOptions<PhotoCopyConfig> _options;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private KdTree<float, LocationData> _tree;
    private volatile bool _isInitialized;

    public ReverseGeocodingService(ILogger<ReverseGeocodingService> logger, IOptions<PhotoCopyConfig> options)
    {
        _logger = logger;
        _options = options;
        _tree = new KdTree<float, LocationData>(2, new FloatMath());
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized) return;  // Fast path - volatile read

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized) return;  // Double-check after acquiring lock

            var filePath = _options.Value.GeonamesPath;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                _logger.LogWarning("GeoNames file not found or not specified. Reverse geocoding will be disabled.");
                return;
            }

            _logger.LogInformation("Loading GeoNames data from {FilePath}...", filePath);

            try
            {
                await Task.Run(() => LoadData(filePath, cancellationToken), cancellationToken);
                _isInitialized = true;
                _logger.LogInformation("GeoNames data loaded. Tree contains {Count} nodes.", _tree.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load GeoNames data.");
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private void LoadData(string filePath, CancellationToken cancellationToken)
    {
        var minPopulation = _options.Value.MinimumPopulation ?? 0;
        var linesRead = 0;
        var locationsAdded = 0;
        var lastLogTime = DateTime.UtcNow;
        
        using var reader = new StreamReader(filePath);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            linesRead++;
            
            // Log progress every 5 seconds
            var now = DateTime.UtcNow;
            if ((now - lastLogTime).TotalSeconds >= 5)
            {
                _logger.LogInformation("GeoNames loading progress: {LinesRead} lines read, {LocationsAdded} locations added...", linesRead, locationsAdded);
                lastLogTime = now;
            }

            var parts = line.Split('\t');
            if (parts.Length < 15) continue; // Need at least 15 columns for population

            // Parse coordinates
            if (!float.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var lat) ||
                !float.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out var lon))
            {
                continue;
            }

            // Parse population (index 14)
            long? population = null;
            if (parts.Length > 14 && long.TryParse(parts[14], out var pop))
            {
                population = pop;
                
                // Apply population filter
                if (minPopulation > 0 && pop < minPopulation)
                {
                    continue;
                }
            }

            var name = parts[1];
            var countryCode = parts[8];
            var admin1Code = parts[10]; // State/Province code
            // County/District code - convert empty string to null for consistency
            var admin2Value = parts.Length > 11 ? parts[11] : null;
            var admin2Code = string.IsNullOrWhiteSpace(admin2Value) ? null : admin2Value;

            var location = new LocationData(name, admin2Code, admin1Code, countryCode, population);
            _tree.Add(new[] { lat, lon }, location);
            locationsAdded++;
        }
        
        _logger.LogInformation("GeoNames loading complete: {LinesRead} lines read, {LocationsAdded} locations added.", linesRead, locationsAdded);
    }

    public LocationData? ReverseGeocode(double latitude, double longitude)
    {
        if (!_isInitialized || _tree.Count == 0) return null;

        // Find nearest neighbor
        var nodes = _tree.GetNearestNeighbours(new[] { (float)latitude, (float)longitude }, 1);
        
        if (nodes != null && nodes.Length > 0)
        {
            return nodes[0].Value;
        }

        return null;
    }

    /// <summary>
    /// Disposes the semaphore used for thread-safe initialization.
    /// </summary>
    public void Dispose()
    {
        _initLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
