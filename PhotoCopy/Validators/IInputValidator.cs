using PhotoCopy.Configuration;

namespace PhotoCopy.Validators;

/// <summary>
/// Validates user input and configuration before command execution.
/// </summary>
public interface IInputValidator
{
    /// <summary>
    /// Validates configuration for copy command execution.
    /// </summary>
    /// <param name="config">The configuration to validate.</param>
    /// <returns>True if valid, false otherwise.</returns>
    bool ValidateCopyConfiguration(PhotoCopyConfig config);

    /// <summary>
    /// Validates that source path is specified and exists.
    /// </summary>
    /// <param name="config">The configuration to validate.</param>
    /// <returns>True if valid, false otherwise.</returns>
    bool ValidateSourceRequired(PhotoCopyConfig config);
}
