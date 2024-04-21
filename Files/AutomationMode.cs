namespace PhotoCopy.Files;

/// <summary>
/// Defines the modes of automation for handling certain operations.
/// </summary>
internal enum AutomationMode
{
    /// <summary>
    /// The operation will prompt the user for input or decision.
    /// </summary>
    Prompt,

    /// <summary>
    /// The operation will abort if an issue arises.
    /// </summary>
    Abort,

    /// <summary>
    /// The operation will attempt to automatically resolve any issues.
    /// </summary>
    Automate
}
