using System;
using System.IO;
using System.Security;
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

    // Windows error codes for transient file system issues
    private const int ERROR_SHARING_VIOLATION = 32;
    private const int ERROR_LOCK_VIOLATION = 33;
    private const int ERROR_NETWORK_BUSY = 54;
    private const int ERROR_DRIVE_LOCKED = 108;
    private const int ERROR_FILE_INVALID = 1006;
    private const int ERROR_CANT_ACCESS_FILE = 1920;

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
            catch (IOException ex) when (attempt < maxRetries && IsTransientIOException(ex))
            {
                LogRetryAttempt(logger, attempt, maxRetries, operationName, ex);
                Thread.Sleep(GetDelayMs(attempt));
            }
            catch (UnauthorizedAccessException ex) when (attempt < maxRetries && IsTransientUnauthorizedAccess(ex))
            {
                LogRetryAttempt(logger, attempt, maxRetries, operationName, ex);
                Thread.Sleep(GetDelayMs(attempt));
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
                await action().ConfigureAwait(false);
                return;
            }
            catch (IOException ex) when (attempt < maxRetries && IsTransientIOException(ex))
            {
                LogRetryAttempt(logger, attempt, maxRetries, operationName, ex);
                await Task.Delay(GetDelayMs(attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException ex) when (attempt < maxRetries && IsTransientUnauthorizedAccess(ex))
            {
                LogRetryAttempt(logger, attempt, maxRetries, operationName, ex);
                await Task.Delay(GetDelayMs(attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Executes a function with retry logic for transient IO errors and returns the result.
    /// </summary>
    public static T ExecuteWithRetry<T>(
        Func<T> func,
        ILogger logger,
        string operationName,
        int maxRetries = DefaultMaxRetries)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                return func();
            }
            catch (IOException ex) when (attempt < maxRetries && IsTransientIOException(ex))
            {
                LogRetryAttempt(logger, attempt, maxRetries, operationName, ex);
                Thread.Sleep(GetDelayMs(attempt));
            }
            catch (UnauthorizedAccessException ex) when (attempt < maxRetries && IsTransientUnauthorizedAccess(ex))
            {
                LogRetryAttempt(logger, attempt, maxRetries, operationName, ex);
                Thread.Sleep(GetDelayMs(attempt));
            }
        }

        // This line will never be reached because the last attempt either succeeds or throws
        throw new InvalidOperationException("Retry logic failed unexpectedly");
    }

    /// <summary>
    /// Async version of ExecuteWithRetry that returns a result.
    /// </summary>
    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> func,
        ILogger logger,
        string operationName,
        int maxRetries = DefaultMaxRetries,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await func().ConfigureAwait(false);
            }
            catch (IOException ex) when (attempt < maxRetries && IsTransientIOException(ex))
            {
                LogRetryAttempt(logger, attempt, maxRetries, operationName, ex);
                await Task.Delay(GetDelayMs(attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException ex) when (attempt < maxRetries && IsTransientUnauthorizedAccess(ex))
            {
                LogRetryAttempt(logger, attempt, maxRetries, operationName, ex);
                await Task.Delay(GetDelayMs(attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        // This line will never be reached because the last attempt either succeeds or throws
        throw new InvalidOperationException("Retry logic failed unexpectedly");
    }

    /// <summary>
    /// Determines if an IOException represents a transient error that should be retried.
    /// </summary>
    /// <param name="ex">The IOException to evaluate.</param>
    /// <returns>True if the error is transient; otherwise, false.</returns>
    public static bool IsTransientIOException(IOException ex)
    {
        // Extract Windows error code from HResult
        var errorCode = ex.HResult & 0xFFFF;
        
        return errorCode switch
        {
            ERROR_SHARING_VIOLATION => true,   // File is in use by another process
            ERROR_LOCK_VIOLATION => true,      // File is locked
            ERROR_NETWORK_BUSY => true,        // Network is busy
            ERROR_DRIVE_LOCKED => true,        // Drive is locked
            ERROR_FILE_INVALID => true,        // Network file invalidated
            ERROR_CANT_ACCESS_FILE => true,    // Network access issue
            _ => false
        };
    }

    /// <summary>
    /// Determines if an UnauthorizedAccessException represents a transient error.
    /// This can happen when antivirus software temporarily locks a file.
    /// </summary>
    /// <param name="ex">The UnauthorizedAccessException to evaluate.</param>
    /// <returns>True if the error is likely transient; otherwise, false.</returns>
    public static bool IsTransientUnauthorizedAccess(UnauthorizedAccessException ex)
    {
        // UnauthorizedAccessException can be transient when:
        // - Antivirus is scanning a newly created file
        // - File indexing service is processing
        // - Cloud sync (OneDrive, Dropbox) is syncing
        // We treat all UnauthorizedAccessException as potentially transient for file operations
        // because the retry cost is low and the benefit is high
        return ex != null;
    }

    /// <summary>
    /// Calculates the delay in milliseconds for a retry attempt using exponential backoff.
    /// </summary>
    private static int GetDelayMs(int attempt)
    {
        return BaseDelayMs * attempt;
    }

    /// <summary>
    /// Logs a retry attempt.
    /// </summary>
    private static void LogRetryAttempt(ILogger logger, int attempt, int maxRetries, string operationName, Exception ex)
    {
        var delay = GetDelayMs(attempt);
        logger.LogWarning(
            "Retry {Attempt}/{MaxRetries} for {Operation} after {Delay}ms: {Error}",
            attempt, maxRetries, operationName, delay, ex.Message);
    }

    #region Deprecated members for backward compatibility

    /// <summary>
    /// Determines if an IOException represents a transient error that should be retried.
    /// </summary>
    /// <remarks>
    /// This method is kept for backward compatibility. Use <see cref="IsTransientIOException"/> instead.
    /// </remarks>
    [Obsolete("Use IsTransientIOException instead")]
    public static bool IsTransientError(IOException ex) => IsTransientIOException(ex);

    #endregion
}
