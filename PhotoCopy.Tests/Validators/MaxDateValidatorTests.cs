using NSubstitute;
using PhotoCopy.Files;
using PhotoCopy.Validators;
using System;

namespace PhotoCopy.Tests.Validators;

public class MaxDateValidatorTests
{
    [Test]
    public async Task Validate_ReturnsTrue_WhenFileDateTimeIsBeforeMaxDate()
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
        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.ValidatorName).IsEqualTo(nameof(MaxDateValidator));
    }

    [Test]
    public async Task Validate_ReturnsTrue_WhenFileDateTimeEqualsMaxDate()
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
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_ReturnsFalse_WhenFileDateTimeIsAfterMaxDate()
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
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.ValidatorName).IsEqualTo(nameof(MaxDateValidator));
        await Assert.That(result.Reason).Contains("exceeds");
    }
}