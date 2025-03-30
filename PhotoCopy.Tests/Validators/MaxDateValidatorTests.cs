using NSubstitute;
using PhotoCopy.Files;
using PhotoCopy.Validators;

namespace PhotoCopy.Tests.Validators;

public class MaxDateValidatorTests
{
    [Fact]
    public void Validate_ReturnsTrue_WhenFileDateTimeIsBeforeMaxDate()
    {
        // Arrange
        var maxDate = new DateTime(2023, 1, 1);
        // Set required properties for Options; only MaxDate matters here.
        var options = new Options
        {
            Source = "dummy",
            Destination = "dummy",
            MaxDate = maxDate
        };

        var validator = new MaxDateValidator(options);

        // Create a substitute for IFile.
        var file = Substitute.For<IFile>();
        // Set the FileDateTime property with a date before maxDate.
        file.FileDateTime.Returns(new FileDateTime(new DateTime(2022, 12, 31), DateTimeSource.Exif));

        // Act
        bool result = validator.Validate(file);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Validate_ReturnsTrue_WhenFileDateTimeEqualsMaxDate()
    {
        // Arrange
        var maxDate = new DateTime(2023, 1, 1);
        var options = new Options
        {
            Source = "dummy",
            Destination = "dummy",
            MaxDate = maxDate
        };

        var validator = new MaxDateValidator(options);

        var file = Substitute.For<IFile>();
        file.FileDateTime.Returns(new FileDateTime(new DateTime(2023, 1, 1), DateTimeSource.Exif));

        // Act
        bool result = validator.Validate(file);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Validate_ReturnsFalse_WhenFileDateTimeIsAfterMaxDate()
    {
        // Arrange
        var maxDate = new DateTime(2023, 1, 1);
        var options = new Options
        {
            Source = "dummy",
            Destination = "dummy",
            MaxDate = maxDate
        };

        var validator = new MaxDateValidator(options);

        var file = Substitute.For<IFile>();
        file.FileDateTime.Returns(new FileDateTime(new DateTime(2023, 1, 2), DateTimeSource.Exif));

        // Act
        bool result = validator.Validate(file);

        // Assert
        Assert.False(result);
    }
}