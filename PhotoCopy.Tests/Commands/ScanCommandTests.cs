using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PhotoCopy.Abstractions;
using PhotoCopy.Commands;
using PhotoCopy.Configuration;
using PhotoCopy.Files;
using PhotoCopy.Tests.TestingImplementation;
using PhotoCopy.Validators;

namespace PhotoCopy.Tests.Commands;

public class ScanCommandTests
{
    private readonly FakeLogger<ScanCommand> _logger;
    private readonly PhotoCopyConfig _config;
    private readonly IOptions<PhotoCopyConfig> _options;
    private readonly IDirectoryScanner _directoryScanner;
    private readonly IValidatorFactory _validatorFactory;
    private readonly IFileValidationService _fileValidationService;
    private readonly IFileFactory _fileFactory;
    private readonly IFileSystem _fileSystem;

    public ScanCommandTests()
    {
        _logger = new FakeLogger<ScanCommand>();
        _config = new PhotoCopyConfig
        {
            Source = @"C:\Source",
            Destination = @"C:\Dest\{year}\{month}\{day}\{name}{ext}",
            DryRun = false
        };
        _options = Microsoft.Extensions.Options.Options.Create(_config);

        _directoryScanner = Substitute.For<IDirectoryScanner>();
        _validatorFactory = Substitute.For<IValidatorFactory>();
        _fileValidationService = new FileValidationService();
        _fileFactory = Substitute.For<IFileFactory>();
        _fileSystem = Substitute.For<IFileSystem>();

        // Default setup for validator factory - return empty validators
        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator>());

        // Default setup for file system - return empty file list
        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(new List<IFile>());

