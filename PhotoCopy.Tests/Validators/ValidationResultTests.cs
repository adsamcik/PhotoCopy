using PhotoCopy.Validators;

namespace PhotoCopy.Tests.Validators;

public class ValidationResultTests
{
    private const string TestValidatorName = "TestValidator";

    #region Success Factory Method Tests

    [Test]
    public async Task Success_ReturnsValidResult()
    {
        // Arrange & Act
        var result = ValidationResult.Success(TestValidatorName);

        // Assert
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Success_HasCorrectValidatorName()
    {
        // Arrange & Act
        var result = ValidationResult.Success(TestValidatorName);

        // Assert
        await Assert.That(result.ValidatorName).IsEqualTo(TestValidatorName);
    }

    [Test]
    public async Task Success_HasNullReason()
    {
        // Arrange & Act
        var result = ValidationResult.Success(TestValidatorName);

        // Assert
        await Assert.That(result.Reason).IsNull();
    }

    #endregion

    #region Fail Factory Method Tests

    [Test]
    public async Task Fail_ReturnsInvalidResult()
    {
        // Arrange
        var reason = "File is too old";

        // Act
        var result = ValidationResult.Fail(TestValidatorName, reason);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task Fail_HasCorrectValidatorName()
    {
        // Arrange
        var reason = "File is too old";

        // Act
        var result = ValidationResult.Fail(TestValidatorName, reason);

        // Assert
        await Assert.That(result.ValidatorName).IsEqualTo(TestValidatorName);
    }

    [Test]
    public async Task Fail_HasCorrectReason()
    {
        // Arrange
        var reason = "File is too old";

        // Act
        var result = ValidationResult.Fail(TestValidatorName, reason);

        // Assert
        await Assert.That(result.Reason).IsEqualTo(reason);
    }

    #endregion

    #region Deconstruct Tests

    [Test]
    public async Task Deconstruct_SuccessResult_DeconstructsCorrectly()
    {
        // Arrange
        var result = ValidationResult.Success(TestValidatorName);

        // Act
        var (isValid, validatorName, reason) = result;

        // Assert
        await Assert.That(isValid).IsTrue();
        await Assert.That(validatorName).IsEqualTo(TestValidatorName);
        await Assert.That(reason).IsNull();
    }

    [Test]
    public async Task Deconstruct_FailResult_DeconstructsCorrectly()
    {
        // Arrange
        var expectedReason = "Validation failed";
        var result = ValidationResult.Fail(TestValidatorName, expectedReason);

        // Act
        var (isValid, validatorName, reason) = result;

        // Assert
        await Assert.That(isValid).IsFalse();
        await Assert.That(validatorName).IsEqualTo(TestValidatorName);
        await Assert.That(reason).IsEqualTo(expectedReason);
    }

    #endregion

    #region Record Equality Tests

    [Test]
    public async Task Equality_TwoSuccessResults_WithSameValidatorName_AreEqual()
    {
        // Arrange
        var result1 = ValidationResult.Success(TestValidatorName);
        var result2 = ValidationResult.Success(TestValidatorName);

        // Act & Assert
        await Assert.That(result1).IsEqualTo(result2);
        await Assert.That(result1 == result2).IsTrue();
    }

    [Test]
    public async Task Equality_TwoSuccessResults_WithDifferentValidatorNames_AreNotEqual()
    {
        // Arrange
        var result1 = ValidationResult.Success("Validator1");
        var result2 = ValidationResult.Success("Validator2");

        // Act & Assert
        await Assert.That(result1).IsNotEqualTo(result2);
        await Assert.That(result1 != result2).IsTrue();
    }

    [Test]
    public async Task Equality_TwoFailResults_WithSameValidatorNameAndReason_AreEqual()
    {
        // Arrange
        var reason = "Same reason";
        var result1 = ValidationResult.Fail(TestValidatorName, reason);
        var result2 = ValidationResult.Fail(TestValidatorName, reason);

        // Act & Assert
        await Assert.That(result1).IsEqualTo(result2);
        await Assert.That(result1 == result2).IsTrue();
    }

    [Test]
    public async Task Equality_TwoFailResults_WithDifferentReasons_AreNotEqual()
    {
        // Arrange
        var result1 = ValidationResult.Fail(TestValidatorName, "Reason 1");
        var result2 = ValidationResult.Fail(TestValidatorName, "Reason 2");

        // Act & Assert
        await Assert.That(result1).IsNotEqualTo(result2);
        await Assert.That(result1 != result2).IsTrue();
    }

    [Test]
    public async Task Equality_SuccessAndFailResults_AreNotEqual()
    {
        // Arrange
        var successResult = ValidationResult.Success(TestValidatorName);
        var failResult = ValidationResult.Fail(TestValidatorName, null);

        // Act & Assert
        await Assert.That(successResult).IsNotEqualTo(failResult);
        await Assert.That(successResult != failResult).IsTrue();
    }

    [Test]
    public async Task GetHashCode_EqualResults_HaveSameHashCode()
    {
        // Arrange
        var result1 = ValidationResult.Success(TestValidatorName);
        var result2 = ValidationResult.Success(TestValidatorName);

        // Act & Assert
        await Assert.That(result1.GetHashCode()).IsEqualTo(result2.GetHashCode());
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task Fail_WithNullReason_IsValid()
    {
        // Arrange & Act
        var result = ValidationResult.Fail(TestValidatorName, null);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.ValidatorName).IsEqualTo(TestValidatorName);
        await Assert.That(result.Reason).IsNull();
    }

    [Test]
    public async Task Fail_WithEmptyReason_IsValid()
    {
        // Arrange & Act
        var result = ValidationResult.Fail(TestValidatorName, string.Empty);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.ValidatorName).IsEqualTo(TestValidatorName);
        await Assert.That(result.Reason).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Fail_WithWhitespaceReason_PreservesWhitespace()
    {
        // Arrange
        var whitespaceReason = "   ";

        // Act
        var result = ValidationResult.Fail(TestValidatorName, whitespaceReason);

        // Assert
        await Assert.That(result.Reason).IsEqualTo(whitespaceReason);
    }

    [Test]
    public async Task TwoFailResults_WithNullReasons_AreEqual()
    {
        // Arrange
        var result1 = ValidationResult.Fail(TestValidatorName, null);
        var result2 = ValidationResult.Fail(TestValidatorName, null);

        // Act & Assert
        await Assert.That(result1).IsEqualTo(result2);
    }

    [Test]
    public async Task TwoFailResults_WithEmptyReasons_AreEqual()
    {
        // Arrange
        var result1 = ValidationResult.Fail(TestValidatorName, string.Empty);
        var result2 = ValidationResult.Fail(TestValidatorName, string.Empty);

        // Act & Assert
        await Assert.That(result1).IsEqualTo(result2);
    }

    [Test]
    public async Task FailResult_NullReasonAndEmptyReason_AreNotEqual()
    {
        // Arrange
        var resultWithNull = ValidationResult.Fail(TestValidatorName, null);
        var resultWithEmpty = ValidationResult.Fail(TestValidatorName, string.Empty);

        // Act & Assert
        await Assert.That(resultWithNull).IsNotEqualTo(resultWithEmpty);
    }

    #endregion
}
