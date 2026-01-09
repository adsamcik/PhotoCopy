using System;
using System.Collections.Generic;
using System.IO;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using PhotoCopy.Progress;

namespace PhotoCopy.Tests.Progress;

/// <summary>
/// Test-specific logger that captures log entries in an isolated list per test
/// </summary>
public class TestLogger : ILogger
{
    public List<TestLogEntry> Logs { get; } = new();

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Logs.Add(new TestLogEntry
        {
            LogLevel = logLevel,
            EventId = eventId,
            Message = formatter(state, exception),
            Exception = exception
        });
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoopDisposable();

    private class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

public class TestLogEntry
{
    public LogLevel LogLevel { get; set; }
    public EventId EventId { get; set; }
    public string Message { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
}

public class ConsoleProgressReporterTests
{
    #region ReportProgress Tests

    [Test]
    public void ReportProgress_LogsProgress()
    {
        // Arrange
        var logger = new TestLogger();
        var reporter = new ConsoleProgressReporter(logger, verbose: true);
        var progress = new CopyProgress(
            CurrentFile: 5,
            TotalFiles: 100,
            BytesProcessed: 5000,
            TotalBytes: 100000,
            CurrentFileName: "photo.jpg",
            Elapsed: TimeSpan.FromSeconds(10));

        // Act
        reporter.Report(progress);

        // Assert
        logger.Logs.Should().HaveCountGreaterThan(0);
        var logEntry = logger.Logs[^1];
        logEntry.LogLevel.Should().Be(LogLevel.Information);
        logEntry.Message.Should().Contain("Progress:");
        logEntry.Message.Should().Contain("5%");
        logEntry.Message.Should().Contain("5/100");
    }

    [Test]
    public void ReportProgress_InNonVerboseMode_ReportsEveryFivePercent()
    {
        // Arrange
        var logger = new TestLogger();
        var reporter = new ConsoleProgressReporter(logger, verbose: false);

        // Act - Report at 5%
        var progress5 = new CopyProgress(5, 100, 500, 10000, "file1.jpg", TimeSpan.FromSeconds(5));
        reporter.Report(progress5);

        // Report at 6% (should be skipped)
        var progress6 = new CopyProgress(6, 100, 600, 10000, "file2.jpg", TimeSpan.FromSeconds(6));
        reporter.Report(progress6);

        // Report at 10%
        var progress10 = new CopyProgress(10, 100, 1000, 10000, "file3.jpg", TimeSpan.FromSeconds(10));
        reporter.Report(progress10);

        // Assert - Should have 2 log entries (5% and 10%), not 3
        logger.Logs.Should().HaveCount(2);
    }

    [Test]
    public void ReportProgress_InVerboseMode_ReportsEveryUpdate()
    {
        // Arrange
        var logger = new TestLogger();
        var reporter = new ConsoleProgressReporter(logger, verbose: true);

        // Act
        for (int i = 1; i <= 5; i++)
        {
            var progress = new CopyProgress(i, 100, i * 100, 10000, $"file{i}.jpg", TimeSpan.FromSeconds(i));
            reporter.Report(progress);
        }

        // Assert - Should have 5 log entries in verbose mode
        logger.Logs.Should().HaveCount(5);
    }

    [Test]
    public void ReportProgress_WithLongFileName_TruncatesFileName()
    {
        // Arrange
        var logger = new TestLogger();
        var reporter = new ConsoleProgressReporter(logger, verbose: true);
        var longFileName = "this_is_a_very_long_file_name_that_should_be_truncated.jpg";
        var progress = new CopyProgress(
            CurrentFile: 5,
            TotalFiles: 100,
            BytesProcessed: 500,
            TotalBytes: 10000,
            CurrentFileName: longFileName,
            Elapsed: TimeSpan.FromSeconds(5));

        // Act
        reporter.Report(progress);

        // Assert
        logger.Logs.Should().HaveCountGreaterThan(0);
        var logEntry = logger.Logs[^1];
        logEntry.Message.Should().Contain("...");
    }

    [Test]
    public void ReportProgress_WithZeroElapsed_ShowsCalculatingETA()
    {
        // Arrange
        var logger = new TestLogger();
        var reporter = new ConsoleProgressReporter(logger, verbose: true);
        var progress = new CopyProgress(
            CurrentFile: 0,
            TotalFiles: 100,
            BytesProcessed: 0,
            TotalBytes: 10000,
            CurrentFileName: "first.jpg",
            Elapsed: TimeSpan.Zero);

        // Act
        reporter.Report(progress);

        // Assert
        logger.Logs.Should().HaveCountGreaterThan(0);
        var logEntry = logger.Logs[^1];
        logEntry.Message.Should().Contain("calculating...");
    }

