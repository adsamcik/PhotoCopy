using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Configuration;
using PhotoCopy.Rollback;
using PhotoCopy.Tests.TestingImplementation;

namespace PhotoCopy.Tests.Rollback;

public class TransactionLoggerTests
{
    private readonly ILogger<TransactionLogger> _logger;
    private readonly IOptions<PhotoCopyConfig> _options;
    private readonly PhotoCopyConfig _config;
    private readonly string _testDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public TransactionLoggerTests()
    {
        _logger = new FakeLogger<TransactionLogger>();
        _testDirectory = Path.Combine(Path.GetTempPath(), $"TransactionLoggerTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        
        _config = new PhotoCopyConfig
        {
            Source = Path.Combine(_testDirectory, "source"),
            Destination = Path.Combine(_testDirectory, "dest", "{Year}", "{name}{ext}"),
            DryRun = false
        };
        _options = Microsoft.Extensions.Options.Options.Create(_config);
        
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        
        SharedLogs.Clear();
    }

    [After(Test)]
    public void Cleanup()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
        SharedLogs.Clear();
    }

    #region StartTransaction Tests

    [Test]
    public void StartTransaction_CreatesNewTransaction()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        var sourceDir = Path.Combine(_testDirectory, "source");
        var destPattern = Path.Combine(_testDirectory, "dest", "{Year}", "{name}{ext}");

        // Act
        var transactionId = logger.BeginTransaction(sourceDir, destPattern, isDryRun: false);

