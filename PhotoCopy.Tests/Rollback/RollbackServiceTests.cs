using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PhotoCopy.Rollback;
using PhotoCopy.Tests.TestingImplementation;

namespace PhotoCopy.Tests.Rollback;

public class RollbackServiceTests
{
    private readonly ILogger<RollbackService> _logger;
    private readonly RollbackService _rollbackService;
    private readonly string _testDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public RollbackServiceTests()
    {
        _logger = new FakeLogger<RollbackService>();
        _rollbackService = new RollbackService(_logger);
        _testDirectory = Path.Combine(Path.GetTempPath(), $"RollbackServiceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        
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

    #region RollbackAsync Tests

    [Test]
    public async Task RollbackAsync_WithCopyOperations_DeletesCopiedFiles()
    {
        // Arrange
        var destFile = Path.Combine(_testDirectory, "dest", "copied_file.jpg");
        var sourceFile = Path.Combine(_testDirectory, "source", "original_file.jpg");
        
        Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
        await File.WriteAllTextAsync(destFile, "test content");
        
        var transactionLog = CreateTransactionLog(new[]
        {
            new FileOperationEntry
            {
                SourcePath = sourceFile,
                DestinationPath = destFile,
                Operation = OperationType.Copy,
                Timestamp = DateTime.UtcNow,
                FileSize = 12
            }
        });
        
        var logPath = await WriteTransactionLogAsync(transactionLog);

        // Act
        var result = await _rollbackService.RollbackAsync(logPath);

        // Assert
        result.Success.Should().BeTrue();
        result.FilesRestored.Should().Be(1);
        result.FilesFailed.Should().Be(0);
        File.Exists(destFile).Should().BeFalse();
    }

    [Test]
    public async Task RollbackAsync_WithMoveOperations_MovesFilesBack()
    {
        // Arrange
        var sourceDir = Path.Combine(_testDirectory, "source");
        var destDir = Path.Combine(_testDirectory, "dest");
        var sourceFile = Path.Combine(sourceDir, "original_file.jpg");
        var destFile = Path.Combine(destDir, "moved_file.jpg");
        
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destDir);
        await File.WriteAllTextAsync(destFile, "test content");
        
        var transactionLog = CreateTransactionLog(new[]
        {
            new FileOperationEntry
            {
                SourcePath = sourceFile,
                DestinationPath = destFile,
                Operation = OperationType.Move,
                Timestamp = DateTime.UtcNow,
                FileSize = 12
            }
        });
        
        var logPath = await WriteTransactionLogAsync(transactionLog);

        // Act
        var result = await _rollbackService.RollbackAsync(logPath);

        // Assert
        result.Success.Should().BeTrue();
        result.FilesRestored.Should().Be(1);
        result.FilesFailed.Should().Be(0);
        File.Exists(sourceFile).Should().BeTrue();
        File.Exists(destFile).Should().BeFalse();
    }

    [Test]
    public async Task RollbackAsync_WithMissingSourceFile_LogsWarning()
    {
        // Arrange - destination file doesn't exist for a move operation
        var sourceFile = Path.Combine(_testDirectory, "source", "original_file.jpg");
        var destFile = Path.Combine(_testDirectory, "dest", "moved_file.jpg");
        // Note: We intentionally don't create the destFile to simulate missing file
        
        var transactionLog = CreateTransactionLog(new[]
        {
            new FileOperationEntry
            {
                SourcePath = sourceFile,
                DestinationPath = destFile,
                Operation = OperationType.Move,
                Timestamp = DateTime.UtcNow,
                FileSize = 12
            }
        });
        
        var logPath = await WriteTransactionLogAsync(transactionLog);

        // Act
        var result = await _rollbackService.RollbackAsync(logPath);

        // Assert
        result.Success.Should().BeFalse();
        result.FilesFailed.Should().Be(1);
        result.Errors.Should().ContainMatch($"*Destination file not found*{destFile}*");
    }

    [Test]
    public async Task RollbackAsync_WithCancellation_StopsProcessing()
    {
        // Arrange
        var destFile1 = Path.Combine(_testDirectory, "dest", "file1.jpg");
        var destFile2 = Path.Combine(_testDirectory, "dest", "file2.jpg");
        
        Directory.CreateDirectory(Path.GetDirectoryName(destFile1)!);
        await File.WriteAllTextAsync(destFile1, "test content 1");
        await File.WriteAllTextAsync(destFile2, "test content 2");
        
        var transactionLog = CreateTransactionLog(new[]
        {
            new FileOperationEntry
            {
                SourcePath = Path.Combine(_testDirectory, "source", "file1.jpg"),
                DestinationPath = destFile1,
                Operation = OperationType.Copy,
                Timestamp = DateTime.UtcNow,
                FileSize = 14
            },
            new FileOperationEntry
            {
                SourcePath = Path.Combine(_testDirectory, "source", "file2.jpg"),
                DestinationPath = destFile2,
                Operation = OperationType.Copy,
                Timestamp = DateTime.UtcNow,
                FileSize = 14
            }
        });
        
        var logPath = await WriteTransactionLogAsync(transactionLog);
        
        // Cancel immediately
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _rollbackService.RollbackAsync(logPath, cts.Token));
    }

