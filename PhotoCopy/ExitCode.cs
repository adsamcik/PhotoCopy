namespace PhotoCopy;

/// <summary>
/// Standard exit codes for the PhotoCopy application.
/// </summary>
public enum ExitCode
{
    /// <summary>
    /// Operation completed successfully.
    /// </summary>
    Success = 0,

    /// <summary>
    /// Operation failed due to an error.
    /// </summary>
    Error = 1,

    /// <summary>
    /// Operation was cancelled by the user.
    /// </summary>
    Cancelled = 2
}
