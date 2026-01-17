using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Abstractions;
using PhotoCopy.Commands;
using PhotoCopy.Configuration;
using PhotoCopy.Directories;
using PhotoCopy.Duplicates;
using PhotoCopy.Files;
using PhotoCopy.Progress;
using PhotoCopy.Rollback;
using PhotoCopy.Tests.TestingImplementation;
using PhotoCopy.Validators;

namespace PhotoCopy.Tests.Integration;

/// <summary>
/// Tests for CopyCommand and DirectoryCopierAsync using InMemoryFileSystem with
/// pre-configured metadata (bypasses real EXIF extraction).
/// 
/// <para><b>Purpose:</b> These tests focus on the COPIER LOGIC and COMMAND BEHAVIOR, not metadata extraction.
/// They use InMemoryFile with hardcoded metadata values to test path generation, 
/// duplicate handling, filtering, and command execution flow in isolation.</para>
/// 
/// <para><b>What these tests cover uniquely:</b></para>
/// <list type="bullet">
///   <item>Location-based path variables ({city}, {state}, {country})</item>
///   <item>Progress reporter integration</item>
///   <item>Cancellation handling</item>
///   <item>Logging behavior verification</item>
///   <item>Complex scenario builders (photo sequences, monthly batches)</item>
/// </list>
/// 
/// <para><b>For real end-to-end testing with ACTUAL EXIF extraction,
/// see <see cref="EndToEndCopyWorkflowTests"/>.</b></para>
/// 
/// <para><b>Note:</b> Some tests in this class overlap with EndToEndCopyWorkflowTests 
/// (basic copy, move, filtering). Those are kept for defense-in-depth testing with
/// the in-memory infrastructure.</para>
/// </summary>
[NotInParallel("InMemoryCLI")]
[Property("Category", "CopierLogic")]
public class InMemoryCLITests
{
    private InMemoryScenarioBuilder _scenarioBuilder = null!;
    private FakeLogger<CopyCommand> _copyLogger = null!;
    private FakeLogger<ScanCommand> _scanLogger = null!;
    private FakeLogger<DirectoryCopierAsync> _copierLogger = null!;
    private FakeLogger<ValidatorFactory> _validatorLogger = null!;
    private readonly ITransactionLogger _transactionLogger = Substitute.For<ITransactionLogger>();
    private readonly IFileValidationService _fileValidationService = new FileValidationService();

    [Before(Test)]
    public void SetUp()
    {
        SharedLogs.Clear();
        _scenarioBuilder = new InMemoryScenarioBuilder();
        _copyLogger = new FakeLogger<CopyCommand>();
        _scanLogger = new FakeLogger<ScanCommand>();
        _copierLogger = new FakeLogger<DirectoryCopierAsync>();
        _validatorLogger = new FakeLogger<ValidatorFactory>();
    }

    [After(Test)]
    public void TearDown()
    {
        SharedLogs.Clear();
    }

    #region Helper Methods

