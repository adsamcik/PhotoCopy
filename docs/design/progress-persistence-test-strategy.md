# Progress Persistence / Resume Support - Test Strategy

> **Author**: Test Engineering Analysis  
> **Date**: January 2026  
> **Feature**: Checkpoint/Resume for large file operations (10K+ files)

## Executive Summary

This document outlines a comprehensive test strategy for implementing Progress Persistence / Resume Support in PhotoCopy. The feature must handle:
- Periodic checkpoint saving during copy/move operations
- `--resume` flag to continue from last checkpoint
- Graceful handling of interruptions (Ctrl+C, crashes, power loss)
- Consistency guarantees for partially completed operations

---

## 1. Test Categories Needed

### 1.1 Unit Tests (`PhotoCopy.Tests/Persistence/`)

| Category | Purpose | Priority |
|----------|---------|----------|
| `CheckpointSerializerTests` | Verify checkpoint file format (JSON/binary serialization) | P0 |
| `CheckpointStateTests` | Validate checkpoint state machine (NotStarted → InProgress → Completed/Failed) | P0 |
| `ResumeValidatorTests` | Test validation of checkpoint files (version, corruption, staleness) | P0 |
| `FileHashMatcherTests` | Verify file identity matching between sessions | P1 |
| `ProgressCalculatorTests` | Test progress percentage calculation with resume offset | P1 |

### 1.2 Integration Tests (`PhotoCopy.Tests/Integration/`)

| Category | Purpose | Priority |
|----------|---------|----------|
| `CheckpointPersistenceIntegrationTests` | End-to-end checkpoint save/load with real file system | P0 |
| `ResumeFromCheckpointTests` | Full resume workflow with partial progress | P0 |
| `InterruptionRecoveryTests` | Simulated crash/cancellation scenarios | P0 |
| `ConcurrentCheckpointTests` | Race condition testing for checkpoint writes | P1 |

### 1.3 E2E Tests (`PhotoCopy.Tests/E2E/`)

| Category | Purpose | Priority |
|----------|---------|----------|
| `ResumeCommandE2ETests` | CLI `--resume` flag behavior | P0 |
| `LargeCollectionResumeTests` | 10K+ file resume scenarios | P1 |
| `CrossSessionResumeTests` | Resume after process restart | P0 |

---

## 2. Critical Scenarios That MUST Be Tested

### 2.1 Core Resume Flow (P0 - Must Pass)

```csharp
// Scenario: Resume after 50% completion
[Test]
public async Task Resume_AfterHalfCompletion_ProcessesOnlyRemainingFiles()
{
    // Given: 100 files, 50 already copied, checkpoint exists
    // When: Run with --resume
    // Then: Only 50 files processed, final count = 100
}

// Scenario: Checkpoint corruption detection
[Test]
public async Task Resume_WithCorruptedCheckpoint_FailsWithClearError()
{
    // Given: Checkpoint file with invalid JSON/truncated data
    // When: Run with --resume
    // Then: Exit code indicates corruption, suggests full restart
}

// Scenario: Source files changed since checkpoint
[Test]
public async Task Resume_WhenSourceFilesModified_DetectsAndReports()
{
    // Given: Checkpoint from yesterday, source files modified today
    // When: Run with --resume
    // Then: Detects mismatch, offers options (restart/skip modified/abort)
}
```

### 2.2 Checkpoint Integrity (P0)

```csharp
// Scenario: Checkpoint during file copy (mid-write crash)
[Test]
public async Task Resume_AfterMidFileCrash_CleansUpPartialFile()
{
    // Given: File was being written when crash occurred
    // When: Resume
    // Then: Partial file is deleted, file is re-copied completely
}

// Scenario: Atomic checkpoint writes
[Test]
public async Task Checkpoint_Save_IsAtomic()
{
    // Given: Checkpoint being saved
    // When: Process killed during save
    // Then: Previous valid checkpoint remains intact (write-then-rename)
}
```

### 2.3 Edge Cases (P1)

