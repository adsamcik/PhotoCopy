using PhotoCopy.Abstractions;
using PhotoCopy.Files;

namespace PhotoCopy.Validators;

public interface IValidator
{
    string Name { get; }

    /// <summary>
    /// Validates if file is compliant with the validator conditions
    /// </summary>
    /// <param name="file">File</param>
    /// <returns>Detailed result describing whether the file passed.</returns>
    ValidationResult Validate(IFile file);
}
