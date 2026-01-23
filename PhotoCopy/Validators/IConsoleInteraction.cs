namespace PhotoCopy.Validators;

/// <summary>
/// Abstraction for console interaction to enable testing.
/// </summary>
public interface IConsoleInteraction
{
    /// <summary>
    /// Writes a line to the console.
    /// </summary>
    /// <param name="message">The message to write.</param>
    void WriteLine(string message);

    /// <summary>
    /// Reads a line from the console.
    /// </summary>
    /// <returns>The line read, or null if no input is available.</returns>
    string? ReadLine();

    /// <summary>
    /// Gets whether input is redirected (non-interactive mode).
    /// </summary>
    bool IsInputRedirected { get; }
}
