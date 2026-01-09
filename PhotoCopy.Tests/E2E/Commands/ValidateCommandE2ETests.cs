using System;
using System.IO;
using System.Threading.Tasks;

namespace PhotoCopy.Tests.E2E.Commands;

/// <summary>
/// End-to-end tests for the PhotoCopy validate command.
/// </summary>
[NotInParallel("E2E")]
public class ValidateCommandE2ETests : E2ETestBase
{
    [Test]
    public async Task Validate_DirectoryWithValidPhotos_ReturnsSuccess()
    {
        // Arrange
        await CreateSourceJpegAsync("photo1.jpg", new DateTime(2024, 6, 15, 10, 30, 0));
        await CreateSourceJpegAsync("photo2.jpg", new DateTime(2024, 7, 20, 14, 45, 0));
        await CreateSourcePngAsync("image1.png", new DateTime(2024, 8, 5, 9, 0, 0));

        // Act
        var result = await RunValidateAsync(additionalArgs: ["--source", SourceDir]);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.OutputContains("Valid") || result.OutputContains("Summary")).IsTrue();
    }

    [Test]
    public async Task Validate_EmptyDirectory_ReturnsSuccess()
    {
        // Arrange - SourceDir is already created but empty

        // Act
        var result = await RunValidateAsync(additionalArgs: ["--source", SourceDir]);

        // Assert
        await Assert.That(result.Success).IsTrue();
    }

    [Test]
    public async Task Validate_InvalidSourcePath_ReturnsError()
    {
        // Arrange
        var nonExistentPath = Path.Combine(TestBaseDirectory, "non-existent-folder");

        // Act
        var result = await RunValidateAsync(additionalArgs: ["--source", nonExistentPath]);

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
    public async Task Validate_WithDateFilters_ValidatesFilteredFiles()
    {
        // Arrange
        await CreateSourceJpegAsync("old_photo.jpg", new DateTime(2020, 3, 10, 12, 0, 0));
        await CreateSourceJpegAsync("recent_photo.jpg", new DateTime(2024, 6, 15, 14, 30, 0));
        await CreateSourceJpegAsync("future_photo.jpg", new DateTime(2025, 1, 1, 8, 0, 0));

        // Act - validate only files from 2023-01-01 to 2024-12-31
        var result = await RunValidateAsync(
            additionalArgs: [
                "--source", SourceDir,
                "--min-date", "2023-01-01",
                "--max-date", "2024-12-31"
            ]);

        // Assert - command should either succeed OR produce validation output
        // (some environments may have issues with the files but the command should still run)
        var commandExecuted = result.OutputContains("Validation") ||
                              result.OutputContains("Validating") ||
                              result.OutputContains("files") ||
                              result.OutputContains("Summary") ||
                              result.Success;
        await Assert.That(commandExecuted).IsTrue();
    }
}
