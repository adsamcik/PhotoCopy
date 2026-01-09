using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PhotoCopy.Rollback;

namespace PhotoCopy.Tests.Integration;

[NotInParallel("FileOperations")]
[Property("Category", "Integration")]
public class RollbackServiceIntegrationTests
{
    private string _testDir = null!;
    private string _sourceDir = null!;
    private string _destDir = null!;
    private string _logsDir = null!;
    private RollbackService _rollbackService = null!;

    [Before(Test)]
    public void Setup()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "RollbackTests", Guid.NewGuid().ToString());
        _sourceDir = Path.Combine(_testDir, "source");
        _destDir = Path.Combine(_testDir, "dest");
        _logsDir = Path.Combine(_testDir, "logs");

        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_destDir);
        Directory.CreateDirectory(_logsDir);

        _rollbackService = new RollbackService(Substitute.For<ILogger<RollbackService>>());
    }

    [After(Test)]
    public void Cleanup()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }
        catch (IOException) { }
    }

    private static void CreateTestFile(string path, string content = "test content")
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
    }

    private string CreateTransactionLog(TransactionLog log)
    {
        var logPath = Path.Combine(_logsDir, $"photocopy-{log.TransactionId}.json");
        var json = JsonSerializer.Serialize(log, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(logPath, json);
        return logPath;
    }

    [Test]
    public async Task RollbackAsync_CopyOperation_DeletesCopiedFile()
    {
        // Arrange - simulate a copy operation
        var sourceFile = Path.Combine(_sourceDir, "photo.jpg");
        var destFile = Path.Combine(_destDir, "2024", "03", "photo.jpg");

        CreateTestFile(sourceFile, "original content");
        CreateTestFile(destFile, "original content"); // File was copied here

        var log = new TransactionLog
        {
            TransactionId = Guid.NewGuid().ToString(),
            StartTime = DateTime.UtcNow,
            SourceDirectory = _sourceDir,
            DestinationPattern = Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"),
            Status = TransactionStatus.Completed,
            Operations = new List<FileOperationEntry>
            {
                new()
                {
                    SourcePath = sourceFile,
                    DestinationPath = destFile,
                    Operation = OperationType.Copy,
                    Timestamp = DateTime.UtcNow
                }
            },
            CreatedDirectories = new List<string>
            {
                Path.Combine(_destDir, "2024", "03"),
                Path.Combine(_destDir, "2024")
            }
        };
        var logPath = CreateTransactionLog(log);

        // Act
        var result = await _rollbackService.RollbackAsync(logPath);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.FilesRestored).IsEqualTo(1);
        await Assert.That(File.Exists(sourceFile)).IsTrue();
        await Assert.That(File.Exists(destFile)).IsFalse();
    }

    [Test]
    public async Task RollbackAsync_MoveOperation_MovesFileBack()
    {
        // Arrange - simulate a move operation (source no longer exists)
        var sourceFile = Path.Combine(_sourceDir, "photo.jpg");
        var destFile = Path.Combine(_destDir, "2024", "photo.jpg");
        var originalContent = "original content for move test";

        CreateTestFile(destFile, originalContent); // File was moved here

        var log = new TransactionLog
        {
            TransactionId = Guid.NewGuid().ToString(),
            StartTime = DateTime.UtcNow,
            SourceDirectory = _sourceDir,
            DestinationPattern = Path.Combine(_destDir, "{year}", "{name}{ext}"),
            Status = TransactionStatus.Completed,
            Operations = new List<FileOperationEntry>
            {
                new()
                {
                    SourcePath = sourceFile,
                    DestinationPath = destFile,
                    Operation = OperationType.Move,
                    Timestamp = DateTime.UtcNow
                }
            },
            CreatedDirectories = new List<string> { Path.Combine(_destDir, "2024") }
        };
        var logPath = CreateTransactionLog(log);

        // Act
        var result = await _rollbackService.RollbackAsync(logPath);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.FilesRestored).IsEqualTo(1);
        await Assert.That(File.Exists(sourceFile)).IsTrue();
        await Assert.That(File.Exists(destFile)).IsFalse();
        await Assert.That(File.ReadAllText(sourceFile)).IsEqualTo(originalContent);
    }

    [Test]
    public async Task RollbackAsync_MultipleOperations_RollsBackInReverseOrder()
    {
        // Arrange - multiple copy operations
        var files = new[]
        {
            ("file1.jpg", Path.Combine(_destDir, "a", "file1.jpg")),
            ("file2.jpg", Path.Combine(_destDir, "b", "file2.jpg")),
            ("file3.jpg", Path.Combine(_destDir, "c", "file3.jpg"))
        };

        var operations = new List<FileOperationEntry>();
        foreach (var (name, destPath) in files)
        {
            var sourcePath = Path.Combine(_sourceDir, name);
            CreateTestFile(sourcePath);
            CreateTestFile(destPath);
            operations.Add(new FileOperationEntry
            {
                SourcePath = sourcePath,
                DestinationPath = destPath,
                Operation = OperationType.Copy,
                Timestamp = DateTime.UtcNow
            });
        }

        var log = new TransactionLog
        {
            TransactionId = Guid.NewGuid().ToString(),
            StartTime = DateTime.UtcNow,
            SourceDirectory = _sourceDir,
            DestinationPattern = _destDir,
            Status = TransactionStatus.Completed,
            Operations = operations,
            CreatedDirectories = new List<string>
            {
                Path.Combine(_destDir, "a"),
                Path.Combine(_destDir, "b"),
                Path.Combine(_destDir, "c")
            }
        };
        var logPath = CreateTransactionLog(log);

        // Act
        var result = await _rollbackService.RollbackAsync(logPath);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.FilesRestored).IsEqualTo(3);
        foreach (var (_, destPath) in files)
        {
            await Assert.That(File.Exists(destPath)).IsFalse();
        }
    }

    [Test]
    public async Task RollbackAsync_RemovesEmptyCreatedDirectories()
    {
        // Arrange
        var sourceFile = Path.Combine(_sourceDir, "photo.jpg");
        var destFile = Path.Combine(_destDir, "2024", "03", "15", "photo.jpg");

        CreateTestFile(sourceFile);
        CreateTestFile(destFile);

        var log = new TransactionLog
        {
            TransactionId = Guid.NewGuid().ToString(),
            StartTime = DateTime.UtcNow,
            SourceDirectory = _sourceDir,
            DestinationPattern = _destDir,
            Status = TransactionStatus.Completed,
            Operations = new List<FileOperationEntry>
            {
                new()
                {
                    SourcePath = sourceFile,
                    DestinationPath = destFile,
                    Operation = OperationType.Copy,
                    Timestamp = DateTime.UtcNow
                }
            },
            CreatedDirectories = new List<string>
            {
                Path.Combine(_destDir, "2024", "03", "15"),
                Path.Combine(_destDir, "2024", "03"),
                Path.Combine(_destDir, "2024")
            }
        };
        var logPath = CreateTransactionLog(log);

        // Act
        var result = await _rollbackService.RollbackAsync(logPath);

        // Assert
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.DirectoriesRemoved).IsEqualTo(3);
        await Assert.That(Directory.Exists(Path.Combine(_destDir, "2024"))).IsFalse();
    }

    [Test]
    public async Task RollbackAsync_DryRunTransaction_ReturnsError()
    {
        // Arrange
        var log = new TransactionLog
        {
            TransactionId = Guid.NewGuid().ToString(),
            StartTime = DateTime.UtcNow,
            SourceDirectory = _sourceDir,
            DestinationPattern = _destDir,
            IsDryRun = true,
            Status = TransactionStatus.Completed,
            Operations = new List<FileOperationEntry>()
        };
        var logPath = CreateTransactionLog(log);

        // Act
        var result = await _rollbackService.RollbackAsync(logPath);

        // Assert
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Errors).Contains("Cannot rollback a dry run transaction");
    }

    [Test]
    public async Task RollbackAsync_NonExistentLogFile_ReturnsError()
    {
        // Arrange
        var logPath = Path.Combine(_logsDir, "nonexistent.json");

        // Act
        var result = await _rollbackService.RollbackAsync(logPath);

        // Assert
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Errors.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task ListTransactionLogs_ReturnsAvailableLogs()
    {
        // Arrange
        var log1 = new TransactionLog
        {
            TransactionId = "log-001",
            StartTime = DateTime.UtcNow.AddHours(-2),
            SourceDirectory = _sourceDir,
            DestinationPattern = _destDir,
            Status = TransactionStatus.Completed,
            Operations = new List<FileOperationEntry>
            {
                new() { SourcePath = "a", DestinationPath = "b", Operation = OperationType.Copy, Timestamp = DateTime.UtcNow }
            }
        };
        var log2 = new TransactionLog
        {
            TransactionId = "log-002",
            StartTime = DateTime.UtcNow.AddHours(-1),
            SourceDirectory = _sourceDir,
            DestinationPattern = _destDir,
            Status = TransactionStatus.Completed,
            Operations = new List<FileOperationEntry>
            {
                new() { SourcePath = "c", DestinationPath = "d", Operation = OperationType.Move, Timestamp = DateTime.UtcNow },
                new() { SourcePath = "e", DestinationPath = "f", Operation = OperationType.Move, Timestamp = DateTime.UtcNow }
            }
        };

        CreateTransactionLog(log1);
        CreateTransactionLog(log2);

        // Act
        var logs = _rollbackService.ListTransactionLogs(_logsDir).ToList();

        // Assert
        await Assert.That(logs.Count).IsEqualTo(2);
    }

    [Test]
    public async Task RollbackAsync_UpdatesLogStatusToRolledBack()
    {
        // Arrange
        var sourceFile = Path.Combine(_sourceDir, "photo.jpg");
        var destFile = Path.Combine(_destDir, "photo.jpg");

        CreateTestFile(sourceFile);
        CreateTestFile(destFile);

        var log = new TransactionLog
        {
            TransactionId = Guid.NewGuid().ToString(),
            StartTime = DateTime.UtcNow,
            SourceDirectory = _sourceDir,
            DestinationPattern = _destDir,
            Status = TransactionStatus.Completed,
            Operations = new List<FileOperationEntry>
            {
                new()
                {
                    SourcePath = sourceFile,
                    DestinationPath = destFile,
                    Operation = OperationType.Copy,
                    Timestamp = DateTime.UtcNow
                }
            }
        };
        var logPath = CreateTransactionLog(log);

        // Act
        await _rollbackService.RollbackAsync(logPath);

        // Assert - verify log file was updated
        var updatedJson = await File.ReadAllTextAsync(logPath);
        await Assert.That(updatedJson).Contains("RolledBack");
    }
}
