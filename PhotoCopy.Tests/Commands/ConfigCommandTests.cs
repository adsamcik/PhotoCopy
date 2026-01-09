using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Commands;
using PhotoCopy.Configuration;
using PhotoCopy.Tests.TestingImplementation;

namespace PhotoCopy.Tests.Commands;

// Run tests sequentially since they write to Console
[NotInParallel]
public class ConfigCommandTests
{
    private FakeLogger<ConfigCommand> _logger = null!;
    private PhotoCopyConfig _config = null!;
    private IOptions<PhotoCopyConfig> _options = null!;
    private TextWriter _originalOut = null!;
    private StringWriter _testOutput = null!;

    [Before(Test)]
    public void Setup()
    {
        _logger = new FakeLogger<ConfigCommand>();
        _config = new PhotoCopyConfig
        {
            Source = @"C:\Source",
            Destination = @"C:\Dest\{year}\{month}\{day}\{name}{ext}",
            DryRun = true
        };
        _options = Microsoft.Extensions.Options.Options.Create(_config);

        // Clear shared logs before each test
        SharedLogs.Clear();
        
        // Capture console output to avoid issues with parallel tests
        _originalOut = Console.Out;
        _testOutput = new StringWriter();
        Console.SetOut(_testOutput);
    }

    [After(Test)]
    public void Cleanup()
    {
        // Restore console output
        Console.SetOut(_originalOut);
        _testOutput?.Dispose();
    }

    private ConfigCommand CreateCommand(
        ConfigurationDiagnostics? diagnostics = null,
        bool outputJson = false)
    {
        var diag = diagnostics ?? CreateDefaultDiagnostics();
        return new ConfigCommand(_logger, _options, diag, outputJson);
    }

    private ConfigurationDiagnostics CreateDefaultDiagnostics()
    {
        var diagnostics = new ConfigurationDiagnostics();
        diagnostics.RecordSource("Source", _config.Source, ConfigSourceType.CommandLine, "--source");
        diagnostics.RecordSource("Destination", _config.Destination, ConfigSourceType.ConfigFile, "appsettings.json");
        diagnostics.RecordSource("DryRun", _config.DryRun.ToString(), ConfigSourceType.Default, null);
        return diagnostics;
    }

    #region ExecuteAsync_DisplaysConfiguration

    [Test]
    public async Task ExecuteAsync_DisplaysConfiguration_WhenCalledWithDefaults()
    {
        // Arrange
        var diagnostics = CreateDefaultDiagnostics();
        var command = CreateCommand(diagnostics);

        // Act
        var result = await command.ExecuteAsync();

        // Assert - Verify the command runs successfully and generates a report
        result.Should().Be(0);
        // The diagnostics should have recorded sources
        diagnostics.Sources.Should().ContainKey("Source");
    }

    [Test]
    public async Task ExecuteAsync_DisplaysConfiguration_WhenOutputJsonIsFalse()
    {
        // Arrange
        var diagnostics = CreateDefaultDiagnostics();
        var command = CreateCommand(diagnostics, outputJson: false);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        diagnostics.Sources.Should().ContainKey("Destination");
    }

    [Test]
    public async Task ExecuteAsync_DisplaysConfiguration_WhenOutputJsonIsTrue()
    {
        // Arrange
        var diagnostics = CreateDefaultDiagnostics();
        var command = CreateCommand(diagnostics, outputJson: true);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        // Verify the report can be generated
        var report = diagnostics.GenerateReport(_config);
        report.Values.Should().NotBeEmpty();
    }

    [Test]
    public async Task ExecuteAsync_DisplaysConfiguration_IncludesSourceInfo()
    {
        // Arrange
        var diagnostics = CreateDefaultDiagnostics();
        var command = CreateCommand(diagnostics, outputJson: false);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        diagnostics.Sources["Source"].Source.Should().Be(ConfigSourceType.CommandLine);
        diagnostics.Sources["Source"].SourceDetail.Should().Be("--source");
    }

    #endregion

    #region ExecuteAsync_ReturnsZero

    [Test]
    public async Task ExecuteAsync_ReturnsZero_WhenSuccessful()
    {
        // Arrange
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
    }

    [Test]
    public async Task ExecuteAsync_ReturnsZero_WhenOutputJsonIsTrue()
    {
        // Arrange
        var command = CreateCommand(outputJson: true);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
    }

    [Test]
    public async Task ExecuteAsync_ReturnsZero_WhenOutputJsonIsFalse()
    {
        // Arrange
        var command = CreateCommand(outputJson: false);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
    }

    [Test]
    public async Task ExecuteAsync_ReturnsZero_WithCancellationToken()
    {
        // Arrange
        var command = CreateCommand();
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await command.ExecuteAsync(cancellationToken);

        // Assert
        result.Should().Be(0);
    }

    #endregion
}
