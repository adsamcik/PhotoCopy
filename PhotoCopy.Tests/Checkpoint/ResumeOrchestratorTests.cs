using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PhotoCopy.Checkpoint;
using PhotoCopy.Checkpoint.Models;
using PhotoCopy.Configuration;
using PhotoCopy.Directories;
using PhotoCopy.Files;
using PhotoCopy.Tests.Checkpoint.Fakes;

namespace PhotoCopy.Tests.Checkpoint;

/// <summary>
/// Unit tests for ResumeOrchestrator class.
/// </summary>
public class ResumeOrchestratorTests
{
    private ICheckpointStore _mockStore = null!;
    private ICheckpointValidator _mockValidator = null!;
    private ILogger<ResumeOrchestrator> _mockLogger = null!;
    private FakeClock _clock = null!;
    private ResumeOrchestrator _orchestrator = null!;

    [Before(Test)]
    public void Setup()
    {
        _mockStore = Substitute.For<ICheckpointStore>();
        _mockValidator = Substitute.For<ICheckpointValidator>();
        _mockLogger = Substitute.For<ILogger<ResumeOrchestrator>>();
        _clock = new FakeClock(new DateTime(2026, 1, 20, 12, 0, 0, DateTimeKind.Utc));
        _orchestrator = new ResumeOrchestrator(_mockStore, _mockValidator, _mockLogger, _clock);
    }

    #region Constructor Tests

