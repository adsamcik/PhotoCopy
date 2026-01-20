using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Files;

namespace PhotoCopy.Tests.Files;

public class CompanionGpsEnricherTests
{
    private string CreateTempFile(string fileName)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PhotoCopyTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, fileName);
        File.WriteAllText(path, "test content");
        return path;
    }

    private static void CleanupFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            var dir = Path.GetDirectoryName(path);
            if (dir != null && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #region IsEnabled Tests

    [Test]
    public void IsEnabled_WhenWindowNotConfigured_ReturnsFalse()
    {
        // Arrange
        var config = new PhotoCopyConfig { GpsProximityWindowMinutes = null };
        var enricher = CreateEnricher(config);

        // Assert
        enricher.IsEnabled.Should().BeFalse();
    }

    [Test]
    public void IsEnabled_WhenWindowIsZero_ReturnsFalse()
    {
        // Arrange
        var config = new PhotoCopyConfig { GpsProximityWindowMinutes = 0 };
        var enricher = CreateEnricher(config);

        // Assert
        enricher.IsEnabled.Should().BeFalse();
    }

    [Test]
    public void IsEnabled_WhenWindowIsNegative_ReturnsFalse()
    {
        // Arrange
        var config = new PhotoCopyConfig { GpsProximityWindowMinutes = -5 };
        var enricher = CreateEnricher(config);

        // Assert
        enricher.IsEnabled.Should().BeFalse();
    }

    [Test]
    public void IsEnabled_WhenWindowIsPositive_ReturnsTrue()
    {
        // Arrange
        var config = new PhotoCopyConfig { GpsProximityWindowMinutes = 5 };
        var enricher = CreateEnricher(config);

        // Assert
        enricher.IsEnabled.Should().BeTrue();
    }

    #endregion

    #region EnrichFiles Tests

    [Test]
    public void EnrichFiles_WhenDisabled_DoesNothing()
    {
        // Arrange
        var config = new PhotoCopyConfig { GpsProximityWindowMinutes = null };
        var gpsIndex = new GpsLocationIndex();
        gpsIndex.AddLocation(new DateTime(2024, 7, 20, 10, 0, 0), 48.8566, 2.3522);
        
        var geocodingService = Substitute.For<IReverseGeocodingService>();
        var enricher = CreateEnricher(config, gpsIndex, geocodingService);

        var tempFile = CreateTempFile("video.mp4");
        var file = CreateFileWithMetadata(tempFile, new DateTime(2024, 7, 20, 10, 2, 0), UnknownFileReason.NoGpsData);
        
        // Act
        enricher.EnrichFiles(new[] { file });

        // Assert
        file.Location.Should().BeNull();
        geocodingService.DidNotReceive().ReverseGeocode(Arg.Any<double>(), Arg.Any<double>());
    }

    [Test]
    public void EnrichFiles_FileWithLocation_SkipsFile()
    {
        // Arrange
        var config = new PhotoCopyConfig { GpsProximityWindowMinutes = 5 };
        var gpsIndex = new GpsLocationIndex();
        gpsIndex.AddLocation(new DateTime(2024, 7, 20, 10, 0, 0), 48.8566, 2.3522);
        
        var geocodingService = Substitute.For<IReverseGeocodingService>();
        var enricher = CreateEnricher(config, gpsIndex, geocodingService);

        var tempFile = CreateTempFile("photo.jpg");
        var file = CreateFileWithMetadata(tempFile, new DateTime(2024, 7, 20, 10, 2, 0), UnknownFileReason.None);
        file.Location = new LocationData("Paris", "Paris", null, "Île-de-France", "FR");
        
        // Act
        enricher.EnrichFiles(new[] { file });

        // Assert
        geocodingService.DidNotReceive().ReverseGeocode(Arg.Any<double>(), Arg.Any<double>());
    }

    [Test]
    public void EnrichFiles_FileWithGpsExtractionError_SkipsFile()
    {
        // Arrange
        var config = new PhotoCopyConfig { GpsProximityWindowMinutes = 5 };
        var gpsIndex = new GpsLocationIndex();
        gpsIndex.AddLocation(new DateTime(2024, 7, 20, 10, 0, 0), 48.8566, 2.3522);
        
        var geocodingService = Substitute.For<IReverseGeocodingService>();
        var enricher = CreateEnricher(config, gpsIndex, geocodingService);

        var tempFile = CreateTempFile("corrupted.jpg");
        var file = CreateFileWithMetadata(tempFile, new DateTime(2024, 7, 20, 10, 2, 0), UnknownFileReason.GpsExtractionError);
        
        // Act
        enricher.EnrichFiles(new[] { file });

        // Assert
        file.Location.Should().BeNull();
        geocodingService.DidNotReceive().ReverseGeocode(Arg.Any<double>(), Arg.Any<double>());
    }

    [Test]
    public void EnrichFiles_FileWithNoGpsData_UsesCompanionGps()
    {
        // Arrange
        var config = new PhotoCopyConfig { GpsProximityWindowMinutes = 5 };
        var gpsIndex = new GpsLocationIndex();
        gpsIndex.AddLocation(new DateTime(2024, 7, 20, 10, 0, 0), 48.8566, 2.3522);
        
        var locationData = new LocationData("Paris", "Paris", null, "Île-de-France", "FR");
        var geocodingService = Substitute.For<IReverseGeocodingService>();
        geocodingService.ReverseGeocode(Arg.Any<double>(), Arg.Any<double>()).Returns(locationData);
        
        var enricher = CreateEnricher(config, gpsIndex, geocodingService);

        var tempFile = CreateTempFile("video.mp4");
        var file = CreateFileWithMetadata(tempFile, new DateTime(2024, 7, 20, 10, 2, 0), UnknownFileReason.NoGpsData);
        
        // Act
        enricher.EnrichFiles(new[] { file });

        // Assert
        file.Location.Should().NotBeNull();
        file.Location!.City.Should().Be("Paris");
        file.Location!.Country.Should().Be("FR");
        file.UnknownReason.Should().Be(UnknownFileReason.None);
        
        geocodingService.Received(1).ReverseGeocode(
            Arg.Is<double>(lat => Math.Abs(lat - 48.8566) < 0.0001),
            Arg.Is<double>(lon => Math.Abs(lon - 2.3522) < 0.0001));
    }

    [Test]
    public void EnrichFiles_NoNearbyGps_LeavesFileUnchanged()
    {
        // Arrange
        var config = new PhotoCopyConfig { GpsProximityWindowMinutes = 5 };
        var gpsIndex = new GpsLocationIndex();
        gpsIndex.AddLocation(new DateTime(2024, 7, 20, 10, 0, 0), 48.8566, 2.3522);
        
        var geocodingService = Substitute.For<IReverseGeocodingService>();
        var enricher = CreateEnricher(config, gpsIndex, geocodingService);

        var tempFile = CreateTempFile("video.mp4");
        // 20 minutes after the GPS location - outside 5 minute window
        var file = CreateFileWithMetadata(tempFile, new DateTime(2024, 7, 20, 10, 20, 0), UnknownFileReason.NoGpsData);
        
        // Act
        enricher.EnrichFiles(new[] { file });

        // Assert
        file.Location.Should().BeNull();
        file.UnknownReason.Should().Be(UnknownFileReason.NoGpsData);
        geocodingService.DidNotReceive().ReverseGeocode(Arg.Any<double>(), Arg.Any<double>());
    }

    [Test]
    public void EnrichFiles_GeocodingFails_SetsGeocodingFailedReason()
    {
        // Arrange
        var config = new PhotoCopyConfig { GpsProximityWindowMinutes = 5 };
        var gpsIndex = new GpsLocationIndex();
        gpsIndex.AddLocation(new DateTime(2024, 7, 20, 10, 0, 0), 48.8566, 2.3522);
        
        var geocodingService = Substitute.For<IReverseGeocodingService>();
        geocodingService.ReverseGeocode(Arg.Any<double>(), Arg.Any<double>()).Returns((LocationData?)null);
        
        var enricher = CreateEnricher(config, gpsIndex, geocodingService);

        var tempFile = CreateTempFile("video.mp4");
        var file = CreateFileWithMetadata(tempFile, new DateTime(2024, 7, 20, 10, 2, 0), UnknownFileReason.NoGpsData);
        
        // Act
        enricher.EnrichFiles(new[] { file });

        // Assert
        file.Location.Should().BeNull();
        file.UnknownReason.Should().Be(UnknownFileReason.GeocodingFailed);
    }

    [Test]
    public void EnrichFiles_EmptyGpsIndex_DoesNothing()
    {
        // Arrange
        var config = new PhotoCopyConfig { GpsProximityWindowMinutes = 5 };
        var gpsIndex = new GpsLocationIndex(); // Empty
        
        var geocodingService = Substitute.For<IReverseGeocodingService>();
        var enricher = CreateEnricher(config, gpsIndex, geocodingService);

        var tempFile = CreateTempFile("video.mp4");
        var file = CreateFileWithMetadata(tempFile, new DateTime(2024, 7, 20, 10, 2, 0), UnknownFileReason.NoGpsData);
        
        // Act
        enricher.EnrichFiles(new[] { file });

        // Assert
        file.Location.Should().BeNull();
        geocodingService.DidNotReceive().ReverseGeocode(Arg.Any<double>(), Arg.Any<double>());
    }

    [Test]
    public void EnrichFiles_MultipleFiles_EnrichesOnlyMissingGps()
    {
        // Arrange
        var config = new PhotoCopyConfig { GpsProximityWindowMinutes = 5 };
        var gpsIndex = new GpsLocationIndex();
        gpsIndex.AddLocation(new DateTime(2024, 7, 20, 10, 0, 0), 48.8566, 2.3522);
        
        var locationData = new LocationData("Paris", "Paris", null, "Île-de-France", "FR");
        var geocodingService = Substitute.For<IReverseGeocodingService>();
        geocodingService.ReverseGeocode(Arg.Any<double>(), Arg.Any<double>()).Returns(locationData);
        
        var enricher = CreateEnricher(config, gpsIndex, geocodingService);

        var tempFile1 = CreateTempFile("photo_with_gps.jpg");
        var file1 = CreateFileWithMetadata(tempFile1, new DateTime(2024, 7, 20, 10, 0, 0), UnknownFileReason.None);
        file1.Location = new LocationData("Existing", "Existing", null, null, "US");

        var tempFile2 = CreateTempFile("video_no_gps.mp4");
        var file2 = CreateFileWithMetadata(tempFile2, new DateTime(2024, 7, 20, 10, 2, 0), UnknownFileReason.NoGpsData);

        var tempFile3 = CreateTempFile("video_far.mp4");
        var file3 = CreateFileWithMetadata(tempFile3, new DateTime(2024, 7, 20, 11, 0, 0), UnknownFileReason.NoGpsData);
        
        // Act
        enricher.EnrichFiles(new[] { file1, file2, file3 });

        // Assert
        // File1 should keep its existing location
        file1.Location!.City.Should().Be("Existing");
        
        // File2 should get companion GPS
        file2.Location.Should().NotBeNull();
        file2.Location!.City.Should().Be("Paris");
        
        // File3 is too far away, should not get GPS
        file3.Location.Should().BeNull();
        
        // Only one geocoding call (for file2)
        geocodingService.Received(1).ReverseGeocode(Arg.Any<double>(), Arg.Any<double>());
    }

    #endregion

    #region Helper Methods

    private CompanionGpsEnricher CreateEnricher(
        PhotoCopyConfig config,
        IGpsLocationIndex? gpsIndex = null,
        IReverseGeocodingService? geocodingService = null)
    {
        return new CompanionGpsEnricher(
            gpsIndex ?? new GpsLocationIndex(),
            geocodingService ?? Substitute.For<IReverseGeocodingService>(),
            Options.Create(config),
            Substitute.For<ILogger<CompanionGpsEnricher>>());
    }

    private FileWithMetadata CreateFileWithMetadata(string path, DateTime dateTime, UnknownFileReason unknownReason)
    {
        var fileInfo = new FileInfo(path);
        var fileDateTime = new FileDateTime(dateTime, DateTimeSource.ExifDateTimeOriginal);
        var file = new FileWithMetadata(fileInfo, fileDateTime, Substitute.For<ILogger>())
        {
            UnknownReason = unknownReason
        };
        return file;
    }

    #endregion
}