```csharp
// Scenario: Resume with deleted destination files
[Test]
public async Task Resume_WhenDestinationFilesDeleted_RecopiesTombstonedFiles()

// Scenario: Resume after config change
[Test]
public async Task Resume_WithDifferentDestinationPattern_RejectsOrWarns()

// Scenario: Resume with different --parallel setting
[Test]
public async Task Resume_WithDifferentParallelism_WorksCorrectly()

// Scenario: Empty checkpoint (0 files processed)
[Test]
public async Task Resume_FromEmptyCheckpoint_StartsFromBeginning()
```

---

## 3. Simulating Interruption/Crash Scenarios

### 3.1 Controlled Cancellation (Using CancellationToken)

```csharp
public class InterruptionSimulator
{
    /// <summary>
    /// Cancels after N files are processed.
    /// </summary>
    public static CancellationTokenSource CreateCancelAfterNFiles(int n, IProgressReporter reporter)
    {
        var cts = new CancellationTokenSource();
        var filesProcessed = 0;
        
        reporter.OnFileProcessed += (_, _) =>
        {
            if (Interlocked.Increment(ref filesProcessed) >= n)
            {
                cts.Cancel();
            }
        };
        
        return cts;
    }
}

// Usage in tests:
[Test]
public async Task Resume_AfterCancellation_ContinuesFromLastCheckpoint()
{
    // Arrange
    var files = CreateTestFiles(100);
    var cts = InterruptionSimulator.CreateCancelAfterNFiles(50, progressReporter);
    
    // Act - First run (will be cancelled)
    var firstResult = await copier.CopyAsync(validators, progressReporter, cts.Token);
    
    // Assert checkpoint exists
    CheckpointExists(checkpointPath).Should().BeTrue();
    GetCheckpointProgress(checkpointPath).FilesProcessed.Should().BeGreaterOrEqual(50);
    
    // Act - Resume
    config.Resume = true;
    var secondResult = await copier.CopyAsync(validators, progressReporter, CancellationToken.None);
    
    // Assert
    (firstResult.FilesProcessed + secondResult.FilesProcessed).Should().BeGreaterOrEqualTo(100);
    GetAllDestinationFiles().Should().HaveCount(100);
}
```

### 3.2 Simulating Crashes with IFileSystem Mock

```csharp
public class CrashingFileSystem : IFileSystem
{
    private readonly IFileSystem _inner;
    private readonly int _crashAfterNCopies;
    private int _copyCount;

    public CrashingFileSystem(IFileSystem inner, int crashAfterNCopies)
    {
        _inner = inner;
        _crashAfterNCopies = crashAfterNCopies;
    }

    public void CopyFile(string source, string destination, bool overwrite = false)
    {
        if (Interlocked.Increment(ref _copyCount) >= _crashAfterNCopies)
        {
            throw new SimulatedCrashException("Process terminated unexpectedly");
        }
        _inner.CopyFile(source, destination, overwrite);
    }
    
    // Delegate other methods to _inner...
}

public class SimulatedCrashException : Exception
{
    public SimulatedCrashException(string message) : base(message) { }
}
```

### 3.3 Simulating Mid-Write Crashes

```csharp
public class PartialWriteFileSystem : IFileSystem
{
    private readonly IFileSystem _inner;
    private readonly string _crashOnFile;
    private readonly double _percentageWritten;

    public void CopyFile(string source, string destination, bool overwrite = false)
    {
        if (Path.GetFileName(source) == _crashOnFile)
        {
            // Write partial file
            var content = File.ReadAllBytes(source);
            var partialContent = content.Take((int)(content.Length * _percentageWritten)).ToArray();
            File.WriteAllBytes(destination, partialContent);
            throw new IOException("Simulated disk full during write");
        }
        _inner.CopyFile(source, destination, overwrite);
    }
}
```

### 3.4 Process-Level Testing (E2E)

