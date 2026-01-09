using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PhotoCopy.Tests.E2E.Commands;

/// <summary>
/// End-to-end tests for the PhotoCopy scan command.
/// </summary>
[NotInParallel("E2E")]
public class ScanCommandE2ETests : E2ETestBase
{
    [Test]
    public async Task Scan_DirectoryWithPhotos_ReturnsSuccess()
    {
        // Arrange
        await CreateSourceJpegAsync("photo1.jpg", new DateTime(2024, 5, 10));
        await CreateSourceJpegAsync("photo2.jpg", new DateTime(2024, 6, 15));
        await CreateSourcePngAsync("image.png", new DateTime(2024, 7, 20));

        // Act
        var result = await RunScanAsync();

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.ExitCode).IsEqualTo(0);
    }

    [Test]
    public async Task Scan_DirectoryWithPhotos_ShowsFileInfo()
    {
        // Arrange
        await CreateSourceJpegAsync("vacation.jpg", new DateTime(2024, 8, 15));
        await CreateSourcePngAsync("sunset.png", new DateTime(2024, 9, 20));

        // Act
        var result = await RunScanAsync(verbose: true);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.OutputContains("vacation.jpg")).IsTrue();
        await Assert.That(result.OutputContains("sunset.png")).IsTrue();
        // Verify scan completion message with file count
        await Assert.That(result.OutputContains("2 files")).IsTrue();
    }

    [Test]
    public async Task Scan_WithJsonOutput_ReturnsValidJson()
    {
        // Arrange
        await CreateSourceJpegAsync("photo.jpg", new DateTime(2024, 3, 25));

        // Act
        var result = await RunScanAsync(additionalArgs: ["--json"]);

        // Assert
        await Assert.That(result.Success).IsTrue();

        // Extract JSON from output that may have log prefixes
        var jsonOutput = ExtractJson(result.StandardOutput);
        await Assert.That(jsonOutput).IsNotNull();

        // Verify output is valid JSON
        var isValidJson = false;
        try
        {
            using var jsonDoc = JsonDocument.Parse(jsonOutput!);
            isValidJson = true;

            // Verify expected JSON structure
            var root = jsonDoc.RootElement;
            var hasStatistics = root.TryGetProperty("Statistics", out _);
            var hasFiles = root.TryGetProperty("Files", out var filesElement);

            await Assert.That(hasStatistics).IsTrue();
            await Assert.That(hasFiles).IsTrue();
            await Assert.That(filesElement.GetArrayLength()).IsEqualTo(1);
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
    public async Task Scan_EmptyDirectory_ReturnsSuccessWithNoFiles()
    {
        // Arrange - source directory is already created and empty

        // Act
        var result = await RunScanAsync();

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.OutputContains("0 files")).IsTrue();
    }

    [Test]
    public async Task Scan_InvalidSourcePath_ReturnsError()
    {
        // Arrange
        var nonExistentPath = Path.Combine(TestBaseDirectory, "nonexistent", "path");

        // Act
        var result = await RunScanAsync(source: nonExistentPath);

        // Assert - command may return success but should have error/warning message in output
        // or return non-zero exit code
        var hasErrorMessage = result.OutputContains("not found") ||
                              result.OutputContains("does not exist") ||
                              result.OutputContains("error") ||
                              result.OutputContains("fail") ||
                              result.OutputContains("invalid") ||
                              result.ErrorContains("not found") ||
                              result.ErrorContains("does not exist") ||
                              result.ErrorContains("error");
        var hasNonZeroExitCode = result.ExitCode != 0;
        
        await Assert.That(hasErrorMessage || hasNonZeroExitCode).IsTrue();
    }

    [Test]
    public async Task Scan_WithDateFilter_ShowsFilteredResults()
    {
        // Arrange
        await CreateSourceJpegAsync("old_photo.jpg", new DateTime(2020, 3, 15));
        await CreateSourceJpegAsync("recent_photo.jpg", new DateTime(2024, 6, 20));
        await CreateSourceJpegAsync("new_photo.jpg", new DateTime(2024, 9, 10));

        // Act - filter to only show files from 2024 onwards
        var result = await RunScanAsync(
            verbose: true,
            additionalArgs: ["--min-date", "2024-01-01"]);

        // Assert
        await Assert.That(result.Success).IsTrue();
        // Should show only the 2024 files
        await Assert.That(result.OutputContains("2 files") || result.OutputContains("2 valid")).IsTrue();
        // In verbose mode, should show the matching files
        await Assert.That(result.OutputContains("recent_photo.jpg")).IsTrue();
        await Assert.That(result.OutputContains("new_photo.jpg")).IsTrue();
    }

    [Test]
    public async Task Scan_SubfoldersIncluded_AllFilesListed()
    {
        // Arrange - create files in root and various subfolders
        await CreateSourceJpegAsync("root_photo.jpg", new DateTime(2024, 1, 10));
        await CreateSourceJpegAsync("subfolder_photo1.jpg", new DateTime(2024, 2, 15), subfolder: "vacation");
        await CreateSourcePngAsync("subfolder_photo2.png", new DateTime(2024, 3, 20), subfolder: "vacation");
        await CreateSourceJpegAsync("deep_photo.jpg", new DateTime(2024, 4, 25), subfolder: Path.Combine("2024", "summer"));

        // Act
        var result = await RunScanAsync(verbose: true);

        // Assert
        await Assert.That(result.Success).IsTrue();
        // All 4 files should be found
        await Assert.That(result.OutputContains("4 files")).IsTrue();
        // Verify each file appears in verbose output
        await Assert.That(result.OutputContains("root_photo.jpg")).IsTrue();
        await Assert.That(result.OutputContains("subfolder_photo1.jpg")).IsTrue();
        await Assert.That(result.OutputContains("subfolder_photo2.png")).IsTrue();
        await Assert.That(result.OutputContains("deep_photo.jpg")).IsTrue();
    }

    [Test]
    public async Task Scan_MixedFileTypes_AllReported()
    {
        // Arrange - create mix of JPG and PNG files
        await CreateSourceJpegAsync("image1.jpg", new DateTime(2024, 5, 10));
        await CreateSourceJpegAsync("image2.jpeg", new DateTime(2024, 5, 11));
        await CreateSourcePngAsync("image3.png", new DateTime(2024, 5, 12));
        await CreateSourcePngAsync("image4.PNG", new DateTime(2024, 5, 13));

        // Act
        var result = await RunScanAsync(verbose: true);

        // Assert
        await Assert.That(result.Success).IsTrue();
        // All 4 files should be reported
        await Assert.That(result.OutputContains("4 files")).IsTrue();
        // Verify each file type is found
        await Assert.That(result.OutputContains("image1.jpg")).IsTrue();
        await Assert.That(result.OutputContains("image2.jpeg")).IsTrue();
        await Assert.That(result.OutputContains("image3.png")).IsTrue();
        await Assert.That(result.OutputContains("image4.PNG")).IsTrue();
    }
}
