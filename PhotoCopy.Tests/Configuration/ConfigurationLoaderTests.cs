using System;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using PhotoCopy.Commands;
using PhotoCopy.Configuration;

namespace PhotoCopy.Tests.Configuration;

/// <summary>
/// Unit tests for ConfigurationLoader static methods.
/// </summary>
public class ConfigurationLoaderTests
{
    private string _testDirectory = null!;

    [Before(Test)]
    public void Setup()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"ConfigLoaderTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
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

    #region Load Tests

    [Test]
    public async Task Load_WithDefaultOptions_ReturnsValidConfig()
    {
        // Arrange
        var options = new CopyOptions();

        // Act
        var config = ConfigurationLoader.Load(options);

        // Assert
        await Assert.That(config).IsNotNull();
        await Assert.That(config.DryRun).IsFalse();
        await Assert.That(config.SkipExisting).IsFalse();
        await Assert.That(config.Overwrite).IsFalse();
    }

    [Test]
    public async Task Load_WithYamlConfigFile_LoadsSettings()
    {
        // Arrange
        var yamlContent = @"
source: C:\Photos\Source
destination: C:\Photos\Dest\{year}\{month}
dryRun: true
skipExisting: true
";
        var yamlPath = Path.Combine(_testDirectory, "config.yaml");
        await File.WriteAllTextAsync(yamlPath, yamlContent);

        var options = new CopyOptions { ConfigPath = yamlPath };

        // Act
        var config = ConfigurationLoader.Load(options);

        // Assert
        await Assert.That(config).IsNotNull();
        await Assert.That(config.Source).IsEqualTo(@"C:\Photos\Source");
        await Assert.That(config.Destination).IsEqualTo(@"C:\Photos\Dest\{year}\{month}");
        await Assert.That(config.DryRun).IsTrue();
        await Assert.That(config.SkipExisting).IsTrue();
    }

    [Test]
    public async Task Load_WithYmlExtension_LoadsSettings()
    {
        // Arrange
        var yamlContent = @"
source: C:\Photos\Source
dryRun: true
";
        var ymlPath = Path.Combine(_testDirectory, "config.yml");
        await File.WriteAllTextAsync(ymlPath, yamlContent);

        var options = new CopyOptions { ConfigPath = ymlPath };

        // Act
        var config = ConfigurationLoader.Load(options);

        // Assert
        await Assert.That(config).IsNotNull();
        await Assert.That(config.Source).IsEqualTo(@"C:\Photos\Source");
        await Assert.That(config.DryRun).IsTrue();
    }

    [Test]
    public async Task Load_WithJsonConfigFile_LoadsSettings()
    {
        // Arrange
        var jsonContent = @"{
  ""source"": ""C:\\Photos\\Source"",
  ""destination"": ""C:\\Photos\\Dest"",
  ""dryRun"": true,
  ""overwrite"": true
}";
        var jsonPath = Path.Combine(_testDirectory, "config.json");
        await File.WriteAllTextAsync(jsonPath, jsonContent);

        var options = new CopyOptions { ConfigPath = jsonPath };

        // Act
        var config = ConfigurationLoader.Load(options);

        // Assert
        await Assert.That(config).IsNotNull();
        await Assert.That(config.Source).IsEqualTo(@"C:\Photos\Source");
        await Assert.That(config.DryRun).IsTrue();
        await Assert.That(config.Overwrite).IsTrue();
    }

    [Test]
    public async Task Load_WithCliOverrides_OverridesConfigFile()
    {
        // Arrange
        var yamlContent = @"
source: C:\Config\Source
destination: C:\Config\Dest
dryRun: false
";
        var yamlPath = Path.Combine(_testDirectory, "config.yaml");
        await File.WriteAllTextAsync(yamlPath, yamlContent);

        var options = new CopyOptions
        {
            ConfigPath = yamlPath,
            Source = @"C:\CLI\Source",       // Override
            DryRun = true                    // Override
        };

        // Act
        var config = ConfigurationLoader.Load(options);

        // Assert
        await Assert.That(config.Source).IsEqualTo(@"C:\CLI\Source"); // CLI wins
        await Assert.That(config.Destination).IsEqualTo(@"C:\Config\Dest"); // From config file
        await Assert.That(config.DryRun).IsTrue(); // CLI wins
    }

    #endregion

    #region LoadWithDiagnostics Tests

    [Test]
    public async Task LoadWithDiagnostics_TracksConfigSources()
    {
        // Arrange
        var yamlContent = @"
source: C:\Photos\Source
dryRun: true
";
        var yamlPath = Path.Combine(_testDirectory, "config.yaml");
        await File.WriteAllTextAsync(yamlPath, yamlContent);

        var options = new CopyOptions
        {
            ConfigPath = yamlPath,
            LogLevel = OutputLevel.Verbose  // CLI override
        };

        // Act
        var (config, diagnostics) = ConfigurationLoader.LoadWithDiagnostics(options);

        // Assert
        await Assert.That(config).IsNotNull();
        await Assert.That(diagnostics).IsNotNull();
        
        // Verify diagnostics tracked the sources via Sources property
        await Assert.That(diagnostics.Sources.Count).IsGreaterThan(0);
        
        // Check LogLevel was tracked as command line override
        await Assert.That(diagnostics.Sources.ContainsKey("LogLevel")).IsTrue();
        await Assert.That(diagnostics.Sources["LogLevel"].Source).IsEqualTo(ConfigSourceType.CommandLine);
    }

    [Test]
    public async Task LoadWithDiagnostics_RecordsDefaults()
    {
        // Arrange
        var options = new CopyOptions();

        // Act
        var (config, diagnostics) = ConfigurationLoader.LoadWithDiagnostics(options);

        // Assert
        // Should have recorded defaults for config properties
        await Assert.That(diagnostics.Sources.Count).IsGreaterThan(0);
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public async Task Load_WithMissingConfigFile_ThrowsException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDirectory, "nonexistent.yaml");
        var options = new CopyOptions { ConfigPath = nonExistentPath };

        // Act & Assert
        Exception? exception = null;
        try
        {
            ConfigurationLoader.Load(options);
        }
        catch (FileNotFoundException ex)
        {
            exception = ex;
        }

        await Assert.That(exception).IsNotNull();
    }

    #endregion
}
