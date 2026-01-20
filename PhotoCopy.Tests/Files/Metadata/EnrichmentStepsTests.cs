using System;
using System.IO;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Files;
using PhotoCopy.Files.Metadata;

namespace PhotoCopy.Tests.Files.Metadata;

public class EnrichmentStepsTests
{
    #region DateTimeMetadataEnrichmentStep Tests

    [Test]
    public void DateTimeEnrichmentStep_EnrichesFileWithDateTime()
    {
        // Arrange
        var expectedDateTime = new FileDateTime(
            new DateTime(2024, 7, 20, 10, 30, 45),
            DateTimeSource.ExifDateTimeOriginal);

        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetDateTime(Arg.Any<FileInfo>()).Returns(expectedDateTime);

        var step = new DateTimeMetadataEnrichmentStep(metadataExtractor);
        var tempFile = CreateTempFile("photo_datetime.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            context.Metadata.DateTime.Should().NotBeNull();
            context.Metadata.DateTime.Should().Be(expectedDateTime);
            context.Metadata.DateTime.DateTime.Should().Be(new DateTime(2024, 7, 20, 10, 30, 45));
            context.Metadata.DateTime.Source.Should().Be(DateTimeSource.ExifDateTimeOriginal);

            // Verify extractor was called
            metadataExtractor.Received(1).GetDateTime(Arg.Any<FileInfo>());
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public void DateTimeEnrichmentStep_WithFileCreationSource_UpdatesMetadata()
    {
        // Arrange
        var creationDateTime = new FileDateTime(
            new DateTime(2023, 12, 25, 8, 0, 0),
            DateTimeSource.FileCreation);

        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetDateTime(Arg.Any<FileInfo>()).Returns(creationDateTime);

        var step = new DateTimeMetadataEnrichmentStep(metadataExtractor);
        var tempFile = CreateTempFile("document.pdf");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            context.Metadata.DateTime.Source.Should().Be(DateTimeSource.FileCreation);
            context.Metadata.DateTime.DateTime.Should().Be(new DateTime(2023, 12, 25, 8, 0, 0));
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public void DateTimeEnrichmentStep_OverwritesDefaultDateTime()
    {
        // Arrange
        var exifDateTime = new FileDateTime(
            new DateTime(2022, 5, 10, 15, 45, 30),
            DateTimeSource.ExifDateTimeDigitized);

        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetDateTime(Arg.Any<FileInfo>()).Returns(exifDateTime);

        var step = new DateTimeMetadataEnrichmentStep(metadataExtractor);
        var tempFile = CreateTempFile("vacation.jpg");

        try
        {
            var fileInfo = new FileInfo(tempFile);
            var context = new FileMetadataContext(fileInfo);
            var originalDateTime = context.Metadata.DateTime;

            // Act
            step.Enrich(context);

            // Assert - should have replaced the default file-based datetime with EXIF data
            context.Metadata.DateTime.Should().NotBe(originalDateTime);
            context.Metadata.DateTime.Should().Be(exifDateTime);
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    #endregion

    #region LocationMetadataEnrichmentStep Tests

    [Test]
    public void LocationEnrichmentStep_WithGpsCoordinates_AddsLocation()
    {
        // Arrange
        var latitude = 40.7128;
        var longitude = -74.0060;
        var expectedLocation = new LocationData("New York", "New York", null, "NY", "USA");

        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetCoordinates(Arg.Any<FileInfo>())
            .Returns((latitude, longitude));

        var reverseGeocodingService = Substitute.For<IReverseGeocodingService>();
        reverseGeocodingService.ReverseGeocode(latitude, longitude)
            .Returns(expectedLocation);

        var step = new LocationMetadataEnrichmentStep(metadataExtractor, reverseGeocodingService);
        var tempFile = CreateTempFile("geotagged_photo.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            context.Metadata.Location.Should().NotBeNull();
            context.Metadata.Location.Should().Be(expectedLocation);
            context.Metadata.Location!.City.Should().Be("New York");
            context.Metadata.Location.State.Should().Be("NY");
            context.Metadata.Location.Country.Should().Be("USA");

            // Verify services were called
            metadataExtractor.Received(1).GetCoordinates(Arg.Any<FileInfo>());
            reverseGeocodingService.Received(1).ReverseGeocode(latitude, longitude);
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public void LocationEnrichmentStep_WithoutGps_NoLocation()
    {
        // Arrange
        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetCoordinates(Arg.Any<FileInfo>())
            .Returns((ValueTuple<double, double>?)null);

        var reverseGeocodingService = Substitute.For<IReverseGeocodingService>();

        var step = new LocationMetadataEnrichmentStep(metadataExtractor, reverseGeocodingService);
        var tempFile = CreateTempFile("no_gps_photo.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            context.Metadata.Location.Should().BeNull();

            // Verify extractor was called but geocoding service was NOT called
            metadataExtractor.Received(1).GetCoordinates(Arg.Any<FileInfo>());
            reverseGeocodingService.DidNotReceive().ReverseGeocode(Arg.Any<double>(), Arg.Any<double>());
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public void LocationEnrichmentStep_WithCoordinatesButNoGeocodingResult_LocationIsNull()
    {
        // Arrange
        var latitude = 0.0;
        var longitude = 0.0;

        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetCoordinates(Arg.Any<FileInfo>())
            .Returns((latitude, longitude));

        var reverseGeocodingService = Substitute.For<IReverseGeocodingService>();
        reverseGeocodingService.ReverseGeocode(latitude, longitude)
            .Returns((LocationData?)null);

        var step = new LocationMetadataEnrichmentStep(metadataExtractor, reverseGeocodingService);
        var tempFile = CreateTempFile("ocean_photo.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            context.Metadata.Location.Should().BeNull();

            // Verify both services were called
            metadataExtractor.Received(1).GetCoordinates(Arg.Any<FileInfo>());
            reverseGeocodingService.Received(1).ReverseGeocode(latitude, longitude);
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public void LocationEnrichmentStep_WithDifferentCoordinates_ReturnsCorrectLocation()
    {
        // Arrange
        var latitude = 51.5074;
        var longitude = -0.1278;
        var expectedLocation = new LocationData("London", "London", null, null, "United Kingdom");

        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetCoordinates(Arg.Any<FileInfo>())
            .Returns((latitude, longitude));

        var reverseGeocodingService = Substitute.For<IReverseGeocodingService>();
        reverseGeocodingService.ReverseGeocode(latitude, longitude)
            .Returns(expectedLocation);

        var step = new LocationMetadataEnrichmentStep(metadataExtractor, reverseGeocodingService);
        var tempFile = CreateTempFile("london_photo.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            context.Metadata.Location.Should().NotBeNull();
            context.Metadata.Location!.City.Should().Be("London");
            context.Metadata.Location.State.Should().BeNull();
            context.Metadata.Location.Country.Should().Be("United Kingdom");
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    #endregion

    #region ChecksumMetadataEnrichmentStep Tests

    [Test]
    public void ChecksumEnrichmentStep_WhenEnabled_CalculatesChecksum()
    {
        // Arrange
        var expectedChecksum = "abc123def456789";
        var config = new PhotoCopyConfig { CalculateChecksums = true };
        var options = Microsoft.Extensions.Options.Options.Create(config);

        var checksumCalculator = Substitute.For<IChecksumCalculator>();
        checksumCalculator.Calculate(Arg.Any<FileInfo>()).Returns(expectedChecksum);

        var step = new ChecksumMetadataEnrichmentStep(checksumCalculator, options);
        var tempFile = CreateTempFile("checksum_enabled.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            context.Metadata.Checksum.Should().NotBeNull();
            context.Metadata.Checksum.Should().Be(expectedChecksum);

            // Verify calculator was called
            checksumCalculator.Received(1).Calculate(Arg.Any<FileInfo>());
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public void ChecksumEnrichmentStep_WhenDisabled_SkipsCalculation()
    {
        // Arrange
        var config = new PhotoCopyConfig { CalculateChecksums = false };
        var options = Microsoft.Extensions.Options.Options.Create(config);

        var checksumCalculator = Substitute.For<IChecksumCalculator>();

        var step = new ChecksumMetadataEnrichmentStep(checksumCalculator, options);
        var tempFile = CreateTempFile("checksum_disabled.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            context.Metadata.Checksum.Should().BeNull();

            // Verify calculator was NOT called
            checksumCalculator.DidNotReceive().Calculate(Arg.Any<FileInfo>());
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public void ChecksumEnrichmentStep_WhenEnabledByDefault_CalculatesChecksum()
    {
        // Arrange - default config has CalculateChecksums = true
        var config = new PhotoCopyConfig();
        var options = Microsoft.Extensions.Options.Options.Create(config);

        var checksumCalculator = Substitute.For<IChecksumCalculator>();
        checksumCalculator.Calculate(Arg.Any<FileInfo>()).Returns("default_checksum_value");

        var step = new ChecksumMetadataEnrichmentStep(checksumCalculator, options);
        var tempFile = CreateTempFile("checksum_default.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert - default is true, so checksum should be calculated
            context.Metadata.Checksum.Should().NotBeNull();
            context.Metadata.Checksum.Should().Be("default_checksum_value");
            checksumCalculator.Received(1).Calculate(Arg.Any<FileInfo>());
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public void ChecksumEnrichmentStep_WithDifferentChecksum_ReturnsCorrectValue()
    {
        // Arrange
        var sha256Checksum = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        var config = new PhotoCopyConfig { CalculateChecksums = true };
        var options = Microsoft.Extensions.Options.Options.Create(config);

        var checksumCalculator = Substitute.For<IChecksumCalculator>();
        checksumCalculator.Calculate(Arg.Any<FileInfo>()).Returns(sha256Checksum);

        var step = new ChecksumMetadataEnrichmentStep(checksumCalculator, options);
        var tempFile = CreateTempFile("sha256_checksum.dat");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            context.Metadata.Checksum.Should().Be(sha256Checksum);
            context.Metadata.Checksum!.Length.Should().Be(64); // SHA-256 produces 64 hex characters
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public void ChecksumEnrichmentStep_DoesNotAffectOtherMetadataProperties()
    {
        // Arrange
        var config = new PhotoCopyConfig { CalculateChecksums = true };
        var options = Microsoft.Extensions.Options.Options.Create(config);

        var checksumCalculator = Substitute.For<IChecksumCalculator>();
        checksumCalculator.Calculate(Arg.Any<FileInfo>()).Returns("checksum123");

        var step = new ChecksumMetadataEnrichmentStep(checksumCalculator, options);
        var tempFile = CreateTempFile("isolated_checksum.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));
            var originalDateTime = context.Metadata.DateTime;
            var testLocation = new LocationData("TestCity", "TestCity", null, "TestState", "TestCountry");
            context.Metadata.Location = testLocation;

            // Act
            step.Enrich(context);

            // Assert - other properties should remain unchanged
            context.Metadata.DateTime.Should().Be(originalDateTime);
            context.Metadata.Location.Should().Be(testLocation);
            context.Metadata.Checksum.Should().Be("checksum123");
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    #endregion
    #region LocationMetadataEnrichmentStep UnknownReason Tests

    [Test]
    public void LocationEnrichmentStep_WithGpsCoordinates_SetsUnknownReasonToNone()
    {
        // Arrange
        var latitude = 40.7128;
        var longitude = -74.0060;
        var expectedLocation = new LocationData("New York", "New York", null, "NY", "USA");

        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetCoordinates(Arg.Any<FileInfo>())
            .Returns((latitude, longitude));

        var reverseGeocodingService = Substitute.For<IReverseGeocodingService>();
        reverseGeocodingService.ReverseGeocode(latitude, longitude)
            .Returns(expectedLocation);

        var step = new LocationMetadataEnrichmentStep(metadataExtractor, reverseGeocodingService);
        var tempFile = CreateTempFile("location_test.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            context.Metadata.Location.Should().NotBeNull();
            context.Metadata.UnknownReason.Should().Be(UnknownFileReason.None);
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public void LocationEnrichmentStep_WithoutGps_SetsUnknownReasonToNoGpsData()
    {
        // Arrange
        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetCoordinates(Arg.Any<FileInfo>())
            .Returns((ValueTuple<double, double>?)null);

        var reverseGeocodingService = Substitute.For<IReverseGeocodingService>();

        var step = new LocationMetadataEnrichmentStep(metadataExtractor, reverseGeocodingService);
        var tempFile = CreateTempFile("no_gps_reason.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            context.Metadata.Location.Should().BeNull();
            context.Metadata.UnknownReason.Should().Be(UnknownFileReason.NoGpsData);
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public void LocationEnrichmentStep_WhenGpsExtractionThrows_SetsUnknownReasonToGpsExtractionError()
    {
        // Arrange
        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetCoordinates(Arg.Any<FileInfo>())
            .Returns(x => throw new Exception("Corrupt EXIF data"));

        var reverseGeocodingService = Substitute.For<IReverseGeocodingService>();

        var step = new LocationMetadataEnrichmentStep(metadataExtractor, reverseGeocodingService);
        var tempFile = CreateTempFile("corrupt_exif.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            context.Metadata.Location.Should().BeNull();
            context.Metadata.UnknownReason.Should().Be(UnknownFileReason.GpsExtractionError);
            
            // Verify geocoding was not attempted
            reverseGeocodingService.DidNotReceive().ReverseGeocode(Arg.Any<double>(), Arg.Any<double>());
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public void LocationEnrichmentStep_WhenGeocodingReturnsNull_SetsUnknownReasonToGeocodingFailed()
    {
        // Arrange
        var latitude = 0.0;
        var longitude = 0.0;

        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetCoordinates(Arg.Any<FileInfo>())
            .Returns((latitude, longitude));

        var reverseGeocodingService = Substitute.For<IReverseGeocodingService>();
        reverseGeocodingService.ReverseGeocode(latitude, longitude)
            .Returns((LocationData?)null);

        var step = new LocationMetadataEnrichmentStep(metadataExtractor, reverseGeocodingService);
        var tempFile = CreateTempFile("ocean_coords.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            context.Metadata.Location.Should().BeNull();
            context.Metadata.UnknownReason.Should().Be(UnknownFileReason.GeocodingFailed);
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    #endregion

    #region LocationMetadataEnrichmentStep GpsIndex Population Tests

    [Test]
    public void LocationEnrichmentStep_WithGpsIndex_AddsLocationToIndex()
    {
        // Arrange
        var latitude = 40.7128;
        var longitude = -74.0060;
        var expectedLocation = new LocationData("New York", "New York", null, "NY", "USA");
        var gpsIndex = new GpsLocationIndex();

        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetCoordinates(Arg.Any<FileInfo>())
            .Returns((latitude, longitude));

        var reverseGeocodingService = Substitute.For<IReverseGeocodingService>();
        reverseGeocodingService.ReverseGeocode(latitude, longitude)
            .Returns(expectedLocation);

        var step = new LocationMetadataEnrichmentStep(metadataExtractor, reverseGeocodingService, gpsIndex);
        var tempFile = CreateTempFile("gps_index_test.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            gpsIndex.Count.Should().Be(1);
            var result = gpsIndex.FindNearest(context.Metadata.DateTime.DateTime, TimeSpan.FromMinutes(1));
            result.Should().NotBeNull();
            result!.Value.Latitude.Should().BeApproximately(latitude, 0.0001);
            result!.Value.Longitude.Should().BeApproximately(longitude, 0.0001);
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public void LocationEnrichmentStep_WithNoGps_DoesNotAddToIndex()
    {
        // Arrange
        var gpsIndex = new GpsLocationIndex();

        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetCoordinates(Arg.Any<FileInfo>())
            .Returns((ValueTuple<double, double>?)null);

        var reverseGeocodingService = Substitute.For<IReverseGeocodingService>();

        var step = new LocationMetadataEnrichmentStep(metadataExtractor, reverseGeocodingService, gpsIndex);
        var tempFile = CreateTempFile("no_gps_index.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            gpsIndex.Count.Should().Be(0);
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public void LocationEnrichmentStep_StoresCoordinatesInContext()
    {
        // Arrange
        var latitude = 48.8566;
        var longitude = 2.3522;
        var expectedLocation = new LocationData("Paris", "Paris", null, "Île-de-France", "FR");

        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetCoordinates(Arg.Any<FileInfo>())
            .Returns((latitude, longitude));

        var reverseGeocodingService = Substitute.For<IReverseGeocodingService>();
        reverseGeocodingService.ReverseGeocode(latitude, longitude)
            .Returns(expectedLocation);

        var step = new LocationMetadataEnrichmentStep(metadataExtractor, reverseGeocodingService);
        var tempFile = CreateTempFile("coords_in_context.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            context.Coordinates.Should().NotBeNull();
            context.Coordinates!.Value.Latitude.Should().BeApproximately(latitude, 0.0001);
            context.Coordinates!.Value.Longitude.Should().BeApproximately(longitude, 0.0001);
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    #endregion

    #region CompanionGpsEnrichmentStep Tests

    [Test]
    public void CompanionGpsEnrichmentStep_WhenDisabled_DoesNotEnrich()
    {
        // Arrange
        var config = new PhotoCopyConfig { GpsProximityWindowMinutes = null };
        var options = Microsoft.Extensions.Options.Options.Create(config);
        var gpsIndex = new GpsLocationIndex();
        gpsIndex.AddLocation(new DateTime(2024, 7, 20, 10, 0, 0), 48.8566, 2.3522);
        
        var geocodingService = Substitute.For<IReverseGeocodingService>();
        var logger = Substitute.For<ILogger<CompanionGpsEnrichmentStep>>();
        
        var step = new CompanionGpsEnrichmentStep(gpsIndex, geocodingService, options, logger);
        var tempFile = CreateTempFile("disabled_companion.mp4");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));
            context.Metadata.UnknownReason = UnknownFileReason.NoGpsData;

            // Act
            step.Enrich(context);

            // Assert
            context.Metadata.Location.Should().BeNull();
            geocodingService.DidNotReceive().ReverseGeocode(Arg.Any<double>(), Arg.Any<double>());
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public void CompanionGpsEnrichmentStep_FileWithLocation_SkipsFile()
    {
        // Arrange
        var config = new PhotoCopyConfig { GpsProximityWindowMinutes = 5 };
        var options = Microsoft.Extensions.Options.Options.Create(config);
        var gpsIndex = new GpsLocationIndex();
        gpsIndex.AddLocation(new DateTime(2024, 7, 20, 10, 0, 0), 48.8566, 2.3522);
        
        var geocodingService = Substitute.For<IReverseGeocodingService>();
        var logger = Substitute.For<ILogger<CompanionGpsEnrichmentStep>>();
        
        var step = new CompanionGpsEnrichmentStep(gpsIndex, geocodingService, options, logger);
        var tempFile = CreateTempFile("has_location.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));
            context.Metadata.Location = new LocationData("Existing", "Existing", null, null, "US");
            context.Metadata.UnknownReason = UnknownFileReason.None;

            // Act
            step.Enrich(context);

            // Assert
            context.Metadata.Location!.City.Should().Be("Existing");
            geocodingService.DidNotReceive().ReverseGeocode(Arg.Any<double>(), Arg.Any<double>());
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public void CompanionGpsEnrichmentStep_FileWithCoordinates_SkipsFile()
    {
        // Arrange
        var config = new PhotoCopyConfig { GpsProximityWindowMinutes = 5 };
        var options = Microsoft.Extensions.Options.Options.Create(config);
        var gpsIndex = new GpsLocationIndex();
        gpsIndex.AddLocation(new DateTime(2024, 7, 20, 10, 0, 0), 48.8566, 2.3522);
        
        var geocodingService = Substitute.For<IReverseGeocodingService>();
        var logger = Substitute.For<ILogger<CompanionGpsEnrichmentStep>>();
        
        var step = new CompanionGpsEnrichmentStep(gpsIndex, geocodingService, options, logger);
        var tempFile = CreateTempFile("has_coords.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));
            context.Coordinates = (51.5074, -0.1278); // Already has coordinates
            context.Metadata.UnknownReason = UnknownFileReason.GeocodingFailed;

            // Act
            step.Enrich(context);

            // Assert
            context.Metadata.Location.Should().BeNull();
            geocodingService.DidNotReceive().ReverseGeocode(Arg.Any<double>(), Arg.Any<double>());
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public void CompanionGpsEnrichmentStep_FileWithNoGpsData_UsesCompanionGps()
    {
        // Arrange
        var config = new PhotoCopyConfig { GpsProximityWindowMinutes = 5 };
        var options = Microsoft.Extensions.Options.Options.Create(config);
        var gpsIndex = new GpsLocationIndex();
        var targetTime = new DateTime(2024, 7, 20, 10, 0, 0);
        gpsIndex.AddLocation(targetTime, 48.8566, 2.3522);
        
        var locationData = new LocationData("Paris", "Paris", null, "Île-de-France", "FR");
        var geocodingService = Substitute.For<IReverseGeocodingService>();
        geocodingService.ReverseGeocode(Arg.Any<double>(), Arg.Any<double>()).Returns(locationData);
        
        var logger = Substitute.For<ILogger<CompanionGpsEnrichmentStep>>();
        
        var step = new CompanionGpsEnrichmentStep(gpsIndex, geocodingService, options, logger);
        var tempFile = CreateTempFile("no_gps_companion.mp4");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));
            // Set file time to be within the window
            context.Metadata.DateTime = new FileDateTime(targetTime.AddMinutes(2), DateTimeSource.ExifDateTimeOriginal);
            context.Metadata.UnknownReason = UnknownFileReason.NoGpsData;

            // Act
            step.Enrich(context);

            // Assert
            context.Metadata.Location.Should().NotBeNull();
            context.Metadata.Location!.City.Should().Be("Paris");
            context.Metadata.UnknownReason.Should().Be(UnknownFileReason.None);
            context.Coordinates.Should().NotBeNull();
            
            geocodingService.Received(1).ReverseGeocode(
                Arg.Is<double>(lat => Math.Abs(lat - 48.8566) < 0.0001),
                Arg.Is<double>(lon => Math.Abs(lon - 2.3522) < 0.0001));
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public void CompanionGpsEnrichmentStep_NoNearbyGps_LeavesFileUnchanged()
    {
        // Arrange
        var config = new PhotoCopyConfig { GpsProximityWindowMinutes = 5 };
        var options = Microsoft.Extensions.Options.Options.Create(config);
        var gpsIndex = new GpsLocationIndex();
        gpsIndex.AddLocation(new DateTime(2024, 7, 20, 10, 0, 0), 48.8566, 2.3522);
        
        var geocodingService = Substitute.For<IReverseGeocodingService>();
        var logger = Substitute.For<ILogger<CompanionGpsEnrichmentStep>>();
        
        var step = new CompanionGpsEnrichmentStep(gpsIndex, geocodingService, options, logger);
        var tempFile = CreateTempFile("far_from_gps.mp4");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));
            // 20 minutes away - outside the 5 minute window
            context.Metadata.DateTime = new FileDateTime(new DateTime(2024, 7, 20, 10, 20, 0), DateTimeSource.ExifDateTimeOriginal);
            context.Metadata.UnknownReason = UnknownFileReason.NoGpsData;

            // Act
            step.Enrich(context);

            // Assert
            context.Metadata.Location.Should().BeNull();
            context.Metadata.UnknownReason.Should().Be(UnknownFileReason.NoGpsData);
            geocodingService.DidNotReceive().ReverseGeocode(Arg.Any<double>(), Arg.Any<double>());
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public void CompanionGpsEnrichmentStep_GeocodingFails_SetsGeocodingFailedReason()
    {
        // Arrange
        var config = new PhotoCopyConfig { GpsProximityWindowMinutes = 5 };
        var options = Microsoft.Extensions.Options.Options.Create(config);
        var gpsIndex = new GpsLocationIndex();
        var targetTime = new DateTime(2024, 7, 20, 10, 0, 0);
        gpsIndex.AddLocation(targetTime, 48.8566, 2.3522);
        
        var geocodingService = Substitute.For<IReverseGeocodingService>();
        geocodingService.ReverseGeocode(Arg.Any<double>(), Arg.Any<double>()).Returns((LocationData?)null);
        
        var logger = Substitute.For<ILogger<CompanionGpsEnrichmentStep>>();
        
        var step = new CompanionGpsEnrichmentStep(gpsIndex, geocodingService, options, logger);
        var tempFile = CreateTempFile("geocoding_failed.mp4");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));
            context.Metadata.DateTime = new FileDateTime(targetTime.AddMinutes(2), DateTimeSource.ExifDateTimeOriginal);
            context.Metadata.UnknownReason = UnknownFileReason.NoGpsData;

            // Act
            step.Enrich(context);

            // Assert
            context.Metadata.Location.Should().BeNull();
            context.Metadata.UnknownReason.Should().Be(UnknownFileReason.GeocodingFailed);
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    #endregion

    #region Helper Methods

    private static string CreateTempFile(string fileName)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "EnrichmentStepsTests", fileName);
        var directory = Path.GetDirectoryName(tempPath)!;

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(tempPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }); // Minimal JPEG header
        return tempPath;
    }

    private static void CleanupFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #endregion
}
