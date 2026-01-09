using System.Collections.Generic;
using PhotoCopy.Files;

namespace PhotoCopy.Validators;

/// <summary>
/// Service for validating files against a collection of validators.
/// </summary>
public interface IFileValidationService
{
    /// <summary>
    /// Validates a file against all provided validators, stopping at first failure.
    /// </summary>
    FileValidationResult ValidateFirstFailure(IFile file, IReadOnlyCollection<IValidator> validators);

    /// <summary>
    /// Validates a file against all provided validators, collecting all failures.
    /// </summary>
    FileValidationResult ValidateAll(IFile file, IReadOnlyCollection<IValidator> validators);
}
