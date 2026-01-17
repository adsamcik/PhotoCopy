using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Configuration;
using PhotoCopy.Directories;
using PhotoCopy.Files;
using PhotoCopy.Progress;
using PhotoCopy.Rollback;
using PhotoCopy.Tests.TestingImplementation;
using PhotoCopy.Validators;

namespace PhotoCopy.Tests.Integration;

/// <summary>
/// Tests that validate the in-memory test infrastructure works correctly.
/// 
/// <para>These tests verify that:</para>
/// <list type="bullet">
///   <item><see cref="InMemoryFileSystem"/> correctly stores and retrieves files</item>
///   <item><see cref="InMemoryFile"/> provides expected metadata</item>
///   <item><see cref="InMemoryScenarioBuilder"/> creates valid test scenarios</item>
///   <item>DirectoryCopierAsync works correctly with in-memory infrastructure</item>
/// </list>
/// 
/// <para><b>Note:</b> These tests use HARDCODED metadata (not real EXIF extraction).
/// For real extraction tests, see <see cref="EndToEndCopyWorkflowTests"/>.</para>
/// </summary>
[NotInParallel("InMemoryValidation")]
[Property("Category", "InfrastructureValidation")]
public class InMemoryInfrastructureValidationTests
{
    [Test]
    public async Task InMemoryFileSystem_EnumeratesFilesCorrectly()
    {
        // Arrange
        var scenario = new InMemoryScenarioBuilder()
            .WithSourceDirectory(TestPaths.Source)
            .WithPhoto("test.jpg", new DateTime(2024, 5, 15))
            .BuildWithDetails();

        // Act
        var files = scenario.FileSystem.EnumerateFiles(scenario.SourceDirectory).ToList();

        // Assert
        await Assert.That(files.Count).IsEqualTo(1);
        await Assert.That(files[0].File.Name).IsEqualTo("test.jpg");
        await Assert.That(files[0].FileDateTime.Taken).IsEqualTo(new DateTime(2024, 5, 15));
    }

    [Test]
    public async Task DirectoryCopierAsync_BuildsPlan_WithInMemoryFiles()
    {
        // Arrange
        var scenario = new InMemoryScenarioBuilder()
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("photo1.jpg", new DateTime(2024, 6, 20))
            .WithPhoto("photo2.jpg", new DateTime(2024, 7, 25))
            .BuildWithDetails();

        var config = new PhotoCopyConfig
        {
            Source = scenario.SourceDirectory,
            Destination = TestPaths.DestPattern("{year}", "{month}", "{name}{ext}"),
            DryRun = false
        };

        var logger = new FakeLogger<DirectoryCopierAsync>();
        var copier = new DirectoryCopierAsync(logger, scenario.FileSystem, Microsoft.Extensions.Options.Options.Create(config), Substitute.For<ITransactionLogger>(), new FileValidationService());

        // Act
        var plan = await copier.BuildPlanAsync(Array.Empty<IValidator>());

        // Assert
        await Assert.That(plan.Operations.Count).IsEqualTo(2);
        
        var paths = plan.Operations.Select(o => o.DestinationPath).OrderBy(p => p).ToList();
        await Assert.That(paths[0]).IsEqualTo(TestPaths.InDest("2024", "06", "photo1.jpg"));
        await Assert.That(paths[1]).IsEqualTo(TestPaths.InDest("2024", "07", "photo2.jpg"));
    }

