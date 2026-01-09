using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoCopy.Tests.E2E;

/// <summary>
/// Provides methods to run external processes and capture their output.
/// </summary>
public static class ProcessRunner
{
    /// <summary>
    /// Default timeout for process execution (60 seconds).
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Runs an executable with the specified verb and arguments.
    /// </summary>
    /// <param name="executablePath">Path to the executable (.exe or .dll).</param>
    /// <param name="verb">The PhotoCopy command verb (e.g., "copy", "scan", "validate").</param>
    /// <param name="args">Additional arguments for the command.</param>
    /// <param name="timeout">Optional timeout. Defaults to 60 seconds.</param>
    /// <param name="workingDirectory">Optional working directory for the process.</param>
    /// <returns>A ProcessResult capturing the execution outcome.</returns>
    /// <exception cref="TimeoutException">Thrown if the process does not complete within the timeout.</exception>
    public static async Task<ProcessResult> RunAsync(
        string executablePath,
        string verb,
        string[] args,
        TimeSpan? timeout = null,
        string? workingDirectory = null)
    {
        timeout ??= DefaultTimeout;

        var (fileName, arguments) = BuildCommandLine(executablePath, verb, args);
        var fullCommandLine = $"{fileName} {arguments}";

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? string.Empty
        };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();

        using var process = new Process { StartInfo = startInfo };
        using var cts = new CancellationTokenSource(timeout.Value);

        // Use TaskCompletionSource to handle async output
        var stdoutComplete = new TaskCompletionSource<bool>();
        var stderrComplete = new TaskCompletionSource<bool>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stdoutComplete.TrySetResult(true);
            }
            else
            {
                lock (stdoutBuilder)
                {
                    if (stdoutBuilder.Length > 0)
                        stdoutBuilder.AppendLine();
                    stdoutBuilder.Append(e.Data);
                }
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stderrComplete.TrySetResult(true);
            }
            else
            {
                lock (stderrBuilder)
                {
                    if (stderrBuilder.Length > 0)
                        stderrBuilder.AppendLine();
                    stderrBuilder.Append(e.Data);
                }
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for process to exit with timeout
            var processExitTask = process.WaitForExitAsync(cts.Token);

            try
            {
                await processExitTask;
            }
            catch (OperationCanceledException)
            {
                // Timeout occurred - try to kill the process
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort kill
                }

                stopwatch.Stop();
                throw new TimeoutException(
                    $"Process did not complete within {timeout.Value.TotalSeconds} seconds. " +
                    $"Command: {fullCommandLine}");
            }

            // Wait for output streams to complete (with a short additional timeout)
            var outputTimeout = Task.Delay(TimeSpan.FromSeconds(5));
            await Task.WhenAny(
                Task.WhenAll(stdoutComplete.Task, stderrComplete.Task),
                outputTimeout);

            stopwatch.Stop();

            return new ProcessResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = stdoutBuilder.ToString(),
                StandardError = stderrBuilder.ToString(),
                Duration = stopwatch.Elapsed,
                CommandLine = fullCommandLine
            };
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ProcessResult
            {
                ExitCode = -1,
                StandardOutput = stdoutBuilder.ToString(),
                StandardError = $"Failed to run process: {ex.Message}\n{stderrBuilder}",
                Duration = stopwatch.Elapsed,
                CommandLine = fullCommandLine
            };
        }
    }

    /// <summary>
    /// Builds the command line based on executable type (.exe vs .dll).
    /// </summary>
    private static (string FileName, string Arguments) BuildCommandLine(
        string executablePath,
        string verb,
        string[] args)
    {
        var escapedArgs = new StringBuilder();

        // Add the verb first
        if (!string.IsNullOrEmpty(verb))
        {
            escapedArgs.Append(EscapeArgument(verb));
        }

        // Add remaining arguments
        foreach (var arg in args)
        {
            if (escapedArgs.Length > 0)
                escapedArgs.Append(' ');
            escapedArgs.Append(EscapeArgument(arg));
        }

        // Check if this is a .dll file - need to run via dotnet
        if (executablePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var dllPath = EscapeArgument(executablePath);
            return ("dotnet", $"{dllPath} {escapedArgs}".Trim());
        }
        else
        {
            // Direct .exe execution
            return (executablePath, escapedArgs.ToString());
        }
    }

    /// <summary>
    /// Escapes an argument for safe command-line usage.
    /// </summary>
    private static string EscapeArgument(string arg)
    {
        if (string.IsNullOrEmpty(arg))
            return "\"\"";

        // If the argument contains spaces, quotes, or special characters, wrap in quotes
        if (arg.Contains(' ') || arg.Contains('"') || arg.Contains('\t'))
        {
            // Escape any embedded quotes by doubling them
            var escaped = arg.Replace("\"", "\\\"");
            return $"\"{escaped}\"";
        }

        return arg;
    }
}