```csharp
[Test]
public async Task E2E_Resume_AfterProcessKill()
{
    // Start copy process
    var process = ProcessRunner.Start("PhotoCopy.exe", "copy", "--source", source, "--dest", dest);
    
    // Wait for some files to be processed (monitor checkpoint file)
    await WaitForCheckpointProgress(checkpointPath, minFiles: 50);
    
    // Kill process (simulating crash)
    process.Kill();
    await process.WaitForExitAsync();
    
    // Resume
    var resumeResult = await ProcessRunner.RunAsync("PhotoCopy.exe", "copy", "--resume", "--source", source);
    
    // Verify completion
    resumeResult.ExitCode.Should().Be(0);
    GetDestinationFileCount().Should().Be(100);
}
```

---

## 4. Edge Cases That Would Catch Bugs

### 4.1 File System Edge Cases

| Edge Case | What Could Go Wrong | Test Strategy |
|-----------|---------------------|---------------|
| **Empty source directory** | Checkpoint with 0 files, resume handling | Create checkpoint for empty dir, verify resume is no-op |
| **Single file** | Off-by-one in progress calculation | Test with exactly 1 file, verify 100% or 0% only |
| **Files deleted during operation** | Checkpoint references non-existent files | Delete files after checkpoint, verify skip + warning |
| **Files added during operation** | New files missed in resume | Add files after checkpoint, verify they're skipped or processed |
| **Very long file paths** | Path truncation in checkpoint | Test with 260+ char paths |
| **Unicode file names** | Encoding issues in checkpoint JSON | Test with emoji, CJK, RTL characters |
| **Read-only checkpoint location** | Crash on checkpoint save | Mock permission denied, verify graceful handling |

### 4.2 Timing Edge Cases

| Edge Case | What Could Go Wrong | Test Strategy |
|-----------|---------------------|---------------|
| **Checkpoint during first file** | No valid checkpoint state | Crash during first file copy, verify restart from beginning |
| **Checkpoint on last file** | Resume processes 0 files, reports weird progress | Resume when 99/100 done, verify single file processed |
| **Rapid consecutive checkpoints** | File contention, corruption | Parallel writes with low interval |
| **Checkpoint across DST boundary** | Timestamp comparison issues | Mock system clock, cross DST |

### 4.3 Configuration Edge Cases

| Edge Case | What Could Go Wrong | Test Strategy |
|-----------|---------------------|---------------|
| **Resume with `--dry-run`** | Unclear what happens | Should reject or simulate resume |
| **Resume after source moved** | Path mismatch | Verify detection and clear error |
| **Resume with different `--destination`** | Files in wrong location | Should reject with suggestion |
| **Multiple concurrent PhotoCopy processes** | Checkpoint corruption | Test with file locking |

### 4.4 Data Integrity Edge Cases

```csharp
[Test]
public async Task Resume_PreservesFileChecksums()
{
    // Verify files copied after resume have same checksums as source
}

[Test]
public async Task Resume_HandlesZeroByteFiles()
{
    // Zero-byte files should be tracked correctly in checkpoint
}

[Test]
public async Task Resume_HandlesDuplicateFilenames()
{
    // Multiple photo_001.jpg in different folders
    // Checkpoint must track full paths, not just names
}

[Test]
public async Task Resume_WithSkipExisting_TracksSkippedFilesCorrectly()
{
    // Skipped files shouldn't be re-evaluated on resume
}
```

---

## 5. Test Infrastructure Needed

### 5.1 New Interfaces for Testability

```csharp
// Abstraction for checkpoint persistence
public interface ICheckpointPersistence
{
    Task<Checkpoint?> LoadAsync(string path, CancellationToken ct = default);
    Task SaveAsync(string path, Checkpoint checkpoint, CancellationToken ct = default);
    bool Exists(string path);
    void Delete(string path);
}

// Abstraction for checkpoint location resolution
public interface ICheckpointLocator
{
    string GetCheckpointPath(string sourceDirectory, string? customPath = null);
}

// Abstraction for file identity (to detect changes between sessions)
public interface IFileIdentityProvider
{
    FileIdentity GetIdentity(IFile file);
}

public record FileIdentity(string Path, long Size, DateTime LastModified, string? Checksum);
```

### 5.2 Test Doubles

