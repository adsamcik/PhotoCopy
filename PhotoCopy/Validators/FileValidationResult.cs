using System.Collections.Generic;
using PhotoCopy.Files;

namespace PhotoCopy.Validators;

/// <summary>
/// Result of validating a single file against multiple validators.
/// </summary>
public sealed record FileValidationResult
{
    private FileValidationResult(IFile file, bool isValid, IReadOnlyList<ValidationResult> failures)
    {
        File = file;
        IsValid = isValid;
        Failures = failures;
    }

    public IFile File { get; }
    public bool IsValid { get; }
    public IReadOnlyList<ValidationResult> Failures { get; }

    /// <summary>
    /// Gets the first failure reason, if any.
    /// </summary>
    public string? FirstRejectionReason => Failures.Count > 0 ? Failures[0].Reason : null;

    /// <summary>
    /// Gets the first failing validator name, if any.
    /// </summary>
    public string? FirstValidatorName => Failures.Count > 0 ? Failures[0].ValidatorName : null;

    public static FileValidationResult Valid(IFile file) 
        => new(file, true, []);

    public static FileValidationResult Invalid(IFile file, IReadOnlyList<ValidationResult> failures) 
        => new(file, false, failures);
}
