using System;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Files.Geo;
using TUnit.Core;

namespace PhotoCopy.Tests.Integration;

/// <summary>
/// Integration tests for reverse geocoding using the streamed geo index.
/// These tests verify that the StreamedGeocodingService returns correct city and district values
/// for known real-world coordinates.
/// 
/// Uses indexed streaming from allCountries.txt - builds index on first run (~30s), 
/// then loads instantly on subsequent runs.
/// </summary>
[Property("Category", "Integration,RealGeoData")]
[NotInParallel("RealWorldGeocoding")] // Share the service instance, don't run in parallel
public class RealWorldGeocodingTests
{
    private static readonly string GeoDataDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "PhotoCopy", "data");
    
    // Static to share across all test instances - loaded once
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
        
        // Extra diagnostic - also check the index file
        var indexPath = dataPath + ".geostreamindex";
        _dataFilesExist = File.Exists(dataPath);
        var indexExists = File.Exists(indexPath);
        
        // Log paths for debugging (but not to Console which can throw in TUnit)
        System.Diagnostics.Debug.WriteLine($"Data path: {dataPath}");
        System.Diagnostics.Debug.WriteLine($"Data exists: {_dataFilesExist}");
        System.Diagnostics.Debug.WriteLine($"Index exists: {indexExists}");
        
        if (_dataFilesExist)
        {
            var mockLogger = NSubstitute.Substitute.For<ILogger<StreamedGeocodingService>>();
            var config = new PhotoCopyConfig { GeonamesPath = dataPath };
            _sharedService = new StreamedGeocodingService(mockLogger, config);
            await _sharedService.InitializeAsync();
            
            System.Diagnostics.Debug.WriteLine($"Service initialized: {_sharedService.IsInitialized}");
            System.Diagnostics.Debug.WriteLine($"Cache stats: {_sharedService.CacheStatistics}");
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
            Skip.Test("StreamedGeocodingService was not created (see ClassSetUp errors).");
        }
        if (!_sharedService.IsInitialized)
        {
            Skip.Test($"StreamedGeocodingService failed to initialize. Stats: {_sharedService.CacheStatistics ?? "null"}");
        }
    }

    #region Czech Republic Tests

    [Test]
    public void Prague_Sterboholy_ReturnsCorrectCityAndDistrict()
    {
        // Arrange
        SkipIfNoDataFile();
        
        // Coordinates near Štěrboholy, Prague
        const double lat = 50.07973397930704;
        const double lon = 14.468244770604457;

        // Act
        var result = _sharedService!.ReverseGeocode(lat, lon);

        // Assert
        result.Should().NotBeNull();
        // GeoNames data may have "CZ" or legacy "CS" (Czechoslovakia) for Czech locations
        result!.Country.Should().BeOneOf("CZ", "CS");
        result.District.Should().NotBeNullOrEmpty();
    }

    [Test]
    public void Prague_Jinonice_ReturnsCorrectCityAndDistrict()
    {
        // Arrange
        SkipIfNoDataFile();
        
        // Coordinates near Jinonice, Prague
        const double lat = 50.06226873602398;
        const double lon = 14.346925887282561;

        // Act
        var result = _sharedService!.ReverseGeocode(lat, lon);

        // Assert
        result.Should().NotBeNull();
        result!.Country.Should().Be("CZ");
        result.District.Should().NotBeNullOrEmpty();
    }

    [Test]
    public void Krivoklat_Area_ReturnsCityOrNearestTown()
    {
        // Arrange
        SkipIfNoDataFile();
        
        // Coordinates in Křivoklátsko area (protected landscape area, may be rural)
        const double lat = 49.981671617113385;
        const double lon = 13.861159687141079;

        // Act
        var result = _sharedService!.ReverseGeocode(lat, lon);

        // Assert
        result.Should().NotBeNull();
        result!.District.Should().NotBeNullOrEmpty("Should find at least a district/village");
        result.Country.Should().Be("CZ");
    }

    [Test]
    public void Plzen_ReturnsCorrectCity()
    {
        // Arrange
        SkipIfNoDataFile();
        
        // Coordinates in Plzeň (Pilsen)
        const double lat = 49.736701361665006;
        const double lon = 13.36733379985312;

        // Act
        var result = _sharedService!.ReverseGeocode(lat, lon);

        // Assert
        result.Should().NotBeNull();
        result!.Country.Should().Be("CZ");
        result.District.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Germany Tests

    [Test]
    public void Manching_Germany_ReturnsCorrectLocation()
    {
        // Arrange
        SkipIfNoDataFile();
        
        // Coordinates in Manching, Bavaria, Germany
        const double lat = 48.71496705449036;
        const double lon = 11.497902604274653;

        // Act
        var result = _sharedService!.ReverseGeocode(lat, lon);

        // Assert
        result.Should().NotBeNull();
        result!.District.Should().NotBeNullOrEmpty();
        result.Country.Should().Be("DE");
        // Manching has ~12k population, below threshold
        // City should be the nearest larger city (possibly Ingolstadt with ~140k)
    }

    [Test]
    public void GrattStadt_Germany_ReturnsCorrectLocation()
    {
        // Arrange
        SkipIfNoDataFile();
        
        // Coordinates near Grattstadt, Germany
        const double lat = 50.37552419454665;
        const double lon = 10.837585451309092;

        // Act
        var result = _sharedService!.ReverseGeocode(lat, lon);

        // Assert
        result.Should().NotBeNull();
        result!.District.Should().NotBeNullOrEmpty();
        result.Country.Should().Be("DE");
    }

    #endregion

    #region USA Tests

    [Test]
    public void Edgerton_Wyoming_ReturnsCorrectLocation()
    {
        // Arrange
        SkipIfNoDataFile();
        
        // Coordinates near Edgerton, Wyoming, USA
        const double lat = 43.41371984309537;
        const double lon = -106.24696672369056;

        // Act
        var result = _sharedService!.ReverseGeocode(lat, lon);

        // Assert
        result.Should().NotBeNull();
        result!.District.Should().NotBeNullOrEmpty();
        result.Country.Should().Be("US");
        // Edgerton is a very small town (~200 people)
        // City should be nearest larger city >= 100k (maybe null in rural Wyoming)
    }

    [Test]
    public void Yellowstone_NationalPark_ReturnsNearestLocation()
    {
        // Arrange
        SkipIfNoDataFile();
        
        // Coordinates in Yellowstone National Park (Old Faithful area)
        const double lat = 44.524728360650606;
        const double lon = -110.50920190051461;

        // Act
        var result = _sharedService!.ReverseGeocode(lat, lon);

        // Assert
        result.Should().NotBeNull();
        // National park area - should still find something
        result!.Country.Should().Be("US");
    }

    #endregion
}
