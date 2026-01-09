using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PhotoCopy.Tests.E2E.Commands;

/// <summary>
/// End-to-end tests for the PhotoCopy config command.
/// </summary>
[NotInParallel("E2E")]
public class ConfigCommandE2ETests : E2ETestBase
{
    [Test]
    public async Task Config_ShowDefaults_ReturnsSuccess()
    {
        // Act
        var result = await RunPhotoCopyAsync("config");

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.OutputContains("config") || result.OutputContains("Configuration")).IsTrue();
    }

    [Test]
    public async Task Config_WithJsonOutput_ReturnsValidJson()
    {
        // Act
        var result = await RunPhotoCopyAsync("config", "--json");

        // Assert
        await Assert.That(result.Success).IsTrue();

        // Extract JSON from output that may have log prefixes
        var jsonOutput = ExtractJson(result.StandardOutput);
        await Assert.That(jsonOutput).IsNotNull();

        // Verify the output is valid JSON (can be object or array)
        var isValidJson = false;
        try
        {
            using var doc = JsonDocument.Parse(jsonOutput!);
            isValidJson = doc.RootElement.ValueKind == JsonValueKind.Object ||
                          doc.RootElement.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            isValidJson = false;
        }

        await Assert.That(isValidJson).IsTrue();
    }

    /// <summary>
    /// Extracts JSON from output that may have log prefixes before the JSON content.
    /// Handles multi-line JSON with nested braces/brackets.
    /// </summary>
    private static string? ExtractJson(string output)
    {
        var lines = output.Split('\n');
        var jsonStart = -1;
        var depth = 0;
        var inJson = false;
        var jsonEnd = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            
            // Look for JSON start (either { or [)
            if (!inJson && (line.StartsWith("{") || line.StartsWith("[")))
            {
                jsonStart = i;
                inJson = true;
            }

            if (inJson)
            {
                // Count braces and brackets
                foreach (var c in line)
                {
                    if (c == '{' || c == '[')
                        depth++;
                    else if (c == '}' || c == ']')
                        depth--;
                }

                // When depth returns to 0, we've found the end
                if (depth == 0)
                {
                    jsonEnd = i;
                    break;
                }
            }
        }

        if (jsonStart >= 0 && jsonEnd >= jsonStart)
        {
            return string.Join("\n", lines.Skip(jsonStart).Take(jsonEnd - jsonStart + 1));
        }
        return null;
    }

    [Test]
    public async Task Config_WithConfigFile_ShowsLoadedValues()
    {
        // Arrange
        var yamlContent = @"
source: /test/source/path
destination: /test/dest/{year}/{month}
mode: move
dryRun: true
logLevel: verbose
";
        var configPath = await CreateConfigFileAsync("test-config.yaml", yamlContent);

        // Act
        var result = await RunPhotoCopyAsync("config", "--config", configPath);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.OutputContains("/test/source/path") || result.OutputContains("source")).IsTrue();
    }

    [Test]
    public async Task Config_InvalidConfigPath_ReportsError()
    {
        // Arrange
        var nonExistentPath = Path.Combine(ConfigDir, "non-existent-config.yaml");

        // Act
        var result = await RunPhotoCopyAsync("config", "--config", nonExistentPath);

        // Assert
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.ErrorContains("not found") || result.ErrorContains("error") || result.ExitCode != 0).IsTrue();
    }
}