```csharp
// In-memory checkpoint storage for unit tests
public class InMemoryCheckpointPersistence : ICheckpointPersistence
{
    private readonly Dictionary<string, Checkpoint> _checkpoints = new();
    
    public int SaveCount { get; private set; }
    public List<Checkpoint> SaveHistory { get; } = new();
    
    public Task<Checkpoint?> LoadAsync(string path, CancellationToken ct = default)
    {
        _checkpoints.TryGetValue(path, out var checkpoint);
        return Task.FromResult(checkpoint);
    }
    
    public Task SaveAsync(string path, Checkpoint checkpoint, CancellationToken ct = default)
    {
        _checkpoints[path] = checkpoint;
        SaveHistory.Add(checkpoint with { }); // Clone to preserve history
        SaveCount++;
        return Task.CompletedTask;
    }
    
    public bool Exists(string path) => _checkpoints.ContainsKey(path);
    public void Delete(string path) => _checkpoints.Remove(path);
}

// Delayed/failing checkpoint persistence for edge case testing
public class FailingCheckpointPersistence : ICheckpointPersistence
{
    public int FailOnSaveNumber { get; set; } = -1;
    private int _saveAttempts;
    
    public Task SaveAsync(string path, Checkpoint checkpoint, CancellationToken ct = default)
    {
        if (++_saveAttempts == FailOnSaveNumber)
        {
            throw new IOException("Simulated checkpoint save failure");
        }
        // ... actual save logic
    }
}
```

### 5.3 Test Data Builders

```csharp
public class CheckpointBuilder
{
    private readonly Checkpoint _checkpoint = new()
    {
        Version = Checkpoint.CurrentVersion,
        CreatedAt = DateTime.UtcNow,
        SourceDirectory = "C:\\Source",
        DestinationPattern = "C:\\Dest\\{year}\\{name}{ext}",
        Status = CheckpointStatus.InProgress
    };
    
    public CheckpointBuilder WithProcessedFiles(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _checkpoint.ProcessedFiles.Add(new ProcessedFileEntry($"file{i}.jpg", $"C:\\Dest\\2025\\file{i}.jpg"));
        }
        return this;
    }
    
    public CheckpointBuilder WithStatus(CheckpointStatus status)
    {
        _checkpoint.Status = status;
        return this;
    }
    
    public CheckpointBuilder Corrupted()
    {
        _checkpoint.Version = -999; // Invalid version
        return this;
    }
    
    public Checkpoint Build() => _checkpoint;
}
```

### 5.4 Assertion Extensions

```csharp
public static class CheckpointAssertionExtensions
{
    public static void ShouldBeValidCheckpoint(this Checkpoint checkpoint)
    {
        checkpoint.Version.Should().BeGreaterThan(0);
        checkpoint.SourceDirectory.Should().NotBeNullOrEmpty();
        checkpoint.CreatedAt.Should().BeBefore(DateTime.UtcNow.AddMinutes(1));
    }
    
    public static void ShouldBeResumableFrom(this Checkpoint checkpoint, int expectedProcessedCount)
    {
        checkpoint.Status.Should().Be(CheckpointStatus.InProgress);
        checkpoint.ProcessedFiles.Should().HaveCount(expectedProcessedCount);
    }
}
```

---

## 6. Property-Based Testing Opportunities

### 6.1 Using FsCheck with TUnit

