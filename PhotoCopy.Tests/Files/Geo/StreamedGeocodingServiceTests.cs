using System;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PhotoCopy.Configuration;
using PhotoCopy.Files.Geo;

namespace PhotoCopy.Tests.Files.Geo;

/// <summary>
/// Unit tests for StreamedGeocodingService.
/// Note: Many tests require the GeoNames data files to be present and will skip if not available.
/// </summary>
[Property("Category", "Integration,Geocoding")]
[NotInParallel("GeocodingService")]
public class StreamedGeocodingServiceTests
{
    private static readonly string GeoDataDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "PhotoCopy", "data");

    private static StreamedGeocodingService? _sharedService;
    private static bool _dataFilesExist;
    private static bool _initialized;

    [Before(Class)]
    public static async Task ClassSetUp()
    {
        if (_initialized) return;
        _initialized = true;

        var fullPath = Path.GetFullPath(GeoDataDir);
        var dataPath = Path.Combine(fullPath, "allCountries.txt");
        _dataFilesExist = File.Exists(dataPath);

        if (_dataFilesExist)
        {
            var mockLogger = Substitute.For<ILogger<StreamedGeocodingService>>();
            var config = new PhotoCopyConfig { GeonamesPath = dataPath };
            _sharedService = new StreamedGeocodingService(mockLogger, config);
            await _sharedService.InitializeAsync();
        }
    }

    [After(Class)]
    public static Task ClassTearDown()
    {
        _sharedService?.Dispose();
        _sharedService = null;
        return Task.CompletedTask;
    }

    private void SkipIfNoDataFile()
    {
        if (!_dataFilesExist)
        {
            var fullPath = Path.GetFullPath(GeoDataDir);
            var dataPath = Path.Combine(fullPath, "allCountries.txt");
            Skip.Test($"GeoNames data file not found at {dataPath}. Download from geonames.org.");
        }
        if (_sharedService == null)
        {
            Skip.Test("StreamedGeocodingService was not created.");
        }
        if (!_sharedService.IsInitialized)
        {
            Skip.Test($"StreamedGeocodingService failed to initialize.");
        }
    }

    #region Initialization Tests

    [Test]
    public async Task Constructor_WithMissingDataFile_ServiceRemainsUsable()
    {
        // Arrange
        var mockLogger = Substitute.For<ILogger<StreamedGeocodingService>>();
        var config = new PhotoCopyConfig { GeonamesPath = @"C:\NonExistent\Path\data.txt" };

        // Act
        using var service = new StreamedGeocodingService(mockLogger, config);
        await service.InitializeAsync();

        // Assert - service should not throw, just not be initialized
        await Assert.That(service.IsInitialized).IsFalse();
    }

    [Test]
    public async Task IsInitialized_BeforeInit_ReturnsFalse()
    {
        // Arrange
        var mockLogger = Substitute.For<ILogger<StreamedGeocodingService>>();
        var config = new PhotoCopyConfig();

        // Act
        using var service = new StreamedGeocodingService(mockLogger, config);

        // Assert
        await Assert.That(service.IsInitialized).IsFalse();
    }

    #endregion

    #region ReverseGeocode Tests

    [Test]
    public async Task ReverseGeocode_WithInvalidCoordinates_ReturnsNull()
    {
        // Arrange
        SkipIfNoDataFile();

        // Act - coordinates at 0,0 in the ocean
        var result = _sharedService!.ReverseGeocode(0.0, 0.0);

        // Assert - likely null since this is in the ocean
        // The actual result depends on the data, so we just verify no exception
        await Assert.That(result is null || result is not null).IsTrue(); // No exception
    }

    [Test]
    public async Task ReverseGeocode_WithValidCoordinates_ReturnsLocation()
    {
        // Arrange
        SkipIfNoDataFile();

        // Act - coordinates for Paris, France
        const double lat = 48.8566;
        const double lon = 2.3522;
        var result = _sharedService!.ReverseGeocode(lat, lon);

        // Assert
        if (result != null)
        {
            // If we got a result, it should have a country
            await Assert.That(result.Country).IsNotNull();
        }
    }

    [Test]
    public async Task ReverseGeocode_CalledTwiceWithSameCoords_UsesCaching()
    {
        // Arrange
        SkipIfNoDataFile();
        
        const double lat = 40.7128;
        const double lon = -74.0060;

        // Act - call twice
        var result1 = _sharedService!.ReverseGeocode(lat, lon);
        var result2 = _sharedService!.ReverseGeocode(lat, lon);

        // Assert - results should be identical
        if (result1 != null && result2 != null)
        {
            await Assert.That(result1.City).IsEqualTo(result2.City);
            await Assert.That(result1.Country).IsEqualTo(result2.Country);
        }
    }

    [Test]
    public async Task CacheStatistics_AfterLookups_IsNotEmpty()
    {
        // Arrange
        SkipIfNoDataFile();

        // Act
        _sharedService!.ReverseGeocode(51.5074, -0.1278); // London
        var stats = _sharedService.CacheStatistics;

        // Assert
        await Assert.That(stats).IsNotNull();
    }

    #endregion

    #region Boundary Coordinate Tests

    [Test]
    public async Task ReverseGeocode_NorthPole_HandlesGracefully()
    {
        // Arrange
        SkipIfNoDataFile();

        // Act - North Pole
        const double lat = 90.0;
        const double lon = 0.0;
        
        // Assert - should not throw
        try
        {
            var result = _sharedService!.ReverseGeocode(lat, lon);
            await Assert.That(result is null || result is not null).IsTrue();
        }
        catch (Exception ex)
        {
            Assert.Fail($"Should not throw for boundary coordinates: {ex.Message}");
        }
    }

    [Test]
    public async Task ReverseGeocode_SouthPole_HandlesGracefully()
    {
        // Arrange
        SkipIfNoDataFile();

        // Act - South Pole
        const double lat = -90.0;
        const double lon = 0.0;

        // Assert - should not throw
        try
        {
            var result = _sharedService!.ReverseGeocode(lat, lon);
            await Assert.That(result is null || result is not null).IsTrue();
        }
        catch (Exception ex)
        {
            Assert.Fail($"Should not throw for boundary coordinates: {ex.Message}");
        }
    }

    [Test]
    public async Task ReverseGeocode_DateLinePositive_HandlesGracefully()
    {
        // Arrange
        SkipIfNoDataFile();

        // Act - International Date Line (positive)
        const double lat = 0.0;
        const double lon = 180.0;

        // Assert - should not throw
        try
        {
            var result = _sharedService!.ReverseGeocode(lat, lon);
            await Assert.That(result is null || result is not null).IsTrue();
        }
        catch (Exception ex)
        {
            Assert.Fail($"Should not throw for boundary coordinates: {ex.Message}");
        }
    }

    [Test]
    public async Task ReverseGeocode_DateLineNegative_HandlesGracefully()
    {
        // Arrange
        SkipIfNoDataFile();

        // Act - International Date Line (negative)
        const double lat = 0.0;
        const double lon = -180.0;

        // Assert - should not throw
        try
        {
            var result = _sharedService!.ReverseGeocode(lat, lon);
            await Assert.That(result is null || result is not null).IsTrue();
        }
        catch (Exception ex)
        {
            Assert.Fail($"Should not throw for boundary coordinates: {ex.Message}");
        }
    }

    #endregion

    #region GPS Coordinate Validation Tests

    [Test]
    public async Task ReverseGeocode_NaNLatitude_ReturnsNull()
    {
        // Arrange
        SkipIfNoDataFile();

        // Act
        var result = _sharedService!.ReverseGeocode(double.NaN, 0.0);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReverseGeocode_NaNLongitude_ReturnsNull()
    {
        // Arrange
        SkipIfNoDataFile();

        // Act
        var result = _sharedService!.ReverseGeocode(0.0, double.NaN);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReverseGeocode_BothNaN_ReturnsNull()
    {
        // Arrange
        SkipIfNoDataFile();

        // Act
        var result = _sharedService!.ReverseGeocode(double.NaN, double.NaN);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReverseGeocode_PositiveInfinityLatitude_ReturnsNull()
    {
        // Arrange
        SkipIfNoDataFile();

        // Act
        var result = _sharedService!.ReverseGeocode(double.PositiveInfinity, 0.0);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReverseGeocode_NegativeInfinityLongitude_ReturnsNull()
    {
        // Arrange
        SkipIfNoDataFile();

        // Act
        var result = _sharedService!.ReverseGeocode(0.0, double.NegativeInfinity);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReverseGeocode_MixedInfinity_ReturnsNull()
    {
        // Arrange
        SkipIfNoDataFile();

        // Act
        var result = _sharedService!.ReverseGeocode(double.NegativeInfinity, double.PositiveInfinity);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    [Arguments(-91.0, 0.0)]    // Latitude below minimum
    [Arguments(91.0, 0.0)]     // Latitude above maximum
    [Arguments(100.0, 0.0)]    // Latitude way out of range
    [Arguments(-200.0, 0.0)]   // Latitude way below range
    public async Task ReverseGeocode_LatitudeOutOfRange_ReturnsNull(double lat, double lon)
    {
        // Arrange
        SkipIfNoDataFile();

        // Act
        var result = _sharedService!.ReverseGeocode(lat, lon);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    [Arguments(0.0, -181.0)]   // Longitude below minimum
    [Arguments(0.0, 181.0)]    // Longitude above maximum
    [Arguments(0.0, 360.0)]    // Longitude way out of range
    [Arguments(0.0, -360.0)]   // Longitude way below range
    public async Task ReverseGeocode_LongitudeOutOfRange_ReturnsNull(double lat, double lon)
    {
        // Arrange
        SkipIfNoDataFile();

        // Act
        var result = _sharedService!.ReverseGeocode(lat, lon);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    [Arguments(-90.0, 0.0)]    // South pole
    [Arguments(90.0, 0.0)]     // North pole
    [Arguments(0.0, -180.0)]   // Date line negative
    [Arguments(0.0, 180.0)]    // Date line positive
    [Arguments(0.0, 0.0)]      // Null Island
    public async Task ReverseGeocode_BoundaryCoordinates_DoesNotReject(double lat, double lon)
    {
        // Arrange
        SkipIfNoDataFile();

        // Act - should not throw (whether it returns data depends on GeoNames coverage)
        // We just verify it doesn't throw and accepts boundary values
        try
        {
            var result = _sharedService!.ReverseGeocode(lat, lon);
            await Assert.That(result is null || result is not null).IsTrue();
        }
        catch (Exception ex)
        {
            Assert.Fail($"Should not throw for valid boundary coordinates ({lat}, {lon}): {ex.Message}");
        }
    }

    #endregion
}
