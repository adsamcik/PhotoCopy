using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PhotoCopy.Files;

namespace PhotoCopy.Tests.Files;

/// <summary>
/// Unit tests for RetryHelper class.
/// Tests cover retry logic for transient IO errors.
/// </summary>
public class RetryHelperTests
{
    private readonly ILogger _mockLogger;

    public RetryHelperTests()
    {
        _mockLogger = Substitute.For<ILogger>();
    }

    #region Helper Methods

    private static IOException CreateTransientIOException(int errorCode)
    {
        // Create IOException with specific HResult for sharing/lock violation
        // HResult format: 0x80070000 | errorCode
        var ex = new IOException($"Transient error with code {errorCode}");
        // Use reflection to set HResult since it's read-only in IOException
        var hResultProperty = typeof(Exception).GetProperty("HResult");
        hResultProperty?.SetValue(ex, unchecked((int)(0x80070000 | errorCode)));
        return ex;
    }

    private static IOException CreateNonTransientIOException()
    {
        // Create IOException with non-transient HResult (e.g., file not found = 2)
        var ex = new IOException("Non-transient error");
        var hResultProperty = typeof(Exception).GetProperty("HResult");
        hResultProperty?.SetValue(ex, unchecked((int)(0x80070000 | 2)));
        return ex;
    }

    #endregion

    #region Synchronous ExecuteWithRetry Tests

    [Test]
    public void ExecuteWithRetry_SuccessfulOnFirstTry_CompletesWithoutRetry()
    {
        // Arrange
        var callCount = 0;
        Action action = () => callCount++;

        // Act
        RetryHelper.ExecuteWithRetry(action, _mockLogger, "TestOperation");

        // Assert
        callCount.Should().Be(1);
    }

    [Test]
    public void ExecuteWithRetry_TransientSharingViolation_RetriesAndSucceeds()
    {
        // Arrange - ERROR_SHARING_VIOLATION = 32
        var callCount = 0;
        Action action = () =>
        {
            callCount++;
            if (callCount < 2)
            {
                throw CreateTransientIOException(32);
            }
        };

        // Act
        RetryHelper.ExecuteWithRetry(action, _mockLogger, "TestOperation");

        // Assert
        callCount.Should().Be(2);
    }

    [Test]
    public void ExecuteWithRetry_TransientLockViolation_RetriesAndSucceeds()
    {
        // Arrange - ERROR_LOCK_VIOLATION = 33
        var callCount = 0;
        Action action = () =>
        {
            callCount++;
            if (callCount < 2)
            {
                throw CreateTransientIOException(33);
            }
        };

        // Act
        RetryHelper.ExecuteWithRetry(action, _mockLogger, "TestOperation");

        // Assert
        callCount.Should().Be(2);
    }

    [Test]
    public void ExecuteWithRetry_NonTransientIOException_ThrowsImmediatelyWithoutRetry()
    {
        // Arrange
        var callCount = 0;
        Action action = () =>
        {
            callCount++;
            throw CreateNonTransientIOException();
        };

        // Act & Assert
        var act = () => RetryHelper.ExecuteWithRetry(action, _mockLogger, "TestOperation");
        act.Should().Throw<IOException>();
        callCount.Should().Be(1);
    }

    [Test]
    public void ExecuteWithRetry_MaxRetriesExceeded_ThrowsIOException()
    {
        // Arrange
        var callCount = 0;
        Action action = () =>
        {
            callCount++;
            throw CreateTransientIOException(32);
        };

        // Act & Assert
        var act = () => RetryHelper.ExecuteWithRetry(action, _mockLogger, "TestOperation", maxRetries: 3);
        act.Should().Throw<IOException>();
        callCount.Should().Be(3);
    }

    [Test]
    public void ExecuteWithRetry_MultipleTransientFailures_RetriesUntilSuccess()
    {
        // Arrange
        var callCount = 0;
        Action action = () =>
        {
            callCount++;
            if (callCount < 3)
            {
                throw CreateTransientIOException(32);
            }
        };

        // Act
        RetryHelper.ExecuteWithRetry(action, _mockLogger, "TestOperation", maxRetries: 3);

        // Assert
        callCount.Should().Be(3);
    }

    [Test]
    public void ExecuteWithRetry_CustomMaxRetries_RespectsLimit()
    {
        // Arrange
        var callCount = 0;
        Action action = () =>
        {
            callCount++;
            throw CreateTransientIOException(32);
        };

        // Act & Assert
        var act = () => RetryHelper.ExecuteWithRetry(action, _mockLogger, "TestOperation", maxRetries: 5);
        act.Should().Throw<IOException>();
        callCount.Should().Be(5);
    }

    #endregion

    #region Asynchronous ExecuteWithRetryAsync Tests

    [Test]
    public async Task ExecuteWithRetryAsync_SuccessfulOnFirstTry_CompletesWithoutRetry()
    {
        // Arrange
        var callCount = 0;
        Func<Task> action = () =>
        {
            callCount++;
            return Task.CompletedTask;
        };

        // Act
        await RetryHelper.ExecuteWithRetryAsync(action, _mockLogger, "TestOperation");

        // Assert
        callCount.Should().Be(1);
    }

    [Test]
    public async Task ExecuteWithRetryAsync_TransientSharingViolation_RetriesAndSucceeds()
    {
        // Arrange - ERROR_SHARING_VIOLATION = 32
        var callCount = 0;
        Func<Task> action = () =>
        {
            callCount++;
            if (callCount < 2)
            {
                throw CreateTransientIOException(32);
            }
            return Task.CompletedTask;
        };

        // Act
        await RetryHelper.ExecuteWithRetryAsync(action, _mockLogger, "TestOperation");

        // Assert
        callCount.Should().Be(2);
    }

    [Test]
    public async Task ExecuteWithRetryAsync_NonTransientIOException_ThrowsImmediatelyWithoutRetry()
    {
        // Arrange
        var callCount = 0;
        Func<Task> action = () =>
        {
            callCount++;
            throw CreateNonTransientIOException();
        };

        // Act & Assert
        var act = async () => await RetryHelper.ExecuteWithRetryAsync(action, _mockLogger, "TestOperation");
        await act.Should().ThrowAsync<IOException>();
        callCount.Should().Be(1);
    }

    [Test]
    public async Task ExecuteWithRetryAsync_MaxRetriesExceeded_ThrowsIOException()
    {
        // Arrange
        var callCount = 0;
        Func<Task> action = () =>
        {
            callCount++;
            throw CreateTransientIOException(32);
        };

        // Act & Assert
        var act = async () => await RetryHelper.ExecuteWithRetryAsync(action, _mockLogger, "TestOperation", maxRetries: 3);
        await act.Should().ThrowAsync<IOException>();
        callCount.Should().Be(3);
    }

    [Test]
    public async Task ExecuteWithRetryAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var callCount = 0;
        Func<Task> action = () =>
        {
            callCount++;
            cts.Cancel(); // Cancel before retry delay
            throw CreateTransientIOException(32);
        };

        // Act & Assert
        var act = async () => await RetryHelper.ExecuteWithRetryAsync(
            action, _mockLogger, "TestOperation", cancellationToken: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion
}
