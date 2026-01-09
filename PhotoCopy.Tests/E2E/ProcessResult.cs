using System;
using System.Linq;

namespace PhotoCopy.Tests.E2E;

/// <summary>
/// Captures the result of running an external process.
/// </summary>
public sealed record ProcessResult
{
    /// <summary>
    /// The exit code returned by the process.
    /// </summary>
    public int ExitCode { get; init; }

    /// <summary>
    /// The complete standard output from the process.
    /// </summary>
    public string StandardOutput { get; init; } = string.Empty;

    /// <summary>
    /// The complete standard error from the process.
    /// </summary>
    public string StandardError { get; init; } = string.Empty;

    /// <summary>
    /// How long the process took to execute.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// The full command line that was executed.
    /// </summary>
    public string CommandLine { get; init; } = string.Empty;

    /// <summary>
    /// Whether the process exited successfully (exit code 0).
    /// </summary>
    public bool Success => ExitCode == 0;

    /// <summary>
    /// Standard output split into individual lines.
    /// Empty lines are preserved.
    /// </summary>
    public string[] OutputLines => StandardOutput.Split(
        new[] { "\r\n", "\n" },
        StringSplitOptions.None);

    /// <summary>
    /// Standard error split into individual lines.
    /// Empty lines are preserved.
    /// </summary>
    public string[] ErrorLines => StandardError.Split(
        new[] { "\r\n", "\n" },
        StringSplitOptions.None);

    /// <summary>
    /// Non-empty output lines (trimmed).
    /// </summary>
    public string[] NonEmptyOutputLines => OutputLines
        .Select(l => l.Trim())
        .Where(l => !string.IsNullOrEmpty(l))
        .ToArray();

    /// <summary>
    /// Non-empty error lines (trimmed).
    /// </summary>
    public string[] NonEmptyErrorLines => ErrorLines
        .Select(l => l.Trim())
        .Where(l => !string.IsNullOrEmpty(l))
        .ToArray();

    /// <summary>
    /// Checks if the standard output contains the specified text (case-insensitive).
    /// </summary>
    public bool OutputContains(string text) =>
        StandardOutput.Contains(text, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Checks if the standard error contains the specified text (case-insensitive).
    /// </summary>
    public bool ErrorContains(string text) =>
        StandardError.Contains(text, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns a formatted summary of the process result for debugging.
    /// </summary>
    public override string ToString()
    {
        return $"""
            ProcessResult:
              Command: {CommandLine}
              ExitCode: {ExitCode}
              Duration: {Duration.TotalMilliseconds:F0}ms
              Success: {Success}
              StdOut ({OutputLines.Length} lines): {(StandardOutput.Length > 200 ? StandardOutput[..200] + "..." : StandardOutput)}
              StdErr ({ErrorLines.Length} lines): {(StandardError.Length > 200 ? StandardError[..200] + "..." : StandardError)}
            """;
    }
}
