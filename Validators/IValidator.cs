using PhotoCopy.Files;

namespace PhotoCopy.Validators;

internal interface IValidator
{
    /// <summary>
    /// Validates if file is compliant with the validator conditions
    /// </summary>
    /// <param name="file">File</param>
    /// <returns>true if file satisfies the validator.</returns>
    bool Validate(IFile file);
}
