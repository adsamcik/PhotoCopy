using System.Diagnostics.CodeAnalysis;
using PhotoCopy.Files;

namespace PhotoCopy.Validators;

/// <summary>
/// Represents the outcome of validating a single <see cref="IFile"/> instance.
/// </summary>
public readonly record struct ValidationResult(bool IsValid, string ValidatorName, string? Reason = null)
{
    public static ValidationResult Success(string validatorName) => new(true, validatorName, null);

    public static ValidationResult Fail(string validatorName, string? reason)
        => new(false, validatorName, reason);

    public void Deconstruct(out bool isValid, out string validatorName, out string? reason)
    {
        isValid = IsValid;
        validatorName = ValidatorName;
        reason = Reason;
    }
}