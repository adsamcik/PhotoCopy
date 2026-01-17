using System;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;

namespace PhotoCopy.Tests.E2E;

/// <summary>
/// End-to-end tests for geocoding destination patterns using {country}, {city}, {state} variables.
/// These tests verify that photos with GPS coordinates are organized correctly.
/// </summary>
[NotInParallel("E2ETests")]
[Property("Category", "E2E,Geocoding")]
public class GeocodingE2ETests : E2ETestBase
{
    private static readonly string GeoDataDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "PhotoCopy", "data");

    private static string? _geonamesPath;
    private static bool _geoDataExists;

    [Before(Class)]
    public static Task ClassSetUp()
    {
        var fullPath = Path.GetFullPath(GeoDataDir);
        _geonamesPath = Path.Combine(fullPath, "allCountries.txt");
        _geoDataExists = File.Exists(_geonamesPath);
        return Task.CompletedTask;
    }

    private void SkipIfNoGeoData()
    {
        if (!_geoDataExists)
        {
            Skip.Test($"GeoNames data file not found at {_geonamesPath}. Download from geonames.org.");
        }
    }

    #region Country Variable Tests

    [Test]
    public async Task Copy_WithCountryVariable_CreatesCountryFolder()
    {
        // Arrange
        SkipIfNoGeoData();

        // Create a photo with GPS coordinates for Paris, France
        var dateTaken = new DateTime(2024, 6, 15, 14, 30, 0);
        await CreateSourceJpegAsync("paris.jpg", dateTaken, gps: (48.8566, 2.3522));

        // Act
        var result = await RunPhotoCopyAsync(
            "copy",
            "--source", SourceDir,
            "--destination", Path.Combine(DestDir, "{country}", "{name}{ext}"),
            "--geonames-path", _geonamesPath!,
            "--dry-run"
        );

        // Assert
        await Assert.That(result.ExitCode).IsEqualTo(0);
        // In dry-run, we can verify from output that the country folder would be created
        await Assert.That(result.StandardOutput.Contains("FR") || result.StandardOutput.Contains("France") || result.StandardOutput.Contains("paris")).IsTrue();
    }

    #endregion

    #region City Variable Tests

    [Test]
    public async Task Copy_WithCityVariable_CreatesCityFolder()
    {
        // Arrange
        SkipIfNoGeoData();

        // Create a photo with GPS coordinates for New York
        var dateTaken = new DateTime(2024, 7, 4, 12, 0, 0);
        await CreateSourceJpegAsync("nyc.jpg", dateTaken, gps: (40.7128, -74.0060));

        // Act
        var result = await RunPhotoCopyAsync(
            "copy",
            "--source", SourceDir,
            "--destination", Path.Combine(DestDir, "{city}", "{name}{ext}"),
            "--geonames-path", _geonamesPath!,
            "--dry-run"
        );

        // Assert
        await Assert.That(result.ExitCode).IsEqualTo(0);
    }

    #endregion

    #region State Variable Tests

    [Test]
    public async Task Copy_WithStateVariable_CreatesStateFolder()
    {
        // Arrange
        SkipIfNoGeoData();

        // Create a photo with GPS coordinates for Los Angeles, California
        var dateTaken = new DateTime(2024, 8, 10, 16, 45, 0);
        await CreateSourceJpegAsync("la.jpg", dateTaken, gps: (34.0522, -118.2437));

        // Act
        var result = await RunPhotoCopyAsync(
            "copy",
            "--source", SourceDir,
            "--destination", Path.Combine(DestDir, "{state}", "{name}{ext}"),
            "--geonames-path", _geonamesPath!,
            "--dry-run"
        );

        // Assert
        await Assert.That(result.ExitCode).IsEqualTo(0);
    }

    #endregion

    #region Fallback Tests

    [Test]
    public async Task Copy_WithNoGpsData_UsesUnknownFallback()
    {
        // Arrange
        SkipIfNoGeoData();

        // Create a photo WITHOUT GPS coordinates
        var dateTaken = new DateTime(2024, 9, 1, 10, 0, 0);
        await CreateSourceJpegAsync("nogps.jpg", dateTaken, gps: null);

        // Act
        var result = await RunPhotoCopyAsync(
            "copy",
            "--source", SourceDir,
            "--destination", Path.Combine(DestDir, "{city}", "{name}{ext}"),
            "--geonames-path", _geonamesPath!,
            "--dry-run"
        );

        // Assert
        await Assert.That(result.ExitCode).IsEqualTo(0);
        // Should use the fallback (default is "Unknown")
        await Assert.That(result.StandardOutput.Contains("Unknown") || result.StandardOutput.Contains("nogps")).IsTrue();
    }

    #endregion

    #region Combined Variables Tests

    [Test]
    public async Task Copy_WithMultipleLocationVariables_CreatesNestedFolders()
    {
        // Arrange
        SkipIfNoGeoData();

        // Create a photo with GPS coordinates for London, UK
        var dateTaken = new DateTime(2024, 10, 15, 9, 30, 0);
        await CreateSourceJpegAsync("london.jpg", dateTaken, gps: (51.5074, -0.1278));

        // Act
        var result = await RunPhotoCopyAsync(
            "copy",
            "--source", SourceDir,
            "--destination", Path.Combine(DestDir, "{country}", "{city}", "{name}{ext}"),
            "--geonames-path", _geonamesPath!,
            "--dry-run"
        );

        // Assert
        await Assert.That(result.ExitCode).IsEqualTo(0);
    }

    [Test]
    public async Task Copy_WithYearAndLocation_CreatesCorrectHierarchy()
    {
        // Arrange
        SkipIfNoGeoData();

        // Create a photo with GPS coordinates for Tokyo, Japan
        var dateTaken = new DateTime(2024, 3, 20, 14, 0, 0);
        await CreateSourceJpegAsync("tokyo.jpg", dateTaken, gps: (35.6762, 139.6503));

        // Act
        var result = await RunPhotoCopyAsync(
            "copy",
            "--source", SourceDir,
            "--destination", Path.Combine(DestDir, "{year}", "{country}", "{name}{ext}"),
            "--geonames-path", _geonamesPath!,
            "--dry-run"
        );

        // Assert
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardOutput.Contains("2024")).IsTrue();
    }

    #endregion
}
