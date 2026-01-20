using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PhotoCopy.Logging;

namespace PhotoCopy.Tests.Logging;

/// <summary>
/// Unit tests for JsonConsoleFormatter.
/// </summary>
public class JsonConsoleFormatterTests
{
    private readonly JsonConsoleFormatter _formatter;
    private readonly StringWriter _output;

    public JsonConsoleFormatterTests()
    {
        var options = new PhotoCopyJsonFormatterOptions();
        var monitor = new TestOptionsMonitor<PhotoCopyJsonFormatterOptions>(options);
        _formatter = new JsonConsoleFormatter(monitor);
        _output = new StringWriter();
    }

    [Test]
    public async Task Write_WithSimpleMessage_WritesValidJson()
    {
        // Arrange
        var logEntry = CreateLogEntry(LogLevel.Information, "Test message");

        // Act
        _formatter.Write(logEntry, null, _output);
        var json = _output.ToString().Trim();

        // Assert
        await Assert.That(json).IsNotNull().And.IsNotEmpty();
        
        var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        await Assert.That(root.TryGetProperty("timestamp", out _)).IsTrue();
        await Assert.That(root.GetProperty("level").GetString()).IsEqualTo("info");
        await Assert.That(root.GetProperty("message").GetString()).IsEqualTo("Test message");
        await Assert.That(root.GetProperty("category").GetString()).IsEqualTo("TestCategory");
    }

    [Test]
    public async Task Write_WithDifferentLogLevels_MapsLevelCorrectly()
    {
        // Arrange & Act & Assert
        var testCases = new Dictionary<LogLevel, string>
        {
            { LogLevel.Trace, "trace" },
            { LogLevel.Debug, "debug" },
            { LogLevel.Information, "info" },
            { LogLevel.Warning, "warn" },
            { LogLevel.Error, "error" },
            { LogLevel.Critical, "critical" }
        };

        foreach (var (logLevel, expectedLevel) in testCases)
        {
            var output = new StringWriter();
            var logEntry = CreateLogEntry(logLevel, $"Message for {logLevel}");

            _formatter.Write(logEntry, null, output);
            var json = output.ToString().Trim();

            var document = JsonDocument.Parse(json);
            var level = document.RootElement.GetProperty("level").GetString();

            await Assert.That(level).IsEqualTo(expectedLevel);
        }
    }

    [Test]
    public async Task Write_WithException_IncludesExceptionDetails()
    {
        // Arrange
        var exception = new InvalidOperationException("Something went wrong");
        var logEntry = CreateLogEntry(LogLevel.Error, "Error occurred", exception);

        // Act
        _formatter.Write(logEntry, null, _output);
        var json = _output.ToString().Trim();

        // Assert
        var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        await Assert.That(root.TryGetProperty("exception", out var exceptionElement)).IsTrue();
        await Assert.That(exceptionElement.GetProperty("type").GetString()).IsEqualTo("System.InvalidOperationException");
        await Assert.That(exceptionElement.GetProperty("message").GetString()).IsEqualTo("Something went wrong");
    }

    [Test]
    public async Task Write_WithStructuredProperties_IncludesProperties()
    {
        // Arrange
        var state = new List<KeyValuePair<string, object?>>
        {
            new("FileName", "test.jpg"),
            new("FileSize", 1024),
            new("{OriginalFormat}", "Processing {FileName} ({FileSize} bytes)")
        };
        
        var logEntry = new LogEntry<IReadOnlyList<KeyValuePair<string, object?>>>(
            LogLevel.Information,
            "TestCategory",
            new EventId(1),
            state,
            null,
            (s, _) => "Processing test.jpg (1024 bytes)");

        // Act
        _formatter.Write(logEntry, null, _output);
        var json = _output.ToString().Trim();

        // Assert
        var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        await Assert.That(root.TryGetProperty("properties", out var properties)).IsTrue();
        await Assert.That(properties.GetProperty("FileName").GetString()).IsEqualTo("test.jpg");
        await Assert.That(properties.GetProperty("FileSize").GetInt32()).IsEqualTo(1024);
        
        // Verify {OriginalFormat} is excluded
        await Assert.That(properties.TryGetProperty("{OriginalFormat}", out _)).IsFalse();
    }

    [Test]
    public async Task Write_WithTimestamp_FormatsAsIso8601()
    {
        // Arrange
        var logEntry = CreateLogEntry(LogLevel.Information, "Test message");

        // Act
        _formatter.Write(logEntry, null, _output);
        var json = _output.ToString().Trim();

        // Assert
        var document = JsonDocument.Parse(json);
        var timestampString = document.RootElement.GetProperty("timestamp").GetString();

        await Assert.That(timestampString).IsNotNull();
        
        // Should be valid ISO 8601 format
        var parsed = DateTimeOffset.TryParse(timestampString, out var timestamp);
        await Assert.That(parsed).IsTrue();
        
        // Should be recent (within last minute)
        var age = DateTimeOffset.Now - timestamp;
        await Assert.That(age.TotalMinutes).IsLessThan(1);
    }

    [Test]
    public async Task Write_WithNullMessage_DoesNotWrite()
    {
        // Arrange
        var logEntry = new LogEntry<string>(
            LogLevel.Information,
            "TestCategory",
            new EventId(1),
            "test",
            null,
            (_, _) => null!);

        // Act
        _formatter.Write(logEntry, null, _output);
        var json = _output.ToString();

        // Assert
        await Assert.That(json).IsEmpty();
    }

    [Test]
    public async Task Write_WithNestedExceptionDetails_IncludesInnerException()
    {
        // Arrange
        var innerException = new ArgumentException("Inner error");
        var outerException = new InvalidOperationException("Outer error", innerException);
        var logEntry = CreateLogEntry(LogLevel.Error, "Error occurred", outerException);

        // Act
        _formatter.Write(logEntry, null, _output);
        var json = _output.ToString().Trim();

        // Assert
        var document = JsonDocument.Parse(json);
        var exceptionElement = document.RootElement.GetProperty("exception");

        await Assert.That(exceptionElement.GetProperty("message").GetString()).IsEqualTo("Outer error");
        await Assert.That(exceptionElement.GetProperty("innerException").GetString()).IsEqualTo("Inner error");
    }

    [Test]
    public async Task Write_OutputIsSingleLine()
    {
        // Arrange
        var logEntry = CreateLogEntry(LogLevel.Information, "Test message with\nmultiple\nlines");

        // Act
        _formatter.Write(logEntry, null, _output);
        var output = _output.ToString();

        // Assert - should be exactly one line (ending with newline)
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        await Assert.That(lines.Length).IsEqualTo(1);
    }

    private static LogEntry<string> CreateLogEntry(LogLevel logLevel, string message, Exception? exception = null)
    {
        return new LogEntry<string>(
            logLevel,
            "TestCategory",
            new EventId(1),
            message,
            exception,
            (state, _) => state);
    }

    /// <summary>
    /// Simple options monitor for testing.
    /// </summary>
    private class TestOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
    {
        public TestOptionsMonitor(TOptions currentValue)
        {
            CurrentValue = currentValue;
        }

        public TOptions CurrentValue { get; }

        public TOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}
