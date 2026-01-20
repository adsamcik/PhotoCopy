using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Configuration;
using PhotoCopy.Files;
using PhotoCopy.Files.Metadata;

namespace PhotoCopy.Tests.Files.Metadata;

public class TimeOffsetEnrichmentStepTests
{
    private static IOptions<PhotoCopyConfig> CreateOptions(TimeSpan? offset = null)
    {
        var config = new PhotoCopyConfig { TimeOffset = offset };
        return Options.Create(config);
    }

    private static FileMetadataContext CreateContext(DateTime dateTime)
    {
        var fileInfo = new FileInfo(Path.GetTempFileName());
        var context = new FileMetadataContext(fileInfo);
        context.Metadata.DateTime = new FileDateTime(dateTime, DateTimeSource.ExifDateTimeOriginal);
        return context;
    }

    [Test]
    public async Task Enrich_WithPositiveOffset_AdjustsDateTimeForward()
    {
        // Arrange
        var offset = TimeSpan.FromHours(2);
        var options = CreateOptions(offset);
        var logger = Substitute.For<ILogger<TimeOffsetEnrichmentStep>>();
        var step = new TimeOffsetEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 6, 15, 10, 30, 0);
        var context = CreateContext(originalDate);

        // Act
        step.Enrich(context);

        // Assert
        var expectedDate = new DateTime(2024, 6, 15, 12, 30, 0);
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(expectedDate);
    }

    [Test]
    public async Task Enrich_WithNegativeOffset_AdjustsDateTimeBackward()
    {
        // Arrange
        var offset = TimeSpan.FromHours(-3);
        var options = CreateOptions(offset);
        var logger = Substitute.For<ILogger<TimeOffsetEnrichmentStep>>();
        var step = new TimeOffsetEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 6, 15, 10, 30, 0);
        var context = CreateContext(originalDate);

        // Act
        step.Enrich(context);

        // Assert
        var expectedDate = new DateTime(2024, 6, 15, 7, 30, 0);
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(expectedDate);
    }

    [Test]
    public async Task Enrich_WithDayOffset_AdjustsDateCorrectly()
    {
        // Arrange
        var offset = TimeSpan.FromDays(1);
        var options = CreateOptions(offset);
        var logger = Substitute.For<ILogger<TimeOffsetEnrichmentStep>>();
        var step = new TimeOffsetEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 6, 15, 10, 30, 0);
        var context = CreateContext(originalDate);

        // Act
        step.Enrich(context);

        // Assert
        var expectedDate = new DateTime(2024, 6, 16, 10, 30, 0);
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(expectedDate);
    }

    [Test]
    public async Task Enrich_WithNullOffset_DoesNotModifyDateTime()
    {
        // Arrange
        var options = CreateOptions(null);
        var logger = Substitute.For<ILogger<TimeOffsetEnrichmentStep>>();
        var step = new TimeOffsetEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 6, 15, 10, 30, 0);
        var context = CreateContext(originalDate);

        // Act
        step.Enrich(context);

        // Assert
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(originalDate);
    }

    [Test]
    public async Task Enrich_WithZeroOffset_DoesNotModifyDateTime()
    {
        // Arrange
        var options = CreateOptions(TimeSpan.Zero);
        var logger = Substitute.For<ILogger<TimeOffsetEnrichmentStep>>();
        var step = new TimeOffsetEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 6, 15, 10, 30, 0);
        var context = CreateContext(originalDate);

        // Act
        step.Enrich(context);

        // Assert
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(originalDate);
    }

    [Test]
    public async Task Enrich_PreservesDateTimeSource()
    {
        // Arrange
        var offset = TimeSpan.FromHours(1);
        var options = CreateOptions(offset);
        var logger = Substitute.For<ILogger<TimeOffsetEnrichmentStep>>();
        var step = new TimeOffsetEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 6, 15, 10, 30, 0);
        var context = CreateContext(originalDate);
        // Set a specific source
        context.Metadata.DateTime = new FileDateTime(originalDate, DateTimeSource.ExifDateTimeDigitized);

        // Act
        step.Enrich(context);

        // Assert
        await Assert.That(context.Metadata.DateTime.Source).IsEqualTo(DateTimeSource.ExifDateTimeDigitized);
    }

    [Test]
    public async Task Enrich_WithComplexOffset_AdjustsCorrectly()
    {
        // Arrange - 1 day, 2 hours, 30 minutes
        var offset = new TimeSpan(1, 2, 30, 0);
        var options = CreateOptions(offset);
        var logger = Substitute.For<ILogger<TimeOffsetEnrichmentStep>>();
        var step = new TimeOffsetEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 6, 15, 10, 30, 0);
        var context = CreateContext(originalDate);

        // Act
        step.Enrich(context);

        // Assert
        var expectedDate = new DateTime(2024, 6, 16, 13, 0, 0);
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(expectedDate);
    }

    [Test]
    public async Task Enrich_CrossingMidnight_AdjustsDateCorrectly()
    {
        // Arrange
        var offset = TimeSpan.FromHours(5);
        var options = CreateOptions(offset);
        var logger = Substitute.For<ILogger<TimeOffsetEnrichmentStep>>();
        var step = new TimeOffsetEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 6, 15, 22, 0, 0);
        var context = CreateContext(originalDate);

        // Act
        step.Enrich(context);

        // Assert
        var expectedDate = new DateTime(2024, 6, 16, 3, 0, 0);
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(expectedDate);
    }

    [Test]
    public async Task Enrich_CrossingMonthBoundary_AdjustsDateCorrectly()
    {
        // Arrange
        var offset = TimeSpan.FromDays(2);
        var options = CreateOptions(offset);
        var logger = Substitute.For<ILogger<TimeOffsetEnrichmentStep>>();
        var step = new TimeOffsetEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 6, 30, 12, 0, 0);
        var context = CreateContext(originalDate);

        // Act
        step.Enrich(context);

        // Assert
        var expectedDate = new DateTime(2024, 7, 2, 12, 0, 0);
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(expectedDate);
    }

    [Test]
    public async Task Enrich_CrossingYearBoundary_AdjustsDateCorrectly()
    {
        // Arrange
        var offset = TimeSpan.FromDays(-5);
        var options = CreateOptions(offset);
        var logger = Substitute.For<ILogger<TimeOffsetEnrichmentStep>>();
        var step = new TimeOffsetEnrichmentStep(options, logger);

        var originalDate = new DateTime(2024, 1, 3, 12, 0, 0);
        var context = CreateContext(originalDate);

        // Act
        step.Enrich(context);

        // Assert
        var expectedDate = new DateTime(2023, 12, 29, 12, 0, 0);
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(expectedDate);
    }

    [Test]
    public async Task Enrich_LogsOnce_WhenOffsetApplied()
    {
        // Arrange
        var offset = TimeSpan.FromHours(1);
        var options = CreateOptions(offset);
        var logger = Substitute.For<ILogger<TimeOffsetEnrichmentStep>>();
        var step = new TimeOffsetEnrichmentStep(options, logger);

        var context1 = CreateContext(new DateTime(2024, 1, 1));
        var context2 = CreateContext(new DateTime(2024, 1, 2));

        // Act - process multiple files
        step.Enrich(context1);
        step.Enrich(context2);

        // Assert - should only log once
        logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
