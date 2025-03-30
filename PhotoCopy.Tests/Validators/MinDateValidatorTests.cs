using NSubstitute;
using PhotoCopy.Files;
using PhotoCopy.Validators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PhotoCopy.Tests.Validators;

public class MinDateValidatorTests
{
    [Fact]
    public void Validate_ReturnsTrue_WhenFileDateTimeIsAfterMinDate()
    {
        // Arrange
        var minDate = new DateTime(2023, 1, 1);
        var options = new Options
        {
            Source = "dummy",
            Destination = "dummy",
            MinDate = minDate
        };

        var validator = new MinDateValidator(options);

        var file = Substitute.For<IFile>();
        file.FileDateTime.Returns(new FileDateTime(new DateTime(2023, 1, 2), DateTimeSource.Exif));

        // Act
        bool result = validator.Validate(file);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Validate_ReturnsTrue_WhenFileDateTimeEqualsMinDate()
    {
        // Arrange
        var minDate = new DateTime(2023, 1, 1);
        var options = new Options
        {
            Source = "dummy",
            Destination = "dummy",
            MinDate = minDate
        };

        var validator = new MinDateValidator(options);

        var file = Substitute.For<IFile>();
        file.FileDateTime.Returns(new FileDateTime(new DateTime(2023, 1, 1), DateTimeSource.Exif));

        // Act
        bool result = validator.Validate(file);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Validate_ReturnsFalse_WhenFileDateTimeIsBeforeMinDate()
    {
        // Arrange
        var minDate = new DateTime(2023, 1, 1);
        var options = new Options
        {
            Source = "dummy",
            Destination = "dummy",
            MinDate = minDate
        };

        var validator = new MinDateValidator(options);

        var file = Substitute.For<IFile>();
        file.FileDateTime.Returns(new FileDateTime(new DateTime(2022, 12, 31), DateTimeSource.Exif));

        // Act
        bool result = validator.Validate(file);

        // Assert
        Assert.False(result);
    }
}