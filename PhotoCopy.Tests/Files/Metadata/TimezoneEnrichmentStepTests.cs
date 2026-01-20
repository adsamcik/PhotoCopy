using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Configuration;
using PhotoCopy.Files;
using PhotoCopy.Files.Metadata;

namespace PhotoCopy.Tests.Files.Metadata;

public class TimezoneEnrichmentStepTests
{
    private static IOptions<PhotoCopyConfig> CreateOptions(TimezoneHandling handling = TimezoneHandling.Original)
    {
        var config = new PhotoCopyConfig { TimezoneHandling = handling };
        return Options.Create(config);
    }

    private static FileMetadataContext CreateContext(DateTime dateTime, (double Latitude, double Longitude)? coordinates = null)
    {
        var fileInfo = new FileInfo(Path.GetTempFileName());
        var context = new FileMetadataContext(fileInfo);
        context.Metadata.DateTime = new FileDateTime(dateTime, DateTimeSource.ExifDateTimeOriginal);
        context.Coordinates = coordinates;
        return context;
    }

    #region Original Mode Tests

    [Test]
    public async Task Enrich_OriginalMode_DoesNotModifyDateTime()
    {
        // Arrange
        var options = CreateOptions(TimezoneHandling.Original);
        var logger = Substitute.For<ILogger<TimezoneEnrichmentStep>>();
        var step = new TimezoneEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 6, 15, 10, 30, 0);
        var context = CreateContext(originalDate);

        // Act
        step.Enrich(context);

