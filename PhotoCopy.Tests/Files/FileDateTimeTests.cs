using System;
using AwesomeAssertions;
using PhotoCopy.Files;

namespace PhotoCopy.Tests.Files;

public class FileDateTimeTests : TestBase
{
    #region Constructor Tests - Simple Constructor

    [Test]
    public void Constructor_WithDateTimeAndSource_SetsPropertiesCorrectly()
    {
        // Arrange
        var dateTime = new DateTime(2023, 6, 15, 10, 30, 0);
        var source = DateTimeSource.ExifDateTimeOriginal;

        // Act
        var fileDateTime = new FileDateTime(dateTime, source);

        // Assert
        fileDateTime.DateTime.Should().Be(dateTime);
        fileDateTime.Source.Should().Be(source);
        fileDateTime.Created.Should().Be(dateTime);
        fileDateTime.Modified.Should().Be(dateTime);
        fileDateTime.Taken.Should().Be(dateTime);
    }

    [Test]
    public void Constructor_WithFileCreationSource_SetsAllDatesToSameValue()
    {
        // Arrange
        var dateTime = new DateTime(2022, 3, 20, 14, 45, 30);
        var source = DateTimeSource.FileCreation;

        // Act
        var fileDateTime = new FileDateTime(dateTime, source);

        // Assert
        fileDateTime.DateTime.Should().Be(dateTime);
        fileDateTime.Source.Should().Be(source);
        fileDateTime.Created.Should().Be(dateTime);
        fileDateTime.Modified.Should().Be(dateTime);
        fileDateTime.Taken.Should().Be(dateTime);
    }

    [Test]
    public void Constructor_WithFileModificationSource_SetsAllDatesToSameValue()
    {
        // Arrange
        var dateTime = new DateTime(2021, 12, 25, 8, 0, 0);
        var source = DateTimeSource.FileModification;

        // Act
        var fileDateTime = new FileDateTime(dateTime, source);

        // Assert
        fileDateTime.DateTime.Should().Be(dateTime);
        fileDateTime.Source.Should().Be(source);
    }

    #endregion

    #region Constructor Tests - Three Date Constructor

    [Test]
    public void Constructor_WithExifDateTime_UsesExifAsPreferred()
    {
        // Arrange
        var created = new DateTime(2023, 1, 1, 12, 0, 0);
        var modified = new DateTime(2023, 2, 1, 12, 0, 0);
        var taken = new DateTime(2023, 3, 1, 12, 0, 0);

        // Act
        var fileDateTime = new FileDateTime(created, modified, taken);

        // Assert
        fileDateTime.DateTime.Should().Be(taken);
        fileDateTime.Source.Should().Be(DateTimeSource.ExifDateTimeOriginal);
    }

    [Test]
    public void Constructor_WithNoExif_UsesFileCreationDate()
    {
        // Arrange
        var created = new DateTime(2023, 1, 15, 10, 30, 0);
        var modified = new DateTime(2023, 2, 20, 14, 45, 0);
        var taken = default(DateTime);

        // Act
        var fileDateTime = new FileDateTime(created, modified, taken);

        // Assert
        fileDateTime.DateTime.Should().Be(created);
        fileDateTime.Source.Should().Be(DateTimeSource.FileCreation);
    }

    [Test]
    public void Constructor_WithNoExifAndNoCreation_UsesFileModificationDate()
    {
        // Arrange
        var created = default(DateTime);
        var modified = new DateTime(2023, 5, 10, 16, 20, 0);
        var taken = default(DateTime);

        // Act
        var fileDateTime = new FileDateTime(created, modified, taken);

        // Assert
        fileDateTime.DateTime.Should().Be(modified);
        fileDateTime.Source.Should().Be(DateTimeSource.FileModification);
    }

    [Test]
    public void Constructor_WithAllDates_PreferenceOrder()
    {
        // Test the priority: Taken > Created > Modified
        
        // Arrange - All dates provided
        var created = new DateTime(2020, 6, 1);
        var modified = new DateTime(2021, 6, 1);
        var taken = new DateTime(2019, 6, 1); // Oldest but highest priority

        // Act
        var fileDateTime = new FileDateTime(created, modified, taken);

        // Assert - Taken date should be preferred
        fileDateTime.DateTime.Should().Be(taken);
        fileDateTime.Source.Should().Be(DateTimeSource.ExifDateTimeOriginal);
        
        // Verify individual dates are preserved
        fileDateTime.Created.Should().Be(created);
        fileDateTime.Modified.Should().Be(modified);
        fileDateTime.Taken.Should().Be(taken);
    }