        // Clear shared logs before each test
        SharedLogs.Clear();
    }

    private ScanCommand CreateCommand(bool outputJson = false) => new(
        _logger,
        _options,
        _directoryScanner,
        _validatorFactory,
        _fileValidationService,
        _fileFactory,
        _fileSystem,
        outputJson);

    private static IFile CreateMockFile(string name, DateTime? dateTime = null)
    {
        var file = Substitute.For<IFile>();
        var fileInfo = new FileInfo(Path.Combine(Path.GetTempPath(), name));

        file.File.Returns(fileInfo);
        file.FileDateTime.Returns(new FileDateTime(
            dateTime ?? DateTime.Now,
            DateTimeSource.FileCreation));

        return file;
    }

    private static IValidator CreateMockValidator(bool isValid = true, string? rejectionReason = null)
    {
        var validator = Substitute.For<IValidator>();
        validator.Name.Returns("MockValidator");
        validator.Validate(Arg.Any<IFile>())
            .Returns(isValid
                ? ValidationResult.Success("MockValidator")
                : ValidationResult.Fail("MockValidator", rejectionReason ?? "Validation failed"));
        return validator;
    }

    #region ExecuteAsync_WithValidInput_ReturnsZero

    [Test]
    public async Task ExecuteAsync_WithValidInput_ReturnsZero_WhenNoFiles()
    {
        // Arrange
        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(new List<IFile>());

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
    }

    [Test]
    public async Task ExecuteAsync_WithValidInput_ReturnsZero_WhenFilesExist()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFile(@"C:\Source\file1.jpg"),
            CreateMockFile(@"C:\Source\file2.jpg")
        };

        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(files);

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
    }

    [Test]
    public async Task ExecuteAsync_WithValidInput_ReturnsZero_WhenValidatorsPass()
    {
        // Arrange
        var files = new List<IFile> { CreateMockFile(@"C:\Source\file1.jpg") };
        _fileSystem.EnumerateFiles(Arg.Any<string>()).Returns(files);

        var validator = CreateMockValidator(isValid: true);
        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator> { validator });

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
    }

    [Test]
    public async Task ExecuteAsync_WithValidInput_ReturnsZero_WhenSomeFilesFail()
    {
        // Arrange - Even when some files fail validation, scan should return 0
        var validFile = CreateMockFile(@"C:\Source\valid.jpg");
        var invalidFile = CreateMockFile(@"C:\Source\invalid.jpg");

        var files = new List<IFile> { validFile, invalidFile };
        _fileSystem.EnumerateFiles(Arg.Any<string>()).Returns(files);

        var validator = Substitute.For<IValidator>();
        validator.Name.Returns("TestValidator");
        validator.Validate(validFile).Returns(ValidationResult.Success("TestValidator"));
        validator.Validate(invalidFile).Returns(ValidationResult.Fail("TestValidator", "Invalid file"));

        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator> { validator });

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
        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Throws(new InvalidOperationException("Test error"));

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(1);
    }

    [Test]
    public async Task ExecuteAsync_OnError_ReturnsOne_WhenValidatorFactoryThrows()
    {
        // Arrange
        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Throws(new InvalidOperationException("Validator creation failed"));

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(1);
    }

    [Test]
    public async Task ExecuteAsync_OnError_ReturnsOne_WhenIOExceptionOccurs()
    {
        // Arrange
        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Throws(new IOException("Disk read error"));

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(1);
    }

    [Test]
    public async Task ExecuteAsync_OnError_LogsError()
    {
        // Arrange
        var exception = new InvalidOperationException("Test error message");
        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Throws(exception);

        var command = CreateCommand();
        SharedLogs.Clear();

        // Act
        await command.ExecuteAsync();

        // Assert
        var logs = SharedLogs.Entries.ToList();
        logs.Should().Contain(entry =>
            entry.LogLevel == LogLevel.Error &&
            entry.Message.Contains("failed"));
    }

    #endregion

    #region ExecuteAsync_OnCancellation_ReturnsTwo

    [Test]
    public async Task ExecuteAsync_OnCancellation_ReturnsTwo_WhenCancelled()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var files = new List<IFile> { CreateMockFile(@"C:\Source\file1.jpg") };
        _fileSystem.EnumerateFiles(Arg.Any<string>()).Returns(files);

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync(cts.Token);

        // Assert
        result.Should().Be(2);
    }

    [Test]
    public async Task ExecuteAsync_OnCancellation_ReturnsTwo_WhenCancelledDuringIteration()
    {
        // Arrange
        using var cts = new CancellationTokenSource();

        var file1 = CreateMockFile(@"C:\Source\file1.jpg");
        var file2 = CreateMockFile(@"C:\Source\file2.jpg");

        // Create an enumerable that cancels after yielding first file
        IEnumerable<IFile> GetFilesWithCancellation()
        {
            yield return file1;
            cts.Cancel(); // Cancel after first file
            yield return file2;
        }

        _fileSystem.EnumerateFiles(Arg.Any<string>()).Returns(GetFilesWithCancellation());

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync(cts.Token);

        // Assert
        result.Should().Be(2);
    }

    [Test]
    public async Task ExecuteAsync_OnCancellation_LogsWarning()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var files = new List<IFile> { CreateMockFile(@"C:\Source\file1.jpg") };
        _fileSystem.EnumerateFiles(Arg.Any<string>()).Returns(files);

        var command = CreateCommand();
        SharedLogs.Clear();

        // Act
        await command.ExecuteAsync(cts.Token);

        // Assert
        var logs = SharedLogs.Entries.ToList();
        logs.Should().Contain(entry =>
            entry.LogLevel == LogLevel.Warning &&
            entry.Message.Contains("cancelled"));
    }

    #endregion

    #region ExecuteAsync_ScansDirectory

    [Test]
    public async Task ExecuteAsync_ScansDirectory_CallsEnumerateFilesWithSource()
    {
        // Arrange
        var command = CreateCommand();

        // Act
        await command.ExecuteAsync();

        // Assert
        _fileSystem.Received(1).EnumerateFiles(_config.Source);
    }

    [Test]
    public async Task ExecuteAsync_ScansDirectory_EnumeratesAllFiles()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFile(@"C:\Source\file1.jpg"),
            CreateMockFile(@"C:\Source\file2.jpg"),
            CreateMockFile(@"C:\Source\file3.jpg")
        };

        _fileSystem.EnumerateFiles(Arg.Any<string>()).Returns(files);

        var validator = CreateMockValidator(isValid: true);
        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator> { validator });

        var command = CreateCommand();

        // Act
        await command.ExecuteAsync();

        // Assert - Validator should be called for each file
        validator.Received(3).Validate(Arg.Any<IFile>());
    }

    [Test]
    public async Task ExecuteAsync_ScansDirectory_CreatesValidatorsFromConfig()
    {
        // Arrange
        var command = CreateCommand();

        // Act
        await command.ExecuteAsync();

        // Assert
        _validatorFactory.Received(1).Create(_config);
    }

    [Test]
    public async Task ExecuteAsync_ScansDirectory_ValidatesEachFile()
    {
        // Arrange
        var file1 = CreateMockFile(@"C:\Source\file1.jpg");
        var file2 = CreateMockFile(@"C:\Source\file2.jpg");
        var files = new List<IFile> { file1, file2 };

        _fileSystem.EnumerateFiles(Arg.Any<string>()).Returns(files);

        var validator = CreateMockValidator(isValid: true);
        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator> { validator });

        var command = CreateCommand();

        // Act
        await command.ExecuteAsync();

        // Assert
        validator.Received(1).Validate(file1);
        validator.Received(1).Validate(file2);
    }

    [Test]
    public async Task ExecuteAsync_ScansDirectory_AppliesMultipleValidators()
    {
        // Arrange
        var file = CreateMockFile(@"C:\Source\file1.jpg");
        _fileSystem.EnumerateFiles(Arg.Any<string>()).Returns(new List<IFile> { file });

        var validator1 = CreateMockValidator(isValid: true);
        var validator2 = CreateMockValidator(isValid: true);
        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator> { validator1, validator2 });

        var command = CreateCommand();

        // Act
        await command.ExecuteAsync();

        // Assert
        validator1.Received(1).Validate(file);
        validator2.Received(1).Validate(file);
    }

    [Test]
    public async Task ExecuteAsync_ScansDirectory_StopsValidationOnFirstFailure()
    {
        // Arrange
        var file = CreateMockFile(@"C:\Source\file1.jpg");
        _fileSystem.EnumerateFiles(Arg.Any<string>()).Returns(new List<IFile> { file });

        var failingValidator = CreateMockValidator(isValid: false, rejectionReason: "First validator failed");
        var secondValidator = CreateMockValidator(isValid: true);

        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator> { failingValidator, secondValidator });

        var command = CreateCommand();

        // Act
        await command.ExecuteAsync();

        // Assert - Second validator should NOT be called since first failed
        failingValidator.Received(1).Validate(file);
        secondValidator.DidNotReceive().Validate(Arg.Any<IFile>());
    }

    #endregion

    #region ExecuteAsync_ReportsScanResults

    [Test]
    public async Task ExecuteAsync_ReportsScanResults_LogsScanStart()
    {
        // Arrange
        var command = CreateCommand();
        SharedLogs.Clear();

        // Act
        await command.ExecuteAsync();

        // Assert
        var logs = SharedLogs.Entries.ToList();
        logs.Should().Contain(entry =>
            entry.LogLevel == LogLevel.Information &&
            entry.Message.Contains("Scanning"));
    }

    [Test]
    public async Task ExecuteAsync_ReportsScanResults_LogsScanComplete()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFile(@"C:\Source\file1.jpg"),
            CreateMockFile(@"C:\Source\file2.jpg")
        };
        _fileSystem.EnumerateFiles(Arg.Any<string>()).Returns(files);

        var command = CreateCommand();
        SharedLogs.Clear();

        // Act
        await command.ExecuteAsync();

        // Assert
        var logs = SharedLogs.Entries.ToList();
        logs.Should().Contain(entry =>
            entry.LogLevel == LogLevel.Information &&
            entry.Message.Contains("complete"));
    }

    [Test]
    public async Task ExecuteAsync_ReportsScanResults_ReportsFileCount()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFile(@"C:\Source\file1.jpg"),
            CreateMockFile(@"C:\Source\file2.jpg"),
            CreateMockFile(@"C:\Source\file3.jpg")
        };
        _fileSystem.EnumerateFiles(Arg.Any<string>()).Returns(files);

        var command = CreateCommand();
        SharedLogs.Clear();

        // Act
        await command.ExecuteAsync();

        // Assert
        var logs = SharedLogs.Entries.ToList();
        logs.Should().Contain(entry =>
            entry.LogLevel == LogLevel.Information &&
            entry.Message.Contains("3"));
    }

    [Test]
    public async Task ExecuteAsync_ReportsScanResults_ReportsValidAndSkippedCounts()
    {
        // Arrange
        var validFile = CreateMockFile(@"C:\Source\valid.jpg");
        var invalidFile = CreateMockFile(@"C:\Source\invalid.jpg");
        var files = new List<IFile> { validFile, invalidFile };
        _fileSystem.EnumerateFiles(Arg.Any<string>()).Returns(files);

        var validator = Substitute.For<IValidator>();
        validator.Name.Returns("TestValidator");
        validator.Validate(validFile).Returns(ValidationResult.Success("TestValidator"));
        validator.Validate(invalidFile).Returns(ValidationResult.Fail("TestValidator", "Invalid"));

        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator> { validator });

        var command = CreateCommand();
        SharedLogs.Clear();

        // Act
        await command.ExecuteAsync();

        // Assert - Check logs contain valid/skipped info
        var logs = SharedLogs.Entries.ToList();
        logs.Should().Contain(entry =>
            entry.LogLevel == LogLevel.Information &&
            entry.Message.Contains("valid") &&
            entry.Message.Contains("skipped"));
    }

    [Test]
    public async Task ExecuteAsync_ReportsScanResults_WithJsonOutput()
    {
        // Arrange
        var file = CreateMockFile(@"C:\Source\file1.jpg");
        _fileSystem.EnumerateFiles(Arg.Any<string>()).Returns(new List<IFile> { file });

        var command = CreateCommand(outputJson: true);

        // Capture console output
        using var consoleOutput = new StringWriter();
        Console.SetOut(consoleOutput);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        // JSON output should contain statistics
        var output = consoleOutput.ToString();
        output.Should().Contain("TotalFiles");
    }

    [Test]
    public async Task ExecuteAsync_ReportsScanResults_IncludesSourcePath()
    {
        // Arrange
        var command = CreateCommand();
        SharedLogs.Clear();

        // Act
        await command.ExecuteAsync();

        // Assert
        var logs = SharedLogs.Entries.ToList();
        logs.Should().Contain(entry =>
            entry.LogLevel == LogLevel.Information &&
            entry.Message.Contains(_config.Source));
    }

    #endregion
}
