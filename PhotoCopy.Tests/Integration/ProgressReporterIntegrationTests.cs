using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using PhotoCopy.Progress;

namespace PhotoCopy.Tests.Integration;

public class TestLogger : ILogger
{
    public List<string> Messages { get; } = [];
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Messages.Add($"[{logLevel}] {formatter(state, exception)}");
    public bool IsEnabled(LogLevel logLevel) => true;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}

[Property("Category", "Integration")]
public class ProgressReporterIntegrationTests
{
    private TestLogger _logger = null!;

    [Before(Test)]
    public Task Setup()
    {
        _logger = new TestLogger();
        return Task.CompletedTask;
    }

    [Test]
    public async Task Report_VerboseMode_LogsProgress()
    {
        var reporter = new ConsoleProgressReporter(_logger, verbose: true);
        var progress = new CopyProgress(5, 10, 5000, 10000, "test.jpg", TimeSpan.FromSeconds(5));

        reporter.Report(progress);

        await Assert.That(_logger.Messages).Contains(m => m.Contains("Progress"));
    }

    [Test]
    public async Task Report_NonVerboseMode_SkipsNonFivePercentIntervals()
    {
        var reporter = new ConsoleProgressReporter(_logger, verbose: false);
        
        reporter.Report(new CopyProgress(10, 100, 1000, 10000, "file1.jpg", TimeSpan.FromSeconds(1)));
        reporter.Report(new CopyProgress(12, 100, 1200, 10000, "file2.jpg", TimeSpan.FromSeconds(1)));

        await Assert.That(_logger.Messages.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Complete_LogsFinalStatistics()
    {
        var reporter = new ConsoleProgressReporter(_logger, verbose: true);
        var finalProgress = new CopyProgress(100, 100, 1048576, 1048576, "done.jpg", TimeSpan.FromMinutes(2));

        reporter.Complete(finalProgress);

        await Assert.That(_logger.Messages).Contains(m => m.Contains("Completed") && m.Contains("1 MB"));
    }

    [Test]
    public async Task ReportError_LogsExceptionWithFileName()
    {
        var reporter = new ConsoleProgressReporter(_logger, verbose: true);
        var exception = new IOException("File access denied");

        reporter.ReportError("problematic.jpg", exception);

        await Assert.That(_logger.Messages).Contains(m => m.Contains("Error") && m.Contains("problematic.jpg"));
    }

    [Test]
    public async Task Report_WithZeroElapsedTime_ShowsCalculating()
    {
        var reporter = new ConsoleProgressReporter(_logger, verbose: true);
        var progress = new CopyProgress(0, 10, 0, 10000, "start.jpg", TimeSpan.Zero);

        reporter.Report(progress);

        await Assert.That(_logger.Messages).Contains(m => m.Contains("calculating"));
    }

    [Test]
    public async Task Report_LongFileName_TruncatesTo30Chars()
    {
        var reporter = new ConsoleProgressReporter(_logger, verbose: true);
        var longFileName = "this_is_a_very_long_filename_that_exceeds_thirty_characters.jpg";
        var progress = new CopyProgress(5, 10, 5000, 10000, longFileName, TimeSpan.FromSeconds(5));

        reporter.Report(progress);

        await Assert.That(_logger.Messages[0]).DoesNotContain(longFileName);
    }

    [Test]
    public async Task Report_MultipleUpdates_LogsEachInVerboseMode()
    {
        var reporter = new ConsoleProgressReporter(_logger, verbose: true);

        for (int i = 1; i <= 3; i++)
            reporter.Report(new CopyProgress(i, 10, i * 1000, 10000, $"file{i}.jpg", TimeSpan.FromSeconds(i)));

        await Assert.That(_logger.Messages.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Complete_WithLargeFiles_FormatsAsGB()
    {
        var reporter = new ConsoleProgressReporter(_logger, verbose: true);
        var finalProgress = new CopyProgress(1000, 1000, 1073741824, 1073741824, "done.jpg", TimeSpan.FromHours(1.5));

        reporter.Complete(finalProgress);

        await Assert.That(_logger.Messages).Contains(m => m.Contains("1 GB"));
    }

    [Test]
    public async Task Report_At100Percent_AlwaysLogs()
    {
        var reporter = new ConsoleProgressReporter(_logger, verbose: false);
        var progress = new CopyProgress(100, 100, 10000, 10000, "final.jpg", TimeSpan.FromSeconds(10));

        reporter.Report(progress);

        await Assert.That(_logger.Messages).Contains(m => m.Contains("100%"));
    }

    [Test]
    public async Task Complete_FormatsTimeCorrectly()
    {
        var reporter = new ConsoleProgressReporter(_logger, verbose: true);
        var finalProgress = new CopyProgress(50, 50, 50000, 50000, "done.jpg", TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(30)));

        reporter.Complete(finalProgress);

        await Assert.That(_logger.Messages).Contains(m => m.Contains("5m 30s"));
    }
}