    [Test]
    public void Constructor_WithOnlyModificationDate_UsesModificationDate()
    {
        // Arrange
        var created = default(DateTime);
        var modified = new DateTime(2024, 7, 4, 9, 15, 0);
        var taken = default(DateTime);

        // Act
        var fileDateTime = new FileDateTime(created, modified, taken);

        // Assert
        fileDateTime.DateTime.Should().Be(modified);
        fileDateTime.Source.Should().Be(DateTimeSource.FileModification);
    }

    [Test]
    public void Constructor_WithDefaultDates_SetsSourceToFileModification()
    {
        // Arrange - All dates are default
        var created = default(DateTime);
        var modified = default(DateTime);
        var taken = default(DateTime);

        // Act
        var fileDateTime = new FileDateTime(created, modified, taken);

        // Assert
        fileDateTime.DateTime.Should().Be(default(DateTime));
        fileDateTime.Source.Should().Be(DateTimeSource.FileModification);
    }

    #endregion

    #region DateTime Property Tests

    [Test]
    public void DateTime_ReturnsPreferredDate_WhenTakenIsSet()
    {
        // Arrange
        var taken = new DateTime(2022, 8, 15, 11, 30, 45);
        var fileDateTime = new FileDateTime(
            new DateTime(2022, 1, 1),
            new DateTime(2022, 2, 1),
            taken);

        // Act
        var result = fileDateTime.DateTime;

        // Assert
        result.Should().Be(taken);
    }

    [Test]
    public void DateTime_ReturnsCreationDate_WhenTakenIsDefault()
    {
        // Arrange
        var created = new DateTime(2021, 4, 10, 9, 0, 0);
        var fileDateTime = new FileDateTime(
            created,
            new DateTime(2021, 5, 1),
            default);

        // Act
        var result = fileDateTime.DateTime;

        // Assert
        result.Should().Be(created);
    }

    [Test]
    public void DateTime_ReturnsModificationDate_WhenTakenAndCreatedAreDefault()
    {
        // Arrange
        var modified = new DateTime(2020, 11, 22, 18, 45, 0);
        var fileDateTime = new FileDateTime(default, modified, default);

        // Act
        var result = fileDateTime.DateTime;

        // Assert
        result.Should().Be(modified);
    }

    #endregion

    #region Source Property Tests

    [Test]
    public void Source_IndicatesExifDateTimeOriginal_WhenTakenIsSet()
    {
        // Arrange
        var fileDateTime = new FileDateTime(
            new DateTime(2023, 1, 1),
            new DateTime(2023, 2, 1),
            new DateTime(2023, 3, 1));

        // Act
        var source = fileDateTime.Source;

        // Assert
        source.Should().Be(DateTimeSource.ExifDateTimeOriginal);
    }

    [Test]
    public void Source_IndicatesFileCreation_WhenOnlyCreationDateAvailable()
    {
        // Arrange
        var fileDateTime = new FileDateTime(
            new DateTime(2023, 1, 1),
            new DateTime(2023, 2, 1),
            default);

        // Act
        var source = fileDateTime.Source;

        // Assert
        source.Should().Be(DateTimeSource.FileCreation);
    }

    [Test]
    public void Source_IndicatesFileModification_WhenOnlyModificationDateAvailable()
    {
        // Arrange
        var fileDateTime = new FileDateTime(default, new DateTime(2023, 2, 1), default);

        // Act
        var source = fileDateTime.Source;

        // Assert
        source.Should().Be(DateTimeSource.FileModification);
    }

    [Test]
    public void Source_IndicatesDateSource_FromSimpleConstructor()
    {
        // Arrange & Act
        var exifDateTime = new FileDateTime(DateTime.Now, DateTimeSource.ExifDateTime);
        var exifDigitized = new FileDateTime(DateTime.Now, DateTimeSource.ExifDateTimeDigitized);

        // Assert
        exifDateTime.Source.Should().Be(DateTimeSource.ExifDateTime);
        exifDigitized.Source.Should().Be(DateTimeSource.ExifDateTimeDigitized);
    }

    #endregion

    #region DateTimeSource Enum Tests

    [Test]
    public void DateTimeSource_HasAllExpectedValues()
    {
        // Assert
        Enum.GetValues<DateTimeSource>().Should().HaveCount(6);
        Enum.IsDefined(DateTimeSource.FileCreation).Should().BeTrue();
        Enum.IsDefined(DateTimeSource.FileModification).Should().BeTrue();
        Enum.IsDefined(DateTimeSource.ExifDateTime).Should().BeTrue();
        Enum.IsDefined(DateTimeSource.ExifDateTimeOriginal).Should().BeTrue();
        Enum.IsDefined(DateTimeSource.ExifDateTimeDigitized).Should().BeTrue();
        Enum.IsDefined(DateTimeSource.Sidecar).Should().BeTrue();
    }

