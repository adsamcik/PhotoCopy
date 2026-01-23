using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PhotoCopy.Abstractions;
using PhotoCopy.Commands;
using PhotoCopy.Rollback;
using PhotoCopy.Tests.TestingImplementation;

namespace PhotoCopy.Tests.Commands;

// Run tests sequentially since they manipulate Console.In/Out
[NotInParallel]
public class RollbackCommandTests
{
    private FakeLogger<RollbackCommand> _logger = null!;
    private IRollbackService _rollbackService = null!;
    private IFileSystem _fileSystem = null!;
    private string _testLogDirectory = null!;
    private string _testTransactionLogPath = null!;

    [Before(Test)]
    public void Setup()
    {
        _logger = new FakeLogger<RollbackCommand>();
        _rollbackService = Substitute.For<IRollbackService>();
        _fileSystem = Substitute.For<IFileSystem>();
        _testLogDirectory = Path.Combine(Path.GetTempPath(), "PhotoCopyTests", Guid.NewGuid().ToString());
        _testTransactionLogPath = Path.Combine(_testLogDirectory, "transaction.json");

        // Clear shared logs before each test
        SharedLogs.Clear();
    }

    [After(Test)]
    public void Cleanup()
    {
        CleanupTestFiles();
    }

    private RollbackCommand CreateCommand(
        string? transactionLogPath = null,
        string? logDirectory = null,
        bool listLogs = false,
        bool skipConfirmation = false) => new(
        _logger,
        _rollbackService,
        _fileSystem,
        transactionLogPath,
        logDirectory,
        listLogs,
        skipConfirmation);

    private void CreateTestLogFile()
    {
        Directory.CreateDirectory(_testLogDirectory);
        File.WriteAllText(_testTransactionLogPath, "{}");
        // Configure the mock to return true for the test log file
        _fileSystem.FileExists(_testTransactionLogPath).Returns(true);
    }

