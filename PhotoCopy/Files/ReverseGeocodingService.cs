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

namespace PhotoCopy.Files;

public class ReverseGeocodingService : IReverseGeocodingService
{
    private readonly ILogger<ReverseGeocodingService> _logger;
    private readonly IOptions<Options> _options;
    private KdTree<float, LocationData> _tree;
    private bool _isInitialized;

    public ReverseGeocodingService(ILogger<ReverseGeocodingService> logger, IOptions<Options> options)
    {
        _logger = logger;
        _options = options;
        _tree = new KdTree<float, LocationData>(2, new FloatMath());
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized) return;

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

    private void LoadData(string filePath, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(filePath);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parts = line.Split('\t');
            if (parts.Length < 11) continue;

            // Parse coordinates
            if (!float.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var lat) ||
                !float.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out var lon))
            {
                continue;
            }

            var name = parts[1];
            var countryCode = parts[8];
            var admin1Code = parts[10]; // State/Province code

            var location = new LocationData(name, admin1Code, countryCode);
            _tree.Add(new[] { lat, lon }, location);
        }
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
}
