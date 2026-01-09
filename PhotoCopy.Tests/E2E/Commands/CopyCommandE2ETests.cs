using System;
using System.IO;
using System.Threading.Tasks;

namespace PhotoCopy.Tests.E2E.Commands;

/// <summary>
/// End-to-end tests for the PhotoCopy copy command.
/// </summary>
[NotInParallel("E2E")]
public class CopyCommandE2ETests : E2ETestBase
{
    [Test]
    public async Task Copy_SingleJpegWithExifDate_CreatesYearMonthStructure()
    {
        // Arrange
        var dateTaken = new DateTime(2024, 7, 15, 14, 30, 0);
        await CreateSourceJpegAsync("vacation.jpg", dateTaken);
        
        var destinationPattern = Path.Combine(DestDir, "{year}", "{month}", "{name}{ext}");

        // Act
        var result = await RunCopyAsync(destination: destinationPattern);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(DestinationFileExists(Path.Combine("2024", "07", "vacation.jpg"))).IsTrue();
        await Assert.That(GetDestinationFiles().Length).IsEqualTo(1);
    }

    [Test]
    public async Task Copy_MultiplePngsWithDifferentDates_OrganizedCorrectly()
    {
        // Arrange
        await CreateSourcePngAsync("spring.png", new DateTime(2023, 3, 15));
        await CreateSourcePngAsync("summer.png", new DateTime(2024, 6, 20));
        await CreateSourcePngAsync("winter.png", new DateTime(2024, 12, 5));
        
        var destinationPattern = Path.Combine(DestDir, "{year}", "{month}", "{name}{ext}");

        // Act
        var result = await RunCopyAsync(destination: destinationPattern);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(DestinationFileExists(Path.Combine("2023", "03", "spring.png"))).IsTrue();
        await Assert.That(DestinationFileExists(Path.Combine("2024", "06", "summer.png"))).IsTrue();
        await Assert.That(DestinationFileExists(Path.Combine("2024", "12", "winter.png"))).IsTrue();
        await Assert.That(GetDestinationFiles().Length).IsEqualTo(3);
    }

    [Test]
    public async Task Copy_WithDryRun_NoFilesWritten()
    {
        // Arrange
        await CreateSourceJpegAsync("photo1.jpg", new DateTime(2024, 5, 10));
        await CreateSourceJpegAsync("photo2.jpg", new DateTime(2024, 5, 11));
        
        var destinationPattern = Path.Combine(DestDir, "{year}", "{month}", "{name}{ext}");

        // Act
        var result = await RunCopyAsync(destination: destinationPattern, dryRun: true, verbose: true);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(GetDestinationFiles().Length).IsEqualTo(0);
        await Assert.That(result.OutputContains("photo1.jpg") || result.OutputContains("dry")).IsTrue();
    }

    [Test]
    public async Task Copy_WithMoveMode_DeletesSourceFile()
    {
        // Arrange
        var sourcePath = await CreateSourceJpegAsync("moveme.jpg", new DateTime(2024, 8, 25));
        var destinationPattern = Path.Combine(DestDir, "{year}", "{month}", "{name}{ext}");

        // Act
        var result = await RunCopyAsync(
            destination: destinationPattern, 
            additionalArgs: ["-m", "move"]);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(DestinationFileExists(Path.Combine("2024", "08", "moveme.jpg"))).IsTrue();
        await Assert.That(File.Exists(sourcePath)).IsFalse();
    }

    [Test]
    public async Task Copy_WithMinDateFilter_ExcludesOlderFiles()
    {
        // Arrange
        await CreateSourceJpegAsync("old.jpg", new DateTime(2020, 1, 15));
        await CreateSourceJpegAsync("new.jpg", new DateTime(2024, 6, 20));
        
        var destinationPattern = Path.Combine(DestDir, "{year}", "{month}", "{name}{ext}");

        // Act
        var result = await RunCopyAsync(
            destination: destinationPattern,
            additionalArgs: ["--min-date", "2023-01-01"]);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(DestinationFileExists(Path.Combine("2024", "06", "new.jpg"))).IsTrue();
        await Assert.That(DestinationFileExists(Path.Combine("2020", "01", "old.jpg"))).IsFalse();
        await Assert.That(GetDestinationFiles().Length).IsEqualTo(1);
    }

    [Test]
    public async Task Copy_WithMaxDateFilter_ExcludesNewerFiles()
    {
        // Arrange
        await CreateSourceJpegAsync("old.jpg", new DateTime(2020, 3, 10));
        await CreateSourceJpegAsync("new.jpg", new DateTime(2024, 9, 15));
        
        var destinationPattern = Path.Combine(DestDir, "{year}", "{month}", "{name}{ext}");

        // Act
        var result = await RunCopyAsync(
            destination: destinationPattern,
            additionalArgs: ["--max-date", "2023-12-31"]);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(DestinationFileExists(Path.Combine("2020", "03", "old.jpg"))).IsTrue();
        await Assert.That(DestinationFileExists(Path.Combine("2024", "09", "new.jpg"))).IsFalse();
        await Assert.That(GetDestinationFiles().Length).IsEqualTo(1);
    }