```csharp
// Add package: FsCheck.Xunit or adapt for TUnit

public class CheckpointPropertyTests
{
    [Test]
    public void Checkpoint_RoundTrip_PreservesAllData()
    {
        Prop.ForAll<Checkpoint>(checkpoint =>
        {
            var serialized = CheckpointSerializer.Serialize(checkpoint);
            var deserialized = CheckpointSerializer.Deserialize(serialized);
            return checkpoint.Equals(deserialized);
        }).QuickCheckThrowOnFailure();
    }
    
    [Test]
    public void ProcessedFiles_AreNeverProcessedTwice()
    {
        Prop.ForAll<List<string>>(filePaths =>
        {
            // Given any set of file paths
            var checkpoint = CreateCheckpointWithFiles(filePaths.Take(filePaths.Count / 2));
            var remainingFiles = GetFilesToProcess(filePaths, checkpoint);
            
            // Processed files should never appear in remaining
            var intersection = checkpoint.ProcessedFiles.Intersect(remainingFiles);
            return !intersection.Any();
        }).QuickCheckThrowOnFailure();
    }
    
    [Test]
    public void TotalFilesProcessed_AlwaysMatchesSumOfSessions()
    {
        Prop.ForAll(
            Arb.Default.PositiveInt(),
            Arb.Default.PositiveInt(),
            (firstSession, secondSession) =>
            {
                // Simulate first session processing some files
                // Resume and process more
                // Total should equal sum
                return true; // Implement actual logic
            }).QuickCheckThrowOnFailure();
    }
}
```

### 6.2 Generators for Test Data

```csharp
public class CheckpointGenerators
{
    public static Arbitrary<Checkpoint> ValidCheckpoint()
    {
        return Gen.Choose(1, 1000)
            .Select(count => new CheckpointBuilder()
                .WithProcessedFiles(count)
                .WithStatus(CheckpointStatus.InProgress)
                .Build())
            .ToArbitrary();
    }
    
    public static Arbitrary<FileIdentity> AnyFileIdentity()
    {
        return Gen.zip3(
                Gen.Elements("a.jpg", "b.png", "photo.heic", "video.mp4"),
                Gen.Choose(1, int.MaxValue).Select(i => (long)i),
                Arb.Default.DateTime().Generator)
            .Select(t => new FileIdentity(t.Item1, t.Item2, t.Item3, null))
            .ToArbitrary();
    }
}
```

---

## 7. Integration Test Design for Resume Flow

### 7.1 Test Fixture Structure

```csharp
[NotInParallel("ResumeTests")]
[Property("Category", "Integration,Resume")]
public class ResumeFromCheckpointTests
{
    private string _testBaseDirectory = null!;
    private string _sourceDir = null!;
    private string _destDir = null!;
    private string _checkpointPath = null!;
    
    private ICheckpointPersistence _checkpointPersistence = null!;
    private InMemoryCheckpointPersistence _testCheckpointStore = null!;

    [Before(Test)]
    public void Setup()
    {
        _testBaseDirectory = Path.Combine(Path.GetTempPath(), "ResumeTests", Guid.NewGuid().ToString());
        _sourceDir = Path.Combine(_testBaseDirectory, "source");
        _destDir = Path.Combine(_testBaseDirectory, "dest");
        _checkpointPath = Path.Combine(_destDir, ".photocopy-checkpoint.json");
        
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_destDir);
        
        _testCheckpointStore = new InMemoryCheckpointPersistence();
    }

    [After(Test)]
    public void Cleanup()
    {
        SafeDeleteDirectory(_testBaseDirectory);
    }

    [Test]
    public async Task Resume_WithValidCheckpoint_SkipsAlreadyProcessedFiles()
    {
        // Arrange: Create 10 test files
        var testFiles = await CreateTestFilesAsync(10);
        
        // Create checkpoint showing 5 files already processed
        var checkpoint = new CheckpointBuilder()
            .WithSourceDirectory(_sourceDir)
            .WithDestinationPattern(Path.Combine(_destDir, "{name}{ext}"))
            .WithProcessedFiles(testFiles.Take(5).Select(f => 
                new ProcessedFileEntry(f, Path.Combine(_destDir, Path.GetFileName(f)))))
            .WithStatus(CheckpointStatus.InProgress)
            .Build();
        
        await _testCheckpointStore.SaveAsync(_checkpointPath, checkpoint);
        
        // Manually copy the first 5 files (simulating previous session)
        foreach (var file in testFiles.Take(5))
        {
            File.Copy(file, Path.Combine(_destDir, Path.GetFileName(file)));
        }
        
        // Act: Run with resume
        var config = CreateConfig(resume: true);
        var copier = BuildCopier(config, _testCheckpointStore);
        var result = await copier.CopyAsync(validators, progressReporter, CancellationToken.None);
        
        // Assert
        result.FilesProcessed.Should().Be(5); // Only remaining 5
        result.FilesSkipped.Should().Be(5);   // Previously processed
        Directory.GetFiles(_destDir).Should().HaveCount(10);
    }

    [Test]
    public async Task Resume_WhenNoCheckpointExists_StartsFromBeginning()
    {
        // Arrange
        var testFiles = await CreateTestFilesAsync(10);
        
        // Act: Run with --resume but no checkpoint
        var config = CreateConfig(resume: true);
        var copier = BuildCopier(config, _testCheckpointStore);
        var result = await copier.CopyAsync(validators, progressReporter, CancellationToken.None);
        
        // Assert: Should process all files (no checkpoint = fresh start)
        result.FilesProcessed.Should().Be(10);
    }

    [Test]
    public async Task Resume_WithCompletedCheckpoint_ReportsAlreadyComplete()
    {
        // Arrange
        var checkpoint = new CheckpointBuilder()
            .WithStatus(CheckpointStatus.Completed)
            .Build();
        await _testCheckpointStore.SaveAsync(_checkpointPath, checkpoint);
        
        // Act
        var config = CreateConfig(resume: true);
        var copier = BuildCopier(config, _testCheckpointStore);
        var result = await copier.CopyAsync(validators, progressReporter, CancellationToken.None);
        
        // Assert
        result.FilesProcessed.Should().Be(0);
        // Should log "Previous operation already completed"
    }
}
```

