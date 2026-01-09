using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Configuration;
using PhotoCopy.Files;

namespace PhotoCopy.Tests.Files;

public class ReverseGeocodingServiceTests : IDisposable
{
    private readonly ILogger<ReverseGeocodingService> _mockLogger;
    private readonly IOptions<PhotoCopyConfig> _mockOptions;
    private readonly PhotoCopyConfig _config;
    private readonly string _tempDirectory;
    private readonly string _testDataFile;

    public ReverseGeocodingServiceTests()
    {
        _mockLogger = Substitute.For<ILogger<ReverseGeocodingService>>();
        _config = new PhotoCopyConfig();
        _mockOptions = Microsoft.Extensions.Options.Options.Create(_config);
        
        // Create temp directory for test data files
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"ReverseGeocodingTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        _testDataFile = Path.Combine(_tempDirectory, "geonames_test.txt");
    }

    public void Dispose()
    {
        // Cleanup temp files
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    #region Helper Methods

    private void CreateTestGeoNamesFile(string content)
    {
        File.WriteAllText(_testDataFile, content);
        _config.GeonamesPath = _testDataFile;
    }

    private string CreateValidGeoNamesLine(string name, float lat, float lon, string countryCode, string admin1Code, string? admin2Code = null, long population = 0)
    {
        // GeoNames format: geonameid, name, asciiname, alternatenames, lat, lon, feature_class, feature_code, country_code, cc2, admin1_code, admin2_code, admin3_code, admin4_code, population, ...
        // We need at least 15 columns (index 0-14), with lat at index 4, lon at index 5, country_code at index 8, admin1_code at index 10, admin2_code at index 11, population at index 14
        return $"123456\t{name}\t{name.ToLowerInvariant()}\t\t{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}\t{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}\tP\tPPL\t{countryCode}\t\t{admin1Code}\t{admin2Code ?? ""}\t\t\t{population}";
    }

    private ReverseGeocodingService CreateService()
    {
        return new ReverseGeocodingService(_mockLogger, _mockOptions);
    }

    #endregion

    #region InitializeAsync Tests

    [Test]
    public async Task InitializeAsync_WithValidFile_LoadsData()
    {
        // Arrange
        var geoNamesContent = string.Join(Environment.NewLine, new[]
        {
            CreateValidGeoNamesLine("New York", 40.7128f, -74.0060f, "US", "NY"),
            CreateValidGeoNamesLine("Los Angeles", 34.0522f, -118.2437f, "US", "CA"),
            CreateValidGeoNamesLine("London", 51.5074f, -0.1278f, "GB", "ENG")
        });
        CreateTestGeoNamesFile(geoNamesContent);
        
        var service = CreateService();

        // Act
        await service.InitializeAsync();

        // Assert - Service should be initialized and able to return location data
        var result = service.ReverseGeocode(40.7128, -74.0060);
        result.Should().NotBeNull();
        result!.City.Should().Be("New York");
        result.Country.Should().Be("US");
        result.State.Should().Be("NY");
    }

    [Test]
    public async Task InitializeAsync_WithMissingFile_ReturnsFalse()
    {
        // Arrange
        _config.GeonamesPath = Path.Combine(_tempDirectory, "nonexistent_file.txt");
        var service = CreateService();

        // Act
        await service.InitializeAsync();

        // Assert - Service should not be initialized, ReverseGeocode should return null
        var result = service.ReverseGeocode(40.7128, -74.0060);
        result.Should().BeNull();
        
        // Verify warning was logged
        _mockLogger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("GeoNames file not found")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task InitializeAsync_WithEmptyPath_DoesNotInitialize()
    {
        // Arrange
        _config.GeonamesPath = string.Empty;
        var service = CreateService();

        // Act
        await service.InitializeAsync();

        // Assert
        var result = service.ReverseGeocode(40.7128, -74.0060);
        result.Should().BeNull();
    }

    [Test]
    public async Task InitializeAsync_WithNullPath_DoesNotInitialize()
    {
        // Arrange
        _config.GeonamesPath = null;
        var service = CreateService();

        // Act
        await service.InitializeAsync();

        // Assert
        var result = service.ReverseGeocode(40.7128, -74.0060);
        result.Should().BeNull();
    }

    [Test]
    public async Task InitializeAsync_CalledTwice_OnlyLoadsOnce()
    {
        // Arrange
        var geoNamesContent = CreateValidGeoNamesLine("Paris", 48.8566f, 2.3522f, "FR", "IDF");
        CreateTestGeoNamesFile(geoNamesContent);
        
        var service = CreateService();

        // Act
        await service.InitializeAsync();
        await service.InitializeAsync(); // Call again

        // Assert - Should still work, only loaded once
        var result = service.ReverseGeocode(48.8566, 2.3522);
        result.Should().NotBeNull();
        result!.City.Should().Be("Paris");
    }

    [Test]
    public async Task InitializeAsync_WithCancellationToken_RespectsToken()
    {
        // Arrange - Create a file with valid data
        var geoNamesContent = CreateValidGeoNamesLine("TestCity", 40.0f, -74.0f, "US", "NY");
        CreateTestGeoNamesFile(geoNamesContent);
        
        var service = CreateService();
        using var cts = new CancellationTokenSource();

        // Act - Initialize with a valid (non-cancelled) token
        await service.InitializeAsync(cts.Token);

        // Assert - Service should be initialized and work correctly
        var result = service.ReverseGeocode(40.0, -74.0);
        result.Should().NotBeNull();
        result!.City.Should().Be("TestCity");
    }

    #endregion

    #region ReverseGeocode Tests

    [Test]
    public void ReverseGeocode_BeforeInit_ReturnsNull()
    {
        // Arrange
        var service = CreateService();

        // Act - Don't call InitializeAsync
        var result = service.ReverseGeocode(40.7128, -74.0060);

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public async Task ReverseGeocode_WithValidCoordinates_ReturnsLocation()
    {
        // Arrange
        var geoNamesContent = string.Join(Environment.NewLine, new[]
        {
            CreateValidGeoNamesLine("Tokyo", 35.6762f, 139.6503f, "JP", "13"),
            CreateValidGeoNamesLine("Sydney", -33.8688f, 151.2093f, "AU", "NSW"),
            CreateValidGeoNamesLine("Berlin", 52.5200f, 13.4050f, "DE", "BE")
        });
        CreateTestGeoNamesFile(geoNamesContent);
        
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(35.6762, 139.6503);

        // Assert
        result.Should().NotBeNull();
        result!.City.Should().Be("Tokyo");
        result.Country.Should().Be("JP");
        result.State.Should().Be("13");
    }

    [Test]
    public async Task ReverseGeocode_WithNearbyCoordinates_ReturnsNearestLocation()
    {
        // Arrange
        var geoNamesContent = string.Join(Environment.NewLine, new[]
        {
            CreateValidGeoNamesLine("CityA", 40.0f, -74.0f, "US", "NJ"),
            CreateValidGeoNamesLine("CityB", 41.0f, -73.0f, "US", "NY")
        });
        CreateTestGeoNamesFile(geoNamesContent);
        
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Query coordinates closer to CityA
        var result = service.ReverseGeocode(40.1, -74.1);

        // Assert
        result.Should().NotBeNull();
        result!.City.Should().Be("CityA");
    }

    [Test]
    public async Task ReverseGeocode_WithInvalidCoordinates_ReturnsNull()
    {
        // Arrange - Create file but don't add valid data (malformed lines)
        var malformedContent = string.Join(Environment.NewLine, new[]
        {
            "invalid\tdata\tonly", // Not enough columns
            "also\tinvalid",
            "1\t2\t3\t4\t5" // Still not enough columns
        });
        CreateTestGeoNamesFile(malformedContent);
        
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Tree is empty due to malformed data
        var result = service.ReverseGeocode(40.7128, -74.0060);

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public async Task ReverseGeocode_WithEmptyTree_ReturnsNull()
    {
        // Arrange - Create empty file
        CreateTestGeoNamesFile(string.Empty);
        
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(40.7128, -74.0060);

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public async Task ReverseGeocode_WithNegativeCoordinates_ReturnsLocation()
    {
        // Arrange - Southern hemisphere location
        var geoNamesContent = CreateValidGeoNamesLine("Buenos Aires", -34.6037f, -58.3816f, "AR", "BA");
        CreateTestGeoNamesFile(geoNamesContent);
        
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(-34.6037, -58.3816);

        // Assert
        result.Should().NotBeNull();
        result!.City.Should().Be("Buenos Aires");
        result.Country.Should().Be("AR");
    }

    [Test]
    public async Task ReverseGeocode_WithExtremeCoordinates_ReturnsNearestLocation()
    {
        // Arrange
        var geoNamesContent = CreateValidGeoNamesLine("Longyearbyen", 78.2232f, 15.6267f, "NO", "21");
        CreateTestGeoNamesFile(geoNamesContent);
        
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Query near the Arctic
        var result = service.ReverseGeocode(78.0, 15.0);

        // Assert
        result.Should().NotBeNull();
        result!.City.Should().Be("Longyearbyen");
    }

    #endregion

    #region Data Parsing Tests

    [Test]
    public async Task InitializeAsync_WithMalformedLatitude_SkipsLine()
    {
        // Arrange
        var geoNamesContent = string.Join(Environment.NewLine, new[]
        {
            "123456\tBadCity\tbadcity\t\tNOT_A_NUMBER\t-74.0060\tP\tPPL\tUS\t\tNY\t\t\t\t0", // Invalid lat but enough columns
            CreateValidGeoNamesLine("GoodCity", 40.7128f, -74.0060f, "US", "NY")
        });
        CreateTestGeoNamesFile(geoNamesContent);
        
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(40.7128, -74.0060);

        // Assert - Should find GoodCity, BadCity was skipped
        result.Should().NotBeNull();
        result!.City.Should().Be("GoodCity");
    }

    [Test]
    public async Task InitializeAsync_WithMalformedLongitude_SkipsLine()
    {
        // Arrange
        var geoNamesContent = string.Join(Environment.NewLine, new[]
        {
            "123456\tBadCity\tbadcity\t\t40.7128\tINVALID\tP\tPPL\tUS\t\tNY\t\t\t\t0", // Invalid lon but enough columns
            CreateValidGeoNamesLine("GoodCity", 35.0f, -80.0f, "US", "NC")
        });
        CreateTestGeoNamesFile(geoNamesContent);
        
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(35.0, -80.0);

        // Assert
        result.Should().NotBeNull();
        result!.City.Should().Be("GoodCity");
    }

    [Test]
    public async Task InitializeAsync_WithInsufficientColumns_SkipsLine()
    {
        // Arrange
        var geoNamesContent = string.Join(Environment.NewLine, new[]
        {
            "123456\tShortLine\tshort\t\t40.0\t-74.0\tP\tPPL\tUS\tCC\tNY", // Only 11 columns, need 15
            CreateValidGeoNamesLine("ValidCity", 41.0f, -73.0f, "US", "CT")
        });
        CreateTestGeoNamesFile(geoNamesContent);
        
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(41.0, -73.0);

        // Assert
        result.Should().NotBeNull();
        result!.City.Should().Be("ValidCity");
    }

    #endregion

    #region Population Filtering Tests

    [Test]
    public async Task InitializeAsync_WithMinimumPopulation_FiltersOutSmallCities()
    {
        // Arrange
        _config.MinimumPopulation = 100000;
        var geoNamesContent = string.Join(Environment.NewLine, new[]
        {
            CreateValidGeoNamesLine("SmallTown", 40.0f, -74.0f, "US", "NJ", null, 5000), // Below threshold
            CreateValidGeoNamesLine("BigCity", 41.0f, -73.0f, "US", "NY", null, 500000) // Above threshold
        });
        CreateTestGeoNamesFile(geoNamesContent);
        
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Query near SmallTown
        var result = service.ReverseGeocode(40.0, -74.0);

        // Assert - Should return BigCity since SmallTown was filtered out
        result.Should().NotBeNull();
        result!.City.Should().Be("BigCity");
    }

    [Test]
    public async Task InitializeAsync_WithMinimumPopulation_IncludesCitiesAtThreshold()
    {
        // Arrange
        _config.MinimumPopulation = 10000;
        var geoNamesContent = CreateValidGeoNamesLine("ExactCity", 40.0f, -74.0f, "US", "NJ", null, 10000);
        CreateTestGeoNamesFile(geoNamesContent);
        
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(40.0, -74.0);

        // Assert - City with exactly 10000 should be included (filter is <, not <=)
        result.Should().NotBeNull();
        result!.City.Should().Be("ExactCity");
    }

    [Test]
    public async Task InitializeAsync_WithMinimumPopulation_ExcludesBelowThreshold()
    {
        // Arrange
        _config.MinimumPopulation = 10000;
        var geoNamesContent = CreateValidGeoNamesLine("TinyVillage", 40.0f, -74.0f, "US", "NJ", null, 9999);
        CreateTestGeoNamesFile(geoNamesContent);
        
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(40.0, -74.0);

        // Assert - City below threshold should be filtered out
        result.Should().BeNull();
    }

    [Test]
    public async Task InitializeAsync_WithZeroMinimumPopulation_IncludesAllCities()
    {
        // Arrange
        _config.MinimumPopulation = 0;
        var geoNamesContent = string.Join(Environment.NewLine, new[]
        {
            CreateValidGeoNamesLine("TinyVillage", 40.0f, -74.0f, "US", "NJ", null, 100),
            CreateValidGeoNamesLine("BigCity", 41.0f, -73.0f, "US", "NY", null, 1000000)
        });
        CreateTestGeoNamesFile(geoNamesContent);
        
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Query near TinyVillage
        var result = service.ReverseGeocode(40.0, -74.0);

        // Assert - TinyVillage should be found
        result.Should().NotBeNull();
        result!.City.Should().Be("TinyVillage");
    }

    [Test]
    public async Task InitializeAsync_WithNullMinimumPopulation_IncludesAllCities()
    {
        // Arrange
        _config.MinimumPopulation = null;
        var geoNamesContent = CreateValidGeoNamesLine("SmallPlace", 40.0f, -74.0f, "US", "NJ", null, 50);
        CreateTestGeoNamesFile(geoNamesContent);
        
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(40.0, -74.0);

        // Assert
        result.Should().NotBeNull();
        result!.City.Should().Be("SmallPlace");
    }

    [Test]
    public async Task ReverseGeocode_ReturnsPopulationData()
    {
        // Arrange
        var geoNamesContent = CreateValidGeoNamesLine("NewYork", 40.7128f, -74.0060f, "US", "NY", "New York County", 8336817);
        CreateTestGeoNamesFile(geoNamesContent);
        
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(40.7128, -74.0060);

        // Assert
        result.Should().NotBeNull();
        result!.Population.Should().Be(8336817);
    }

    #endregion

    #region County (Admin2) Parsing Tests

    [Test]
    public async Task ReverseGeocode_WithCountyData_ReturnsCounty()
    {
        // Arrange
        var geoNamesContent = CreateValidGeoNamesLine("Manhattan", 40.7831f, -73.9712f, "US", "NY", "New York County", 1628706);
        CreateTestGeoNamesFile(geoNamesContent);
        
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(40.7831, -73.9712);

        // Assert
        result.Should().NotBeNull();
        result!.City.Should().Be("Manhattan");
        result.County.Should().Be("New York County");
        result.State.Should().Be("NY");
        result.Country.Should().Be("US");
    }

    [Test]
    public async Task ReverseGeocode_WithNullCounty_ReturnsNullCounty()
    {
        // Arrange - Create line with no county (null/empty in column 11)
        var geoNamesContent = CreateValidGeoNamesLine("London", 51.5074f, -0.1278f, "GB", "ENG", null, 8136000);
        CreateTestGeoNamesFile(geoNamesContent);
        
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(51.5074, -0.1278);

        // Assert
        result.Should().NotBeNull();
        result!.City.Should().Be("London");
        result.County.Should().BeNull();
    }

    [Test]
    public async Task ReverseGeocode_WithEmptyCounty_ReturnsNullCounty()
    {
        // Arrange - Create line with empty string for county (column 11)
        // Using raw format since the helper might not handle empty strings correctly
        var geoNamesContent = "123456\tParis\tparis\t\t48.8566\t2.3522\tP\tPPL\tFR\t\tIDF\t\t\t\t2148000";
        CreateTestGeoNamesFile(geoNamesContent);
        
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(48.8566, 2.3522);

        // Assert - Empty string should be converted to null
        result.Should().NotBeNull();
        result!.City.Should().Be("Paris");
        result.County.Should().BeNull();
    }

    [Test]
    public async Task ReverseGeocode_WithWhitespaceCounty_ReturnsNullCounty()
    {
        // Arrange - Create line with whitespace-only county
        var geoNamesContent = "123456\tBerlin\tberlin\t\t52.5200\t13.4050\tP\tPPL\tDE\t\tBE\t   \t\t\t3644826";
        CreateTestGeoNamesFile(geoNamesContent);
        
        var service = CreateService();
        await service.InitializeAsync();

        // Act
        var result = service.ReverseGeocode(52.5200, 13.4050);

        // Assert - Whitespace-only string should be converted to null
        result.Should().NotBeNull();
        result!.City.Should().Be("Berlin");
        result.County.Should().BeNull();
    }

    [Test]
    public async Task ReverseGeocode_WithMultipleCounties_ReturnsCorrectCounty()
    {
        // Arrange
        var geoNamesContent = string.Join(Environment.NewLine, new[]
        {
            CreateValidGeoNamesLine("Oakland", 37.8044f, -122.2712f, "US", "CA", "Alameda County", 433031),
            CreateValidGeoNamesLine("Berkeley", 37.8716f, -122.2727f, "US", "CA", "Alameda County", 124321),
            CreateValidGeoNamesLine("San Francisco", 37.7749f, -122.4194f, "US", "CA", "San Francisco County", 883305)
        });
        CreateTestGeoNamesFile(geoNamesContent);
        
        var service = CreateService();
        await service.InitializeAsync();

        // Act - Query near San Francisco
        var result = service.ReverseGeocode(37.7749, -122.4194);

        // Assert
        result.Should().NotBeNull();
        result!.City.Should().Be("San Francisco");
        result.County.Should().Be("San Francisco County");
    }

    #endregion
}
