using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Files.Geo.Boundaries;

namespace PhotoCopy.Files.Geo;

/// <summary>
/// Enhanced geocoding service that uses country boundary detection to improve
/// accuracy near international borders.
/// 
/// When a photo is taken near a border (e.g., Bratislava suburbs), the nearest
/// city by distance might be in the wrong country (e.g., Vienna). This service
/// first determines which country the GPS point is actually in, then filters
/// the nearest-neighbor search to prefer cities in that country.
/// 
/// Fallback behavior:
/// - If boundary data is unavailable, behaves like standard geocoding
/// - If no city found in detected country, falls back to nearest overall
/// - If point is in ocean/international waters, uses nearest coastal city
/// </summary>
public sealed class BoundaryAwareGeocodingService : IReverseGeocodingService, IDisposable
{
    private readonly ILogger<BoundaryAwareGeocodingService> _logger;
    private readonly PhotoCopyConfig _config;
    private readonly TieredGeocodingService _geocodingService;
    private readonly BoundaryIndex _boundaryIndex;

    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Gets whether the service has been initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// Gets whether boundary-aware filtering is active.
    /// </summary>
    public bool IsBoundaryFilteringEnabled => _boundaryIndex.IsInitialized;

    /// <summary>
    /// Gets cache statistics from the underlying geocoding service.
    /// </summary>
    public string? CacheStatistics => _geocodingService.CacheStatistics;

    public BoundaryAwareGeocodingService(
        ILogger<BoundaryAwareGeocodingService> logger,
        ILogger<TieredGeocodingService> geocodingLogger,
        ILogger<BoundaryIndex> boundaryLogger,
        PhotoCopyConfig config)
    {
        _logger = logger;
        _config = config;
        _geocodingService = new TieredGeocodingService(geocodingLogger, config);
        _boundaryIndex = new BoundaryIndex(boundaryLogger, config);
    }

    /// <summary>
    /// Initializes both the geocoding service and boundary index.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        // Initialize both services in parallel
        await Task.WhenAll(
            _geocodingService.InitializeAsync(cancellationToken),
            _boundaryIndex.InitializeAsync(cancellationToken));

        _initialized = _geocodingService.IsInitialized;

        if (_boundaryIndex.IsInitialized)
        {
            _logger.LogInformation("Boundary-aware geocoding enabled");
        }
        else
        {
            _logger.LogInformation("Boundary data not available - using distance-only geocoding");
        }
    }

    /// <summary>
    /// Performs reverse geocoding with optional country boundary filtering.
    /// </summary>
    public LocationData? ReverseGeocode(double latitude, double longitude)
    {
        if (!_initialized || !_geocodingService.IsInitialized)
            return null;

        // If boundary filtering is not available, use standard geocoding
        if (!_boundaryIndex.IsInitialized)
        {
            return _geocodingService.ReverseGeocode(latitude, longitude);
        }

        try
        {
            // Step 1: Determine which country this point is in
            var countryResult = _boundaryIndex.GetCountry(latitude, longitude);

            // Step 2: Find nearest locations with country preference
            return FindWithCountryPreference(latitude, longitude, countryResult);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error in boundary-aware geocoding for ({Lat}, {Lon}), falling back",
                latitude, longitude);
            return _geocodingService.ReverseGeocode(latitude, longitude);
        }
    }

    private LocationData? FindWithCountryPreference(
        double latitude, 
        double longitude, 
        CountryLookupResult countryResult)
    {
        // If we don't know the country (ocean, etc.), use standard geocoding
        if (countryResult.CountryCode == null)
        {
            return _geocodingService.ReverseGeocode(latitude, longitude);
        }

        // Find nearest place with country filter
        var districtResult = FindNearestInCountry(latitude, longitude, countryResult.CountryCode, citiesOnly: false);
        var cityResult = FindNearestInCountry(latitude, longitude, countryResult.CountryCode, citiesOnly: true);

        // If we found something in the correct country, use it
        if (districtResult != null || cityResult != null)
        {
            return new LocationData(
                District: districtResult?.Location.Name ?? cityResult?.Location.Name ?? string.Empty,
                City: cityResult?.Location.Name,
                County: null,
                State: districtResult?.Location.State ?? cityResult?.Location.State,
                Country: countryResult.CountryCode,
                Population: null);
        }

        // Fallback: No location found in the detected country
        // This can happen at borders where the city center is across the border
        // but the photo was taken on this side
        _logger.LogDebug(
            "No location found in country {Country} for ({Lat}, {Lon}), using nearest overall",
            countryResult.CountryCode, latitude, longitude);

        var fallback = _geocodingService.ReverseGeocode(latitude, longitude);

        // If fallback found something and we're in a border area, log for debugging
        if (fallback != null && countryResult.IsBorderArea && 
            !string.Equals(fallback.Country, countryResult.CountryCode, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Border area: Point in {DetectedCountry} but nearest city {City} is in {CityCountry}",
                countryResult.CountryCode, fallback.City ?? fallback.District, fallback.Country);
        }

        return fallback;
    }

    private GeoLookupResult? FindNearestInCountry(
        double latitude, 
        double longitude, 
        string countryCode, 
        bool citiesOnly)
    {
        // Use the country filter parameter to only search in the target country
        return _geocodingService.FindNearest(
            latitude, 
            longitude, 
            TieredGeocodingService.DefaultMaxDistanceKm, 
            citiesOnly,
            countryFilter: countryCode);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _geocodingService.Dispose();
        _boundaryIndex.Dispose();
    }
}