### 7.2 Full Workflow Integration Test

```csharp
[Test]
public async Task FullWorkflow_InterruptAndResume_CompletesSuccessfully()
{
    // Phase 1: Setup
    var testFiles = await CreateTestFilesAsync(100);
    var cancellationAtFile50 = new TaskCompletionSource<bool>();
    var filesProcessed = 0;
    
    // Phase 2: First run (will be interrupted)
    var config = CreateConfig(resume: false, checkpointInterval: 10);
    var copier = BuildCopierWithInterception(config, onFileProcessed: () =>
    {
        if (++filesProcessed == 50)
        {
            cancellationAtFile50.SetResult(true);
        }
    });
    
    using var cts = new CancellationTokenSource();
    var firstRunTask = copier.CopyAsync(validators, progressReporter, cts.Token);
    
    await cancellationAtFile50.Task;
    cts.Cancel();
    
    var firstResult = await firstRunTask.ShouldThrowAsync<OperationCanceledException>();
    
    // Verify checkpoint was saved
    var checkpoint = await _checkpointPersistence.LoadAsync(_checkpointPath);
    checkpoint.Should().NotBeNull();
    checkpoint!.ProcessedFiles.Count.Should().BeGreaterOrEqualTo(40); // At least 4 checkpoint saves
    
    // Phase 3: Resume
    var resumeConfig = CreateConfig(resume: true);
    var resumeCopier = BuildCopier(resumeConfig, _checkpointPersistence);
    var resumeResult = await resumeCopier.CopyAsync(validators, progressReporter, CancellationToken.None);
    
    // Phase 4: Verify
    var totalProcessed = checkpoint.ProcessedFiles.Count + resumeResult.FilesProcessed;
    totalProcessed.Should().Be(100);
    
    var destFiles = Directory.GetFiles(_destDir, "*", SearchOption.AllDirectories);
    destFiles.Should().HaveCount(100);
    
    // Verify all file contents match
    foreach (var sourceFile in testFiles)
    {
        var destFile = GetExpectedDestinationPath(sourceFile);
        var sourceHash = ComputeHash(sourceFile);
        var destHash = ComputeHash(destFile);
        destHash.Should().Be(sourceHash, $"File {sourceFile} should match its copy");
    }
}
```

---

## 8. What Would Make This Feature Hard to Test (And How to Avoid It)

### 8.1 Potential Testability Issues