    [Test]
    public async Task Copy_SkipExisting_DoesNotOverwrite()
    {
        // Arrange
        await CreateSourceJpegAsync("photo.jpg", new DateTime(2024, 4, 12));
        var destinationPattern = Path.Combine(DestDir, "{year}", "{month}", "{name}{ext}");

        // First copy
        await RunCopyAsync(destination: destinationPattern);
        
        var destFilePath = Path.Combine(DestDir, "2024", "04", "photo.jpg");
        var originalSize = new FileInfo(destFilePath).Length;
        var originalWriteTime = File.GetLastWriteTime(destFilePath);

        // Create a new source file with same name but different date
        File.Delete(Path.Combine(SourceDir, "photo.jpg"));
        await CreateSourceJpegAsync("photo.jpg", new DateTime(2024, 4, 12));
        
        // Wait a bit to ensure timestamp difference
        await Task.Delay(100);

        // Act - run again with skip-existing
        var result = await RunCopyAsync(
            destination: destinationPattern,
            additionalArgs: ["--skip-existing", "true"]);

        // Assert
        await Assert.That(result.Success).IsTrue();
        var newSize = new FileInfo(destFilePath).Length;
        var newWriteTime = File.GetLastWriteTime(destFilePath);
        
        await Assert.That(newSize).IsEqualTo(originalSize);
        await Assert.That(newWriteTime).IsEqualTo(originalWriteTime);
    }

    [Test]
    public async Task Copy_Overwrite_ReplacesExistingFile()
    {
        // Arrange
        await CreateSourceJpegAsync("photo.jpg", new DateTime(2024, 4, 12));
        var destinationPattern = Path.Combine(DestDir, "{year}", "{month}", "{name}{ext}");

        // First copy
        await RunCopyAsync(destination: destinationPattern);
        
        var destFilePath = Path.Combine(DestDir, "2024", "04", "photo.jpg");
        var originalWriteTime = File.GetLastWriteTime(destFilePath);

        // Delete source and create new file with same name
        File.Delete(Path.Combine(SourceDir, "photo.jpg"));
        await Task.Delay(100);
        await CreateSourceJpegAsync("photo.jpg", new DateTime(2024, 4, 12));

        // Act - run again with overwrite
        var result = await RunCopyAsync(
            destination: destinationPattern,
            additionalArgs: ["--overwrite", "true"]);

        // Assert
        await Assert.That(result.Success).IsTrue();
        var newWriteTime = File.GetLastWriteTime(destFilePath);
        
        // File should have been overwritten with a newer write time
        await Assert.That(newWriteTime).IsNotEqualTo(originalWriteTime);
    }

    [Test]
    public async Task Copy_InvalidSourcePath_ReturnsError()
    {
        // Arrange
        var invalidSource = Path.Combine(TestBaseDirectory, "nonexistent_folder");
        var destinationPattern = Path.Combine(DestDir, "{name}{ext}");

        // Act
        var result = await RunCopyAsync(
            source: invalidSource,
            destination: destinationPattern);

        // Assert
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
    }

    [Test]
    public async Task Copy_JpegWithGpsCoordinates_CopiedSuccessfully()
    {
        // Arrange - Create JPEG with GPS coordinates (Paris)
        var gps = (Lat: 48.8566, Lon: 2.3522);
        await CreateSourceJpegAsync("paris.jpg", new DateTime(2024, 7, 14), gps: gps);
        
        var destinationPattern = Path.Combine(DestDir, "{year}", "{month}", "{name}{ext}");

        // Act
        var result = await RunCopyAsync(destination: destinationPattern);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(DestinationFileExists(Path.Combine("2024", "07", "paris.jpg"))).IsTrue();
        await Assert.That(GetDestinationFiles().Length).IsEqualTo(1);
    }

    [Test]
    public async Task Copy_FromSubfolders_PreservesFiles()
    {
        // Arrange - Create files in nested subfolders
        await CreateSourceJpegAsync("root.jpg", new DateTime(2024, 1, 1));
        await CreateSourceJpegAsync("level1.jpg", new DateTime(2024, 2, 2), subfolder: "folder1");
        await CreateSourceJpegAsync("level2.jpg", new DateTime(2024, 3, 3), subfolder: Path.Combine("folder1", "folder2"));
        
        var destinationPattern = Path.Combine(DestDir, "{year}", "{month}", "{name}{ext}");

        // Act
        var result = await RunCopyAsync(destination: destinationPattern);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(DestinationFileExists(Path.Combine("2024", "01", "root.jpg"))).IsTrue();
        await Assert.That(DestinationFileExists(Path.Combine("2024", "02", "level1.jpg"))).IsTrue();
        await Assert.That(DestinationFileExists(Path.Combine("2024", "03", "level2.jpg"))).IsTrue();
        await Assert.That(GetDestinationFiles().Length).IsEqualTo(3);
    }

    [Test]
    public async Task Copy_MixedJpegAndPng_AllCopied()
    {
        // Arrange
        await CreateSourceJpegAsync("photo1.jpg", new DateTime(2024, 5, 1));
        await CreateSourceJpegAsync("photo2.jpg", new DateTime(2024, 5, 2));
        await CreateSourcePngAsync("screenshot1.png", new DateTime(2024, 5, 3));
        await CreateSourcePngAsync("screenshot2.png", new DateTime(2024, 5, 4));
        
        var destinationPattern = Path.Combine(DestDir, "{year}", "{month}", "{name}{ext}");

        // Act
        var result = await RunCopyAsync(destination: destinationPattern);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(DestinationFileExists(Path.Combine("2024", "05", "photo1.jpg"))).IsTrue();
        await Assert.That(DestinationFileExists(Path.Combine("2024", "05", "photo2.jpg"))).IsTrue();
        await Assert.That(DestinationFileExists(Path.Combine("2024", "05", "screenshot1.png"))).IsTrue();
        await Assert.That(DestinationFileExists(Path.Combine("2024", "05", "screenshot2.png"))).IsTrue();
        await Assert.That(GetDestinationFiles().Length).IsEqualTo(4);
    }
}