    [Test]
    public async Task DirectoryCopierAsync_ActuallyCopiesFiles_ToInMemoryDestination()
    {
        // Arrange
        var scenario = new InMemoryScenarioBuilder()
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("vacation.jpg", new DateTime(2024, 8, 10))
            .BuildWithDetails();

        var config = new PhotoCopyConfig
        {
            Source = scenario.SourceDirectory,
            Destination = TestPaths.DestPattern("{year}", "{month}", "{name}{ext}"),
            DryRun = false,
            UseAsync = true,
            Parallelism = 1
        };

        var logger = new FakeLogger<DirectoryCopierAsync>();
        var copier = new DirectoryCopierAsync(logger, scenario.FileSystem, Microsoft.Extensions.Options.Options.Create(config), Substitute.For<ITransactionLogger>(), new FileValidationService());

        // Act
        var result = await copier.CopyAsync(
            Array.Empty<IValidator>(),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert - verify the file was actually copied to the destination
        await Assert.That(result.FilesProcessed).IsEqualTo(1);
        await Assert.That(result.FilesFailed).IsEqualTo(0);
        
        var destinationExists = scenario.FileSystem.FileExists(TestPaths.InDest("2024", "08", "vacation.jpg"));
        await Assert.That(destinationExists).IsTrue();
    }

    [Test]
    public async Task DirectoryCopierAsync_MoveMode_RemovesSourceFile()
    {
        // Arrange
        var scenario = new InMemoryScenarioBuilder()
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("tomove.jpg", new DateTime(2024, 9, 5))
            .BuildWithDetails();

        var config = new PhotoCopyConfig
        {
            Source = scenario.SourceDirectory,
            Destination = TestPaths.DestPattern("{year}", "{month}", "{name}{ext}"),
            DryRun = false,
            Mode = OperationMode.Move,
            UseAsync = true,
            Parallelism = 1
        };

        var logger = new FakeLogger<DirectoryCopierAsync>();
        var copier = new DirectoryCopierAsync(logger, scenario.FileSystem, Microsoft.Extensions.Options.Options.Create(config), Substitute.For<ITransactionLogger>(), new FileValidationService());

        // Act
        var result = await copier.CopyAsync(
            Array.Empty<IValidator>(),
            NullProgressReporter.Instance,
            CancellationToken.None);

        // Assert
        await Assert.That(result.FilesProcessed).IsEqualTo(1);
        
        // Destination should exist
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "09", "tomove.jpg"))).IsTrue();
        
        // Source should be gone (moved)
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InSource("tomove.jpg"))).IsFalse();
    }

    [Test]
    public async Task MockImageGenerator_CreatesValidJpegWithExifDate()
    {
        // Arrange
        var dateTaken = new DateTime(2024, 6, 15, 14, 30, 0);
        
        // Act
        var jpegBytes = MockImageGenerator.CreateJpeg(dateTaken);

        // Assert - verify it's a valid JPEG (starts with FFD8)
        await Assert.That(jpegBytes.Length).IsGreaterThan(100);
        await Assert.That(jpegBytes[0]).IsEqualTo((byte)0xFF);
        await Assert.That(jpegBytes[1]).IsEqualTo((byte)0xD8);
        
        // Verify EXIF marker is present (FFE1)
        var hasExif = false;
        for (int i = 0; i < jpegBytes.Length - 1; i++)
        {
            if (jpegBytes[i] == 0xFF && jpegBytes[i + 1] == 0xE1)
            {
                hasExif = true;
                break;
            }
        }
        await Assert.That(hasExif).IsTrue();
    }

    [Test]
    public async Task InMemoryFile_HasCorrectMetadata()
    {
        // Arrange
        var taken = new DateTime(2024, 7, 20, 10, 0, 0);
        var location = new LocationData("Berlin", "Berlin", null, "Berlin", "Germany");
        
        // Act
        var file = InMemoryFile.CreatePhoto("photo.jpg", taken, location);

        // Assert
        await Assert.That(file.File.Name).IsEqualTo("photo.jpg");
        await Assert.That(file.FileDateTime.Taken).IsEqualTo(taken);
        await Assert.That(file.Location).IsNotNull();
        await Assert.That(file.Location!.City).IsEqualTo("Berlin");
        await Assert.That(file.Location!.Country).IsEqualTo("Germany");
        await Assert.That(file.Checksum).IsNotEmpty();
    }

    [Test]
    public async Task DateValidator_FiltersFiles_InInMemoryScenario()
    {
        // Arrange
        var scenario = new InMemoryScenarioBuilder()
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("old.jpg", new DateTime(2020, 1, 1))    // Before min date
            .WithPhoto("valid.jpg", new DateTime(2024, 6, 15)) // Within range
            .WithPhoto("new.jpg", new DateTime(2030, 1, 1))    // After max date
            .BuildWithDetails();

        var config = new PhotoCopyConfig
        {
            Source = scenario.SourceDirectory,
            Destination = TestPaths.DestPattern("{year}", "{name}{ext}"),
            MinDate = new DateTime(2023, 1, 1),
            MaxDate = new DateTime(2025, 12, 31),
            DryRun = false
        };

        var logger = new FakeLogger<DirectoryCopierAsync>();
        var copier = new DirectoryCopierAsync(logger, scenario.FileSystem, Microsoft.Extensions.Options.Options.Create(config), Substitute.For<ITransactionLogger>(), new FileValidationService());
        
        var validatorFactory = new ValidatorFactory(new FakeLogger<ValidatorFactory>());
        var validators = validatorFactory.Create(config);

        // Act
        var plan = await copier.BuildPlanAsync(validators);

        // Assert - only the valid.jpg should be in the plan
        await Assert.That(plan.Operations.Count).IsEqualTo(1);
        await Assert.That(plan.Operations[0].File.File.Name).IsEqualTo("valid.jpg");
        await Assert.That(plan.SkippedFiles.Count).IsEqualTo(2);
    }
}