| Anti-Pattern | Why It's Hard to Test | How to Avoid |
|--------------|----------------------|--------------|
| **Direct file I/O in checkpoint logic** | Can't unit test without real filesystem | Use `ICheckpointPersistence` interface |
| **Static/global checkpoint path** | Can't run tests in parallel | Pass path via configuration/injection |
| **Tight coupling to `DirectoryCopier`** | Can't test checkpoint logic independently | Extract `ICheckpointManager` interface |
| **Using `DateTime.Now` directly** | Can't test time-based logic (staleness) | Inject `ISystemClock` or `TimeProvider` |
| **Synchronous-only checkpoint saves** | Can't cancel properly, blocks I/O | Use async throughout |
| **Hardcoded checkpoint interval** | Can't verify save frequency easily | Make configurable, expose for tests |

### 8.2 Recommended Architecture for Testability

```csharp
// GOOD: Fully injectable checkpoint manager
public class CheckpointManager : ICheckpointManager
{
    private readonly ICheckpointPersistence _persistence;
    private readonly ICheckpointLocator _locator;
    private readonly IFileIdentityProvider _identityProvider;
    private readonly ISystemClock _clock;
    private readonly ILogger<CheckpointManager> _logger;
    
    public CheckpointManager(
        ICheckpointPersistence persistence,
        ICheckpointLocator locator,
        IFileIdentityProvider identityProvider,
        ISystemClock clock,
        ILogger<CheckpointManager> logger)
    {
        // All dependencies injected
    }
    
    public async Task<ResumeContext?> TryLoadResumeContextAsync(
        PhotoCopyConfig config,
        CancellationToken ct = default)
    {
        // Can be fully unit tested with mocks
    }
}

// GOOD: Separated concerns
public interface ICheckpointManager
{
    Task<ResumeContext?> TryLoadResumeContextAsync(PhotoCopyConfig config, CancellationToken ct = default);
    Task SaveCheckpointAsync(CopyProgress progress, CancellationToken ct = default);
    Task CompleteAsync(CancellationToken ct = default);
    Task FailAsync(string reason, CancellationToken ct = default);
}

public record ResumeContext(
    Checkpoint Checkpoint,
    IReadOnlySet<string> AlreadyProcessedPaths,
    int ResumeFromIndex);
```

### 8.3 Avoiding Time-Based Test Flakiness

```csharp
// BAD: Tests depend on wall clock
public void SaveCheckpoint()
{
    _checkpoint.LastSavedAt = DateTime.UtcNow; // Non-deterministic!
}

// GOOD: Inject time provider
public void SaveCheckpoint()
{
    _checkpoint.LastSavedAt = _clock.UtcNow; // Deterministic in tests
}

// In tests:
var fakeClock = new FakeClock(new DateTime(2025, 1, 15, 10, 0, 0));
// ... setup with fakeClock ...
fakeClock.Advance(TimeSpan.FromHours(1));
// Now can test "checkpoint is 1 hour old" logic
```

### 8.4 Testing File Locking / Concurrency

```csharp
// Use a test-specific lock provider
public interface IFileLock
{
    bool TryAcquire(string path, TimeSpan timeout);
    void Release();
}

// In production: uses actual file locks
// In tests: can simulate contention
public class ControllableFileLock : IFileLock
{
    public bool ShouldBlock { get; set; }
    public int AcquireAttempts { get; private set; }
    
    public bool TryAcquire(string path, TimeSpan timeout)
    {
        AcquireAttempts++;
        return !ShouldBlock;
    }
}
```

---

## 9. Test Execution Strategy

### 9.1 Test Ordering

```
1. Unit Tests (fast, run first)
   └── CheckpointSerializerTests (< 1 sec)
   └── CheckpointStateTests (< 1 sec)
   └── ResumeValidatorTests (< 1 sec)

2. Integration Tests (slower, require real FS)
   └── CheckpointPersistenceIntegrationTests (< 5 sec)
   └── ResumeFromCheckpointTests (< 10 sec)
   └── InterruptionRecoveryTests (< 10 sec)

3. E2E Tests (slowest, full process)
   └── ResumeCommandE2ETests (< 30 sec)
   └── LargeCollectionResumeTests (< 60 sec) [optional, CI-only]
```

### 9.2 Test Data Requirements