    [Test]
    public async Task Constructor_ThrowsArgumentNullException_WhenStoreIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Task.FromResult(new ResumeOrchestrator(null!, _mockValidator, _mockLogger)));
    }

    [Test]
    public async Task Constructor_ThrowsArgumentNullException_WhenValidatorIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Task.FromResult(new ResumeOrchestrator(_mockStore, null!, _mockLogger)));
    }

    [Test]
    public async Task Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Task.FromResult(new ResumeOrchestrator(_mockStore, _mockValidator, null!)));
    }

    #endregion

    #region DetermineResumeActionAsync - FreshStart Flag Tests

    [Test]
    public async Task DetermineResumeActionAsync_ReturnsStartFresh_WhenFreshFlagIsSet()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = @"C:\Source",
            Destination = @"D:\Dest",
            FreshStart = true
        };

        // Act
        var decision = await _orchestrator.DetermineResumeActionAsync(config);

        // Assert
        await Assert.That(decision).IsTypeOf<ResumeDecision.StartFreshDecision>();
        var startFresh = (ResumeDecision.StartFreshDecision)decision;
        await Assert.That(startFresh.Reason).Contains("--fresh");
    }

    [Test]
    public async Task DetermineResumeActionAsync_DoesNotSearchForCheckpoint_WhenFreshFlagIsSet()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = @"C:\Source",
            Destination = @"D:\Dest",
            FreshStart = true
        };

        // Act
        await _orchestrator.DetermineResumeActionAsync(config);

        // Assert
        await _mockStore.DidNotReceive().FindLatestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region DetermineResumeActionAsync - No Checkpoint Tests

    [Test]
    public async Task DetermineResumeActionAsync_ReturnsStartFresh_WhenNoCheckpointFound()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = @"C:\Source",
            Destination = @"D:\Dest"
        };

        _mockStore.FindLatestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CheckpointState?>(null));

        // Act
        var decision = await _orchestrator.DetermineResumeActionAsync(config);

        // Assert
        await Assert.That(decision).IsTypeOf<ResumeDecision.StartFreshDecision>();
        var startFresh = (ResumeDecision.StartFreshDecision)decision;
        await Assert.That(startFresh.Reason).Contains("No previous checkpoint found");
    }

    #endregion

    #region DetermineResumeActionAsync - Invalid Checkpoint Tests

    [Test]
    public async Task DetermineResumeActionAsync_ReturnsStartFresh_WhenCheckpointIsInvalid()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = @"C:\Source",
            Destination = @"D:\Dest"
        };

        var checkpoint = CreateCheckpointState();
        _mockStore.FindLatestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CheckpointState?>(checkpoint));

        var validation = ResumeValidation.Invalid("Configuration has changed");
        _mockValidator.ValidateAsync(Arg.Any<CheckpointState>(), Arg.Any<PhotoCopyConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(validation));

        // Act
        var decision = await _orchestrator.DetermineResumeActionAsync(config);

        // Assert
        await Assert.That(decision).IsTypeOf<ResumeDecision.StartFreshDecision>();
        var startFresh = (ResumeDecision.StartFreshDecision)decision;
        await Assert.That(startFresh.Reason).IsEqualTo("Configuration has changed");
    }

    #endregion

    #region DetermineResumeActionAsync - Resume Flag Tests

    [Test]
    public async Task DetermineResumeActionAsync_ReturnsResumeFromCheckpoint_WhenResumeFlagIsSet()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = @"C:\Source",
            Destination = @"D:\Dest",
            Resume = true
        };

        var checkpoint = CreateCheckpointState();
        _mockStore.FindLatestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CheckpointState?>(checkpoint));

        var validation = CreateValidValidation();
        _mockValidator.ValidateAsync(Arg.Any<CheckpointState>(), Arg.Any<PhotoCopyConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(validation));

        // Act
        var decision = await _orchestrator.DetermineResumeActionAsync(config);

        // Assert
        await Assert.That(decision).IsTypeOf<ResumeDecision.ResumeFromCheckpoint>();
        var resume = (ResumeDecision.ResumeFromCheckpoint)decision;
        await Assert.That(resume.Checkpoint).IsEqualTo(checkpoint);
        await Assert.That(resume.Validation).IsEqualTo(validation);
    }

    #endregion

    #region DetermineResumeActionAsync - Prompt User Tests

    [Test]
    public async Task DetermineResumeActionAsync_ReturnsPromptUser_WhenValidCheckpointExists()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = @"C:\Source",
            Destination = @"D:\Dest",
            Resume = false,
            FreshStart = false
        };

        var checkpoint = CreateCheckpointState();
        _mockStore.FindLatestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CheckpointState?>(checkpoint));

        var validation = CreateValidValidation();
        _mockValidator.ValidateAsync(Arg.Any<CheckpointState>(), Arg.Any<PhotoCopyConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(validation));

        // Act
        var decision = await _orchestrator.DetermineResumeActionAsync(config);

        // Assert
        await Assert.That(decision).IsTypeOf<ResumeDecision.PromptUserDecision>();
        var prompt = (ResumeDecision.PromptUserDecision)decision;
        await Assert.That(prompt.Checkpoint).IsEqualTo(checkpoint);
        await Assert.That(prompt.Validation).IsEqualTo(validation);
    }

    #endregion

    #region DetermineResumeActionAsync - Validation Warnings Tests

    [Test]
    public async Task DetermineResumeActionAsync_ReturnsResumeWithWarnings_WhenValidationHasWarnings()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = @"C:\Source",
            Destination = @"D:\Dest",
            Resume = true
        };

        var checkpoint = CreateCheckpointState();
        _mockStore.FindLatestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CheckpointState?>(checkpoint));

        var validation = ResumeValidation.Valid(
            totalOperations: 100,
            completedOperations: 50,
            warnings: new List<string> { "Some files have changed", "Long idle time" });
        _mockValidator.ValidateAsync(Arg.Any<CheckpointState>(), Arg.Any<PhotoCopyConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(validation));

        // Act
        var decision = await _orchestrator.DetermineResumeActionAsync(config);

        // Assert
        await Assert.That(decision).IsTypeOf<ResumeDecision.ResumeFromCheckpoint>();
        var resume = (ResumeDecision.ResumeFromCheckpoint)decision;
        await Assert.That(resume.Validation.Warnings).Contains("Some files have changed");
    }

    #endregion

    #region DetermineResumeActionAsync - Cancellation Tests

    [Test]
    public async Task DetermineResumeActionAsync_ThrowsArgumentNullException_WhenConfigIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _orchestrator.DetermineResumeActionAsync(null!));
    }

    [Test]
    public async Task DetermineResumeActionAsync_RespectsCanellationToken()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = @"C:\Source",
            Destination = @"D:\Dest"
        };

        var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockStore.FindLatestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<CheckpointState?>>(callInfo =>
            {
                callInfo.Arg<CancellationToken>().ThrowIfCancellationRequested();
                return Task.FromResult<CheckpointState?>(null);
            });

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _orchestrator.DetermineResumeActionAsync(config, cts.Token));
    }

    #endregion

    #region CreateCheckpointState Tests

    [Test]
    public async Task CreateCheckpointState_ThrowsArgumentNullException_WhenPlanIsNull()
    {
        // Arrange
        var config = new PhotoCopyConfig();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _orchestrator.CreateCheckpointState(null!, config));
        await Task.CompletedTask;
    }

    [Test]
    public async Task CreateCheckpointState_ThrowsArgumentNullException_WhenConfigIsNull()
    {
        // Arrange
        var plan = CreateEmptyCopyPlan();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _orchestrator.CreateCheckpointState(plan, null!));
        await Task.CompletedTask;
    }

    [Test]
    public async Task CreateCheckpointState_CreatesValidState()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = @"C:\Source",
            Destination = @"D:\Dest\{Year}"
        };

        var mockFile = Substitute.For<IFile>();
        mockFile.File.Returns(new System.IO.FileInfo(@"C:\Source\test.jpg"));

        var operations = new List<FileCopyPlan>
        {
            new FileCopyPlan(mockFile, @"D:\Dest\2024\test.jpg", Array.Empty<RelatedFilePlan>()),
            new FileCopyPlan(mockFile, @"D:\Dest\2024\test2.jpg", Array.Empty<RelatedFilePlan>())
        };
        var plan = new CopyPlan(operations, Array.Empty<ValidationFailure>(), Array.Empty<string>(), 2048);

        _mockValidator.ComputeConfigHash(Arg.Any<PhotoCopyConfig>())
            .Returns(new byte[16]);
        _mockValidator.ComputePlanHash(Arg.Any<IReadOnlyList<PhotoCopy.Files.IFile>>())
            .Returns(new byte[16]);

        // Act
        var state = _orchestrator.CreateCheckpointState(plan, config);

        // Assert
        await Assert.That(state).IsNotNull();
        await Assert.That(state.SourceDirectory).IsEqualTo(@"C:\Source");
        await Assert.That(state.DestinationPattern).IsEqualTo(@"D:\Dest\{Year}");
        await Assert.That(state.TotalFiles).IsEqualTo(2);
        await Assert.That(state.StartedUtc).IsEqualTo(_clock.UtcNow);
    }

    #endregion

    #region CreateCheckpointStateAsync Tests

    [Test]
    public async Task CreateCheckpointStateAsync_ReturnsCheckpointState()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = @"C:\Source",
            Destination = @"D:\Dest"
        };

        var plan = CreateEmptyCopyPlan();

        _mockValidator.ComputeConfigHash(Arg.Any<PhotoCopyConfig>())
            .Returns(new byte[16]);
        _mockValidator.ComputePlanHash(Arg.Any<IReadOnlyList<PhotoCopy.Files.IFile>>())
            .Returns(new byte[16]);

        // Act
        var state = await _orchestrator.CreateCheckpointStateAsync(plan, config);

        // Assert
        await Assert.That(state).IsNotNull();
    }

    [Test]
    public async Task CreateCheckpointStateAsync_RespectsCancellation()
    {
        // Arrange
        var config = new PhotoCopyConfig { Source = @"C:\Source", Destination = @"D:\Dest" };
        var plan = CreateEmptyCopyPlan();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _orchestrator.CreateCheckpointStateAsync(plan, config, cts.Token));
    }

    #endregion

    #region Helper Methods

    private CheckpointState CreateCheckpointState()
    {
        return new CheckpointState
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Version = 1,
            StartedUtc = _clock.UtcNow.AddHours(-1),
            SourceDirectory = @"C:\Source",
            DestinationPattern = @"D:\Dest",
            TotalFiles = 100,
            TotalBytes = 102400,
            ConfigHash = new byte[16],
            PlanHash = new byte[16],
            Completed = new BitArray(100),
            Failed = new Dictionary<int, string>(),
            Statistics = new CheckpointStatistics
            {
                FilesCompleted = 50,
                FilesSkipped = 5,
                FilesFailed = 2,
                BytesCompleted = 51200,
                LastUpdatedUtc = _clock.UtcNow.AddMinutes(-30)
            },
            FilePath = @"C:\checkpoints\test.checkpoint"
        };
    }

    private static ResumeValidation CreateValidValidation()
    {
        return ResumeValidation.Valid(
            totalOperations: 100,
            completedOperations: 50);
    }

    private static CopyPlan CreateEmptyCopyPlan()
    {
        return new CopyPlan(
            Array.Empty<FileCopyPlan>(),
            Array.Empty<ValidationFailure>(),
            Array.Empty<string>(),
            0);
    }

    #endregion
}
