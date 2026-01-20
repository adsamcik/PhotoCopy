using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using PhotoCopy.Configuration;
using PhotoCopy.Logging;

namespace PhotoCopy.Tests.Logging;

/// <summary>
/// Unit tests for FileLoggerProvider.
/// </summary>
public class FileLoggerProviderTests
{
    private string _testDirectory = null!;
    private string _logFilePath = null!;

    [Before(Test)]
    public void Setup()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"FileLoggerTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        _logFilePath = Path.Combine(_testDirectory, "test.log");
    }

    [After(Test)]
    public void Cleanup()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
            catch { }
        }
    }

    [Test]
    public async Task CreateLogger_ReturnsLogger()
    {
        // Arrange
        await using var provider = new FileLoggerProvider(_logFilePath, LogFormat.Text);

        // Act
        var logger = provider.CreateLogger("TestCategory");

        // Assert
        await Assert.That(logger).IsNotNull();
    }

    [Test]
    public async Task Log_TextFormat_WritesToFile()
    {
        // Arrange
        await using var provider = new FileLoggerProvider(_logFilePath, LogFormat.Text);
        var logger = provider.CreateLogger("TestCategory");

        // Act
        logger.LogInformation("Test message");

        // Wait for async write
        await Task.Delay(100);
        await provider.DisposeAsync();

        // Assert
        var content = await File.ReadAllTextAsync(_logFilePath);
        await Assert.That(content).Contains("Test message");
        await Assert.That(content).Contains("[INF]");
        await Assert.That(content).Contains("[TestCategory]");
    }

    [Test]
    public async Task Log_JsonFormat_WritesValidJson()
    {
        // Arrange
        await using var provider = new FileLoggerProvider(_logFilePath, LogFormat.Json);
        var logger = provider.CreateLogger("TestCategory");

        // Act
        logger.LogWarning("Warning message");

        // Wait for async write
        await Task.Delay(100);
        await provider.DisposeAsync();

        // Assert
        var content = await File.ReadAllTextAsync(_logFilePath);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        await Assert.That(lines.Length).IsGreaterThanOrEqualTo(1);

        var document = JsonDocument.Parse(lines[0]);
        var root = document.RootElement;

        await Assert.That(root.GetProperty("level").GetString()).IsEqualTo("warn");
        await Assert.That(root.GetProperty("message").GetString()).IsEqualTo("Warning message");
        await Assert.That(root.GetProperty("category").GetString()).IsEqualTo("TestCategory");
    }

    [Test]
    public async Task Log_WithException_IncludesExceptionInJson()
    {
        // Arrange
        await using var provider = new FileLoggerProvider(_logFilePath, LogFormat.Json);
        var logger = provider.CreateLogger("TestCategory");
        var exception = new InvalidOperationException("Test error");

        // Act
        logger.LogError(exception, "Error occurred");

        // Wait for async write
        await Task.Delay(100);
        await provider.DisposeAsync();

        // Assert
        var content = await File.ReadAllTextAsync(_logFilePath);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var document = JsonDocument.Parse(lines[0]);
        var root = document.RootElement;

        await Assert.That(root.TryGetProperty("exception", out var exceptionElement)).IsTrue();
        await Assert.That(exceptionElement.GetProperty("type").GetString()).IsEqualTo("System.InvalidOperationException");
        await Assert.That(exceptionElement.GetProperty("message").GetString()).IsEqualTo("Test error");
    }

    [Test]
    public async Task Log_MultipleLevels_AllWritten()
    {
        // Arrange
        await using var provider = new FileLoggerProvider(_logFilePath, LogFormat.Text);
        var logger = provider.CreateLogger("TestCategory");

        // Act
        logger.LogDebug("Debug message");
        logger.LogInformation("Info message");
        logger.LogWarning("Warning message");
        logger.LogError("Error message");

        // Wait for async write
        await Task.Delay(100);
        await provider.DisposeAsync();

        // Assert
        var content = await File.ReadAllTextAsync(_logFilePath);
        
        await Assert.That(content).Contains("[DBG]");
        await Assert.That(content).Contains("[INF]");
        await Assert.That(content).Contains("[WRN]");
        await Assert.That(content).Contains("[ERR]");
    }

    [Test]
    public async Task Log_CreatesDirectoryIfNotExists()
    {
        // Arrange
        var nestedPath = Path.Combine(_testDirectory, "nested", "deep", "test.log");

        // Act
        await using var provider = new FileLoggerProvider(nestedPath, LogFormat.Text);
        var logger = provider.CreateLogger("TestCategory");
        logger.LogInformation("Test message");

        await Task.Delay(100);
        await provider.DisposeAsync();

        // Assert
        await Assert.That(File.Exists(nestedPath)).IsTrue();
    }

    [Test]
    public async Task Log_WithStructuredProperties_IncludedInJson()
    {
        // Arrange
        await using var provider = new FileLoggerProvider(_logFilePath, LogFormat.Json);
        var logger = provider.CreateLogger("TestCategory");

        // Act
        logger.LogInformation("Processing file {FileName} with size {FileSize}", "test.jpg", 1024);

        // Wait for async write
        await Task.Delay(100);
        await provider.DisposeAsync();

        // Assert
        var content = await File.ReadAllTextAsync(_logFilePath);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var document = JsonDocument.Parse(lines[0]);
        var root = document.RootElement;

        await Assert.That(root.TryGetProperty("properties", out var properties)).IsTrue();
        await Assert.That(properties.GetProperty("FileName").GetString()).IsEqualTo("test.jpg");
        await Assert.That(properties.GetProperty("FileSize").GetInt32()).IsEqualTo(1024);
    }

    [Test]
    public async Task Dispose_FlushesRemainingMessages()
    {
        // Arrange
        await using var provider = new FileLoggerProvider(_logFilePath, LogFormat.Text);
        var logger = provider.CreateLogger("TestCategory");

        // Act - log many messages quickly
        for (int i = 0; i < 10; i++)
        {
            logger.LogInformation("Message {Number}", i);
        }

        // Dispose should flush
        await provider.DisposeAsync();

        // Assert - all messages should be written
        var content = await File.ReadAllTextAsync(_logFilePath);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        await Assert.That(lines.Length).IsEqualTo(10);
    }
}
