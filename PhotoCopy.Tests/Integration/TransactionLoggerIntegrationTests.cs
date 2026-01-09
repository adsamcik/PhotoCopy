using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Configuration;
using PhotoCopy.Rollback;

namespace PhotoCopy.Tests.Integration;

[Property("Category", "Integration")]
public class TransactionLoggerIntegrationTests
{
    private readonly string _baseTestDirectory;
    private readonly ILogger<TransactionLogger> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public TransactionLoggerIntegrationTests()
    {
        _baseTestDirectory = Path.Combine(Path.GetTempPath(), "TransactionLoggerIntegrationTests");
        _logger = Substitute.For<ILogger<TransactionLogger>>();
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Create base test directory if it doesn't exist
        if (!Directory.Exists(_baseTestDirectory))
        {
            Directory.CreateDirectory(_baseTestDirectory);
        }
    }

    private string CreateUniqueTestDirectory()
    {
        var uniquePath = Path.Combine(_baseTestDirectory, Guid.NewGuid().ToString());
        Directory.CreateDirectory(uniquePath);
        return uniquePath;
    }

    private void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                // Give Windows a moment to release any locks
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Directory.Delete(path, true);
            }
        }
        catch (IOException)
        {
            // If we can't delete now, don't fail the test
            // The directory will be cleaned up on the next test run
        }
    }

    private TransactionLogger CreateTransactionLogger(string destinationDirectory)
    {
        var config = new PhotoCopyConfig
        {
            Source = Path.Combine(_baseTestDirectory, "source"),
            Destination = destinationDirectory + @"\{Year}\{Month}",
            DryRun = false
        };
        var options = Microsoft.Extensions.Options.Options.Create(config);
        return new TransactionLogger(_logger, options);
    }

    private TransactionLogger CreateTransactionLoggerWithSourceFallback()
    {
        var config = new PhotoCopyConfig
        {
            Source = _baseTestDirectory,
            Destination = "", // Empty destination to force source fallback
            DryRun = false
        };
        var options = Microsoft.Extensions.Options.Options.Create(config);
        return new TransactionLogger(_logger, options);
    }

    #region BeginTransaction Tests

    [Test]
    public async Task BeginTransaction_CreatesTransactionWithUniqueId()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            var destPattern = testDirectory + @"\{Year}";
            Directory.CreateDirectory(sourceDir);

            // Act
            var transactionId = logger.BeginTransaction(sourceDir, destPattern, isDryRun: false);

            // Assert
            await Assert.That(transactionId).IsNotNull();
            await Assert.That(transactionId).IsNotEmpty();
            await Assert.That(logger.CurrentTransaction).IsNotNull();
            await Assert.That(logger.CurrentTransaction!.TransactionId).IsEqualTo(transactionId);
            await Assert.That(logger.CurrentTransaction.Status).IsEqualTo(TransactionStatus.InProgress);
            await Assert.That(logger.TransactionLogPath).IsNotNull();
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task BeginTransaction_CreatesLogDirectory()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);

            // Act
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);

            // Assert
            var logDir = Path.GetDirectoryName(logger.TransactionLogPath);
            await Assert.That(Directory.Exists(logDir)).IsTrue();
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task BeginTransaction_GeneratesUniqueIdsForMultipleTransactions()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger1 = CreateTransactionLogger(testDirectory);
            var logger2 = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);

            // Act
            var id1 = logger1.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);
            await Task.Delay(10); // Small delay to ensure different timestamp
            var id2 = logger2.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);

            // Assert
            await Assert.That(id1).IsNotEqualTo(id2);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task BeginTransaction_SetsDryRunFlag()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);

            // Act
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: true);

            // Assert
            await Assert.That(logger.CurrentTransaction!.IsDryRun).IsTrue();
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region LogOperation Tests

    [Test]
    public async Task LogOperation_AddsOperationToTransaction()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);

            // Act
            logger.LogOperation(
                sourcePath: @"C:\source\photo.jpg",
                destinationPath: @"C:\dest\2024\photo.jpg",
                operation: OperationType.Copy,
                fileSize: 12345,
                checksum: "abc123"
            );

            // Assert
            await Assert.That(logger.CurrentTransaction!.Operations.Count).IsEqualTo(1);
            var op = logger.CurrentTransaction.Operations[0];
            await Assert.That(op.SourcePath).IsEqualTo(@"C:\source\photo.jpg");
            await Assert.That(op.DestinationPath).IsEqualTo(@"C:\dest\2024\photo.jpg");
            await Assert.That(op.Operation).IsEqualTo(OperationType.Copy);
            await Assert.That(op.FileSize).IsEqualTo(12345);
            await Assert.That(op.Checksum).IsEqualTo("abc123");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task LogOperation_AddsMultipleOperations()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);

            // Act
            logger.LogOperation(@"C:\source\photo1.jpg", @"C:\dest\photo1.jpg", OperationType.Copy, 1000);
            logger.LogOperation(@"C:\source\photo2.jpg", @"C:\dest\photo2.jpg", OperationType.Move, 2000);
            logger.LogOperation(@"C:\source\photo3.jpg", @"C:\dest\photo3.jpg", OperationType.Copy, 3000);

            // Assert
            await Assert.That(logger.CurrentTransaction!.Operations.Count).IsEqualTo(3);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public void LogOperation_WithoutTransaction_ThrowsException()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                logger.LogOperation(@"C:\source\photo.jpg", @"C:\dest\photo.jpg", OperationType.Copy, 1000));
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region LogDirectoryCreated Tests

    [Test]
    public async Task LogDirectoryCreated_AddsDirectoryToTransaction()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);

            // Act
            logger.LogDirectoryCreated(@"C:\dest\2024");
            logger.LogDirectoryCreated(@"C:\dest\2024\January");

            // Assert
            await Assert.That(logger.CurrentTransaction!.CreatedDirectories.Count).IsEqualTo(2);
            await Assert.That(logger.CurrentTransaction.CreatedDirectories[0]).IsEqualTo(@"C:\dest\2024");
            await Assert.That(logger.CurrentTransaction.CreatedDirectories[1]).IsEqualTo(@"C:\dest\2024\January");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public void LogDirectoryCreated_WithoutTransaction_ThrowsException()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                logger.LogDirectoryCreated(@"C:\dest\2024"));
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region CompleteTransaction Tests

    [Test]
    public async Task CompleteTransaction_SetsStatusToCompleted()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);
            logger.LogOperation(@"C:\source\photo.jpg", @"C:\dest\photo.jpg", OperationType.Copy, 1000);

            // Act
            logger.CompleteTransaction();

            // Assert
            await Assert.That(logger.CurrentTransaction!.Status).IsEqualTo(TransactionStatus.Completed);
            await Assert.That(logger.CurrentTransaction.EndTime).IsNotNull();
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public void CompleteTransaction_WithoutTransaction_ThrowsException()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => logger.CompleteTransaction());
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region FailTransaction Tests

    [Test]
    public async Task FailTransaction_SetsStatusToFailed()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);
            logger.LogOperation(@"C:\source\photo.jpg", @"C:\dest\photo.jpg", OperationType.Copy, 1000);

            // Act
            logger.FailTransaction("Test error message");

            // Assert
            await Assert.That(logger.CurrentTransaction!.Status).IsEqualTo(TransactionStatus.Failed);
            await Assert.That(logger.CurrentTransaction.EndTime).IsNotNull();
            await Assert.That(logger.CurrentTransaction.ErrorMessage).IsEqualTo("Test error message");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public void FailTransaction_WithoutTransaction_ThrowsException()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => logger.FailTransaction("Error"));
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region SaveAsync Tests

    [Test]
    public async Task SaveAsync_WritesTransactionLogToDisk()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);
            logger.LogOperation(@"C:\source\photo.jpg", @"C:\dest\photo.jpg", OperationType.Copy, 1000, "checksum123");
            logger.CompleteTransaction();

            // Act
            await logger.SaveAsync();

            // Assert
            await Assert.That(File.Exists(logger.TransactionLogPath)).IsTrue();
            var fileContent = await File.ReadAllTextAsync(logger.TransactionLogPath!);
            await Assert.That(fileContent).IsNotEmpty();
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task SaveAsync_CreatesValidJsonFile()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);
            logger.LogOperation(@"C:\source\photo.jpg", @"C:\dest\photo.jpg", OperationType.Copy, 1000);
            logger.CompleteTransaction();

            // Act
            await logger.SaveAsync();

            // Assert
            var fileContent = await File.ReadAllTextAsync(logger.TransactionLogPath!);
            Exception? deserializedException = null;
            try
            {
                JsonDocument.Parse(fileContent);
            }
            catch (Exception ex)
            {
                deserializedException = ex;
            }
            await Assert.That(deserializedException).IsNull();
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task SaveAsync_WithCancellationToken_CanBeCancelled()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await logger.SaveAsync(cts.Token));
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task SaveAsync_WithoutTransaction_DoesNotThrow()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);

            // Act
            Exception? exception = null;
            try
            {
                await logger.SaveAsync();
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            // Assert
            await Assert.That(exception).IsNull();
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task SaveAsync_MultipleSaves_OverwritesPreviousFile()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);
            logger.LogOperation(@"C:\source\photo1.jpg", @"C:\dest\photo1.jpg", OperationType.Copy, 1000);

            // Act
            await logger.SaveAsync();
            var firstSaveContent = await File.ReadAllTextAsync(logger.TransactionLogPath!);

            logger.LogOperation(@"C:\source\photo2.jpg", @"C:\dest\photo2.jpg", OperationType.Copy, 2000);
            await logger.SaveAsync();
            var secondSaveContent = await File.ReadAllTextAsync(logger.TransactionLogPath!);

            // Assert
            await Assert.That(secondSaveContent.Length).IsGreaterThan(firstSaveContent.Length);
            await Assert.That(secondSaveContent).Contains("photo2.jpg");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region JSON Serialization/Deserialization Tests

    [Test]
    public async Task SaveAsync_SerializesAllTransactionProperties()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            var transactionId = logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: true);
            logger.LogOperation(@"C:\source\photo.jpg", @"C:\dest\photo.jpg", OperationType.Copy, 12345, "sha256checksum");
            logger.LogDirectoryCreated(@"C:\dest\2024");
            logger.CompleteTransaction();

            // Act
            await logger.SaveAsync();

            // Assert
            var fileContent = await File.ReadAllTextAsync(logger.TransactionLogPath!);
            var document = JsonDocument.Parse(fileContent);
            var root = document.RootElement;

            await Assert.That(root.GetProperty("transactionId").GetString()).IsEqualTo(transactionId);
            await Assert.That(root.GetProperty("sourceDirectory").GetString()).IsEqualTo(sourceDir);
            await Assert.That(root.GetProperty("isDryRun").GetBoolean()).IsTrue();
            await Assert.That(root.GetProperty("status").GetString()).IsEqualTo("Completed");
            await Assert.That(root.GetProperty("operations").GetArrayLength()).IsEqualTo(1);
            await Assert.That(root.GetProperty("createdDirectories").GetArrayLength()).IsEqualTo(1);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task SaveAsync_SerializesOperationEntryCorrectly()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);
            logger.LogOperation(
                sourcePath: @"C:\photos\vacation\IMG_001.jpg",
                destinationPath: @"D:\backup\2024\January\IMG_001.jpg",
                operation: OperationType.Move,
                fileSize: 5242880,
                checksum: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
            );
            logger.CompleteTransaction();

            // Act
            await logger.SaveAsync();

            // Assert
            var fileContent = await File.ReadAllTextAsync(logger.TransactionLogPath!);
            var document = JsonDocument.Parse(fileContent);
            var operation = document.RootElement.GetProperty("operations")[0];

            await Assert.That(operation.GetProperty("sourcePath").GetString()).IsEqualTo(@"C:\photos\vacation\IMG_001.jpg");
            await Assert.That(operation.GetProperty("destinationPath").GetString()).IsEqualTo(@"D:\backup\2024\January\IMG_001.jpg");
            await Assert.That(operation.GetProperty("operation").GetString()).IsEqualTo("Move");
            await Assert.That(operation.GetProperty("fileSize").GetInt64()).IsEqualTo(5242880);
            await Assert.That(operation.GetProperty("checksum").GetString()).IsEqualTo("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task SaveAsync_DeserializesBackToTransactionLog()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            var transactionId = logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);
            logger.LogOperation(@"C:\source\photo1.jpg", @"C:\dest\photo1.jpg", OperationType.Copy, 1000, "checksum1");
            logger.LogOperation(@"C:\source\photo2.jpg", @"C:\dest\photo2.jpg", OperationType.Move, 2000, "checksum2");
            logger.LogDirectoryCreated(@"C:\dest\2024");
            logger.LogDirectoryCreated(@"C:\dest\2024\Photos");
            logger.CompleteTransaction();

            // Act
            await logger.SaveAsync();
            var fileContent = await File.ReadAllTextAsync(logger.TransactionLogPath!);
            var deserializedLog = JsonSerializer.Deserialize<TransactionLog>(fileContent, _jsonOptions);

            // Assert
            await Assert.That(deserializedLog).IsNotNull();
            await Assert.That(deserializedLog!.TransactionId).IsEqualTo(transactionId);
            await Assert.That(deserializedLog.SourceDirectory).IsEqualTo(sourceDir);
            await Assert.That(deserializedLog.Status).IsEqualTo(TransactionStatus.Completed);
            await Assert.That(deserializedLog.Operations.Count).IsEqualTo(2);
            await Assert.That(deserializedLog.CreatedDirectories.Count).IsEqualTo(2);

            // Verify first operation
            await Assert.That(deserializedLog.Operations[0].SourcePath).IsEqualTo(@"C:\source\photo1.jpg");
            await Assert.That(deserializedLog.Operations[0].Operation).IsEqualTo(OperationType.Copy);
            await Assert.That(deserializedLog.Operations[0].FileSize).IsEqualTo(1000);

            // Verify second operation
            await Assert.That(deserializedLog.Operations[1].SourcePath).IsEqualTo(@"C:\source\photo2.jpg");
            await Assert.That(deserializedLog.Operations[1].Operation).IsEqualTo(OperationType.Move);
            await Assert.That(deserializedLog.Operations[1].FileSize).IsEqualTo(2000);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task SaveAsync_PreservesFailedStatus()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);
            logger.LogOperation(@"C:\source\photo.jpg", @"C:\dest\photo.jpg", OperationType.Copy, 1000);
            logger.FailTransaction("Disk full error");

            // Act
            await logger.SaveAsync();
            var fileContent = await File.ReadAllTextAsync(logger.TransactionLogPath!);
            var deserializedLog = JsonSerializer.Deserialize<TransactionLog>(fileContent, _jsonOptions);

            // Assert
            await Assert.That(deserializedLog!.Status).IsEqualTo(TransactionStatus.Failed);
            await Assert.That(deserializedLog.ErrorMessage).IsEqualTo("Disk full error");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task SaveAsync_SerializesTimestampsCorrectly()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            var beforeStart = DateTime.UtcNow;
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);
            logger.LogOperation(@"C:\source\photo.jpg", @"C:\dest\photo.jpg", OperationType.Copy, 1000);
            logger.CompleteTransaction();
            var afterComplete = DateTime.UtcNow;

            // Act
            await logger.SaveAsync();
            var fileContent = await File.ReadAllTextAsync(logger.TransactionLogPath!);
            var deserializedLog = JsonSerializer.Deserialize<TransactionLog>(fileContent, _jsonOptions);

            // Assert
            await Assert.That(deserializedLog!.StartTime).IsGreaterThanOrEqualTo(beforeStart);
            await Assert.That(deserializedLog.StartTime).IsLessThanOrEqualTo(afterComplete);
            await Assert.That(deserializedLog.EndTime).IsNotNull();
            await Assert.That(deserializedLog.EndTime!.Value).IsGreaterThanOrEqualTo(beforeStart);
            await Assert.That(deserializedLog.EndTime!.Value).IsLessThanOrEqualTo(afterComplete);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region Log File Path Tests

    [Test]
    public async Task TransactionLogPath_ContainsTransactionId()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);

            // Act
            var transactionId = logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);

            // Assert
            await Assert.That(logger.TransactionLogPath).Contains(transactionId);
            await Assert.That(logger.TransactionLogPath).EndsWith(".json");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task TransactionLogPath_IsInPhotocopyLogsSubdirectory()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);

            // Act
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);

            // Assert
            await Assert.That(logger.TransactionLogPath).Contains(".photocopy-logs");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region Complete Workflow Tests

    [Test]
    public async Task CompleteWorkflow_BeginLogSaveComplete_CreatesValidLogFile()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            var destDir = Path.Combine(testDirectory, "destination");
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(destDir);

            // Create some test files
            var testFile1 = Path.Combine(sourceDir, "photo1.jpg");
            var testFile2 = Path.Combine(sourceDir, "photo2.jpg");
            await File.WriteAllTextAsync(testFile1, "Test content 1");
            await File.WriteAllTextAsync(testFile2, "Test content 2");

            // Act - Complete workflow
            var transactionId = logger.BeginTransaction(sourceDir, destDir + @"\{Year}", isDryRun: false);
            logger.LogDirectoryCreated(Path.Combine(destDir, "2024"));
            logger.LogOperation(testFile1, Path.Combine(destDir, "2024", "photo1.jpg"), OperationType.Copy, 14, "hash1");
            logger.LogOperation(testFile2, Path.Combine(destDir, "2024", "photo2.jpg"), OperationType.Copy, 14, "hash2");
            logger.CompleteTransaction();
            await logger.SaveAsync();

            // Assert
            await Assert.That(File.Exists(logger.TransactionLogPath)).IsTrue();

            var fileContent = await File.ReadAllTextAsync(logger.TransactionLogPath!);
            var deserializedLog = JsonSerializer.Deserialize<TransactionLog>(fileContent, _jsonOptions);

            await Assert.That(deserializedLog!.TransactionId).IsEqualTo(transactionId);
            await Assert.That(deserializedLog.Status).IsEqualTo(TransactionStatus.Completed);
            await Assert.That(deserializedLog.Operations.Count).IsEqualTo(2);
            await Assert.That(deserializedLog.CreatedDirectories.Count).IsEqualTo(1);
            await Assert.That(deserializedLog.IsDryRun).IsFalse();
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task CompleteWorkflow_WithFailure_RecordsError()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);

            // Act - Workflow with failure
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);
            logger.LogOperation(@"C:\source\photo1.jpg", @"C:\dest\photo1.jpg", OperationType.Copy, 1000);
            logger.FailTransaction("Access denied: C:\\dest\\photo2.jpg");
            await logger.SaveAsync();

            // Assert
            var fileContent = await File.ReadAllTextAsync(logger.TransactionLogPath!);
            var deserializedLog = JsonSerializer.Deserialize<TransactionLog>(fileContent, _jsonOptions);

            await Assert.That(deserializedLog!.Status).IsEqualTo(TransactionStatus.Failed);
            await Assert.That(deserializedLog.ErrorMessage).IsEqualTo("Access denied: C:\\dest\\photo2.jpg");
            await Assert.That(deserializedLog.Operations.Count).IsEqualTo(1);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task CompleteWorkflow_DryRun_CreatesLogWithDryRunFlag()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);

            // Act - Dry run workflow
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: true);
            logger.LogOperation(@"C:\source\photo.jpg", @"C:\dest\photo.jpg", OperationType.Copy, 1000);
            logger.CompleteTransaction();
            await logger.SaveAsync();

            // Assert
            var fileContent = await File.ReadAllTextAsync(logger.TransactionLogPath!);
            var deserializedLog = JsonSerializer.Deserialize<TransactionLog>(fileContent, _jsonOptions);

            await Assert.That(deserializedLog!.IsDryRun).IsTrue();
            await Assert.That(deserializedLog.Status).IsEqualTo(TransactionStatus.Completed);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task CompleteWorkflow_LargeNumberOfOperations_SerializesCorrectly()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);

            // Act - Log many operations
            const int operationCount = 100;
            for (var i = 0; i < operationCount; i++)
            {
                logger.LogOperation(
                    $@"C:\source\photo{i:D4}.jpg",
                    $@"C:\dest\2024\photo{i:D4}.jpg",
                    i % 2 == 0 ? OperationType.Copy : OperationType.Move,
                    1000 + i,
                    $"checksum{i:D4}"
                );
            }
            logger.CompleteTransaction();
            await logger.SaveAsync();

            // Assert
            var fileContent = await File.ReadAllTextAsync(logger.TransactionLogPath!);
            var deserializedLog = JsonSerializer.Deserialize<TransactionLog>(fileContent, _jsonOptions);

            await Assert.That(deserializedLog!.Operations.Count).IsEqualTo(operationCount);
            await Assert.That(deserializedLog.Operations[0].SourcePath).IsEqualTo(@"C:\source\photo0000.jpg");
            await Assert.That(deserializedLog.Operations[99].SourcePath).IsEqualTo(@"C:\source\photo0099.jpg");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion

    #region Edge Cases Tests

    [Test]
    public async Task SaveAsync_WithSpecialCharactersInPaths_SerializesCorrectly()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);

            // Act - Log paths with special characters
            logger.LogOperation(
                @"C:\source\Photos & Images\John's Photo (2024).jpg",
                @"C:\dest\2024\Photos & Images\John's Photo (2024).jpg",
                OperationType.Copy,
                1000
            );
            logger.CompleteTransaction();
            await logger.SaveAsync();

            // Assert
            var fileContent = await File.ReadAllTextAsync(logger.TransactionLogPath!);
            var deserializedLog = JsonSerializer.Deserialize<TransactionLog>(fileContent, _jsonOptions);

            await Assert.That(deserializedLog!.Operations[0].SourcePath).Contains("John's Photo");
            await Assert.That(deserializedLog.Operations[0].SourcePath).Contains("&");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task SaveAsync_WithUnicodePaths_SerializesCorrectly()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);

            // Act - Log paths with unicode characters
            logger.LogOperation(
                @"C:\source\Фотографии\日本語ファイル名.jpg",
                @"C:\dest\2024\Фотографии\日本語ファイル名.jpg",
                OperationType.Copy,
                1000
            );
            logger.CompleteTransaction();
            await logger.SaveAsync();

            // Assert
            var fileContent = await File.ReadAllTextAsync(logger.TransactionLogPath!);
            var deserializedLog = JsonSerializer.Deserialize<TransactionLog>(fileContent, _jsonOptions);

            await Assert.That(deserializedLog!.Operations[0].SourcePath).Contains("Фотографии");
            await Assert.That(deserializedLog.Operations[0].SourcePath).Contains("日本語");
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task SaveAsync_WithEmptyOperations_SerializesCorrectly()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);
            // No operations logged
            logger.CompleteTransaction();

            // Act
            await logger.SaveAsync();

            // Assert
            var fileContent = await File.ReadAllTextAsync(logger.TransactionLogPath!);
            var deserializedLog = JsonSerializer.Deserialize<TransactionLog>(fileContent, _jsonOptions);

            await Assert.That(deserializedLog!.Operations.Count).IsEqualTo(0);
            await Assert.That(deserializedLog.Status).IsEqualTo(TransactionStatus.Completed);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task SaveAsync_WithNullChecksum_SerializesCorrectly()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);
            logger.LogOperation(@"C:\source\photo.jpg", @"C:\dest\photo.jpg", OperationType.Copy, 1000, checksum: null);
            logger.CompleteTransaction();

            // Act
            await logger.SaveAsync();

            // Assert
            var fileContent = await File.ReadAllTextAsync(logger.TransactionLogPath!);
            var deserializedLog = JsonSerializer.Deserialize<TransactionLog>(fileContent, _jsonOptions);

            await Assert.That(deserializedLog!.Operations[0].Checksum).IsNull();
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task SaveAsync_WithZeroFileSize_SerializesCorrectly()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);
            logger.LogOperation(@"C:\source\empty.txt", @"C:\dest\empty.txt", OperationType.Copy, fileSize: 0);
            logger.CompleteTransaction();

            // Act
            await logger.SaveAsync();

            // Assert
            var fileContent = await File.ReadAllTextAsync(logger.TransactionLogPath!);
            var deserializedLog = JsonSerializer.Deserialize<TransactionLog>(fileContent, _jsonOptions);

            await Assert.That(deserializedLog!.Operations[0].FileSize).IsEqualTo(0);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task SaveAsync_WithLargeFileSize_SerializesCorrectly()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var logger = CreateTransactionLogger(testDirectory);
            var sourceDir = Path.Combine(testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            logger.BeginTransaction(sourceDir, testDirectory + @"\{Year}", isDryRun: false);
            const long largeFileSize = 10_737_418_240; // 10 GB
            logger.LogOperation(@"C:\source\large.raw", @"C:\dest\large.raw", OperationType.Copy, largeFileSize);
            logger.CompleteTransaction();

            // Act
            await logger.SaveAsync();

            // Assert
            var fileContent = await File.ReadAllTextAsync(logger.TransactionLogPath!);
            var deserializedLog = JsonSerializer.Deserialize<TransactionLog>(fileContent, _jsonOptions);

            await Assert.That(deserializedLog!.Operations[0].FileSize).IsEqualTo(largeFileSize);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    #endregion
}
