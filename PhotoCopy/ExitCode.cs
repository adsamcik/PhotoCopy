namespace PhotoCopy;

/// <summary>
/// Standard exit codes for the PhotoCopy application.
/// These codes are designed for scripting and automation scenarios.
/// </summary>
/// <remarks>
/// Exit codes follow Unix conventions:
/// - 0 = success
/// - 1 = generic error (catchall for unexpected failures)
/// - 2+ = specific error categories
/// </remarks>
public enum ExitCode
{
    /// <summary>
    /// Operation completed successfully with no errors.
    /// </summary>
    Success = 0,

    /// <summary>
    /// Operation failed due to an unexpected or unclassified error.
    /// Use this for errors that don't fit other categories.
    /// </summary>
    Error = 1,

    /// <summary>
    /// Operation was cancelled by the user (e.g., Ctrl+C).
    /// </summary>
    Cancelled = 2,

    /// <summary>
    /// Configuration validation failed. This includes:
    /// - Invalid configuration file syntax
    /// - Invalid paths (source/destination don't exist)
    /// - Conflicting configuration options
    /// - Unknown variables in destination pattern
    /// </summary>
    ConfigurationError = 3,

    /// <summary>
    /// Input validation failed. This is used when:
    /// - Files fail validation rules in the 'validate' command
    /// - Required command-line arguments are missing or invalid
    /// </summary>
    ValidationError = 4,

    /// <summary>
    /// Operation completed but with partial success. This occurs when:
    /// - Some files were processed successfully, but others failed
    /// - Rollback completed but some files could not be restored
    /// </summary>
    PartialSuccess = 5,

    /// <summary>
    /// File system I/O error occurred. This includes:
    /// - Permission denied (UnauthorizedAccessException)
    /// - Disk full
    /// - File/directory not found during operation
    /// - File locking issues
    /// </summary>
    IOError = 6,

    /// <summary>
    /// Command-line argument parsing failed. This occurs when:
    /// - Unknown command or verb specified
    /// - Required arguments missing
    /// - Invalid argument format
    /// </summary>
    InvalidArguments = 7
}
