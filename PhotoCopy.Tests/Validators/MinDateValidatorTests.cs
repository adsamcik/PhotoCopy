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
    [Test]
    public async Task Validate_ReturnsTrue_WhenFileDateTimeIsAfterMinDate()
    {
        // Arrange
        var minDate = new DateTime(2023, 1, 1);
        var validator = new MinDateValidator(minDate);

        var file = Substitute.For<IFile>();
        var testDate = new DateTime(2023, 1, 2);
        file.FileDateTime.Returns(new FileDateTime(testDate, testDate, testDate));

        // Act
        var result = validator.Validate(file);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_ReturnsTrue_WhenFileDateTimeEqualsMinDate()
    {
        // Arrange
        var minDate = new DateTime(2023, 1, 1);
        var validator = new MinDateValidator(minDate);

        var file = Substitute.For<IFile>();
        var testDate = new DateTime(2023, 1, 1);
        file.FileDateTime.Returns(new FileDateTime(testDate, testDate, testDate));

        // Act
        var result = validator.Validate(file);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_ReturnsFalse_WhenFileDateTimeIsBeforeMinDate()
    {
        // Arrange
        var minDate = new DateTime(2023, 1, 1);
        var validator = new MinDateValidator(minDate);

        var file = Substitute.For<IFile>();
        var testDate = new DateTime(2022, 12, 31);
        file.FileDateTime.Returns(new FileDateTime(testDate, testDate, testDate));

        // Act
        var result = validator.Validate(file);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.ValidatorName).IsEqualTo(nameof(MinDateValidator));
        await Assert.That(result.Reason).Contains("earlier");
    }
}