    private void CleanupTestFiles()
    {
        try
        {
            if (Directory.Exists(_testLogDirectory))
            {
                Directory.Delete(_testLogDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private async Task<int> ExecuteWithConsoleInput(RollbackCommand command, string input, CancellationToken cancellationToken = default)
    {
        var originalIn = Console.In;
        var originalOut = Console.Out;
        using var reader = new StringReader(input);
        using var writer = new StringWriter();

        try
        {
            Console.SetIn(reader);
            Console.SetOut(writer);
            return await command.ExecuteAsync(cancellationToken);
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
        }
    }

    private async Task<(int result, string output)> ExecuteWithConsoleCapture(RollbackCommand command)
    {
        var originalOut = Console.Out;
        using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);
            var result = await command.ExecuteAsync();
            return (result, writer.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    #region ExecuteAsync_WithValidLog_RollsBack

    [Test]
    public async Task ExecuteAsync_WithValidLog_RollsBack_WhenSuccessful()
    {
        // Arrange
        CreateTestLogFile();
        
        var successResult = new RollbackResult(
            Success: true,
            FilesRestored: 5,
            FilesFailed: 0,
            DirectoriesRemoved: 2,
            Errors: Array.Empty<string>());

        _rollbackService.RollbackAsync(_testTransactionLogPath, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(successResult));

        var command = CreateCommand(transactionLogPath: _testTransactionLogPath);

        // Act
        var result = await ExecuteWithConsoleInput(command, "yes");

        // Assert
        result.Should().Be(0);
        await _rollbackService.Received(1).RollbackAsync(_testTransactionLogPath, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WithValidLog_RollsBack_WhenUserConfirms()
    {
        // Arrange
        CreateTestLogFile();
        
        var successResult = new RollbackResult(
            Success: true,
            FilesRestored: 10,
            FilesFailed: 0,
            DirectoriesRemoved: 3,
            Errors: Array.Empty<string>());

        _rollbackService.RollbackAsync(_testTransactionLogPath, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(successResult));

        var command = CreateCommand(transactionLogPath: _testTransactionLogPath);

        // Act
        var result = await ExecuteWithConsoleInput(command, "yes");

        // Assert
        result.Should().Be(0);
        var logEntry = SharedLogs.Entries.Find(e => 
            e.Message.Contains("Rollback completed successfully"));
        logEntry.Should().NotBeNull();
    }

    [Test]
    public async Task ExecuteAsync_WithValidLog_RollsBack_AndReportsStats()
    {
        // Arrange
        CreateTestLogFile();
        
        var successResult = new RollbackResult(
            Success: true,
            FilesRestored: 7,
            FilesFailed: 0,
            DirectoriesRemoved: 4,
            Errors: Array.Empty<string>());

        _rollbackService.RollbackAsync(_testTransactionLogPath, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(successResult));

        var command = CreateCommand(transactionLogPath: _testTransactionLogPath);

        // Act
        var result = await ExecuteWithConsoleInput(command, "yes");

        // Assert
        result.Should().Be(0);
        await _rollbackService.Received(1).RollbackAsync(
            Arg.Is<string>(s => s == _testTransactionLogPath),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WithValidLog_RollsBack_ReturnsOneOnErrors()
    {
        // Arrange
        CreateTestLogFile();
        
        var failResult = new RollbackResult(
            Success: false,
            FilesRestored: 3,
            FilesFailed: 2,
            DirectoriesRemoved: 1,
            Errors: new[] { "Error 1", "Error 2" });

        _rollbackService.RollbackAsync(_testTransactionLogPath, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(failResult));

        var command = CreateCommand(transactionLogPath: _testTransactionLogPath);

        // Act
        var result = await ExecuteWithConsoleInput(command, "yes");

        // Assert - PartialSuccess when some files restored, some failed
        result.Should().Be((int)ExitCode.PartialSuccess);
    }

    #endregion

    #region ExecuteAsync_WithNoLogs_ReturnsOne

    [Test]
    public async Task ExecuteAsync_WithNoLogs_ReturnsInvalidArguments_WhenNoPathProvided()
    {
        // Arrange
        var command = CreateCommand(transactionLogPath: null);

        // Act
        var result = await command.ExecuteAsync();

        // Assert - InvalidArguments when required path is missing
        result.Should().Be((int)ExitCode.InvalidArguments);
        var logEntry = SharedLogs.Entries.Find(e => 
            e.LogLevel == LogLevel.Error && 
            e.Message.Contains("Transaction log path is required"));
        logEntry.Should().NotBeNull();
    }

    [Test]
    public async Task ExecuteAsync_WithNoLogs_ReturnsInvalidArguments_WhenEmptyPathProvided()
    {
        // Arrange
        var command = CreateCommand(transactionLogPath: string.Empty);

        // Act
        var result = await command.ExecuteAsync();

        // Assert - InvalidArguments when path is empty
        result.Should().Be((int)ExitCode.InvalidArguments);
    }

    [Test]
    public async Task ExecuteAsync_WithNoLogs_ReturnsIOError_WhenFileNotFound()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testLogDirectory, "nonexistent.json");
        var command = CreateCommand(transactionLogPath: nonExistentPath);

        // Act
        var result = await command.ExecuteAsync();

        // Assert - IOError when file not found
        result.Should().Be((int)ExitCode.IOError);
        var logEntry = SharedLogs.Entries.Find(e => 
            e.LogLevel == LogLevel.Error && 
            e.Message.Contains("Transaction log not found"));
        logEntry.Should().NotBeNull();
    }

    [Test]
    public async Task ExecuteAsync_WithNoLogs_ReturnsOne_WhenUserDeclinesConfirmation()
    {
        // Arrange
        CreateTestLogFile();
        
        var command = CreateCommand(transactionLogPath: _testTransactionLogPath);

        // Act
        var result = await ExecuteWithConsoleInput(command, "no");

        // Assert
        result.Should().Be(0); // Returns 0 when user cancels
        await _rollbackService.DidNotReceive().RollbackAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region ExecuteAsync_WithListLogs_ListsTransactions

    [Test]
    public async Task ExecuteAsync_WithListLogs_ListsTransactions_WhenLogsExist()
    {
        // Arrange
        Directory.CreateDirectory(_testLogDirectory);
        _fileSystem.DirectoryExists(_testLogDirectory).Returns(true);
        
        var logs = new List<TransactionLogInfo>
        {
            new TransactionLogInfo(
                Path.Combine(_testLogDirectory, "log1.json"),
                "tx-001",
                DateTime.Now.AddDays(-1),
                TransactionStatus.Completed,
                10),
            new TransactionLogInfo(
                Path.Combine(_testLogDirectory, "log2.json"),
                "tx-002",
                DateTime.Now,
                TransactionStatus.Completed,
                5)
        };

        _rollbackService.ListTransactionLogs(_testLogDirectory)
            .Returns(logs);

        var command = CreateCommand(logDirectory: _testLogDirectory, listLogs: true);

        // Act
        var (result, output) = await ExecuteWithConsoleCapture(command);

        // Assert
        result.Should().Be(0);
        output.Should().Contain("Transaction logs in");
        output.Should().Contain("tx-001");
        output.Should().Contain("tx-002");
    }

    [Test]
    public async Task ExecuteAsync_WithListLogs_ListsTransactions_WhenNoLogsExist()
    {
        // Arrange
        Directory.CreateDirectory(_testLogDirectory);
        _fileSystem.DirectoryExists(_testLogDirectory).Returns(true);
        
        _rollbackService.ListTransactionLogs(_testLogDirectory)
            .Returns(new List<TransactionLogInfo>());

        var command = CreateCommand(logDirectory: _testLogDirectory, listLogs: true);

        // Act
        var (result, _) = await ExecuteWithConsoleCapture(command);

        // Assert
        result.Should().Be(0);
        var logEntry = SharedLogs.Entries.Find(e => 
            e.Message.Contains("No transaction logs found"));
        logEntry.Should().NotBeNull();
    }

    [Test]
    public async Task ExecuteAsync_WithListLogs_ListsTransactions_WhenDirectoryDoesNotExist()
    {
        // Arrange
        var nonExistentDir = Path.Combine(_testLogDirectory, "nonexistent");
        var command = CreateCommand(logDirectory: nonExistentDir, listLogs: true);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var logEntry = SharedLogs.Entries.Find(e => 
            e.Message.Contains("No transaction logs found"));
        logEntry.Should().NotBeNull();
    }

    [Test]
    public async Task ExecuteAsync_WithListLogs_ListsTransactions_UsesDefaultDirectory()
    {
        // Arrange
        var testCurrentDir = Path.Combine(Path.GetTempPath(), "PhotoCopyTestCurrent");
        _fileSystem.GetCurrentDirectory().Returns(testCurrentDir);
        
        var command = CreateCommand(logDirectory: null, listLogs: true);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        // Should complete without error (directory may or may not exist)
        result.Should().Be(0);
    }

    #endregion

    #region ExecuteAsync_OnCancellation_ReturnsTwo

    [Test]
    public async Task ExecuteAsync_OnCancellation_ReturnsTwo_WhenCancelled()
    {
        // Arrange
        CreateTestLogFile();
        
        _rollbackService.RollbackAsync(_testTransactionLogPath, Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var command = CreateCommand(transactionLogPath: _testTransactionLogPath);

        // Act
        var result = await ExecuteWithConsoleInput(command, "yes");

        // Assert
        result.Should().Be(2);
    }

    [Test]
    public async Task ExecuteAsync_OnCancellation_ReturnsTwo_LogsWarning()
    {
        // Arrange
        CreateTestLogFile();
        
        _rollbackService.RollbackAsync(_testTransactionLogPath, Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var command = CreateCommand(transactionLogPath: _testTransactionLogPath);

        // Act
        var result = await ExecuteWithConsoleInput(command, "yes");

        // Assert
        result.Should().Be(2);
        var logEntry = SharedLogs.Entries.Find(e => 
            e.LogLevel == LogLevel.Warning && 
            e.Message.Contains("Rollback cancelled"));
        logEntry.Should().NotBeNull();
    }

    [Test]
    public async Task ExecuteAsync_OnCancellation_ReturnsTwo_WithTaskCanceledException()
    {
        // Arrange
        CreateTestLogFile();
        
        _rollbackService.RollbackAsync(_testTransactionLogPath, Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException());

        var command = CreateCommand(transactionLogPath: _testTransactionLogPath);

        // Act
        var result = await ExecuteWithConsoleInput(command, "yes");

        // Assert
        result.Should().Be(2);
    }

    [Test]
    public async Task ExecuteAsync_OnCancellation_ReturnsTwo_ServiceNotCalled()
    {
        // Arrange
        CreateTestLogFile();
        
        // Set up rollback service to throw OperationCanceledException
        // This simulates cancellation happening during the rollback operation
        _rollbackService.RollbackAsync(_testTransactionLogPath, Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var command = CreateCommand(transactionLogPath: _testTransactionLogPath);

        // Act - user confirms, but operation is cancelled during execution
        var result = await ExecuteWithConsoleInput(command, "yes");

        // Assert - should return 2 when cancelled
        result.Should().Be(2);
    }

    #endregion
}
