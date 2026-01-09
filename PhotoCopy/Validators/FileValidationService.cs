using System.Collections.Generic;
using PhotoCopy.Files;

namespace PhotoCopy.Validators;

/// <summary>
/// Default implementation of file validation service.
/// </summary>
public class FileValidationService : IFileValidationService
{
    public FileValidationResult ValidateFirstFailure(IFile file, IReadOnlyCollection<IValidator> validators)
    {
        foreach (var validator in validators)
        {
            var result = validator.Validate(file);
            if (!result.IsValid)
            {
                return FileValidationResult.Invalid(file, [result]);
            }
        }

        return FileValidationResult.Valid(file);
    }

    public FileValidationResult ValidateAll(IFile file, IReadOnlyCollection<IValidator> validators)
    {
        var failures = new List<ValidationResult>();

        foreach (var validator in validators)
        {
            var result = validator.Validate(file);
            if (!result.IsValid)
            {
                failures.Add(result);
            }
        }

        return failures.Count > 0
            ? FileValidationResult.Invalid(file, failures)
            : FileValidationResult.Valid(file);
    }
}
