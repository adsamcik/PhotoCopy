using NSubstitute;
using PhotoCopy.Files;
using PhotoCopy.Validators;
using System;
using Xunit;

namespace PhotoCopy.Tests.Validators;

public class MaxDateValidatorTests
{
    [Fact]
    public void Validate_ReturnsTrue_WhenFileDateTimeIsBeforeMaxDate()
    {
        // Arrange
        var maxDate = new DateTime(2023, 1, 1);
        var validator = new MaxDateValidator(maxDate);

        // Create a substitute for IFile.
        var file = Substitute.For<IFile>();
        // Set the FileDateTime property with a date before maxDate.
        var testDate = new DateTime(2022, 12, 31);
        file.FileDateTime.Returns(new FileDateTime(testDate, testDate, testDate));

        // Act
        var result = validator.Validate(file);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(nameof(MaxDateValidator), result.ValidatorName);
    }

    [Fact]
    public void Validate_ReturnsTrue_WhenFileDateTimeEqualsMaxDate()
    {
        // Arrange
        var maxDate = new DateTime(2023, 1, 1);
        var validator = new MaxDateValidator(maxDate);

        var file = Substitute.For<IFile>();
        var testDate = new DateTime(2023, 1, 1);
        file.FileDateTime.Returns(new FileDateTime(testDate, testDate, testDate));

        // Act
        var result = validator.Validate(file);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ReturnsFalse_WhenFileDateTimeIsAfterMaxDate()
    {
        // Arrange
        var maxDate = new DateTime(2023, 1, 1);
        var validator = new MaxDateValidator(maxDate);

        var file = Substitute.For<IFile>();
        var testDate = new DateTime(2023, 1, 2);
        file.FileDateTime.Returns(new FileDateTime(testDate, testDate, testDate));

        // Act
        var result = validator.Validate(file);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(nameof(MaxDateValidator), result.ValidatorName);
        Assert.Contains("exceeds", result.Reason, StringComparison.OrdinalIgnoreCase);
    }
}