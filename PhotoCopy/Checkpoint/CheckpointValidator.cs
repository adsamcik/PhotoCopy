using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PhotoCopy.Checkpoint.Models;
using PhotoCopy.Configuration;
using PhotoCopy.Files;

namespace PhotoCopy.Checkpoint;

/// <summary>
/// Validates checkpoints for resumability and integrity.
/// </summary>
public sealed class CheckpointValidator : ICheckpointValidator
{
    private readonly ISystemClock _clock;

    /// <summary>
    /// Default age threshold for checkpoint warnings (30 days).
    /// </summary>
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(30);

    /// <summary>
    /// Number of random completed files to sample-validate.
    /// </summary>
    private const int SampleSize = 50;

    /// <summary>
    /// Creates a new CheckpointValidator.
    /// </summary>
    /// <param name="clock">System clock for time-based validations. Defaults to SystemClock.Instance.</param>
    public CheckpointValidator(ISystemClock? clock = null)
    {
        _clock = clock ?? SystemClock.Instance;
    }

    /// <inheritdoc/>
    public Task<ResumeValidation> ValidateAsync(
        CheckpointState checkpoint,
        PhotoCopyConfig config,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(config);

        var warnings = new List<string>();

        // 1. Validate source directory matches (case-insensitive on Windows)
        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!string.Equals(
            NormalizePath(checkpoint.SourceDirectory),
            NormalizePath(config.Source),
            comparison))
        {
            return Task.FromResult(ResumeValidation.Invalid(
                $"Source directory mismatch. Checkpoint: '{checkpoint.SourceDirectory}', Current: '{config.Source}'"));
        }

        // 2. Validate destination pattern matches
        if (!string.Equals(
            checkpoint.DestinationPattern,
            config.Destination,
            StringComparison.Ordinal))
        {
            return Task.FromResult(ResumeValidation.Invalid(
                $"Destination pattern mismatch. Checkpoint: '{checkpoint.DestinationPattern}', Current: '{config.Destination}'"));
        }

        // 3. Validate config hash matches (settings affecting output paths)
        var currentConfigHash = ComputeConfigHash(config);
        if (!checkpoint.ConfigHash.AsSpan().SequenceEqual(currentConfigHash))
        {
            return Task.FromResult(ResumeValidation.Invalid(
                "Configuration has changed since checkpoint was created. " +
                "Settings affecting output paths (Mode, DuplicatesFormat, PathCasing, etc.) must match."));
        }

        // 4. Check if checkpoint is too old
        var checkpointAge = _clock.UtcNow - checkpoint.StartedUtc;
        if (checkpointAge > DefaultMaxAge)
        {
            warnings.Add(
                $"Checkpoint is {checkpointAge.Days} days old. " +
                $"Source files may have changed since the operation started.");
        }

        // 5. Validate checkpoint has work remaining
        var completedCount = checkpoint.CompletedCount;
        if (completedCount >= checkpoint.TotalFiles)
        {
            return Task.FromResult(ResumeValidation.Invalid(
                "Checkpoint indicates all operations are already completed."));
        }

        // Build valid result
        return Task.FromResult(ResumeValidation.Valid(
            totalOperations: checkpoint.TotalFiles,
            completedOperations: completedCount,
            warnings: warnings.Count > 0 ? warnings : null));
    }

    /// <inheritdoc/>
    public byte[] ComputeConfigHash(PhotoCopyConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // Hash settings that affect destination paths
        // Order matters - must be deterministic
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        // Destination pattern
        AppendString(hasher, config.Destination);

        // Mode (Copy/Move)
        AppendString(hasher, config.Mode.ToString());

        // DuplicatesFormat
        AppendString(hasher, config.DuplicatesFormat);

        // PathCasing
        AppendString(hasher, config.PathCasing.ToString());

        // UseFullCountryNames
        AppendBool(hasher, config.UseFullCountryNames);

        // LocationGranularity
        AppendString(hasher, config.LocationGranularity.ToString());

        // UnknownLocationFallback
        AppendString(hasher, config.UnknownLocationFallback);

        return hasher.GetHashAndReset();
    }

    /// <inheritdoc/>
    public byte[] ComputePlanHash(IReadOnlyList<IFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        // Sort files by FullName for deterministic ordering
        var sortedFiles = files
            .OrderBy(f => f.File.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var file in sortedFiles)
        {
            // Hash FullName
            AppendString(hasher, file.File.FullName);

            // Hash Length
            AppendLong(hasher, file.File.Length);
        }

        return hasher.GetHashAndReset();
    }

    /// <summary>
    /// Normalizes a path for comparison (removes trailing slashes, normalizes separators).
    /// </summary>
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        // Normalize path separators and remove trailing
        return path
            .Replace('/', System.IO.Path.DirectorySeparatorChar)
            .Replace('\\', System.IO.Path.DirectorySeparatorChar)
            .TrimEnd(System.IO.Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// Appends a string to the incremental hash.
    /// </summary>
    private static void AppendString(IncrementalHash hasher, string? value)
    {
        if (value is null)
        {
            // Hash a marker for null
            hasher.AppendData(new byte[] { 0 });
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        // Include length to prevent concatenation ambiguity
        hasher.AppendData(BitConverter.GetBytes(bytes.Length));
        hasher.AppendData(bytes);
    }

    /// <summary>
    /// Appends a boolean to the incremental hash.
    /// </summary>
    private static void AppendBool(IncrementalHash hasher, bool value)
    {
        hasher.AppendData(new byte[] { value ? (byte)1 : (byte)0 });
    }

    /// <summary>
    /// Appends a long to the incremental hash.
    /// </summary>
    private static void AppendLong(IncrementalHash hasher, long value)
    {
        hasher.AppendData(BitConverter.GetBytes(value));
    }
}