| Test Category | Files Needed | Size | Notes |
|---------------|-------------|------|-------|
| Unit Tests | 0 | N/A | Uses mocks/fakes |
| Integration | 10-100 | 1KB each | MockImageGenerator |
| E2E | 100-1000 | 1KB each | Real temp files |
| Stress | 10,000+ | 1KB each | CI-only, opt-in |

### 9.3 CI/CD Considerations

```yaml
# Example test job configuration
test-resume-feature:
  parallel:
    - unit-tests           # Fast, run on every commit
    - integration-tests    # Run on every commit
  
  sequential:
    - e2e-tests           # Run on PR merge
    - stress-tests        # Nightly only, 10K+ files
```

---

## 10. Recommended Checkpoint Data Structure

Based on test requirements, here's the recommended checkpoint format:

```csharp
public record Checkpoint
{
    public const int CurrentVersion = 1;
    
    public int Version { get; init; } = CurrentVersion;
    public DateTime CreatedAt { get; init; }
    public DateTime LastUpdatedAt { get; init; }
    public string SourceDirectory { get; init; } = string.Empty;
    public string DestinationPattern { get; init; } = string.Empty;
    public CheckpointStatus Status { get; init; }
    public string? FailureReason { get; init; }
    
    // Configuration snapshot for validation on resume
    public ConfigurationSnapshot Config { get; init; } = new();
    
    // Processed files with enough info to verify identity
    public List<ProcessedFileEntry> ProcessedFiles { get; init; } = new();
    
    // Statistics for progress reporting
    public CheckpointStatistics Statistics { get; init; } = new();
}

public record ProcessedFileEntry(
    string SourcePath,
    string DestinationPath,
    long FileSize,
    DateTime SourceLastModified,
    string? Checksum);

public record ConfigurationSnapshot(
    OperationMode Mode,
    bool CalculateChecksums,
    DuplicateHandling DuplicateHandling,
    string? DuplicatesFormat);

public record CheckpointStatistics(
    int TotalFilesDiscovered,
    int FilesProcessed,
    int FilesSkipped,
    int FilesFailed,
    long BytesProcessed);

public enum CheckpointStatus
{
    InProgress,
    Completed,
    Failed,
    Cancelled
}
```

---

## 11. Summary: Test Coverage Matrix

| Component | Unit | Integration | E2E | Property |
|-----------|:----:|:-----------:|:---:|:--------:|
| Checkpoint Serialization | ✅ | ✅ | | ✅ |
| Checkpoint Validation | ✅ | ✅ | | |
| Resume Logic | ✅ | ✅ | ✅ | ✅ |
| Interruption Handling | | ✅ | ✅ | |
| File Identity Matching | ✅ | ✅ | | ✅ |
| Progress Calculation | ✅ | | | |
| CLI `--resume` Flag | | | ✅ | |
| Configuration Compatibility | ✅ | ✅ | | |
| Concurrent Access | | ✅ | | |
| Large Collections (10K+) | | | ✅ | |

---

## Appendix: Sample Test File Structure

```
PhotoCopy.Tests/
├── Persistence/
│   ├── CheckpointSerializerTests.cs
│   ├── CheckpointStateTests.cs
│   ├── CheckpointValidatorTests.cs
│   ├── FileIdentityProviderTests.cs
│   └── ProgressCalculatorTests.cs
├── Integration/
│   ├── CheckpointPersistenceIntegrationTests.cs
│   ├── ResumeFromCheckpointTests.cs
│   ├── InterruptionRecoveryTests.cs
│   └── ConcurrentCheckpointTests.cs
├── E2E/
│   └── Commands/
│       ├── ResumeCommandE2ETests.cs
│       └── LargeCollectionResumeTests.cs
├── TestingImplementation/
│   ├── InMemoryCheckpointPersistence.cs
│   ├── CrashingFileSystem.cs
│   ├── InterruptionSimulator.cs
│   ├── FakeClock.cs
│   └── CheckpointBuilder.cs
└── PropertyTests/
    └── CheckpointPropertyTests.cs
```