    [Test]
    public void ReportProgress_WithValidProgress_ShowsETA()
    {
        // Arrange
        var logger = new TestLogger();
        var reporter = new ConsoleProgressReporter(logger, verbose: true);
        var progress = new CopyProgress(
            CurrentFile: 50,
            TotalFiles: 100,
            BytesProcessed: 5000,
            TotalBytes: 10000,
            CurrentFileName: "midway.jpg",
            Elapsed: TimeSpan.FromMinutes(5));

        // Act
        reporter.Report(progress);

        // Assert
        logger.Logs.Should().HaveCountGreaterThan(0);
        var logEntry = logger.Logs[^1];
        logEntry.Message.Should().Contain("ETA:");
        logEntry.Message.Should().NotContain("calculating...");
    }

    [Test]
    public void ReportProgress_AlwaysReports100Percent()
    {
        // Arrange
        var logger = new TestLogger();
        var reporter = new ConsoleProgressReporter(logger, verbose: false);

        // Act - Report at 100%
        var progress = new CopyProgress(100, 100, 10000, 10000, "final.jpg", TimeSpan.FromMinutes(1));
        reporter.Report(progress);

        // Assert
        logger.Logs.Should().HaveCount(1);
        logger.Logs[0].Message.Should().Contain("100%");
    }

    #endregion

    #region ReportError Tests

    [Test]
    public void ReportError_LogsError()
    {
        // Arrange
        var logger = new TestLogger();
        var reporter = new ConsoleProgressReporter(logger, verbose: false);
        var fileName = "corrupted.jpg";
        var exception = new IOException("File is corrupted");

        // Act
        reporter.ReportError(fileName, exception);

        // Assert
        logger.Logs.Should().HaveCount(1);
        var logEntry = logger.Logs[0];
        logEntry.LogLevel.Should().Be(LogLevel.Error);
        logEntry.Message.Should().Contain(fileName);
        logEntry.Exception.Should().Be(exception);
    }

    [Test]
    public void ReportError_WithDifferentExceptionTypes_LogsCorrectly()
    {
        // Arrange
        var logger = new TestLogger();
        var reporter = new ConsoleProgressReporter(logger, verbose: false);
        var fileName = "access_denied.jpg";
        var exception = new UnauthorizedAccessException("Access denied");

        // Act
        reporter.ReportError(fileName, exception);

        // Assert
        logger.Logs.Should().HaveCount(1);
        var logEntry = logger.Logs[0];
        logEntry.LogLevel.Should().Be(LogLevel.Error);
        logEntry.Exception.Should().BeOfType<UnauthorizedAccessException>();
    }

    [Test]
    public void ReportError_MultipleTimes_LogsAllErrors()
    {
        // Arrange
        var logger = new TestLogger();
        var reporter = new ConsoleProgressReporter(logger, verbose: false);

        // Act
        reporter.ReportError("file1.jpg", new Exception("Error 1"));
        reporter.ReportError("file2.jpg", new Exception("Error 2"));
        reporter.ReportError("file3.jpg", new Exception("Error 3"));

        // Assert
        logger.Logs.Should().HaveCount(3);
        logger.Logs.Should().AllSatisfy(entry => entry.LogLevel.Should().Be(LogLevel.Error));
    }

    #endregion

    #region Complete Tests

    [Test]
    public void Complete_LogsCompletion()
    {
        // Arrange
        var logger = new TestLogger();
        var reporter = new ConsoleProgressReporter(logger, verbose: false);
        var finalProgress = new CopyProgress(
            CurrentFile: 100,
            TotalFiles: 100,
            BytesProcessed: 1_048_576, // 1 MB
            TotalBytes: 1_048_576,
            CurrentFileName: "last.jpg",
            Elapsed: TimeSpan.FromMinutes(2));

        // Act
        reporter.Complete(finalProgress);

        // Assert
        logger.Logs.Should().HaveCount(1);
        var logEntry = logger.Logs[0];
        logEntry.LogLevel.Should().Be(LogLevel.Information);
        logEntry.Message.Should().Contain("Completed:");
        logEntry.Message.Should().Contain("100 files");
    }