    #endregion

    #region Individual Date Property Tests

    [Test]
    public void Created_ReturnsCorrectValue()
    {
        // Arrange
        var created = new DateTime(2023, 5, 15);
        var modified = new DateTime(2023, 6, 15);
        var taken = new DateTime(2023, 7, 15);

        // Act
        var fileDateTime = new FileDateTime(created, modified, taken);

        // Assert
        fileDateTime.Created.Should().Be(created);
    }

    [Test]
    public void Modified_ReturnsCorrectValue()
    {
        // Arrange
        var created = new DateTime(2023, 5, 15);
        var modified = new DateTime(2023, 6, 15);
        var taken = new DateTime(2023, 7, 15);

        // Act
        var fileDateTime = new FileDateTime(created, modified, taken);

        // Assert
        fileDateTime.Modified.Should().Be(modified);
    }

    [Test]
    public void Taken_ReturnsCorrectValue()
    {
        // Arrange
        var created = new DateTime(2023, 5, 15);
        var modified = new DateTime(2023, 6, 15);
        var taken = new DateTime(2023, 7, 15);

        // Act
        var fileDateTime = new FileDateTime(created, modified, taken);

        // Assert
        fileDateTime.Taken.Should().Be(taken);
    }

    #endregion

    #region Edge Cases

    [Test]
    public void Constructor_WithMinValueDates_HandlesCorrectly()
    {
        // Arrange
        var minDate = DateTime.MinValue;

        // Act
        var fileDateTime = new FileDateTime(minDate, minDate, minDate);

        // Assert - MinValue is treated as "not set" (default)
        fileDateTime.DateTime.Should().Be(DateTime.MinValue);
        fileDateTime.Source.Should().Be(DateTimeSource.FileModification);
    }

    [Test]
    public void Constructor_WithMaxValueDates_HandlesCorrectly()
    {
        // Arrange
        var maxDate = DateTime.MaxValue;

        // Act
        var fileDateTime = new FileDateTime(maxDate, DateTimeSource.ExifDateTimeOriginal);

        // Assert
        fileDateTime.DateTime.Should().Be(maxDate);
        fileDateTime.Source.Should().Be(DateTimeSource.ExifDateTimeOriginal);
    }

    [Test]
    public void Constructor_WithMixOfDefaultAndValidDates_SelectsCorrectly()
    {
        // Arrange - Created is default, but Taken and Modified are set
        var created = default(DateTime);
        var modified = new DateTime(2022, 10, 1);
        var taken = new DateTime(2022, 9, 1);

        // Act
        var fileDateTime = new FileDateTime(created, modified, taken);

        // Assert - Taken has highest priority
        fileDateTime.DateTime.Should().Be(taken);
        fileDateTime.Source.Should().Be(DateTimeSource.ExifDateTimeOriginal);
    }

    [Test]
    public void Constructor_PreservesAllDateValues()
    {
        // Arrange
        var created = new DateTime(2023, 1, 1, 10, 0, 0);
        var modified = new DateTime(2023, 2, 2, 11, 0, 0);
        var taken = new DateTime(2023, 3, 3, 12, 0, 0);

        // Act
        var fileDateTime = new FileDateTime(created, modified, taken);

        // Assert - All dates should be preserved
        fileDateTime.Created.Should().Be(created);
        fileDateTime.Modified.Should().Be(modified);
        fileDateTime.Taken.Should().Be(taken);
    }

    #endregion

    #region Time Precision Tests

    [Test]
    public void Constructor_PreservesTimePrecision()
    {
        // Arrange
        var preciseTime = new DateTime(2023, 7, 4, 14, 30, 45, 123);

        // Act
        var fileDateTime = new FileDateTime(preciseTime, DateTimeSource.ExifDateTimeOriginal);

        // Assert
        fileDateTime.DateTime.Should().Be(preciseTime);
        fileDateTime.DateTime.Millisecond.Should().Be(123);
    }

    [Test]
    public void Constructor_ThreeDates_PreservesTimePrecision()
    {
        // Arrange
        var created = new DateTime(2023, 1, 1, 10, 20, 30, 100);
        var modified = new DateTime(2023, 2, 2, 11, 21, 31, 200);
        var taken = new DateTime(2023, 3, 3, 12, 22, 32, 300);

        // Act
        var fileDateTime = new FileDateTime(created, modified, taken);

        // Assert
        fileDateTime.DateTime.Millisecond.Should().Be(300); // Taken date's milliseconds
        fileDateTime.Created.Millisecond.Should().Be(100);
        fileDateTime.Modified.Millisecond.Should().Be(200);
        fileDateTime.Taken.Millisecond.Should().Be(300);
    }

    #endregion
}
