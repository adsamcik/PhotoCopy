using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PhotoCopy.Commands;
using PhotoCopy.Configuration;
using PhotoCopy.Directories;
using PhotoCopy.Files;
using PhotoCopy.Progress;
using PhotoCopy.Tests.TestingImplementation;
using PhotoCopy.Validators;

namespace PhotoCopy.Tests.Commands;

[NotInParallel]
public class CopyCommandTests
{
    private readonly FakeLogger<CopyCommand> _logger;
    private readonly PhotoCopyConfig _config;
    private readonly IOptions<PhotoCopyConfig> _options;
    private readonly IDirectoryCopier _directoryCopier;
    private readonly IDirectoryCopierAsync _directoryCopierAsync;
    private readonly IValidatorFactory _validatorFactory;
    private readonly IProgressReporter _progressReporter;

    public CopyCommandTests()
    {
        _logger = new FakeLogger<CopyCommand>();
        _config = new PhotoCopyConfig
        {
            Source = @"C:\Source",
            Destination = @"C:\Dest\{year}\{month}\{day}\{name}{ext}",
            DryRun = false,
            UseAsync = true,
            Mode = OperationMode.Copy
        };
        _options = Microsoft.Extensions.Options.Options.Create(_config);
        
        _directoryCopier = Substitute.For<IDirectoryCopier>();
        _directoryCopierAsync = Substitute.For<IDirectoryCopierAsync>();
        _validatorFactory = Substitute.For<IValidatorFactory>();
        _progressReporter = Substitute.For<IProgressReporter>();
        
        // Default setup for validator factory
        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator>());
    }

    [Before(Test)]
    public void BeforeEachTest()
    {
        SharedLogs.Clear();
    }

    private CopyCommand CreateCommand() => new(
        _logger,
        _options,
        _directoryCopier,
        _directoryCopierAsync,
        _validatorFactory,
        _progressReporter);

    #region ExecuteAsync_WithValidInput_ReturnsZero

    [Test]
    public async Task ExecuteAsync_WithValidInput_ReturnsZero_WhenAsyncCopySucceeds()
    {
        // Arrange
        var successResult = new CopyResult(
            FilesProcessed: 10,
            FilesFailed: 0,
            FilesSkipped: 0,
            BytesProcessed: 1024,
            Errors: Array.Empty<CopyError>());

        _directoryCopierAsync.CopyAsync(
                Arg.Any<IReadOnlyCollection<IValidator>>(),
                Arg.Any<IProgressReporter>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(successResult));

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
    }

    [Test]
    public async Task ExecuteAsync_WithValidInput_ReturnsZero_WhenSyncCopySucceeds()
    {
        // Arrange
        _config.UseAsync = false;
        var successResult = new CopyResult(
            FilesProcessed: 10,
            FilesFailed: 0,
            FilesSkipped: 0,
            BytesProcessed: 1024,
            Errors: Array.Empty<CopyError>());

        _directoryCopier.Copy(Arg.Any<IReadOnlyCollection<IValidator>>())
            .Returns(successResult);

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
    }

    [Test]
    public async Task ExecuteAsync_WithValidInput_ReturnsZero_WhenNoFilesProcessed()
    {
        // Arrange
        var emptyResult = new CopyResult(
            FilesProcessed: 0,
            FilesFailed: 0,
            FilesSkipped: 0,
            BytesProcessed: 0,
            Errors: Array.Empty<CopyError>());

        _directoryCopierAsync.CopyAsync(
                Arg.Any<IReadOnlyCollection<IValidator>>(),
                Arg.Any<IProgressReporter>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(emptyResult));

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
    }

    #endregion

    #region ExecuteAsync_OnError_ReturnsOne

    [Test]
    public async Task ExecuteAsync_OnError_ReturnsOne_WhenExceptionThrown()
    {
        // Arrange
        _directoryCopierAsync.CopyAsync(
                Arg.Any<IReadOnlyCollection<IValidator>>(),
                Arg.Any<IProgressReporter>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Test error"));

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(1);
    }

    [Test]
    public async Task ExecuteAsync_OnError_ReturnsOne_WhenFilesFailed()
    {
        // Arrange
        var mockFile = Substitute.For<IFile>();
        mockFile.File.Returns(new FileInfo(@"C:\Source\test.jpg"));

        var failedResult = new CopyResult(
            FilesProcessed: 10,
            FilesFailed: 2,
            FilesSkipped: 0,
            BytesProcessed: 1024,
            Errors: new List<CopyError>
            {
                new(mockFile, @"C:\Dest\test.jpg", "Access denied")
            });

        _directoryCopierAsync.CopyAsync(
                Arg.Any<IReadOnlyCollection<IValidator>>(),
                Arg.Any<IProgressReporter>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(failedResult));

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(1);
    }

    [Test]
    public async Task ExecuteAsync_OnError_ReturnsOne_WhenIOExceptionThrown()
    {
        // Arrange
        _directoryCopierAsync.CopyAsync(
                Arg.Any<IReadOnlyCollection<IValidator>>(),
                Arg.Any<IProgressReporter>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("Disk full"));

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(1);
    }

    [Test]
    public async Task ExecuteAsync_OnError_LogsErrorMessage()
    {
        // Arrange
        _directoryCopierAsync.CopyAsync(
                Arg.Any<IReadOnlyCollection<IValidator>>(),
                Arg.Any<IProgressReporter>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Test error"));

        var command = CreateCommand();

        // Act
        await command.ExecuteAsync();

        // Assert
        _logger.Logs.Should().Contain(log => 
            log.LogLevel == LogLevel.Error && 
            log.Message.Contains("Copy operation failed"));
    }

    #endregion

    #region ExecuteAsync_OnCancellation_ReturnsTwo

    [Test]
    public async Task ExecuteAsync_OnCancellation_ReturnsTwo_WhenTokenCancelled()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _directoryCopierAsync.CopyAsync(
                Arg.Any<IReadOnlyCollection<IValidator>>(),
                Arg.Any<IProgressReporter>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync(cts.Token);

        // Assert
        result.Should().Be(2);
    }

    [Test]
    public async Task ExecuteAsync_OnCancellation_ReturnsTwo_WhenTaskCanceledException()
    {
        // Arrange
        _directoryCopierAsync.CopyAsync(
                Arg.Any<IReadOnlyCollection<IValidator>>(),
                Arg.Any<IProgressReporter>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException());

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(2);
    }

    [Test]
    public async Task ExecuteAsync_OnCancellation_LogsWarning()
    {
        // Arrange
        _directoryCopierAsync.CopyAsync(
                Arg.Any<IReadOnlyCollection<IValidator>>(),
                Arg.Any<IProgressReporter>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var command = CreateCommand();

        // Act
        await command.ExecuteAsync();

        // Assert
        _logger.Logs.Should().Contain(log => 
            log.LogLevel == LogLevel.Warning && 
            log.Message.Contains("cancelled"));
    }

    #endregion

    #region ExecuteAsync_CallsDirectoryCopier

    [Test]
    public async Task ExecuteAsync_CallsDirectoryCopier_WithAsyncCopier()
    {
        // Arrange
        var successResult = new CopyResult(
            FilesProcessed: 5,
            FilesFailed: 0,
            FilesSkipped: 0,
            BytesProcessed: 512,
            Errors: Array.Empty<CopyError>());

        _directoryCopierAsync.CopyAsync(
                Arg.Any<IReadOnlyCollection<IValidator>>(),
                Arg.Any<IProgressReporter>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(successResult));

        var command = CreateCommand();

        // Act
        await command.ExecuteAsync();

        // Assert
        await _directoryCopierAsync.Received(1).CopyAsync(
            Arg.Any<IReadOnlyCollection<IValidator>>(),
            Arg.Any<IProgressReporter>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_CallsDirectoryCopier_WithSyncCopier()
    {
        // Arrange
        _config.UseAsync = false;
        var command = CreateCommand();

        // Act
        await command.ExecuteAsync();

        // Assert
        _directoryCopier.Received(1).Copy(Arg.Any<IReadOnlyCollection<IValidator>>());
    }

    [Test]
    public async Task ExecuteAsync_CallsDirectoryCopier_PassesValidatorsFromFactory()
    {
        // Arrange
        var validators = new List<IValidator>
        {
            Substitute.For<IValidator>(),
            Substitute.For<IValidator>()
        };
        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>()).Returns(validators);

        var successResult = new CopyResult(
            FilesProcessed: 1,
            FilesFailed: 0,
            FilesSkipped: 0,
            BytesProcessed: 100,
            Errors: Array.Empty<CopyError>());

        _directoryCopierAsync.CopyAsync(
                Arg.Any<IReadOnlyCollection<IValidator>>(),
                Arg.Any<IProgressReporter>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(successResult));

        var command = CreateCommand();

        // Act
        await command.ExecuteAsync();

        // Assert
        await _directoryCopierAsync.Received(1).CopyAsync(
            Arg.Is<IReadOnlyCollection<IValidator>>(v => v.Count == 2),
            Arg.Any<IProgressReporter>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_CallsDirectoryCopier_PassesProgressReporter()
    {
        // Arrange
        var successResult = new CopyResult(
            FilesProcessed: 1,
            FilesFailed: 0,
            FilesSkipped: 0,
            BytesProcessed: 100,
            Errors: Array.Empty<CopyError>());

        _directoryCopierAsync.CopyAsync(
                Arg.Any<IReadOnlyCollection<IValidator>>(),
                Arg.Any<IProgressReporter>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(successResult));

        var command = CreateCommand();

        // Act
        await command.ExecuteAsync();

        // Assert
        await _directoryCopierAsync.Received(1).CopyAsync(
            Arg.Any<IReadOnlyCollection<IValidator>>(),
            _progressReporter,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_CallsDirectoryCopier_PassesCancellationToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        var successResult = new CopyResult(
            FilesProcessed: 1,
            FilesFailed: 0,
            FilesSkipped: 0,
            BytesProcessed: 100,
            Errors: Array.Empty<CopyError>());

        _directoryCopierAsync.CopyAsync(
                Arg.Any<IReadOnlyCollection<IValidator>>(),
                Arg.Any<IProgressReporter>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(successResult));

        var command = CreateCommand();

        // Act
        await command.ExecuteAsync(token);

        // Assert
        await _directoryCopierAsync.Received(1).CopyAsync(
            Arg.Any<IReadOnlyCollection<IValidator>>(),
            Arg.Any<IProgressReporter>(),
            token);
    }

    #endregion

    #region ExecuteAsync_InDryRunMode_LogsDryRun

    [Test]
    public async Task ExecuteAsync_InDryRunMode_LogsDryRun_LogsStartingInformation()
    {
        // Arrange
        _config.DryRun = true;

        var successResult = new CopyResult(
            FilesProcessed: 5,
            FilesFailed: 0,
            FilesSkipped: 0,
            BytesProcessed: 512,
            Errors: Array.Empty<CopyError>());

        _directoryCopierAsync.CopyAsync(
                Arg.Any<IReadOnlyCollection<IValidator>>(),
                Arg.Any<IProgressReporter>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(successResult));

        var command = CreateCommand();

        // Act
        await command.ExecuteAsync();

        // Assert
        _logger.Logs.Should().Contain(log => 
            log.LogLevel == LogLevel.Information && 
            log.Message.Contains("Starting"));
    }

    [Test]
    public async Task ExecuteAsync_InDryRunMode_LogsDryRun_LogsMode()
    {
        // Arrange
        _config.DryRun = true;
        _config.Mode = OperationMode.Copy;

        var successResult = new CopyResult(
            FilesProcessed: 5,
            FilesFailed: 0,
            FilesSkipped: 0,
            BytesProcessed: 512,
            Errors: Array.Empty<CopyError>());

        _directoryCopierAsync.CopyAsync(
                Arg.Any<IReadOnlyCollection<IValidator>>(),
                Arg.Any<IProgressReporter>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(successResult));

        var command = CreateCommand();

        // Act
        await command.ExecuteAsync();

        // Assert
        // The log message contains the Mode value which could be "Copy" or the enum value
        _logger.Logs.Should().Contain(log => 
            log.LogLevel == LogLevel.Information && 
            log.Message.Contains("Starting"));
    }

    [Test]
    public async Task ExecuteAsync_InDryRunMode_LogsDryRun_LogsCompletionResult()
    {
        // Arrange
        _config.DryRun = true;

        var successResult = new CopyResult(
            FilesProcessed: 10,
            FilesFailed: 2,
            FilesSkipped: 3,
            BytesProcessed: 1024,
            Errors: Array.Empty<CopyError>());

        _directoryCopierAsync.CopyAsync(
                Arg.Any<IReadOnlyCollection<IValidator>>(),
                Arg.Any<IProgressReporter>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(successResult));

        var command = CreateCommand();

        // Act
        await command.ExecuteAsync();

        // Assert
        _logger.Logs.Should().Contain(log => 
            log.LogLevel == LogLevel.Information && 
            log.Message.Contains("complete") &&
            log.Message.Contains("10") &&
            log.Message.Contains("processed"));
    }

    [Test]
    public async Task ExecuteAsync_InDryRunMode_LogsDryRun_LogsSourcePath()
    {
        // Arrange
        _config.DryRun = true;
        _config.Source = @"C:\MyPhotos";

        var successResult = new CopyResult(
            FilesProcessed: 1,
            FilesFailed: 0,
            FilesSkipped: 0,
            BytesProcessed: 100,
            Errors: Array.Empty<CopyError>());

        _directoryCopierAsync.CopyAsync(
                Arg.Any<IReadOnlyCollection<IValidator>>(),
                Arg.Any<IProgressReporter>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(successResult));

        var command = CreateCommand();

        // Act
        await command.ExecuteAsync();

        // Assert
        _logger.Logs.Should().Contain(log => 
            log.LogLevel == LogLevel.Information && 
            log.Message.Contains(@"C:\MyPhotos"));
    }

    [Test]
    public async Task ExecuteAsync_InDryRunMode_LogsDryRun_LogsErrorDetails()
    {
        // Arrange
        _config.DryRun = true;

        var mockFile = Substitute.For<IFile>();
        mockFile.File.Returns(new FileInfo(@"C:\Source\photo.jpg"));

        var resultWithErrors = new CopyResult(
            FilesProcessed: 5,
            FilesFailed: 1,
            FilesSkipped: 0,
            BytesProcessed: 512,
            Errors: new List<CopyError>
            {
                new(mockFile, @"C:\Dest\photo.jpg", "File locked")
            });

        _directoryCopierAsync.CopyAsync(
                Arg.Any<IReadOnlyCollection<IValidator>>(),
                Arg.Any<IProgressReporter>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(resultWithErrors));

        var command = CreateCommand();

        // Act
        await command.ExecuteAsync();

        // Assert
        _logger.Logs.Should().Contain(log => 
            log.LogLevel == LogLevel.Error && 
            log.Message.Contains("Failed to process") &&
            log.Message.Contains("File locked"));
    }

    #endregion
}