        // Assert
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(originalDate);
    }

    [Test]
    public async Task Enrich_OriginalMode_PreservesSource()
    {
        // Arrange
        var options = CreateOptions(TimezoneHandling.Original);
        var logger = Substitute.For<ILogger<TimezoneEnrichmentStep>>();
        var step = new TimezoneEnrichmentStep(options, logger);

        var context = CreateContext(new DateTime(2024, 6, 15, 10, 30, 0));
        context.Metadata.DateTime = new FileDateTime(context.Metadata.DateTime.DateTime, DateTimeSource.ExifDateTimeDigitized);

        // Act
        step.Enrich(context);

        // Assert
        await Assert.That(context.Metadata.DateTime.Source).IsEqualTo(DateTimeSource.ExifDateTimeDigitized);
    }

    [Test]
    public async Task Enrich_OriginalMode_DoesNotLog()
    {
        // Arrange
        var options = CreateOptions(TimezoneHandling.Original);
        var logger = Substitute.For<ILogger<TimezoneEnrichmentStep>>();
        var step = new TimezoneEnrichmentStep(options, logger);

        var context = CreateContext(new DateTime(2024, 6, 15, 10, 30, 0));

        // Act
        step.Enrich(context);

        // Assert - no logging should occur for Original mode
        logger.DidNotReceive().Log(
            Arg.Any<LogLevel>(),
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region Local Mode Tests

    [Test]
    public async Task Enrich_LocalMode_ConvertsToLocalTimezone()
    {
        // Arrange
        var options = CreateOptions(TimezoneHandling.Local);
        var logger = Substitute.For<ILogger<TimezoneEnrichmentStep>>();
        var step = new TimezoneEnrichmentStep(options, logger);

        var utcDate = new DateTime(2024, 6, 15, 12, 0, 0);
        var context = CreateContext(utcDate);

        // Act
        step.Enrich(context);

        // Assert - the result depends on the local timezone, but it should be converted
        var expectedLocal = DateTime.SpecifyKind(utcDate, DateTimeKind.Utc).ToLocalTime();
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(expectedLocal);
    }

    [Test]
    public async Task Enrich_LocalMode_PreservesSource()
    {
        // Arrange
        var options = CreateOptions(TimezoneHandling.Local);
        var logger = Substitute.For<ILogger<TimezoneEnrichmentStep>>();
        var step = new TimezoneEnrichmentStep(options, logger);

        var context = CreateContext(new DateTime(2024, 6, 15, 10, 30, 0));
        context.Metadata.DateTime = new FileDateTime(context.Metadata.DateTime.DateTime, DateTimeSource.ExifDateTimeDigitized);

        // Act
        step.Enrich(context);

        // Assert
        await Assert.That(context.Metadata.DateTime.Source).IsEqualTo(DateTimeSource.ExifDateTimeDigitized);
    }

    [Test]
    public async Task Enrich_LocalMode_LogsMode()
    {
        // Arrange
        var options = CreateOptions(TimezoneHandling.Local);
        var logger = Substitute.For<ILogger<TimezoneEnrichmentStep>>();
        var step = new TimezoneEnrichmentStep(options, logger);

        var context = CreateContext(new DateTime(2024, 6, 15, 10, 30, 0));

        // Act
        step.Enrich(context);

        // Assert
        logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region GPS Derived Mode Tests

    [Test]
    public async Task Enrich_GpsDerived_AppliesCorrectOffsetForEasternHemisphere()
    {
        // Arrange - Tokyo at ~139.7° East = ~+9 hours
        var options = CreateOptions(TimezoneHandling.GpsDerived);
        var logger = Substitute.For<ILogger<TimezoneEnrichmentStep>>();
        var step = new TimezoneEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 6, 15, 12, 0, 0);
        var context = CreateContext(originalDate, (35.6762, 139.6503)); // Tokyo coordinates

        // Act
        step.Enrich(context);

        // Assert - 139.65 / 15 ≈ 9.31, rounds to 9.5 hours
        var expectedOffset = TimeSpan.FromHours(9.5);
        var expectedDate = originalDate.Add(expectedOffset);
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(expectedDate);
    }

    [Test]
    public async Task Enrich_GpsDerived_AppliesCorrectOffsetForWesternHemisphere()
    {
        // Arrange - New York at ~-74° West = ~-5 hours
        var options = CreateOptions(TimezoneHandling.GpsDerived);
        var logger = Substitute.For<ILogger<TimezoneEnrichmentStep>>();
        var step = new TimezoneEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 6, 15, 12, 0, 0);
        var context = CreateContext(originalDate, (40.7128, -74.0060)); // NYC coordinates

        // Act
        step.Enrich(context);

        // Assert - -74 / 15 ≈ -4.93, rounds to -5 hours
        var expectedOffset = TimeSpan.FromHours(-5);
        var expectedDate = originalDate.Add(expectedOffset);
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(expectedDate);
    }

    [Test]
    public async Task Enrich_GpsDerived_AppliesZeroOffsetForPrimeMeridian()
    {
        // Arrange - London at ~0° = 0 hours
        var options = CreateOptions(TimezoneHandling.GpsDerived);
        var logger = Substitute.For<ILogger<TimezoneEnrichmentStep>>();
        var step = new TimezoneEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 6, 15, 12, 0, 0);
        var context = CreateContext(originalDate, (51.5074, -0.1278)); // London coordinates

        // Act
        step.Enrich(context);

        // Assert - close to 0, rounds to 0 hours
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(originalDate);
    }

    [Test]
    public async Task Enrich_GpsDerived_WithoutCoordinates_DoesNotModifyDateTime()
    {
        // Arrange
        var options = CreateOptions(TimezoneHandling.GpsDerived);
        var logger = Substitute.For<ILogger<TimezoneEnrichmentStep>>();
        var step = new TimezoneEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 6, 15, 12, 0, 0);
        var context = CreateContext(originalDate, coordinates: null);

        // Act
        step.Enrich(context);

        // Assert
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(originalDate);
    }

    [Test]
    public async Task Enrich_GpsDerived_PreservesSource()
    {
        // Arrange
        var options = CreateOptions(TimezoneHandling.GpsDerived);
        var logger = Substitute.For<ILogger<TimezoneEnrichmentStep>>();
        var step = new TimezoneEnrichmentStep(options, logger);

        var context = CreateContext(new DateTime(2024, 6, 15, 10, 30, 0), (40.7128, -74.0060));
        context.Metadata.DateTime = new FileDateTime(context.Metadata.DateTime.DateTime, DateTimeSource.ExifDateTimeDigitized);

        // Act
        step.Enrich(context);

        // Assert
        await Assert.That(context.Metadata.DateTime.Source).IsEqualTo(DateTimeSource.ExifDateTimeDigitized);
    }

    [Test]
    public async Task Enrich_GpsDerived_LogsMode()
    {
        // Arrange
        var options = CreateOptions(TimezoneHandling.GpsDerived);
        var logger = Substitute.For<ILogger<TimezoneEnrichmentStep>>();
        var step = new TimezoneEnrichmentStep(options, logger);

        var context = CreateContext(new DateTime(2024, 6, 15, 10, 30, 0), (40.7128, -74.0060));

        // Act
        step.Enrich(context);

        // Assert - logs mode once
        logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region CalculateTimezoneOffset Tests

    [Test]
    public async Task CalculateTimezoneOffset_AtPrimeMeridian_ReturnsZero()
    {
        // Arrange & Act
        var offset = TimezoneEnrichmentStep.CalculateTimezoneOffset(0);

        // Assert
        await Assert.That(offset).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task CalculateTimezoneOffset_At15DegreesEast_ReturnsOneHour()
    {
        // Arrange & Act
        var offset = TimezoneEnrichmentStep.CalculateTimezoneOffset(15);

        // Assert
        await Assert.That(offset).IsEqualTo(TimeSpan.FromHours(1));
    }

    [Test]
    public async Task CalculateTimezoneOffset_At15DegreesWest_ReturnsMinusOneHour()
    {
        // Arrange & Act
        var offset = TimezoneEnrichmentStep.CalculateTimezoneOffset(-15);

        // Assert
        await Assert.That(offset).IsEqualTo(TimeSpan.FromHours(-1));
    }

    [Test]
    public async Task CalculateTimezoneOffset_At180Degrees_ReturnsTwelveHours()
    {
        // Arrange & Act
        var offset = TimezoneEnrichmentStep.CalculateTimezoneOffset(180);

        // Assert
        await Assert.That(offset).IsEqualTo(TimeSpan.FromHours(12));
    }

    [Test]
    public async Task CalculateTimezoneOffset_AtMinus180Degrees_ReturnsMinusTwelveHours()
    {
        // Arrange & Act
        var offset = TimezoneEnrichmentStep.CalculateTimezoneOffset(-180);

        // Assert
        await Assert.That(offset).IsEqualTo(TimeSpan.FromHours(-12));
    }

    [Test]
    public async Task CalculateTimezoneOffset_RoundsToHalfHour_ForPartialValues()
    {
        // Arrange - 22.5° East = 1.5 hours, should round to 1.5
        var offset = TimezoneEnrichmentStep.CalculateTimezoneOffset(22.5);

        // Assert
        await Assert.That(offset).IsEqualTo(TimeSpan.FromHours(1.5));
    }

    [Test]
    public async Task CalculateTimezoneOffset_RoundsDown_ForValuesJustBelow()
    {
        // Arrange - 20° East = 1.33 hours, should round to 1.5
        var offset = TimezoneEnrichmentStep.CalculateTimezoneOffset(20);

        // Assert
        await Assert.That(offset).IsEqualTo(TimeSpan.FromHours(1.5));
    }

    [Test]
    public async Task CalculateTimezoneOffset_RoundsUp_ForValuesJustAbove()
    {
        // Arrange - 10° East = 0.67 hours, should round to 0.5
        var offset = TimezoneEnrichmentStep.CalculateTimezoneOffset(10);

        // Assert
        await Assert.That(offset).IsEqualTo(TimeSpan.FromHours(0.5));
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task Enrich_MultipleFiles_LogsModeOnlyOnce()
    {
        // Arrange
        var options = CreateOptions(TimezoneHandling.GpsDerived);
        var logger = Substitute.For<ILogger<TimezoneEnrichmentStep>>();
        var step = new TimezoneEnrichmentStep(options, logger);

        var context1 = CreateContext(new DateTime(2024, 1, 1), (40.7128, -74.0060));
        var context2 = CreateContext(new DateTime(2024, 1, 2), (35.6762, 139.6503));

        // Act - process multiple files
        step.Enrich(context1);
        step.Enrich(context2);

        // Assert - should only log mode once
        logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task Enrich_GpsDerived_CrossingDateLine_PositiveOffset()
    {
        // Arrange - Near the International Date Line (positive side)
        var options = CreateOptions(TimezoneHandling.GpsDerived);
        var logger = Substitute.For<ILogger<TimezoneEnrichmentStep>>();
        var step = new TimezoneEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 6, 15, 23, 0, 0);
        var context = CreateContext(originalDate, (0, 172.5)); // Near date line in Pacific

        // Act
        step.Enrich(context);

        // Assert - 172.5 / 15 = 11.5 hours
        var expectedOffset = TimeSpan.FromHours(11.5);
        var expectedDate = originalDate.Add(expectedOffset);
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(expectedDate);
    }

    [Test]
    public async Task Enrich_GpsDerived_HandlesEquator()
    {
        // Arrange - On equator at various longitudes
        var options = CreateOptions(TimezoneHandling.GpsDerived);
        var logger = Substitute.For<ILogger<TimezoneEnrichmentStep>>();
        var step = new TimezoneEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 6, 15, 12, 0, 0);
        var context = CreateContext(originalDate, (0, 105)); // Indonesia (0°, 105°E)

        // Act
        step.Enrich(context);

        // Assert - 105 / 15 = 7 hours
        var expectedOffset = TimeSpan.FromHours(7);
        var expectedDate = originalDate.Add(expectedOffset);
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(expectedDate);
    }

    [Test]
    public async Task Enrich_GpsDerived_HandlesPolarRegions()
    {
        // Arrange - Near the North Pole
        var options = CreateOptions(TimezoneHandling.GpsDerived);
        var logger = Substitute.For<ILogger<TimezoneEnrichmentStep>>();
        var step = new TimezoneEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 6, 15, 12, 0, 0);
        var context = CreateContext(originalDate, (89.9, -30)); // Near North Pole

        // Act
        step.Enrich(context);

        // Assert - -30 / 15 = -2 hours
        var expectedOffset = TimeSpan.FromHours(-2);
        var expectedDate = originalDate.Add(expectedOffset);
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(expectedDate);
    }

    #endregion

    #region Real World Location Tests

    [Test]
    public async Task Enrich_GpsDerived_LosAngeles_AppliesCorrectOffset()
    {
        // Arrange - Los Angeles at ~-118° West
        var options = CreateOptions(TimezoneHandling.GpsDerived);
        var logger = Substitute.For<ILogger<TimezoneEnrichmentStep>>();
        var step = new TimezoneEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 6, 15, 12, 0, 0);
        var context = CreateContext(originalDate, (34.0522, -118.2437));

        // Act
        step.Enrich(context);

        // Assert - -118.24 / 15 ≈ -7.88, rounds to -8 hours
        var expectedOffset = TimeSpan.FromHours(-8);
        var expectedDate = originalDate.Add(expectedOffset);
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(expectedDate);
    }

    [Test]
    public async Task Enrich_GpsDerived_Sydney_AppliesCorrectOffset()
    {
        // Arrange - Sydney at ~151° East
        var options = CreateOptions(TimezoneHandling.GpsDerived);
        var logger = Substitute.For<ILogger<TimezoneEnrichmentStep>>();
        var step = new TimezoneEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 6, 15, 12, 0, 0);
        var context = CreateContext(originalDate, (-33.8688, 151.2093));

        // Act
        step.Enrich(context);

        // Assert - 151.21 / 15 ≈ 10.08, rounds to 10 hours
        var expectedOffset = TimeSpan.FromHours(10);
        var expectedDate = originalDate.Add(expectedOffset);
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(expectedDate);
    }

    [Test]
    public async Task Enrich_GpsDerived_Paris_AppliesCorrectOffset()
    {
        // Arrange - Paris at ~2.35° East
        var options = CreateOptions(TimezoneHandling.GpsDerived);
        var logger = Substitute.For<ILogger<TimezoneEnrichmentStep>>();
        var step = new TimezoneEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 6, 15, 12, 0, 0);
        var context = CreateContext(originalDate, (48.8566, 2.3522));

        // Act
        step.Enrich(context);

        // Assert - 2.35 / 15 ≈ 0.16, rounds to 0 hours
        var expectedDate = originalDate; // No change
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(expectedDate);
    }

    [Test]
    public async Task Enrich_GpsDerived_Dubai_AppliesCorrectOffset()
    {
        // Arrange - Dubai at ~55.3° East
        var options = CreateOptions(TimezoneHandling.GpsDerived);
        var logger = Substitute.For<ILogger<TimezoneEnrichmentStep>>();
        var step = new TimezoneEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 6, 15, 12, 0, 0);
        var context = CreateContext(originalDate, (25.2048, 55.2708));

        // Act
        step.Enrich(context);

        // Assert - 55.27 / 15 ≈ 3.68, rounds to 3.5 hours
        var expectedOffset = TimeSpan.FromHours(3.5);
        var expectedDate = originalDate.Add(expectedOffset);
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(expectedDate);
    }

    #endregion
}
