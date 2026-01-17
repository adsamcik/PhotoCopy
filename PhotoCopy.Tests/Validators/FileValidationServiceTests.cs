using System.Collections.Generic;
using AwesomeAssertions;
using NSubstitute;
using PhotoCopy.Files;
using PhotoCopy.Validators;

namespace PhotoCopy.Tests.Validators;

/// <summary>
/// Unit tests for FileValidationService.
/// </summary>
public class FileValidationServiceTests
{
    private readonly FileValidationService _service;

    public FileValidationServiceTests()
    {
        _service = new FileValidationService();
    }

    #region ValidateFirstFailure Tests

    [Test]
    public async Task ValidateFirstFailure_WithNoValidators_ReturnsValidResult()
    {
        // Arrange
        var file = Substitute.For<IFile>();
        var validators = new List<IValidator>();

        // Act
        var result = _service.ValidateFirstFailure(file, validators);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.File).IsEqualTo(file);
    }

    [Test]
    public async Task ValidateFirstFailure_WithPassingValidator_ReturnsValidResult()
    {
        // Arrange
        var file = Substitute.For<IFile>();
        var validator = Substitute.For<IValidator>();
        validator.Validate(file).Returns(new ValidationResult(true, "TestValidator"));
        var validators = new List<IValidator> { validator };

        // Act
        var result = _service.ValidateFirstFailure(file, validators);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task ValidateFirstFailure_WithFailingValidator_ReturnsInvalidResult()
    {
        // Arrange
        var file = Substitute.For<IFile>();
        var validator = Substitute.For<IValidator>();
        validator.Name.Returns("TestValidator");
        validator.Validate(file).Returns(new ValidationResult(false, "TestValidator", "File is invalid"));
        var validators = new List<IValidator> { validator };

        // Act
        var result = _service.ValidateFirstFailure(file, validators);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.FirstValidatorName).IsEqualTo("TestValidator");
        await Assert.That(result.FirstRejectionReason).IsEqualTo("File is invalid");
    }

    [Test]
    public async Task ValidateFirstFailure_WithMultipleValidators_StopsAtFirstFailure()
    {
        // Arrange
        var file = Substitute.For<IFile>();
        
        var validator1 = Substitute.For<IValidator>();
        validator1.Name.Returns("Validator1");
        validator1.Validate(file).Returns(new ValidationResult(false, "Validator1", "First failure"));
        
        var validator2 = Substitute.For<IValidator>();
        validator2.Name.Returns("Validator2");
        
        var validators = new List<IValidator> { validator1, validator2 };

        // Act
        var result = _service.ValidateFirstFailure(file, validators);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.FirstValidatorName).IsEqualTo("Validator1");
        
        // Second validator should never have been called
        validator2.DidNotReceive().Validate(Arg.Any<IFile>());
    }

    [Test]
    public async Task ValidateFirstFailure_WithMultiplePassingValidators_CallsAll()
    {
        // Arrange
        var file = Substitute.For<IFile>();
        
        var validator1 = Substitute.For<IValidator>();
        validator1.Validate(file).Returns(new ValidationResult(true, "Validator1"));
        
        var validator2 = Substitute.For<IValidator>();
        validator2.Validate(file).Returns(new ValidationResult(true, "Validator2"));
        
        var validators = new List<IValidator> { validator1, validator2 };

        // Act
        var result = _service.ValidateFirstFailure(file, validators);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
        
        // Both validators should have been called
        validator1.Received(1).Validate(file);
        validator2.Received(1).Validate(file);
    }

    #endregion

    #region ValidateAll Tests

    [Test]
    public async Task ValidateAll_WithNoValidators_ReturnsValidResult()
    {
        // Arrange
        var file = Substitute.For<IFile>();
        var validators = new List<IValidator>();

        // Act
        var result = _service.ValidateAll(file, validators);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task ValidateAll_WithMultipleFailures_CollectsAllFailures()
    {
        // Arrange
        var file = Substitute.For<IFile>();
        
        var validator1 = Substitute.For<IValidator>();
        validator1.Name.Returns("Validator1");
        validator1.Validate(file).Returns(new ValidationResult(false, "Validator1", "Failure 1"));
        
        var validator2 = Substitute.For<IValidator>();
        validator2.Name.Returns("Validator2");
        validator2.Validate(file).Returns(new ValidationResult(false, "Validator2", "Failure 2"));
        
        var validators = new List<IValidator> { validator1, validator2 };

        // Act
        var result = _service.ValidateAll(file, validators);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Failures.Count).IsEqualTo(2);
        
        // Both validators should have been called
        validator1.Received(1).Validate(file);
        validator2.Received(1).Validate(file);
    }

    [Test]
    public async Task ValidateAll_WithMixedResults_CollectsOnlyFailures()
    {
        // Arrange
        var file = Substitute.For<IFile>();
        
        var validator1 = Substitute.For<IValidator>();
        validator1.Validate(file).Returns(new ValidationResult(true, "Validator1"));
        
        var validator2 = Substitute.For<IValidator>();
        validator2.Name.Returns("Validator2");
        validator2.Validate(file).Returns(new ValidationResult(false, "Validator2", "Failure"));
        
        var validator3 = Substitute.For<IValidator>();
        validator3.Validate(file).Returns(new ValidationResult(true, "Validator3"));
        
        var validators = new List<IValidator> { validator1, validator2, validator3 };

        // Act
        var result = _service.ValidateAll(file, validators);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Failures.Count).IsEqualTo(1);
    }

    #endregion
}
