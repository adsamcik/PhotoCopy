using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Configuration;
using PhotoCopy.Files;

namespace PhotoCopy.Tests.Integration;

/// <summary>
/// Integration tests for the ReverseGeocodingService using GeoNames data.
/// These tests verify the service's ability to perform reverse geocoding lookups
/// using a KdTree-based nearest neighbor search.
/// </summary>
[Property("Category", "Integration")]
public class ReverseGeocodingIntegrationTests : IDisposable
{
    private readonly ILogger<ReverseGeocodingService> _mockLogger;
    private readonly PhotoCopyConfig _config;
    private readonly IOptions<PhotoCopyConfig> _mockOptions;
    private readonly string _tempDirectory;
    private readonly string _testDataFile;

    public ReverseGeocodingIntegrationTests()
    {
        _mockLogger = Substitute.For<ILogger<ReverseGeocodingService>>();
        _config = new PhotoCopyConfig();
        _mockOptions = Microsoft.Extensions.Options.Options.Create(_config);

        // Create temp directory for test data files
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"ReverseGeocodingIntegrationTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        _testDataFile = Path.Combine(_tempDirectory, "geonames_integration_test.txt");
    }

    public void Dispose()
    {
        // Cleanup temp files
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Ignore cleanup failures
            }
        }
    }

    #region Helper Methods

    private void CreateTestGeoNamesFile(string content)
    {
        File.WriteAllText(_testDataFile, content);
        _config.GeonamesPath = _testDataFile;
    }

    /// <summary>
    /// Creates a valid GeoNames formatted line.
    /// GeoNames format: geonameid, name, asciiname, alternatenames, lat, lon, feature_class, feature_code, country_code, cc2, admin1_code, admin2_code, admin3_code, admin4_code, population, ...
    /// </summary>
    private static string CreateGeoNamesLine(string name, float lat, float lon, string countryCode, string admin1Code, string? admin2Code = null, long population = 0)
    {
        // Indices: 0=geonameid, 1=name, 2=asciiname, 3=alternatenames, 4=lat, 5=lon, 6=feature_class, 7=feature_code, 8=country_code, 9=cc2, 10=admin1_code, 11=admin2_code, 12=admin3_code, 13=admin4_code, 14=population
        return $"123456\t{name}\t{name.ToLowerInvariant()}\t\t{lat.ToString(CultureInfo.InvariantCulture)}\t{lon.ToString(CultureInfo.InvariantCulture)}\tP\tPPL\t{countryCode}\t\t{admin1Code}\t{admin2Code ?? ""}\t\t\t{population}";
    }

    private ReverseGeocodingService CreateService()
    {
        return new ReverseGeocodingService(_mockLogger, _mockOptions);
    }

    private void SetupMultiCityDatabase()
    {
        // Create a realistic multi-city database for integration testing
        var geoNamesContent = string.Join(Environment.NewLine, new[]
        {
            // USA cities
            CreateGeoNamesLine("New York", 40.7128f, -74.0060f, "US", "NY"),
            CreateGeoNamesLine("Los Angeles", 34.0522f, -118.2437f, "US", "CA"),
            CreateGeoNamesLine("Chicago", 41.8781f, -87.6298f, "US", "IL"),
            CreateGeoNamesLine("San Francisco", 37.7749f, -122.4194f, "US", "CA"),
            CreateGeoNamesLine("Miami", 25.7617f, -80.1918f, "US", "FL"),
            
            // International cities
            CreateGeoNamesLine("Tokyo", 35.6762f, 139.6503f, "JP", "13"),
            CreateGeoNamesLine("London", 51.5074f, -0.1278f, "GB", "ENG"),
            CreateGeoNamesLine("Paris", 48.8566f, 2.3522f, "FR", "IDF"),
            CreateGeoNamesLine("Sydney", -33.8688f, 151.2093f, "AU", "NSW"),
            CreateGeoNamesLine("Berlin", 52.5200f, 13.4050f, "DE", "BE"),
            CreateGeoNamesLine("Rio de Janeiro", -22.9068f, -43.1729f, "BR", "RJ"),
            
            // Edge case locations
            CreateGeoNamesLine("Reykjavik", 64.1466f, -21.9426f, "IS", "01"), // Near Arctic
            CreateGeoNamesLine("Cape Town", -33.9249f, 18.4241f, "ZA", "WC"), // Southern tip of Africa
            CreateGeoNamesLine("Honolulu", 21.3069f, -157.8583f, "US", "HI"), // Pacific island
            CreateGeoNamesLine("Singapore", 1.3521f, 103.8198f, "SG", "01"), // Near equator
        });
        CreateTestGeoNamesFile(geoNamesContent);
    }

    #endregion

    #region Valid Coordinates Tests

    [Test]
    public async Task ReverseGeocodingService_WithValidCoordinates_ReturnsLocationData()
    {
        // Arrange
        SetupMultiCityDatabase();
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Query for San Francisco coordinates
        var result = service.ReverseGeocode(37.7749, -122.4194);

        // Assert
        result.Should().NotBeNull();
        result!.City.Should().NotBeNullOrEmpty();
        result.Country.Should().NotBeNullOrEmpty();
        result.State.Should().NotBeNull();
    }

    [Test]
    public async Task ReverseGeocodingService_WithNewYorkCoordinates_ReturnsNewYork()
    {
        // Arrange
        SetupMultiCityDatabase();
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Query for New York City coordinates (Times Square area)
        var result = service.ReverseGeocode(40.7128, -74.0060);

        // Assert
        result.Should().NotBeNull();
        result!.City.Should().Be("New York");
        result.State.Should().Be("NY");
        result.Country.Should().Be("US");
    }

    [Test]
    public async Task ReverseGeocodingService_WithTokyoCoordinates_ReturnsTokyo()
    {
        // Arrange
        SetupMultiCityDatabase();
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Query for Tokyo coordinates
        var result = service.ReverseGeocode(35.6762, 139.6503);

        // Assert
        result.Should().NotBeNull();
        result!.City.Should().Be("Tokyo");
        result.Country.Should().Be("JP");
        result.State.Should().Be("13"); // Tokyo prefecture code
    }

    [Test]
    public async Task ReverseGeocodingService_WithLondonCoordinates_ReturnsLondon()
    {
        // Arrange
        SetupMultiCityDatabase();
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Query for London coordinates
        var result = service.ReverseGeocode(51.5074, -0.1278);

        // Assert
        result.Should().NotBeNull();
        result!.City.Should().Be("London");
        result.Country.Should().Be("GB");
        result.State.Should().Be("ENG");
    }

    [Test]
    public async Task ReverseGeocodingService_WithSydneyCoordinates_ReturnsSydney()
    {
        // Arrange - Southern hemisphere test
        SetupMultiCityDatabase();
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Query for Sydney coordinates
        var result = service.ReverseGeocode(-33.8688, 151.2093);

        // Assert
        result.Should().NotBeNull();
        result!.City.Should().Be("Sydney");
        result.Country.Should().Be("AU");
        result.State.Should().Be("NSW");
    }

    #endregion

    #region Ocean/Remote Coordinates Tests

    [Test]
    public async Task ReverseGeocodingService_WithOceanCoordinates_ReturnsNearestLand()
    {
        // Arrange
        SetupMultiCityDatabase();
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Query for coordinates in the Pacific Ocean near Hawaii
        // These coordinates are in the ocean but Hawaii should be the nearest land
        var result = service.ReverseGeocode(22.0, -158.0);

        // Assert - Should return nearest land (Honolulu)
        result.Should().NotBeNull();
        result!.City.Should().Be("Honolulu");
        result.Country.Should().Be("US");
    }

    [Test]
    public async Task ReverseGeocodingService_WithAtlanticOceanCoordinates_ReturnsNearestCoastalCity()
    {
        // Arrange
        SetupMultiCityDatabase();
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Query for coordinates in the Atlantic Ocean, closer to Miami
        var result = service.ReverseGeocode(25.5, -79.0);

        // Assert - Should return Miami as nearest
        result.Should().NotBeNull();
        result!.City.Should().Be("Miami");
    }

    [Test]
    public async Task ReverseGeocodingService_WithArcticCoordinates_ReturnsNearestLocation()
    {
        // Arrange
        SetupMultiCityDatabase();
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Query for coordinates near the Arctic
        var result = service.ReverseGeocode(65.0, -20.0);

        // Assert - Should return Reykjavik as the nearest populated location
        result.Should().NotBeNull();
        result!.City.Should().Be("Reykjavik");
        result.Country.Should().Be("IS");
    }

    #endregion

    #region Invalid Coordinates Tests

    [Test]
    public async Task ReverseGeocodingService_WithInvalidCoordinates_HandlesGracefully()
    {
        // Arrange - Create file with only malformed data
        var malformedContent = string.Join(Environment.NewLine, new[]
        {
            "invalid\tdata\tonly",
            "also\tinvalid\tline",
            "123456\tBadCity\tbad\t\tNOT_A_NUMBER\t-74.0\tP\tPPL\tUS\t\tNY"
        });
        CreateTestGeoNamesFile(malformedContent);
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Query any coordinates (tree should be empty)
        var result = service.ReverseGeocode(40.7128, -74.0060);

        // Assert - Should return null gracefully, not throw
        result.Should().BeNull();
    }

    [Test]
    public async Task ReverseGeocodingService_WithExtremeLatitude_HandlesGracefully()
    {
        // Arrange
        SetupMultiCityDatabase();
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Query with extreme but valid latitude (near poles)
        var result = service.ReverseGeocode(89.9999, 0.0);

        // Assert - Should return nearest location without crashing
        result.Should().NotBeNull();
    }

    [Test]
    public async Task ReverseGeocodingService_WithExtremeLongitude_HandlesGracefully()
    {
        // Arrange
        SetupMultiCityDatabase();
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Query with extreme longitude
        var result = service.ReverseGeocode(0.0, 179.9999);

        // Assert - Should return nearest location without crashing
        result.Should().NotBeNull();
    }

    #endregion

    #region Null/Empty GPS Data Tests

    [Test]
    public async Task ReverseGeocodingService_WithNullGps_ReturnsEmptyLocation()
    {
        // Arrange - Service not initialized (simulates no GPS data scenario)
        _config.GeonamesPath = null;
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(40.7128, -74.0060);

        // Assert - When service is not initialized, returns null
        result.Should().BeNull();
    }

    [Test]
    public async Task ReverseGeocodingService_WithEmptyDatabase_ReturnsNull()
    {
        // Arrange - Empty database file
        CreateTestGeoNamesFile(string.Empty);
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(40.7128, -74.0060);

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void ReverseGeocodingService_BeforeInitialization_ReturnsNull()
    {
        // Arrange - Set up valid database but don't initialize
        SetupMultiCityDatabase();
        var service = CreateService();
        // Note: NOT calling InitializeAsync

        // Act
        var result = service.ReverseGeocode(40.7128, -74.0060);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region LocationData Integration Tests

    [Test]
    public async Task LocationData_UsedInDestinationPath_SubstitutesCorrectly()
    {
        // Arrange
        SetupMultiCityDatabase();
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Get location data for New York
        var locationData = service.ReverseGeocode(40.7128, -74.0060);

        // Assert - Verify the LocationData can be used for path substitution
        locationData.Should().NotBeNull();

        // Simulate destination path substitution
        var destinationTemplate = "{year}/{country}/{state}/{city}/{name}";
        var substitutedPath = destinationTemplate
            .Replace(DestinationVariables.City, locationData!.City ?? "Unknown")
            .Replace(DestinationVariables.State, locationData.State ?? "Unknown")
            .Replace(DestinationVariables.Country, locationData.Country ?? "Unknown");

        substitutedPath.Should().Contain("US");
        substitutedPath.Should().Contain("NY");
        substitutedPath.Should().Contain("New York");
    }

    [Test]
    public async Task LocationData_WithMissingState_SubstitutesAsUnknown()
    {
        // Arrange - Create a location with empty state
        var geoNamesContent = CreateGeoNamesLine("Vatican City", 41.9029f, 12.4534f, "VA", "");
        CreateTestGeoNamesFile(geoNamesContent);
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var locationData = service.ReverseGeocode(41.9029, 12.4534);

        // Assert
        locationData.Should().NotBeNull();
        locationData!.City.Should().Be("Vatican City");
        locationData.Country.Should().Be("VA");
        
        // State is empty string (not null)
        var statePath = locationData.State ?? "Unknown";
        // Empty string should use "Unknown" in path substitution
        if (string.IsNullOrEmpty(locationData.State))
        {
            statePath = "Unknown";
        }
        statePath.Should().Be("Unknown");
    }

    [Test]
    public async Task LocationData_AllFieldsPopulated_ReturnsCompleteData()
    {
        // Arrange
        SetupMultiCityDatabase();
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(48.8566, 2.3522); // Paris

        // Assert - All fields should be populated
        result.Should().NotBeNull();
        result!.City.Should().Be("Paris");
        result.State.Should().Be("IDF");
        result.Country.Should().Be("FR");
    }

    #endregion

    #region Nearest Neighbor Accuracy Tests

    [Test]
    public async Task ReverseGeocodingService_WithSlightlyOffCoordinates_ReturnsCorrectNearestCity()
    {
        // Arrange
        SetupMultiCityDatabase();
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Query for coordinates slightly off from Chicago
        var result = service.ReverseGeocode(41.88, -87.63);

        // Assert - Should still return Chicago
        result.Should().NotBeNull();
        result!.City.Should().Be("Chicago");
        result.State.Should().Be("IL");
    }

    [Test]
    public async Task ReverseGeocodingService_WithCoordinatesBetweenCities_ReturnsNearest()
    {
        // Arrange
        SetupMultiCityDatabase();
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Query for coordinates between Los Angeles and San Francisco (closer to LA)
        var result = service.ReverseGeocode(34.5, -118.0);

        // Assert - Should return LA as it's closer
        result.Should().NotBeNull();
        result!.City.Should().Be("Los Angeles");
    }

    [Test]
    public async Task ReverseGeocodingService_WithEquatorCoordinates_ReturnsNearestEquatorialCity()
    {
        // Arrange
        SetupMultiCityDatabase();
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Query for coordinates near the equator
        var result = service.ReverseGeocode(1.0, 104.0);

        // Assert - Should return Singapore
        result.Should().NotBeNull();
        result!.City.Should().Be("Singapore");
    }

    #endregion

    #region Service Initialization Tests

    [Test]
    public async Task ReverseGeocodingService_InitializedTwice_OnlyLoadsOnce()
    {
        // Arrange
        SetupMultiCityDatabase();
        var service = CreateService();

        // Act
        await service.InitializeAsync();
        await service.InitializeAsync(); // Second call should be no-op

        // Assert - Should still work correctly
        var result = service.ReverseGeocode(40.7128, -74.0060);
        result.Should().NotBeNull();
        result!.City.Should().Be("New York");
    }

    [Test]
    public async Task ReverseGeocodingService_WithNonExistentFile_HandlesGracefully()
    {
        // Arrange
        _config.GeonamesPath = Path.Combine(_tempDirectory, "nonexistent_file.txt");
        var service = CreateService();

        // Act
        await service.InitializeAsync();
        var result = service.ReverseGeocode(40.7128, -74.0060);

        // Assert
        result.Should().BeNull();

        // Verify warning was logged
        _mockLogger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("GeoNames file not found")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region International Character Tests

    [Test]
    public async Task ReverseGeocodingService_WithUnicodeCharacterCityName_ReturnsCorrectName()
    {
        // Arrange
        var geoNamesContent = string.Join(Environment.NewLine, new[]
        {
            CreateGeoNamesLine("São Paulo", -23.5505f, -46.6333f, "BR", "SP"),
            CreateGeoNamesLine("Zürich", 47.3769f, 8.5417f, "CH", "ZH"),
            CreateGeoNamesLine("北京", 39.9042f, 116.4074f, "CN", "11"), // Beijing in Chinese
        });
        CreateTestGeoNamesFile(geoNamesContent);
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var saoPauloResult = service.ReverseGeocode(-23.5505, -46.6333);
        var zurichResult = service.ReverseGeocode(47.3769, 8.5417);

        // Assert
        saoPauloResult.Should().NotBeNull();
        saoPauloResult!.City.Should().Be("São Paulo");
        saoPauloResult.Country.Should().Be("BR");

        zurichResult.Should().NotBeNull();
        zurichResult!.City.Should().Be("Zürich");
        zurichResult.Country.Should().Be("CH");
    }

    #endregion

    #region County (Admin2) Tests

    [Test]
    public async Task ReverseGeocodingService_WithAdmin2Data_ReturnsCounty()
    {
        // Arrange - Create a location with admin2 (county) data
        var geoNamesContent = CreateGeoNamesLine("Manhattan", 40.7831f, -73.9712f, "US", "NY", "061", 1628706);
        CreateTestGeoNamesFile(geoNamesContent);
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(40.7831, -73.9712);

        // Assert
        result.Should().NotBeNull();
        result!.City.Should().Be("Manhattan");
        result.County.Should().Be("061"); // New York County FIPS code
        result.State.Should().Be("NY");
        result.Country.Should().Be("US");
    }

    [Test]
    public async Task ReverseGeocodingService_WithoutAdmin2Data_CountyIsNull()
    {
        // Arrange - Create a location without admin2 data
        var geoNamesContent = CreateGeoNamesLine("London", 51.5074f, -0.1278f, "GB", "ENG");
        CreateTestGeoNamesFile(geoNamesContent);
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(51.5074, -0.1278);

        // Assert
        result.Should().NotBeNull();
        result!.City.Should().Be("London");
        result.County.Should().BeNull(); // Empty string is converted to null
        result.State.Should().Be("ENG");
        result.Country.Should().Be("GB");
    }

    #endregion

    #region Population Filtering Tests

    [Test]
    public async Task ReverseGeocodingService_WithMinimumPopulation_FiltersSmallCities()
    {
        // Arrange - Create cities with different populations
        var geoNamesContent = string.Join(Environment.NewLine, new[]
        {
            CreateGeoNamesLine("Big City", 40.0f, -74.0f, "US", "NY", null, 1000000),
            CreateGeoNamesLine("Small Town", 40.01f, -74.01f, "US", "NY", null, 500), // Very close, but small population
        });
        CreateTestGeoNamesFile(geoNamesContent);
        _config.MinimumPopulation = 1000; // Filter out cities with less than 1000 people
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Query coordinates near Small Town
        var result = service.ReverseGeocode(40.01, -74.01);

        // Assert - Should return Big City (not Small Town, which was filtered)
        result.Should().NotBeNull();
        result!.City.Should().Be("Big City");
        result.Population.Should().Be(1000000);
    }

    [Test]
    public async Task ReverseGeocodingService_WithZeroMinimumPopulation_IncludesAllCities()
    {
        // Arrange
        var geoNamesContent = CreateGeoNamesLine("Tiny Village", 40.0f, -74.0f, "US", "NY", null, 50);
        CreateTestGeoNamesFile(geoNamesContent);
        _config.MinimumPopulation = 0; // No filtering
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(40.0, -74.0);

        // Assert
        result.Should().NotBeNull();
        result!.City.Should().Be("Tiny Village");
        result.Population.Should().Be(50);
    }

    [Test]
    public async Task ReverseGeocodingService_WithNullMinimumPopulation_IncludesAllCities()
    {
        // Arrange
        var geoNamesContent = CreateGeoNamesLine("Hamlet", 40.0f, -74.0f, "US", "NY", null, 25);
        CreateTestGeoNamesFile(geoNamesContent);
        _config.MinimumPopulation = null;
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(40.0, -74.0);

        // Assert
        result.Should().NotBeNull();
        result!.City.Should().Be("Hamlet");
    }

    #endregion

    #region Population Data Tests

    [Test]
    public async Task ReverseGeocodingService_ReturnsPopulation()
    {
        // Arrange
        var geoNamesContent = CreateGeoNamesLine("Los Angeles", 34.0522f, -118.2437f, "US", "CA", "037", 3976322);
        CreateTestGeoNamesFile(geoNamesContent);
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(34.0522, -118.2437);

        // Assert
        result.Should().NotBeNull();
        result!.City.Should().Be("Los Angeles");
        result.Population.Should().Be(3976322);
    }

    #endregion
}
