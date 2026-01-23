using System;

namespace PhotoCopy.Validators;

/// <summary>
/// Default implementation of console interaction using System.Console.
/// </summary>
public class ConsoleInteraction : IConsoleInteraction
{
    /// <inheritdoc />
    public void WriteLine(string message) => Console.WriteLine(message);

    /// <inheritdoc />
    public string? ReadLine() => Console.ReadLine();

    /// <inheritdoc />
    public bool IsInputRedirected => Console.IsInputRedirected;
}
