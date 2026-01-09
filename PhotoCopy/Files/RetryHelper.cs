using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PhotoCopy.Files;

/// <summary>
/// Provides retry logic for transient file operation failures.
/// </summary>
public static class RetryHelper
{
    private const int DefaultMaxRetries = 3;
    private const int BaseDelayMs = 100;

    /// <summary>
    /// Executes an action with retry logic for transient IO errors.
    /// </summary>
    public static void ExecuteWithRetry(
        Action action,
        ILogger logger,
        string operationName,
        int maxRetries = DefaultMaxRetries)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException ex) when (attempt < maxRetries && IsTransientError(ex))
            {
                var delay = BaseDelayMs * attempt;
                logger.LogWarning(
                    "Retry {Attempt}/{MaxRetries} for {Operation} after {Delay}ms: {Error}",
                    attempt, maxRetries, operationName, delay, ex.Message);
                Thread.Sleep(delay);
            }
        }
    }

    /// <summary>
    /// Async version of ExecuteWithRetry.
    /// </summary>
    public static async Task ExecuteWithRetryAsync(
        Func<Task> action,
        ILogger logger,
        string operationName,
        int maxRetries = DefaultMaxRetries,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (IOException ex) when (attempt < maxRetries && IsTransientError(ex))
            {
                var delay = BaseDelayMs * attempt;
                logger.LogWarning(
                    "Retry {Attempt}/{MaxRetries} for {Operation} after {Delay}ms: {Error}",
                    attempt, maxRetries, operationName, delay, ex.Message);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool IsTransientError(IOException ex)
    {
        // Windows error codes for file locking issues
        var hResult = ex.HResult & 0xFFFF;
        return hResult == 32  // ERROR_SHARING_VIOLATION
            || hResult == 33; // ERROR_LOCK_VIOLATION
    }
}