    [Test]
    public void Complete_ShowsFormattedBytes()
    {
        // Arrange
        var logger = new TestLogger();
        var reporter = new ConsoleProgressReporter(logger, verbose: false);
        var finalProgress = new CopyProgress(
            CurrentFile: 50,
            TotalFiles: 50,
            BytesProcessed: 1_073_741_824, // 1 GB
            TotalBytes: 1_073_741_824,
            CurrentFileName: "final.mp4",
            Elapsed: TimeSpan.FromMinutes(30));

        // Act
        reporter.Complete(finalProgress);

        // Assert
        logger.Logs.Should().HaveCount(1);
        var logEntry = logger.Logs[0];
        logEntry.Message.Should().Contain("GB");
    }

    [Test]
    public void Complete_ShowsFormattedTime()
    {
        // Arrange
        var logger = new TestLogger();
        var reporter = new ConsoleProgressReporter(logger, verbose: false);
        var finalProgress = new CopyProgress(
            CurrentFile: 200,
            TotalFiles: 200,
            BytesProcessed: 5_000_000,
            TotalBytes: 5_000_000,
            CurrentFileName: "done.jpg",
            Elapsed: TimeSpan.FromHours(1).Add(TimeSpan.FromMinutes(30)));

        // Act
        reporter.Complete(finalProgress);

        // Assert
        logger.Logs.Should().HaveCount(1);
        var logEntry = logger.Logs[0];
        logEntry.Message.Should().Contain("1h 30m");
    }

    [Test]
    public void Complete_WithShortDuration_ShowsSeconds()
    {
        // Arrange
        var logger = new TestLogger();
        var reporter = new ConsoleProgressReporter(logger, verbose: false);
        var finalProgress = new CopyProgress(
            CurrentFile: 10,
            TotalFiles: 10,
            BytesProcessed: 10000,
            TotalBytes: 10000,
            CurrentFileName: "quick.jpg",
            Elapsed: TimeSpan.FromSeconds(45));

        // Act
        reporter.Complete(finalProgress);

        // Assert
        logger.Logs.Should().HaveCount(1);
        var logEntry = logger.Logs[0];
        logEntry.Message.Should().Contain("45s");
    }

    [Test]
    public void Complete_WithMinutesDuration_ShowsMinutesAndSeconds()
    {
        // Arrange
        var logger = new TestLogger();
        var reporter = new ConsoleProgressReporter(logger, verbose: false);
        var finalProgress = new CopyProgress(
            CurrentFile: 50,
            TotalFiles: 50,
            BytesProcessed: 50000,
            TotalBytes: 50000,
            CurrentFileName: "medium.jpg",
            Elapsed: TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(30)));

        // Act
        reporter.Complete(finalProgress);

        // Assert
        logger.Logs.Should().HaveCount(1);
        var logEntry = logger.Logs[0];
        logEntry.Message.Should().Contain("5m 30s");
    }

    #endregion

    #region Edge Cases

    [Test]
    public void ReportProgress_WithEmptyFileName_HandlesGracefully()
    {
        // Arrange
        var logger = new TestLogger();
        var reporter = new ConsoleProgressReporter(logger, verbose: true);
        var progress = new CopyProgress(
            CurrentFile: 5,
            TotalFiles: 100,
            BytesProcessed: 500,
            TotalBytes: 10000,
            CurrentFileName: "",
            Elapsed: TimeSpan.FromSeconds(5));

        // Act
        reporter.Report(progress);

        // Assert
        logger.Logs.Should().HaveCountGreaterThan(0);
    }

    [Test]
    public void ReportProgress_WithNullFileName_HandlesGracefully()
    {
        // Arrange
        var logger = new TestLogger();
        var reporter = new ConsoleProgressReporter(logger, verbose: true);
        var progress = new CopyProgress(
            CurrentFile: 5,
            TotalFiles: 100,
            BytesProcessed: 500,
            TotalBytes: 10000,
            CurrentFileName: null!,
            Elapsed: TimeSpan.FromSeconds(5));

        // Act
        reporter.Report(progress);

        // Assert
        logger.Logs.Should().HaveCountGreaterThan(0);
    }

    [Test]
    public void Complete_WithZeroBytes_HandlesGracefully()
    {
        // Arrange
        var logger = new TestLogger();
        var reporter = new ConsoleProgressReporter(logger, verbose: false);
        var finalProgress = new CopyProgress(
            CurrentFile: 0,
            TotalFiles: 0,
            BytesProcessed: 0,
            TotalBytes: 0,
            CurrentFileName: "",
            Elapsed: TimeSpan.FromSeconds(1));

        // Act
        reporter.Complete(finalProgress);

        // Assert
        logger.Logs.Should().HaveCount(1);
        var logEntry = logger.Logs[0];
        logEntry.Message.Should().Contain("0 B");
    }

    #endregion
}
