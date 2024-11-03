using PhotoCopy.Directory;
using PhotoCopy.Validators;
using FluentAssertions;
using NSubstitute;
using IFile = PhotoCopy.Files.IFile;

namespace PhotoCopy.Tests;

public class DirectoryCopierTests : IClassFixture<ApplicationStateFixture>
{
    [Fact]
    public void ShouldCopy_AllValidatorsPass_ReturnsTrue()
    {
        // Arrange
        var validator1 = Substitute.For<IValidator>();
        validator1.Validate(Arg.Any<IFile>()).Returns(true);

        var validator2 = Substitute.For<IValidator>();
        validator2.Validate(Arg.Any<IFile>()).Returns(true);

        var validators = new List<IValidator> { validator1, validator2 };
        var file = Substitute.For<IFile>();

        // Act
        var result = DirectoryCopier.ShouldCopy(validators, file);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldCopy_OneValidatorFails_ReturnsFalse()
    {
        // Arrange
        var validator1 = Substitute.For<IValidator>();
        validator1.Validate(Arg.Any<IFile>()).Returns(true);

        var validator2 = Substitute.For<IValidator>();
        validator2.Validate(Arg.Any<IFile>()).Returns(false); // This validator fails

        var validators = new List<IValidator> { validator1, validator2 };
        var file = Substitute.For<IFile>();

        // Act
        var result = DirectoryCopier.ShouldCopy(validators, file);

        // Assert
        result.Should().BeFalse();
    }
}