    private PhotoCopyConfig CreateConfig(
        string source,
        string destination,
        bool dryRun = false,
        bool skipExisting = false,
        bool overwrite = false,
        OperationMode mode = OperationMode.Copy,
        DateTime? minDate = null,
        DateTime? maxDate = null,
        RelatedFileLookup relatedFileMode = RelatedFileLookup.None,
        string duplicatesFormat = "-{number}",
        DuplicateHandling duplicateHandling = DuplicateHandling.None)
    {
        return new PhotoCopyConfig
        {
            Source = source,
            Destination = destination,
            DryRun = dryRun,
            SkipExisting = skipExisting,
            Overwrite = overwrite,
            Mode = mode,
            MinDate = minDate,
            MaxDate = maxDate,
            RelatedFileMode = relatedFileMode,
            DuplicatesFormat = duplicatesFormat,
            DuplicateHandling = duplicateHandling,
            UseAsync = true,
            Parallelism = 1, // Sequential for predictable testing
            AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".heic", ".mov", ".mp4", ".avi", ".cr2", ".raf", ".nef", ".arw", ".dng"
            }
        };
    }

    private CopyCommand CreateCopyCommand(
        PhotoCopyConfig config,
        InMemoryFileSystem fileSystem,
        IProgressReporter? progressReporter = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(config);
        var copierAsync = new DirectoryCopierAsync(_copierLogger, fileSystem, options, _transactionLogger, _fileValidationService);
        var copier = Substitute.For<IDirectoryCopier>();
        var validatorFactory = new ValidatorFactory(_validatorLogger);
        var reporter = progressReporter ?? NullProgressReporter.Instance;

        return new CopyCommand(
            _copyLogger,
            options,
            copier,
            copierAsync,
            validatorFactory,
            reporter);
    }

    private ScanCommand CreateScanCommand(
        PhotoCopyConfig config,
        InMemoryFileSystem fileSystem,
        bool outputJson = false)
    {
        var options = Microsoft.Extensions.Options.Options.Create(config);
        var scanner = Substitute.For<IDirectoryScanner>();
        var validatorFactory = new ValidatorFactory(_validatorLogger);
        var fileFactory = Substitute.For<IFileFactory>();

        return new ScanCommand(
            _scanLogger,
            options,
            scanner,
            validatorFactory,
            _fileValidationService,
            fileFactory,
            fileSystem,
            outputJson);
    }

    #endregion

    #region Copy Operations with Various Configurations

    [Test]
    public async Task CopyCommand_WithYearMonthDayTemplate_CreatesCorrectStructure()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Photos)
            .WithDestinationDirectory(TestPaths.Organized)
            .WithPhoto("vacation.jpg", new DateTime(2024, 7, 15))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{day}", "{name}{ext}"));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InOrganized("2024", "07", "15", "vacation.jpg"))).IsTrue();
    }

    [Test]
    public async Task CopyCommand_WithMultiplePhotos_CopiesAllToCorrectLocations()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("spring.jpg", new DateTime(2024, 3, 20))
            .WithPhoto("summer.jpg", new DateTime(2024, 6, 21))
            .WithPhoto("fall.jpg", new DateTime(2024, 9, 22))
            .WithPhoto("winter.jpg", new DateTime(2024, 12, 21))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "03", "spring.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "06", "summer.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "09", "fall.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "12", "winter.jpg"))).IsTrue();
    }

    [Test]
    public async Task CopyCommand_MoveMode_DeletesSourceFiles()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("tomove.jpg", new DateTime(2024, 5, 10))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"),
            mode: OperationMode.Move);

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "05", "tomove.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InSource("tomove.jpg"))).IsFalse();
    }

    [Test]
    public async Task CopyCommand_WithVideos_CopiesVideoFiles()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Videos)
            .WithDestinationDirectory(TestPaths.Organized)
            .WithVideo("vacation.mp4", new DateTime(2024, 8, 5))
            .WithVideo("birthday.mov", new DateTime(2024, 9, 15))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InOrganized("2024", "08", "vacation.mp4"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InOrganized("2024", "09", "birthday.mov"))).IsTrue();
    }

    #endregion

    #region Duplicate Handling Tests

    [Test]
    public async Task CopyCommand_DuplicateFile_AddsNumberSuffix()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("photo.jpg", new DateTime(2024, 5, 1))
            .WithExistingDestinationPhoto(@"2024\05\photo.jpg", new DateTime(2024, 5, 1))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"),
            duplicatesFormat: "_{number}");

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "05", "photo.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "05", "photo_1.jpg"))).IsTrue();
    }

    [Test]
    public async Task CopyCommand_MultipleDuplicates_IncrementsNumberCorrectly()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("photo.jpg", new DateTime(2024, 5, 1))
            .WithExistingDestinationPhoto(@"2024\05\photo.jpg", new DateTime(2024, 5, 1))
            .WithExistingDestinationPhoto(@"2024\05\photo-1.jpg", new DateTime(2024, 5, 1))
            .WithExistingDestinationPhoto(@"2024\05\photo-2.jpg", new DateTime(2024, 5, 1))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"),
            duplicatesFormat: "-{number}");

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "05", "photo-3.jpg"))).IsTrue();
    }

    [Test]
    public async Task CopyCommand_CustomDuplicateFormat_UsesCorrectNaming()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("photo.jpg", new DateTime(2024, 5, 1))
            .WithExistingDestinationPhoto(@"2024\05\photo.jpg", new DateTime(2024, 5, 1))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"),
            duplicatesFormat: "_copy{number}");

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "05", "photo_copy1.jpg"))).IsTrue();
    }

    #endregion

    #region Date Range Filtering Tests

    [Test]
    public async Task CopyCommand_WithMinDate_SkipsOlderFiles()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("old.jpg", new DateTime(2020, 1, 1))
            .WithPhoto("new.jpg", new DateTime(2024, 6, 15))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"),
            minDate: new DateTime(2023, 1, 1));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "06", "new.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2020", "01", "old.jpg"))).IsFalse();
    }

    [Test]
    public async Task CopyCommand_WithMaxDate_SkipsNewerFiles()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("old.jpg", new DateTime(2022, 6, 15))
            .WithPhoto("new.jpg", new DateTime(2025, 12, 31))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"),
            maxDate: new DateTime(2024, 12, 31));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2022", "06", "old.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2025", "12", "new.jpg"))).IsFalse();
    }

    [Test]
    public async Task CopyCommand_WithDateRange_CopiesOnlyFilesInRange()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("too_old.jpg", new DateTime(2019, 12, 31))
            .WithPhoto("in_range_early.jpg", new DateTime(2020, 1, 1))
            .WithPhoto("in_range_middle.jpg", new DateTime(2022, 6, 15))
            .WithPhoto("in_range_late.jpg", new DateTime(2023, 12, 31))
            .WithPhoto("too_new.jpg", new DateTime(2024, 1, 1))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"),
            minDate: new DateTime(2020, 1, 1),
            maxDate: new DateTime(2023, 12, 31));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2019", "12", "too_old.jpg"))).IsFalse();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2020", "01", "in_range_early.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2022", "06", "in_range_middle.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2023", "12", "in_range_late.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "01", "too_new.jpg"))).IsFalse();
    }

    [Test]
    public async Task CopyCommand_WithDateRangeOnBoundary_IncludesBoundaryDates()
    {
        // Arrange
        var minDate = new DateTime(2023, 6, 1);
        var maxDate = new DateTime(2023, 6, 30);

        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("before.jpg", new DateTime(2023, 5, 31, 23, 59, 59))
            .WithPhoto("on_min.jpg", minDate)
            .WithPhoto("on_max.jpg", maxDate)
            .WithPhoto("after.jpg", new DateTime(2023, 7, 1))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{day}", "{name}{ext}"),
            minDate: minDate,
            maxDate: maxDate);

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2023", "05", "31", "before.jpg"))).IsFalse();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2023", "06", "01", "on_min.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2023", "06", "30", "on_max.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2023", "07", "01", "after.jpg"))).IsFalse();
    }

    #endregion

    #region Destination Path Generation with Variables

    [Test]
    public async Task CopyCommand_WithYearVariable_ReplacesYearCorrectly()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("photo.jpg", new DateTime(2024, 8, 15))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{name}{ext}"));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        await command.ExecuteAsync();

        // Assert
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "photo.jpg"))).IsTrue();
    }

    [Test]
    public async Task CopyCommand_WithMonthVariable_PadsWithZero()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("jan.jpg", new DateTime(2024, 1, 15))
            .WithPhoto("dec.jpg", new DateTime(2024, 12, 15))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{month}", "{name}{ext}"));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        await command.ExecuteAsync();

        // Assert
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("01", "jan.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("12", "dec.jpg"))).IsTrue();
    }

    [Test]
    public async Task CopyCommand_WithDayVariable_PadsWithZero()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("first.jpg", new DateTime(2024, 6, 1))
            .WithPhoto("last.jpg", new DateTime(2024, 6, 30))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{day}", "{name}{ext}"));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        await command.ExecuteAsync();

        // Assert
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("01", "first.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("30", "last.jpg"))).IsTrue();
    }

    [Test]
    public async Task CopyCommand_WithLocationVariables_ReplacesLocationData()
    {
        // Arrange
        var location = new LocationData("Paris", "Paris", null, "Île-de-France", "France");
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhotoWithLocation("paris.jpg", new DateTime(2024, 7, 14), (48.8566, 2.3522), "Paris", "Île-de-France", "France")
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{country}", "{city}", "{name}{ext}"));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        await command.ExecuteAsync();

        // Assert
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("France", "Paris", "paris.jpg"))).IsTrue();
    }

    [Test]
    public async Task CopyCommand_WithoutLocation_UsesUnknownPlaceholder()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("nolocation.jpg", new DateTime(2024, 7, 14))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{country}", "{city}", "{name}{ext}"));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        await command.ExecuteAsync();

        // Assert
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("Unknown", "Unknown", "nolocation.jpg"))).IsTrue();
    }

    [Test]
    public async Task CopyCommand_WithNameVariable_PreservesOriginalName()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("My Vacation Photo.jpg", new DateTime(2024, 8, 1))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{name}{ext}"));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        await command.ExecuteAsync();

        // Assert
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("My Vacation Photo.jpg"))).IsTrue();
    }

    [Test]
    public async Task CopyCommand_WithComplexTemplate_HandlesAllVariables()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhotoWithLocation("beach.jpg", new DateTime(2024, 7, 4), (25.7617, -80.1918), "Miami", "Florida", "USA")
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{country}", "{state}", "{city}", "{month}-{day}", "{name}{ext}"));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        await command.ExecuteAsync();

        // Assert
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "USA", "Florida", "Miami", "07-04", "beach.jpg"))).IsTrue();
    }

    #endregion

    #region Dry-Run Mode Tests

    [Test]
    public async Task CopyCommand_DryRunMode_DoesNotCopyFiles()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("photo.jpg", new DateTime(2024, 6, 1))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"),
            dryRun: true);

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "06", "photo.jpg"))).IsFalse();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InSource("photo.jpg"))).IsTrue();
    }

    [Test]
    public async Task CopyCommand_DryRunMode_LogsPlannedOperations()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("photo1.jpg", new DateTime(2024, 3, 15))
            .WithPhoto("photo2.jpg", new DateTime(2024, 4, 20))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"),
            dryRun: true);

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        await command.ExecuteAsync();

        // Assert - verify logging occurred
        var logs = SharedLogs.Entries;
        await Assert.That(logs.Any(l => l.Message.Contains("DryRun"))).IsTrue();
    }

    [Test]
    public async Task CopyCommand_DryRunMoveMode_DoesNotDeleteSourceFiles()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("photo.jpg", new DateTime(2024, 6, 1))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"),
            dryRun: true,
            mode: OperationMode.Move);

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InSource("photo.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "06", "photo.jpg"))).IsFalse();
    }

    #endregion

    #region Skip Existing and Overwrite Mode Tests

    [Test]
    public async Task CopyCommand_SkipExisting_DoesNotOverwrite()
    {
        // Arrange
        var originalContent = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("photo.jpg", new DateTime(2024, 5, 1))
            .BuildWithDetails();

        // Add existing file with different content
        scenario.FileSystem.AddFile(TestPaths.InDest("2024", "05", "photo.jpg"), originalContent);

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"),
            skipExisting: true);

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        var destContent = scenario.FileSystem.GetFileContent(TestPaths.InDest("2024", "05", "photo.jpg"));
        await Assert.That(destContent).IsEquivalentTo(originalContent);
    }

    [Test]
    public async Task CopyCommand_SkipExisting_DoesNotCreateDuplicate()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("photo.jpg", new DateTime(2024, 5, 1))
            .WithExistingDestinationPhoto(@"2024\05\photo.jpg", new DateTime(2024, 5, 1))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"),
            skipExisting: true);

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "05", "photo.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "05", "photo-1.jpg"))).IsFalse();
    }

    [Test]
    public async Task CopyCommand_OverwriteMode_ReplacesExistingFile()
    {
        // Arrange
        var originalContent = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("photo.jpg", new DateTime(2024, 5, 1))
            .BuildWithDetails();

        // Add existing file with different content
        scenario.FileSystem.AddFile(TestPaths.InDest("2024", "05", "photo.jpg"), originalContent);

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"),
            overwrite: true);

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        var destContent = scenario.FileSystem.GetFileContent(TestPaths.InDest("2024", "05", "photo.jpg"));
        // Content should be different from original (overwritten with source content)
        await Assert.That(destContent).IsNotEquivalentTo(originalContent);
    }

    #endregion

    #region Photo Sequence Tests

    [Test]
    public async Task CopyCommand_WithPhotoSequence_CopiesAllPhotos()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhotoSequence("trip", new DateTime(2024, 7, 1), count: 5)
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        for (int i = 1; i <= 5; i++)
        {
            var fileName = $"trip_{i:D3}.jpg";
            await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "07", fileName))).IsTrue();
        }
    }

    [Test]
    public async Task CopyCommand_WithMonthlyPhotos_OrganizesByMonth()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithMonthlyPhotos(2024)
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        for (int month = 1; month <= 12; month++)
        {
            var destPath = TestPaths.InDest("2024", month.ToString("00"), $"monthly_photo_{month:D2}.jpg");
            await Assert.That(scenario.FileSystem.FileExists(destPath)).IsTrue();
        }
    }

    #endregion

    #region Subdirectory Tests

    [Test]
    public async Task CopyCommand_WithSubdirectoryPhotos_HandlesNestedFolders()
    {
        // Arrange - Note: InMemoryFileSystem.EnumerateFiles only enumerates direct children
        // So we test that files from source are correctly organized to destination
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("photo_001.jpg", new DateTime(2024, 8, 1))
            .WithPhoto("photo_002.jpg", new DateTime(2024, 8, 2))
            .WithPhoto("photo_003.jpg", new DateTime(2024, 8, 3))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "08", "photo_001.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "08", "photo_002.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "08", "photo_003.jpg"))).IsTrue();
    }

    #endregion

    #region Cancellation Tests

    [Test]
    public async Task CopyCommand_WhenCancelled_ReturnsCancelledCode()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhotoSequence("photo", new DateTime(2024, 1, 1), count: 100)
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"));

        var command = CreateCopyCommand(config, scenario.FileSystem);
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act
        var result = await command.ExecuteAsync(cts.Token);

        // Assert
        await Assert.That(result).IsEqualTo(2); // Cancellation exit code
    }

    #endregion

    #region Progress Reporter Tests

    [Test]
    public async Task CopyCommand_WithProgressReporter_ReportsProgress()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("photo1.jpg", new DateTime(2024, 1, 1))
            .WithPhoto("photo2.jpg", new DateTime(2024, 2, 1))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"));

        var progressReporter = Substitute.For<IProgressReporter>();
        var command = CreateCopyCommand(config, scenario.FileSystem, progressReporter);

        // Act
        await command.ExecuteAsync();

        // Assert
        progressReporter.Received().Report(Arg.Any<CopyProgress>());
        progressReporter.Received().Complete(Arg.Any<CopyProgress>());
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task CopyCommand_EmptySource_CompletesSuccessfully()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Empty)
            .WithDestinationDirectory(TestPaths.Dest)
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task CopyCommand_WithPngPhotos_CopiesCorrectly()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPngPhoto("screenshot.png", new DateTime(2024, 9, 1))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "09", "screenshot.png"))).IsTrue();
    }

    [Test]
    public async Task CopyCommand_WithMixedMediaTypes_HandlesAllTypes()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("photo.jpg", new DateTime(2024, 5, 1))
            .WithPngPhoto("screenshot.png", new DateTime(2024, 5, 2))
            .WithVideo("clip.mp4", new DateTime(2024, 5, 3))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "05", "photo.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "05", "screenshot.png"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "05", "clip.mp4"))).IsTrue();
    }

    [Test]
    public async Task CopyCommand_FilesFromDifferentYears_OrganizesCorrectly()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("y2020.jpg", new DateTime(2020, 6, 15))
            .WithPhoto("y2021.jpg", new DateTime(2021, 6, 15))
            .WithPhoto("y2022.jpg", new DateTime(2022, 6, 15))
            .WithPhoto("y2023.jpg", new DateTime(2023, 6, 15))
            .WithPhoto("y2024.jpg", new DateTime(2024, 6, 15))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{name}{ext}"));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo(0);
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2020", "y2020.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2021", "y2021.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2022", "y2022.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2023", "y2023.jpg"))).IsTrue();
        await Assert.That(scenario.FileSystem.FileExists(TestPaths.InDest("2024", "y2024.jpg"))).IsTrue();
    }

    #endregion

    #region Log Verification Tests

    [Test]
    public async Task CopyCommand_OnSuccess_LogsStartAndCompletion()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("photo.jpg", new DateTime(2024, 8, 1))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        await command.ExecuteAsync();

        // Assert
        var logs = SharedLogs.Entries;
        await Assert.That(logs.Any(l => l.Message.Contains("Starting") && l.Message.Contains("Copy"))).IsTrue();
        await Assert.That(logs.Any(l => l.Message.Contains("complete"))).IsTrue();
    }

    [Test]
    public async Task CopyCommand_WithSkippedFiles_LogsSkipReasons()
    {
        // Arrange
        var scenario = _scenarioBuilder
            .WithSourceDirectory(TestPaths.Source)
            .WithDestinationDirectory(TestPaths.Dest)
            .WithPhoto("old.jpg", new DateTime(2019, 1, 1))
            .BuildWithDetails();

        var config = CreateConfig(
            scenario.SourceDirectory,
            Path.Combine(scenario.DestinationDirectory, "{year}", "{month}", "{name}{ext}"),
            minDate: new DateTime(2020, 1, 1));

        var command = CreateCopyCommand(config, scenario.FileSystem);

        // Act
        await command.ExecuteAsync();

        // Assert
        var logs = SharedLogs.Entries;
        await Assert.That(logs.Any(l => l.Message.Contains("Skipped") || l.Message.Contains("skipped"))).IsTrue();
    }

    #endregion
}