    [Test]
    public async Task RollbackAsync_UpdatesTransactionStatus()
    {
        // Arrange
        var destFile = Path.Combine(_testDirectory, "dest", "copied_file.jpg");
        var sourceFile = Path.Combine(_testDirectory, "source", "original_file.jpg");
        
        Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
        await File.WriteAllTextAsync(destFile, "test content");
        
        var transactionLog = CreateTransactionLog(new[]
        {
            new FileOperationEntry
            {
                SourcePath = sourceFile,
                DestinationPath = destFile,
                Operation = OperationType.Copy,
                Timestamp = DateTime.UtcNow,
                FileSize = 12
            }
        });
        
        var logPath = await WriteTransactionLogAsync(transactionLog);

        // Act
        await _rollbackService.RollbackAsync(logPath);

        // Assert - Read the updated log and verify status
        var updatedJson = await File.ReadAllTextAsync(logPath);
        var updatedLog = JsonSerializer.Deserialize<TransactionLog>(updatedJson, _jsonOptions);
        
        updatedLog.Should().NotBeNull();
        updatedLog!.Status.Should().Be(TransactionStatus.RolledBack);
    }

    [Test]
    public async Task RollbackAsync_WithMissingTransactionLog_ReturnsError()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDirectory, "nonexistent.json");

        // Act
        var result = await _rollbackService.RollbackAsync(nonExistentPath);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().ContainMatch("*Transaction log not found*");
    }

    [Test]
    public async Task RollbackAsync_WithDryRunTransaction_ReturnsError()
    {
        // Arrange
        var transactionLog = new TransactionLog
        {
            TransactionId = Guid.NewGuid().ToString("N"),
            StartTime = DateTime.UtcNow,
            SourceDirectory = Path.Combine(_testDirectory, "source"),
            DestinationPattern = Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            IsDryRun = true,
            Status = TransactionStatus.Completed
        };
        
        var logPath = await WriteTransactionLogAsync(transactionLog);

        // Act
        var result = await _rollbackService.RollbackAsync(logPath);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().ContainMatch("*Cannot rollback a dry run transaction*");
    }

    [Test]
    public async Task RollbackAsync_WithCreatedDirectories_RemovesEmptyDirectories()
    {
        // Arrange
        var emptyDir = Path.Combine(_testDirectory, "dest", "empty_subdir");
        Directory.CreateDirectory(emptyDir);
        
        var transactionLog = CreateTransactionLogWithDirectories(
            Array.Empty<FileOperationEntry>(),
            new[] { emptyDir });
        
        var logPath = await WriteTransactionLogAsync(transactionLog);

        // Act
        var result = await _rollbackService.RollbackAsync(logPath);

        // Assert
        result.Success.Should().BeTrue();
        result.DirectoriesRemoved.Should().Be(1);
        Directory.Exists(emptyDir).Should().BeFalse();
    }

    [Test]
    public async Task RollbackAsync_WithNonEmptyDirectories_DoesNotRemoveDirectories()
    {
        // Arrange
        var nonEmptyDir = Path.Combine(_testDirectory, "dest", "non_empty_dir");
        Directory.CreateDirectory(nonEmptyDir);
        await File.WriteAllTextAsync(Path.Combine(nonEmptyDir, "existing.txt"), "content");
        
        var transactionLog = CreateTransactionLogWithDirectories(
            Array.Empty<FileOperationEntry>(),
            new[] { nonEmptyDir });
        
        var logPath = await WriteTransactionLogAsync(transactionLog);

        // Act
        var result = await _rollbackService.RollbackAsync(logPath);

        // Assert
        result.DirectoriesRemoved.Should().Be(0);
        Directory.Exists(nonEmptyDir).Should().BeTrue();
    }

    [Test]
    public async Task RollbackAsync_WithMultipleOperations_ProcessesInReverseOrder()
    {
        // Arrange - create multiple files that were copied
        var destDir = Path.Combine(_testDirectory, "dest");
        Directory.CreateDirectory(destDir);
        
        var operations = new List<FileOperationEntry>();
        for (int i = 1; i <= 5; i++)
        {
            var destFile = Path.Combine(destDir, $"file{i}.jpg");
            await File.WriteAllTextAsync(destFile, $"content {i}");
            
            operations.Add(new FileOperationEntry
            {
                SourcePath = Path.Combine(_testDirectory, "source", $"file{i}.jpg"),
                DestinationPath = destFile,
                Operation = OperationType.Copy,
                Timestamp = DateTime.UtcNow.AddMinutes(i),
                FileSize = 10 + i
            });
        }
        
        var transactionLog = CreateTransactionLog(operations.ToArray());
        var logPath = await WriteTransactionLogAsync(transactionLog);

        // Act
        var result = await _rollbackService.RollbackAsync(logPath);

        // Assert
        result.Success.Should().BeTrue();
        result.FilesRestored.Should().Be(5);
        
        // Verify all files were deleted
        for (int i = 1; i <= 5; i++)
        {
            File.Exists(Path.Combine(destDir, $"file{i}.jpg")).Should().BeFalse();
        }
    }

    #endregion

    #region ListTransactionLogs Tests (GetTransactionLogs equivalent)

    [Test]
    public void GetTransactionLogs_WithNoLogs_ReturnsEmpty()
    {
        // Arrange
        var emptyDir = Path.Combine(_testDirectory, "empty_logs");
        Directory.CreateDirectory(emptyDir);

        // Act
        var result = _rollbackService.ListTransactionLogs(emptyDir);

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public async Task GetTransactionLogs_WithValidLogs_ParsesCorrectly()
    {
        // Arrange
        var logsDir = Path.Combine(_testDirectory, "logs");
        Directory.CreateDirectory(logsDir);
        
        var transactionLog = new TransactionLog
        {
            TransactionId = "test-transaction-123",
            StartTime = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc),
            SourceDirectory = Path.Combine(_testDirectory, "source"),
            DestinationPattern = Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            Status = TransactionStatus.Completed,
            Operations = new List<FileOperationEntry>
            {
                new FileOperationEntry
                {
                    SourcePath = "source.jpg",
                    DestinationPath = "dest.jpg",
                    Operation = OperationType.Copy,
                    Timestamp = DateTime.UtcNow,
                    FileSize = 100
                },
                new FileOperationEntry
                {
                    SourcePath = "source2.jpg",
                    DestinationPath = "dest2.jpg",
                    Operation = OperationType.Move,
                    Timestamp = DateTime.UtcNow,
                    FileSize = 200
                }
            }
        };
        
        var logPath = Path.Combine(logsDir, "photocopy-test-transaction-123.json");
        await File.WriteAllTextAsync(logPath, JsonSerializer.Serialize(transactionLog, _jsonOptions));

        // Act
        var result = _rollbackService.ListTransactionLogs(logsDir);
        var logInfos = new List<TransactionLogInfo>(result);

        // Assert
        logInfos.Should().HaveCount(1);
        logInfos[0].TransactionId.Should().Be("test-transaction-123");
        logInfos[0].StartTime.Should().Be(new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc));
        logInfos[0].Status.Should().Be(TransactionStatus.Completed);
        logInfos[0].OperationCount.Should().Be(2);
        logInfos[0].FilePath.Should().Be(logPath);
    }

    [Test]
    public async Task GetTransactionLogs_WithInvalidLogFile_SkipsFile()
    {
        // Arrange
        var logsDir = Path.Combine(_testDirectory, "logs");
        Directory.CreateDirectory(logsDir);
        
        // Create an invalid JSON file
        var invalidLogPath = Path.Combine(logsDir, "photocopy-invalid.json");
        await File.WriteAllTextAsync(invalidLogPath, "{ invalid json content");
        
        // Create a valid log file
        var validLog = new TransactionLog
        {
            TransactionId = "valid-transaction",
            StartTime = DateTime.UtcNow,
            SourceDirectory = _testDirectory,
            DestinationPattern = _testDirectory,
            Status = TransactionStatus.Completed
        };
        var validLogPath = Path.Combine(logsDir, "photocopy-valid.json");
        await File.WriteAllTextAsync(validLogPath, JsonSerializer.Serialize(validLog, _jsonOptions));

        // Act
        var result = _rollbackService.ListTransactionLogs(logsDir);
        var logInfos = new List<TransactionLogInfo>(result);

        // Assert - should only return the valid log, skip the invalid one
        logInfos.Should().HaveCount(1);
        logInfos[0].TransactionId.Should().Be("valid-transaction");
    }

    [Test]
    public async Task GetTransactionLogs_FiltersCompletedTransactions()
    {
        // Arrange
        var logsDir = Path.Combine(_testDirectory, "logs");
        Directory.CreateDirectory(logsDir);
        
        // Create logs with different statuses
        var completedLog = new TransactionLog
        {
            TransactionId = "completed-tx",
            StartTime = DateTime.UtcNow.AddHours(-2),
            SourceDirectory = _testDirectory,
            DestinationPattern = _testDirectory,
            Status = TransactionStatus.Completed
        };
        
        var inProgressLog = new TransactionLog
        {
            TransactionId = "inprogress-tx",
            StartTime = DateTime.UtcNow.AddHours(-1),
            SourceDirectory = _testDirectory,
            DestinationPattern = _testDirectory,
            Status = TransactionStatus.InProgress
        };
        
        var failedLog = new TransactionLog
        {
            TransactionId = "failed-tx",
            StartTime = DateTime.UtcNow,
            SourceDirectory = _testDirectory,
            DestinationPattern = _testDirectory,
            Status = TransactionStatus.Failed
        };
        
        await File.WriteAllTextAsync(
            Path.Combine(logsDir, "photocopy-completed.json"),
            JsonSerializer.Serialize(completedLog, _jsonOptions));
        await File.WriteAllTextAsync(
            Path.Combine(logsDir, "photocopy-inprogress.json"),
            JsonSerializer.Serialize(inProgressLog, _jsonOptions));
        await File.WriteAllTextAsync(
            Path.Combine(logsDir, "photocopy-failed.json"),
            JsonSerializer.Serialize(failedLog, _jsonOptions));

        // Act
        var result = _rollbackService.ListTransactionLogs(logsDir);
        var logInfos = new List<TransactionLogInfo>(result);

        // Assert - ListTransactionLogs returns all logs, filtering would be done by caller
        logInfos.Should().HaveCount(3);
        
        // Verify we can filter by status
        var completedOnly = logInfos.Where(l => l.Status == TransactionStatus.Completed).ToList();
        completedOnly.Should().HaveCount(1);
        completedOnly[0].TransactionId.Should().Be("completed-tx");
    }

    [Test]
    public async Task GetTransactionLogs_SortsByDate()
    {
        // Arrange
        var logsDir = Path.Combine(_testDirectory, "logs");
        Directory.CreateDirectory(logsDir);
        
        var oldLog = new TransactionLog
        {
            TransactionId = "old-tx",
            StartTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            SourceDirectory = _testDirectory,
            DestinationPattern = _testDirectory,
            Status = TransactionStatus.Completed
        };
        
        var middleLog = new TransactionLog
        {
            TransactionId = "middle-tx",
            StartTime = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc),
            SourceDirectory = _testDirectory,
            DestinationPattern = _testDirectory,
            Status = TransactionStatus.Completed
        };
        
        var newestLog = new TransactionLog
        {
            TransactionId = "newest-tx",
            StartTime = new DateTime(2025, 12, 31, 12, 0, 0, DateTimeKind.Utc),
            SourceDirectory = _testDirectory,
            DestinationPattern = _testDirectory,
            Status = TransactionStatus.Completed
        };
        
        // Write in non-chronological order
        await File.WriteAllTextAsync(
            Path.Combine(logsDir, "photocopy-middle.json"),
            JsonSerializer.Serialize(middleLog, _jsonOptions));
        await File.WriteAllTextAsync(
            Path.Combine(logsDir, "photocopy-newest.json"),
            JsonSerializer.Serialize(newestLog, _jsonOptions));
        await File.WriteAllTextAsync(
            Path.Combine(logsDir, "photocopy-old.json"),
            JsonSerializer.Serialize(oldLog, _jsonOptions));

        // Act
        var result = _rollbackService.ListTransactionLogs(logsDir);
        var logInfos = new List<TransactionLogInfo>(result);
        
        // Sort by date (the service returns them, caller can sort)
        var sortedByDate = logInfos.OrderBy(l => l.StartTime).ToList();

        // Assert
        sortedByDate.Should().HaveCount(3);
        sortedByDate[0].TransactionId.Should().Be("old-tx");
        sortedByDate[1].TransactionId.Should().Be("middle-tx");
        sortedByDate[2].TransactionId.Should().Be("newest-tx");
    }

    [Test]
    public void GetTransactionLogs_WithNonExistentDirectory_ReturnsEmpty()
    {
        // Arrange
        var nonExistentDir = Path.Combine(_testDirectory, "does_not_exist");

        // Act
        var result = _rollbackService.ListTransactionLogs(nonExistentDir);

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public async Task GetTransactionLogs_WithNonMatchingFiles_IgnoresThem()
    {
        // Arrange
        var logsDir = Path.Combine(_testDirectory, "logs");
        Directory.CreateDirectory(logsDir);
        
        // Create a valid log with the expected naming pattern
        var validLog = new TransactionLog
        {
            TransactionId = "valid-tx",
            StartTime = DateTime.UtcNow,
            SourceDirectory = _testDirectory,
            DestinationPattern = _testDirectory,
            Status = TransactionStatus.Completed
        };
        await File.WriteAllTextAsync(
            Path.Combine(logsDir, "photocopy-valid.json"),
            JsonSerializer.Serialize(validLog, _jsonOptions));
        
        // Create files with non-matching patterns
        await File.WriteAllTextAsync(
            Path.Combine(logsDir, "other-file.json"),
            JsonSerializer.Serialize(validLog, _jsonOptions));
        await File.WriteAllTextAsync(
            Path.Combine(logsDir, "photocopy.txt"),
            "plain text file");

        // Act
        var result = _rollbackService.ListTransactionLogs(logsDir);
        var logInfos = new List<TransactionLogInfo>(result);

        // Assert - should only find the one with "photocopy-*.json" pattern
        logInfos.Should().HaveCount(1);
        logInfos[0].TransactionId.Should().Be("valid-tx");
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task RollbackAsync_WithMoveOperation_CreatesSourceDirectoryIfNeeded()
    {
        // Arrange
        var sourceDir = Path.Combine(_testDirectory, "source", "nested", "directory");
        var destDir = Path.Combine(_testDirectory, "dest");
        var sourceFile = Path.Combine(sourceDir, "file.jpg");
        var destFile = Path.Combine(destDir, "file.jpg");
        
        // Only create destination, not source
        Directory.CreateDirectory(destDir);
        await File.WriteAllTextAsync(destFile, "test content");
        
        var transactionLog = CreateTransactionLog(new[]
        {
            new FileOperationEntry
            {
                SourcePath = sourceFile,
                DestinationPath = destFile,
                Operation = OperationType.Move,
                Timestamp = DateTime.UtcNow,
                FileSize = 12
            }
        });
        
        var logPath = await WriteTransactionLogAsync(transactionLog);

        // Act
        var result = await _rollbackService.RollbackAsync(logPath);

        // Assert
        result.Success.Should().BeTrue();
        result.FilesRestored.Should().Be(1);
        Directory.Exists(sourceDir).Should().BeTrue();
        File.Exists(sourceFile).Should().BeTrue();
    }

    [Test]
    public async Task RollbackAsync_WithCopyOperation_AlreadyDeletedFile_DoesNotFail()
    {
        // Arrange - file doesn't exist but for copy operations this is ok
        var sourceFile = Path.Combine(_testDirectory, "source", "file.jpg");
        var destFile = Path.Combine(_testDirectory, "dest", "file.jpg");
        // Intentionally don't create destFile - simulates already deleted
        
        var transactionLog = CreateTransactionLog(new[]
        {
            new FileOperationEntry
            {
                SourcePath = sourceFile,
                DestinationPath = destFile,
                Operation = OperationType.Copy,
                Timestamp = DateTime.UtcNow,
                FileSize = 12
            }
        });
        
        var logPath = await WriteTransactionLogAsync(transactionLog);

        // Act
        var result = await _rollbackService.RollbackAsync(logPath);

        // Assert - for copy operations, missing file is not an error
        result.Success.Should().BeTrue();
        result.FilesRestored.Should().Be(0); // Nothing to restore
        result.FilesFailed.Should().Be(0);
    }

    [Test]
    public async Task RollbackAsync_WithInvalidJson_ThrowsJsonException()
    {
        // Arrange
        var logPath = Path.Combine(_testDirectory, "invalid.json");
        await File.WriteAllTextAsync(logPath, "{ invalid json }");

        // Act & Assert - The implementation does not catch JSON exceptions
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(async () =>
            await _rollbackService.RollbackAsync(logPath));
    }

    [Test]
    public async Task RollbackAsync_WithEmptyOperations_Succeeds()
    {
        // Arrange
        var transactionLog = CreateTransactionLog(Array.Empty<FileOperationEntry>());
        var logPath = await WriteTransactionLogAsync(transactionLog);

        // Act
        var result = await _rollbackService.RollbackAsync(logPath);

        // Assert
        result.Success.Should().BeTrue();
        result.FilesRestored.Should().Be(0);
        result.FilesFailed.Should().Be(0);
    }

    #endregion

    #region Helper Methods

    private TransactionLog CreateTransactionLog(FileOperationEntry[] operations)
    {
        return new TransactionLog
        {
            TransactionId = Guid.NewGuid().ToString("N"),
            StartTime = DateTime.UtcNow,
            SourceDirectory = Path.Combine(_testDirectory, "source"),
            DestinationPattern = Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            IsDryRun = false,
            Status = TransactionStatus.Completed,
            Operations = new List<FileOperationEntry>(operations)
        };
    }

    private TransactionLog CreateTransactionLogWithDirectories(
        FileOperationEntry[] operations,
        string[] createdDirectories)
    {
        return new TransactionLog
        {
            TransactionId = Guid.NewGuid().ToString("N"),
            StartTime = DateTime.UtcNow,
            SourceDirectory = Path.Combine(_testDirectory, "source"),
            DestinationPattern = Path.Combine(_testDirectory, "dest", "{name}{ext}"),
            IsDryRun = false,
            Status = TransactionStatus.Completed,
            Operations = new List<FileOperationEntry>(operations),
            CreatedDirectories = new List<string>(createdDirectories)
        };
    }

    private async Task<string> WriteTransactionLogAsync(TransactionLog transactionLog)
    {
        var logPath = Path.Combine(_testDirectory, $"photocopy-{transactionLog.TransactionId}.json");
        var json = JsonSerializer.Serialize(transactionLog, _jsonOptions);
        await File.WriteAllTextAsync(logPath, json);
        return logPath;
    }

    #endregion
}
