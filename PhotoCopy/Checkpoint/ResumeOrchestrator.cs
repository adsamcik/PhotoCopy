using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PhotoCopy.Checkpoint.Models;
using PhotoCopy.Configuration;
using PhotoCopy.Directories;

namespace PhotoCopy.Checkpoint;

/// <summary>
/// Base type for resume decision outcomes.
/// </summary>
public abstract record ResumeDecision
{
    private ResumeDecision() { }

    /// <summary>
    /// Decision to start a fresh operation (no resume).
    /// </summary>
    public sealed record StartFreshDecision(string Reason) : ResumeDecision;

    /// <summary>
    /// Decision to resume from an existing checkpoint.
    /// </summary>
    public sealed record ResumeFromCheckpoint(
        CheckpointState Checkpoint, 
        ResumeValidation Validation) : ResumeDecision;

    /// <summary>
    /// Decision requires user confirmation before proceeding.
    /// </summary>
    public sealed record PromptUserDecision(
        CheckpointState Checkpoint, 
        ResumeValidation Validation) : ResumeDecision;
}

/// <summary>
/// Orchestrates checkpoint detection, validation, and resume decision logic.
/// </summary>
public sealed class ResumeOrchestrator
{
    private readonly ICheckpointStore _store;
    private readonly ICheckpointValidator _validator;
    private readonly ILogger<ResumeOrchestrator> _logger;
    private readonly ISystemClock _clock;

    /// <summary>
    /// Creates a new resume orchestrator.
    /// </summary>
    /// <param name="store">Checkpoint persistence store.</param>
    /// <param name="validator">Checkpoint validator.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="clock">System clock (defaults to SystemClock.Instance).</param>
    public ResumeOrchestrator(
        ICheckpointStore store,
        ICheckpointValidator validator,
        ILogger<ResumeOrchestrator> logger,
        ISystemClock? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? SystemClock.Instance;
    }

    /// <summary>
    /// Determines the appropriate resume action based on configuration and existing checkpoints.
    /// </summary>
    /// <param name="config">Current configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resume decision indicating how to proceed.</returns>
    public async Task<ResumeDecision> DetermineResumeActionAsync(
        PhotoCopyConfig config,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        // Check for --fresh flag first
        if (config.FreshStart)
        {
            _logger.LogInformation("Starting fresh: --fresh flag specified");
            return new ResumeDecision.StartFreshDecision("--fresh flag specified");
        }

        // Look for existing checkpoint
        _logger.LogDebug("Searching for existing checkpoint for source: {Source}, destination: {Destination}",
            config.Source, config.Destination);

        var checkpoint = await _store.FindLatestAsync(
            config.Source,
            config.Destination,
            ct).ConfigureAwait(false);

        if (checkpoint is null)
        {
            _logger.LogDebug("No previous checkpoint found");
            return new ResumeDecision.StartFreshDecision("No previous checkpoint found");
        }

        _logger.LogInformation("Found checkpoint from {StartedUtc:u}, session: {SessionId}",
            checkpoint.StartedUtc, checkpoint.SessionId);

        // Validate the checkpoint
        var validation = await _validator.ValidateAsync(checkpoint, config, ct).ConfigureAwait(false);

        if (!validation.IsValid)
        {
            _logger.LogWarning("Checkpoint is invalid: {Reason}", validation.InvalidReason);
            return new ResumeDecision.StartFreshDecision(validation.InvalidReason ?? "Checkpoint validation failed");
        }

        // Log any warnings
        foreach (var warning in validation.Warnings)
        {
            _logger.LogWarning("Checkpoint warning: {Warning}", warning);
        }

        // Log changed files if any
        if (validation.ChangedFiles.Count > 0)
        {
            _logger.LogWarning("{Count} source file(s) have changed since checkpoint was created:",
                validation.ChangedFiles.Count);

            foreach (var change in validation.ChangedFiles.Take(10))
            {
                _logger.LogWarning("  [{ChangeType}] {Path}: {Details}",
                    change.ChangeType, change.SourcePath, change.Details);
            }

            if (validation.ChangedFiles.Count > 10)
            {
                _logger.LogWarning("  ... and {Count} more changed files",
                    validation.ChangedFiles.Count - 10);
            }
        }

        _logger.LogInformation("Checkpoint is valid: {Completed}/{Total} operations completed ({Percentage:F1}%)",
            validation.CompletedOperations,
            validation.TotalOperations,
            validation.CompletionPercentage);

        // If --resume flag is set, resume directly without prompting
        if (config.Resume)
        {
            _logger.LogInformation("Resuming automatically: --resume flag specified");
            return new ResumeDecision.ResumeFromCheckpoint(checkpoint, validation);
        }

        // Otherwise, let the caller handle user interaction
        _logger.LogDebug("Checkpoint found, prompting user for decision");
        return new ResumeDecision.PromptUserDecision(checkpoint, validation);
    }

    /// <summary>
    /// Creates a new checkpoint state from a copy plan.
    /// </summary>
    /// <param name="plan">The copy plan to create a checkpoint for.</param>
    /// <param name="config">Current configuration.</param>
    /// <returns>A new checkpoint state initialized from the plan.</returns>
    public CheckpointState CreateCheckpointState(CopyPlan plan, PhotoCopyConfig config)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(config);

        var files = plan.Operations.Select(op => op.File).ToList();
        var configHash = _validator.ComputeConfigHash(config);
        var planHash = _validator.ComputePlanHash(files);

        var state = CheckpointState.CreateNew(
            sourceDirectory: config.Source,
            destinationPattern: config.Destination,
            totalFiles: plan.Operations.Count,
            totalBytes: plan.TotalBytes,
            configHash: configHash,
            planHash: planHash,
            startedUtc: _clock.UtcNow);

        _logger.LogDebug("Created checkpoint state: session={SessionId}, files={TotalFiles}, bytes={TotalBytes}",
            state.SessionId, state.TotalFiles, state.TotalBytes);

        return state;
    }

    /// <summary>
    /// Creates a new checkpoint state from a copy plan asynchronously.
    /// </summary>
    /// <param name="plan">The copy plan to create a checkpoint for.</param>
    /// <param name="config">Current configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A new checkpoint state initialized from the plan.</returns>
    public Task<CheckpointState> CreateCheckpointStateAsync(
        CopyPlan plan,
        PhotoCopyConfig config,
        CancellationToken ct = default)
    {
        // Currently synchronous, but exposed as async for future flexibility
        // (e.g., if hash computation becomes async or needs I/O)
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(CreateCheckpointState(plan, config));
    }
}