        // Assert
        logger.CurrentTransaction.Should().NotBeNull();
        logger.CurrentTransaction!.SourceDirectory.Should().Be(sourceDir);
        logger.CurrentTransaction.DestinationPattern.Should().Be(destPattern);
        logger.CurrentTransaction.IsDryRun.Should().BeFalse();
    }

    [Test]
    public void StartTransaction_SetsInProgressStatus()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        var sourceDir = Path.Combine(_testDirectory, "source");
        var destPattern = Path.Combine(_testDirectory, "dest", "{Year}", "{name}{ext}");

        // Act
        logger.BeginTransaction(sourceDir, destPattern, isDryRun: false);

        // Assert
        logger.CurrentTransaction.Should().NotBeNull();
        logger.CurrentTransaction!.Status.Should().Be(TransactionStatus.InProgress);
    }

    [Test]
    public void StartTransaction_GeneratesTransactionId()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        var sourceDir = Path.Combine(_testDirectory, "source");
        var destPattern = Path.Combine(_testDirectory, "dest", "{Year}", "{name}{ext}");

        // Act
        var transactionId = logger.BeginTransaction(sourceDir, destPattern, isDryRun: false);

        // Assert
        transactionId.Should().NotBeNullOrEmpty();
        logger.CurrentTransaction.Should().NotBeNull();
        logger.CurrentTransaction!.TransactionId.Should().Be(transactionId);
        
        // Verify transaction ID format: yyyyMMdd-HHmmss-8charGuid
        transactionId.Should().MatchRegex(@"^\d{8}-\d{6}-[a-f0-9]{8}$");
    }

    [Test]
    public void StartTransaction_SetsStartTime()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        var beforeTime = DateTime.UtcNow;
        var sourceDir = Path.Combine(_testDirectory, "source");
        var destPattern = Path.Combine(_testDirectory, "dest", "{Year}", "{name}{ext}");

        // Act
        logger.BeginTransaction(sourceDir, destPattern, isDryRun: false);
        var afterTime = DateTime.UtcNow;

        // Assert
        logger.CurrentTransaction.Should().NotBeNull();
        logger.CurrentTransaction!.StartTime.Should().BeOnOrAfter(beforeTime);
        logger.CurrentTransaction.StartTime.Should().BeOnOrBefore(afterTime);
    }

    [Test]
    public void StartTransaction_SetsDryRunFlag()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        var sourceDir = Path.Combine(_testDirectory, "source");
        var destPattern = Path.Combine(_testDirectory, "dest", "{Year}", "{name}{ext}");

        // Act
        logger.BeginTransaction(sourceDir, destPattern, isDryRun: true);

        // Assert
        logger.CurrentTransaction.Should().NotBeNull();
        logger.CurrentTransaction!.IsDryRun.Should().BeTrue();
    }

    [Test]
    public void StartTransaction_SetsTransactionLogPath()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        var sourceDir = Path.Combine(_testDirectory, "source");
        var destPattern = Path.Combine(_testDirectory, "dest", "{Year}", "{name}{ext}");

        // Act
        var transactionId = logger.BeginTransaction(sourceDir, destPattern, isDryRun: false);

        // Assert
        logger.TransactionLogPath.Should().NotBeNullOrEmpty();
        logger.TransactionLogPath.Should().Contain(transactionId);
        logger.TransactionLogPath.Should().EndWith(".json");
    }

    [Test]
    public void StartTransaction_GeneratesUniqueTransactionIds()
    {
        // Arrange
        var logger1 = CreateTransactionLogger();
        var logger2 = CreateTransactionLogger();
        var sourceDir = Path.Combine(_testDirectory, "source");
        var destPattern = Path.Combine(_testDirectory, "dest", "{Year}", "{name}{ext}");

        // Act
        var transactionId1 = logger1.BeginTransaction(sourceDir, destPattern, isDryRun: false);
        var transactionId2 = logger2.BeginTransaction(sourceDir, destPattern, isDryRun: false);

        // Assert
        transactionId1.Should().NotBe(transactionId2);
    }

    #endregion

    #region AddEntry Tests (LogOperation)

    [Test]
    public void AddCopyEntry_AddsEntryToLog()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);
        
        var sourcePath = Path.Combine(_testDirectory, "source", "photo.jpg");
        var destPath = Path.Combine(_testDirectory, "dest", "photo.jpg");

        // Act
        logger.LogOperation(sourcePath, destPath, OperationType.Copy, fileSize: 1024, checksum: "abc123");

        // Assert
        logger.CurrentTransaction.Should().NotBeNull();
        logger.CurrentTransaction!.Operations.Should().HaveCount(1);
        
        var entry = logger.CurrentTransaction.Operations[0];
        entry.Operation.Should().Be(OperationType.Copy);
        entry.SourcePath.Should().Be(sourcePath);
        entry.DestinationPath.Should().Be(destPath);
        entry.FileSize.Should().Be(1024);
        entry.Checksum.Should().Be("abc123");
    }

    [Test]
    public void AddMoveEntry_AddsEntryToLog()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);
        
        var sourcePath = Path.Combine(_testDirectory, "source", "video.mp4");
        var destPath = Path.Combine(_testDirectory, "dest", "video.mp4");

        // Act
        logger.LogOperation(sourcePath, destPath, OperationType.Move, fileSize: 2048);

        // Assert
        logger.CurrentTransaction.Should().NotBeNull();
        logger.CurrentTransaction!.Operations.Should().HaveCount(1);
        
        var entry = logger.CurrentTransaction.Operations[0];
        entry.Operation.Should().Be(OperationType.Move);
        entry.SourcePath.Should().Be(sourcePath);
        entry.DestinationPath.Should().Be(destPath);
        entry.FileSize.Should().Be(2048);
    }

    [Test]
    public void AddEntry_TracksSourceAndDestination()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);
        
        var sourcePath = Path.Combine(_testDirectory, "source", "documents", "report.pdf");
        var destPath = Path.Combine(_testDirectory, "dest", "2025", "report.pdf");

        // Act
        logger.LogOperation(sourcePath, destPath, OperationType.Copy, fileSize: 5000);

        // Assert
        logger.CurrentTransaction.Should().NotBeNull();
        var entry = logger.CurrentTransaction!.Operations[0];
        entry.SourcePath.Should().Be(sourcePath);
        entry.DestinationPath.Should().Be(destPath);
    }

    [Test]
    public void AddEntry_SetsTimestamp()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);
        
        var beforeTime = DateTime.UtcNow;

        // Act
        logger.LogOperation("source.jpg", "dest.jpg", OperationType.Copy, fileSize: 100);
        var afterTime = DateTime.UtcNow;

        // Assert
        var entry = logger.CurrentTransaction!.Operations[0];
        entry.Timestamp.Should().BeOnOrAfter(beforeTime);
        entry.Timestamp.Should().BeOnOrBefore(afterTime);
    }

    [Test]
    public void AddEntry_WithoutChecksum_AllowsNullChecksum()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);

        // Act
        logger.LogOperation("source.jpg", "dest.jpg", OperationType.Copy, fileSize: 100);

        // Assert
        var entry = logger.CurrentTransaction!.Operations[0];
        entry.Checksum.Should().BeNull();
    }

    [Test]
    public void AddEntry_WithNoTransaction_ThrowsInvalidOperationException()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        // Do not start a transaction

        // Act & Assert
        var action = () => logger.LogOperation("source.jpg", "dest.jpg", OperationType.Copy, fileSize: 100);
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*No transaction in progress*");
    }

    [Test]
    public void AddEntry_MultipleEntries_TracksAllEntries()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);

        // Act
        logger.LogOperation("file1.jpg", "dest1.jpg", OperationType.Copy, fileSize: 100);
        logger.LogOperation("file2.jpg", "dest2.jpg", OperationType.Move, fileSize: 200);
        logger.LogOperation("file3.jpg", "dest3.jpg", OperationType.Copy, fileSize: 300);

        // Assert
        logger.CurrentTransaction!.Operations.Should().HaveCount(3);
        logger.CurrentTransaction.Operations[0].SourcePath.Should().Be("file1.jpg");
        logger.CurrentTransaction.Operations[1].SourcePath.Should().Be("file2.jpg");
        logger.CurrentTransaction.Operations[2].SourcePath.Should().Be("file3.jpg");
    }

    #endregion

    #region LogDirectoryCreated Tests

    [Test]
    public void LogDirectoryCreated_AddsDirectoryToList()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);
        
        var directoryPath = Path.Combine(_testDirectory, "dest", "2025", "January");

        // Act
        logger.LogDirectoryCreated(directoryPath);

        // Assert
        logger.CurrentTransaction!.CreatedDirectories.Should().HaveCount(1);
        logger.CurrentTransaction.CreatedDirectories[0].Should().Be(directoryPath);
    }

    [Test]
    public void LogDirectoryCreated_WithNoTransaction_ThrowsInvalidOperationException()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        // Do not start a transaction

        // Act & Assert
        var action = () => logger.LogDirectoryCreated("some/directory");
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*No transaction in progress*");
    }

    [Test]
    public void LogDirectoryCreated_MultipleDirectories_TracksAllDirectories()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);

        // Act
        logger.LogDirectoryCreated("dir1");
        logger.LogDirectoryCreated("dir2");
        logger.LogDirectoryCreated("dir3");

        // Assert
        logger.CurrentTransaction!.CreatedDirectories.Should().HaveCount(3);
        logger.CurrentTransaction.CreatedDirectories.Should().Contain("dir1");
        logger.CurrentTransaction.CreatedDirectories.Should().Contain("dir2");
        logger.CurrentTransaction.CreatedDirectories.Should().Contain("dir3");
    }

    #endregion

    #region MarkAsCompleted Tests (CompleteTransaction)

    [Test]
    public void MarkAsCompleted_SetsCompletedStatus()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);
        logger.LogOperation("source.jpg", "dest.jpg", OperationType.Copy, fileSize: 100);

        // Act
        logger.CompleteTransaction();

        // Assert
        logger.CurrentTransaction!.Status.Should().Be(TransactionStatus.Completed);
    }

    [Test]
    public void MarkAsCompleted_SetsEndTime()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);
        
        var beforeComplete = DateTime.UtcNow;

        // Act
        logger.CompleteTransaction();
        var afterComplete = DateTime.UtcNow;

        // Assert
        logger.CurrentTransaction!.EndTime.Should().NotBeNull();
        logger.CurrentTransaction.EndTime!.Value.Should().BeOnOrAfter(beforeComplete);
        logger.CurrentTransaction.EndTime.Value.Should().BeOnOrBefore(afterComplete);
    }

    [Test]
    public void MarkAsCompleted_WithNoTransaction_ThrowsInvalidOperationException()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        // Do not start a transaction

        // Act & Assert
        var action = () => logger.CompleteTransaction();
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*No transaction in progress*");
    }

    #endregion

    #region MarkAsFailed Tests (FailTransaction)

    [Test]
    public void MarkAsFailed_SetsFailedStatus()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);

        // Act
        logger.FailTransaction("Something went wrong");

        // Assert
        logger.CurrentTransaction!.Status.Should().Be(TransactionStatus.Failed);
    }

    [Test]
    public void MarkAsFailed_SetsErrorMessage()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);
        var errorMessage = "Disk full: unable to write to destination";

        // Act
        logger.FailTransaction(errorMessage);

        // Assert
        logger.CurrentTransaction!.ErrorMessage.Should().Be(errorMessage);
    }

    [Test]
    public void MarkAsFailed_SetsEndTime()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);
        
        var beforeFail = DateTime.UtcNow;

        // Act
        logger.FailTransaction("Error occurred");
        var afterFail = DateTime.UtcNow;

        // Assert
        logger.CurrentTransaction!.EndTime.Should().NotBeNull();
        logger.CurrentTransaction.EndTime!.Value.Should().BeOnOrAfter(beforeFail);
        logger.CurrentTransaction.EndTime.Value.Should().BeOnOrBefore(afterFail);
    }

    [Test]
    public void MarkAsFailed_WithNoTransaction_ThrowsInvalidOperationException()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        // Do not start a transaction

        // Act & Assert
        var action = () => logger.FailTransaction("Error");
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*No transaction in progress*");
    }

    #endregion

    #region SaveAsync Tests

    [Test]
    public async Task SaveAsync_WritesLogToFile()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);
        logger.LogOperation("source.jpg", "dest.jpg", OperationType.Copy, fileSize: 1024, checksum: "abc123");
        logger.CompleteTransaction();

        // Act
        await logger.SaveAsync();

        // Assert
        var logPath = logger.TransactionLogPath;
        logPath.Should().NotBeNull();
        File.Exists(logPath).Should().BeTrue();
        
        var content = await File.ReadAllTextAsync(logPath!);
        content.Should().NotBeNullOrEmpty();
        content.Should().Contain(logger.CurrentTransaction!.TransactionId);
    }

    [Test]
    public async Task SaveAsync_CreatesDirectory()
    {
        // Arrange
        var customConfig = new PhotoCopyConfig
        {
            Source = Path.Combine(_testDirectory, "source"),
            Destination = Path.Combine(_testDirectory, "custom_dest", "{Year}", "{name}{ext}")
        };
        var options = Microsoft.Extensions.Options.Options.Create(customConfig);
        var logger = new TransactionLogger(_logger, options);
        
        logger.BeginTransaction(
            customConfig.Source,
            customConfig.Destination,
            isDryRun: false);

        // Act
        await logger.SaveAsync();

        // Assert
        var logPath = logger.TransactionLogPath;
        logPath.Should().NotBeNull();
        var logDir = Path.GetDirectoryName(logPath);
        Directory.Exists(logDir).Should().BeTrue();
    }

    [Test]
    public async Task SaveAsync_WritesValidJson()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);
        logger.LogOperation("source.jpg", "dest.jpg", OperationType.Copy, fileSize: 1024);
        logger.CompleteTransaction();

        // Act
        await logger.SaveAsync();

        // Assert
        var logPath = logger.TransactionLogPath;
        var content = await File.ReadAllTextAsync(logPath!);
        
        // Should be able to deserialize without exception
        var deserializedLog = JsonSerializer.Deserialize<TransactionLog>(content, _jsonOptions);
        deserializedLog.Should().NotBeNull();
        deserializedLog!.TransactionId.Should().Be(logger.CurrentTransaction!.TransactionId);
    }

    [Test]
    public async Task SaveAsync_WithNoTransaction_DoesNothing()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        // Do not start a transaction

        // Act - should not throw
        await logger.SaveAsync();

        // Assert
        logger.TransactionLogPath.Should().BeNull();
    }

    [Test]
    public async Task SaveAsync_PreservesAllOperations()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);
        
        logger.LogOperation("file1.jpg", "dest1.jpg", OperationType.Copy, fileSize: 100);
        logger.LogOperation("file2.mp4", "dest2.mp4", OperationType.Move, fileSize: 200);
        logger.LogOperation("file3.png", "dest3.png", OperationType.Copy, fileSize: 300);
        logger.CompleteTransaction();

        // Act
        await logger.SaveAsync();

        // Assert
        var logPath = logger.TransactionLogPath;
        var content = await File.ReadAllTextAsync(logPath!);
        var deserializedLog = JsonSerializer.Deserialize<TransactionLog>(content, _jsonOptions);
        
        deserializedLog!.Operations.Should().HaveCount(3);
        deserializedLog.Operations[0].FileSize.Should().Be(100);
        deserializedLog.Operations[1].FileSize.Should().Be(200);
        deserializedLog.Operations[2].FileSize.Should().Be(300);
    }

    [Test]
    public async Task SaveAsync_WithCancellationToken_Cancels()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);
        
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        var action = async () => await logger.SaveAsync(cts.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region TransactionLog Tracking Tests

    [Test]
    public void TransactionLog_TracksEntryCount()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);

        // Act
        logger.LogOperation("file1.jpg", "dest1.jpg", OperationType.Copy, fileSize: 100);
        logger.LogOperation("file2.jpg", "dest2.jpg", OperationType.Move, fileSize: 200);
        logger.LogOperation("file3.jpg", "dest3.jpg", OperationType.Copy, fileSize: 300);
        logger.LogOperation("file4.jpg", "dest4.jpg", OperationType.Move, fileSize: 400);
        logger.LogOperation("file5.jpg", "dest5.jpg", OperationType.Copy, fileSize: 500);

        // Assert
        logger.CurrentTransaction!.Operations.Count.Should().Be(5);
    }

    [Test]
    public void TransactionLog_CalculatesTotalSize()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);

        // Act
        logger.LogOperation("file1.jpg", "dest1.jpg", OperationType.Copy, fileSize: 1000);
        logger.LogOperation("file2.jpg", "dest2.jpg", OperationType.Move, fileSize: 2000);
        logger.LogOperation("file3.jpg", "dest3.jpg", OperationType.Copy, fileSize: 3000);

        // Assert - Calculate total size by summing all operation file sizes
        var totalSize = 0L;
        foreach (var op in logger.CurrentTransaction!.Operations)
        {
            totalSize += op.FileSize;
        }
        totalSize.Should().Be(6000);
    }

    [Test]
    public void TransactionLog_TracksOperationsByType()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);

        // Act
        logger.LogOperation("file1.jpg", "dest1.jpg", OperationType.Copy, fileSize: 100);
        logger.LogOperation("file2.jpg", "dest2.jpg", OperationType.Move, fileSize: 200);
        logger.LogOperation("file3.jpg", "dest3.jpg", OperationType.Copy, fileSize: 300);
        logger.LogOperation("file4.jpg", "dest4.jpg", OperationType.Move, fileSize: 400);

        // Assert
        var copyCount = 0;
        var moveCount = 0;
        foreach (var op in logger.CurrentTransaction!.Operations)
        {
            if (op.Operation == OperationType.Copy) copyCount++;
            if (op.Operation == OperationType.Move) moveCount++;
        }
        
        copyCount.Should().Be(2);
        moveCount.Should().Be(2);
    }

    #endregion

    #region Edge Cases

    [Test]
    public void CurrentTransaction_BeforeBeginTransaction_IsNull()
    {
        // Arrange
        var logger = CreateTransactionLogger();

        // Assert
        logger.CurrentTransaction.Should().BeNull();
    }

    [Test]
    public void TransactionLogPath_BeforeBeginTransaction_IsNull()
    {
        // Arrange
        var logger = CreateTransactionLogger();

        // Assert
        logger.TransactionLogPath.Should().BeNull();
    }

    [Test]
    public async Task SaveAsync_PreservesCreatedDirectories()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);
        
        logger.LogDirectoryCreated("dir1/subdir1");
        logger.LogDirectoryCreated("dir2/subdir2");
        logger.CompleteTransaction();

        // Act
        await logger.SaveAsync();

        // Assert
        var logPath = logger.TransactionLogPath;
        var content = await File.ReadAllTextAsync(logPath!);
        var deserializedLog = JsonSerializer.Deserialize<TransactionLog>(content, _jsonOptions);
        
        deserializedLog!.CreatedDirectories.Should().HaveCount(2);
        deserializedLog.CreatedDirectories.Should().Contain("dir1/subdir1");
        deserializedLog.CreatedDirectories.Should().Contain("dir2/subdir2");
    }

    [Test]
    public async Task SaveAsync_PreservesErrorMessage()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);
        
        var errorMessage = "Critical error: insufficient permissions";
        logger.FailTransaction(errorMessage);

        // Act
        await logger.SaveAsync();

        // Assert
        var logPath = logger.TransactionLogPath;
        var content = await File.ReadAllTextAsync(logPath!);
        var deserializedLog = JsonSerializer.Deserialize<TransactionLog>(content, _jsonOptions);
        
        deserializedLog!.Status.Should().Be(TransactionStatus.Failed);
        deserializedLog.ErrorMessage.Should().Be(errorMessage);
    }

    [Test]
    public void BeginTransaction_WithDryRun_SetsIsDryRunTrue()
    {
        // Arrange
        var logger = CreateTransactionLogger();

        // Act
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: true);

        // Assert
        logger.CurrentTransaction!.IsDryRun.Should().BeTrue();
    }

    [Test]
    public async Task SaveAsync_WithLargeNumberOfOperations_WritesSuccessfully()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);

        // Add 100 operations
        for (int i = 0; i < 100; i++)
        {
            logger.LogOperation(
                $"source/file{i}.jpg",
                $"dest/file{i}.jpg",
                i % 2 == 0 ? OperationType.Copy : OperationType.Move,
                fileSize: 1000 + i);
        }
        logger.CompleteTransaction();

        // Act
        await logger.SaveAsync();

        // Assert
        var logPath = logger.TransactionLogPath;
        var content = await File.ReadAllTextAsync(logPath!);
        var deserializedLog = JsonSerializer.Deserialize<TransactionLog>(content, _jsonOptions);
        
        deserializedLog!.Operations.Should().HaveCount(100);
    }

    [Test]
    public async Task SaveAsync_UsesJsonCamelCaseNaming()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(
            Path.Combine(_testDirectory, "source"),
            Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            isDryRun: false);
        logger.LogOperation("source.jpg", "dest.jpg", OperationType.Copy, fileSize: 1024);
        logger.CompleteTransaction();

        // Act
        await logger.SaveAsync();

        // Assert
        var logPath = logger.TransactionLogPath;
        var content = await File.ReadAllTextAsync(logPath!);
        
        // Verify camelCase naming is used
        content.Should().Contain("\"transactionId\"");
        content.Should().Contain("\"startTime\"");
        content.Should().Contain("\"sourceDirectory\"");
        content.Should().Contain("\"destinationPattern\"");
        content.Should().Contain("\"isDryRun\"");
    }

    #endregion

    #region Size Limit Tests

    [Test]
    public async Task IsLogFull_BeforeAnyOperations_ReturnsFalse()
    {
        // Arrange
        var logger = CreateTransactionLogger();
        logger.BeginTransaction(_testDirectory, "{year}/{month}/{name}{ext}", false);

        // Act & Assert
        await Assert.That(logger.IsLogFull).IsFalse();
    }

    [Test]
    public async Task MaxOperationsPerLog_HasExpectedValue()
    {
        // Assert - MaxOperationsPerLog should be 100000
        var value = TransactionLogger.MaxOperationsPerLog;
        await Assert.That(value).IsEqualTo(100000);
    }

    #endregion

    #region Helper Methods

    private TransactionLogger CreateTransactionLogger()
    {
        return new TransactionLogger(_logger, _options);
    }

    #endregion
}
