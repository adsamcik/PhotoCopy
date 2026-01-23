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
using PhotoCopy.Abstractions;
using PhotoCopy.Commands;
using PhotoCopy.Configuration;
using PhotoCopy.Files;
using PhotoCopy.Tests.TestingImplementation;
using PhotoCopy.Validators;

namespace PhotoCopy.Tests.Commands;

public class ValidateCommandTests
{
    private readonly FakeLogger<ValidateCommand> _logger;
    private readonly PhotoCopyConfig _config;
    private readonly IOptions<PhotoCopyConfig> _options;
    private readonly IValidatorFactory _validatorFactory;
    private readonly IFileValidationService _fileValidationService;
    private readonly IFileSystem _fileSystem;

    public ValidateCommandTests()
    {
        _logger = new FakeLogger<ValidateCommand>();
        _config = new PhotoCopyConfig
        {
            Source = @"C:\Source",
            Destination = @"C:\Dest\{year}\{month}\{day}\{name}{ext}",
            DryRun = false
        };
        _options = Microsoft.Extensions.Options.Options.Create(_config);

        _validatorFactory = Substitute.For<IValidatorFactory>();
        _fileValidationService = new FileValidationService();
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

    private ValidateCommand CreateCommand() => new(
        _logger,
        _options,
        _validatorFactory,
        _fileValidationService,
        _fileSystem);

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

    #region ExecuteAsync_WithValidConfig_ReturnsZero

    [Test]
    public async Task ExecuteAsync_WithValidConfig_ReturnsZero_WhenNoFiles()
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
    public async Task ExecuteAsync_WithValidConfig_ReturnsZero_WhenAllFilesValid()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFile("photo1.jpg"),
            CreateMockFile("photo2.jpg"),
            CreateMockFile("photo3.jpg")
        };

        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(files);

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
    public async Task ExecuteAsync_WithValidConfig_ReturnsZero_WhenNoValidators()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFile("photo1.jpg")
        };

        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(files);

        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator>());

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
    }

    #endregion

    #region ExecuteAsync_WithInvalidConfig_ReturnsOne

    [Test]
    public async Task ExecuteAsync_WithInvalidFiles_ReturnsValidationError_WhenSingleFileInvalid()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFile("invalid_photo.jpg")
        };

        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(files);

        var validator = CreateMockValidator(isValid: false, rejectionReason: "File too small");
        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator> { validator });

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert - ValidationError when files fail validation
        result.Should().Be((int)ExitCode.ValidationError);
    }

    [Test]
    public async Task ExecuteAsync_WithInvalidFiles_ReturnsValidationError_WhenMultipleFilesInvalid()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFile("invalid1.jpg"),
            CreateMockFile("invalid2.jpg"),
            CreateMockFile("invalid3.jpg")
        };

        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(files);

        var validator = CreateMockValidator(isValid: false, rejectionReason: "Validation failed");
        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator> { validator });

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert - ValidationError when files fail validation
        result.Should().Be((int)ExitCode.ValidationError);
    }

    [Test]
    public async Task ExecuteAsync_WithInvalidFiles_ReturnsValidationError_WhenMixedValidAndInvalidFiles()
    {
        // Arrange
        var validFile = CreateMockFile("valid.jpg");
        var invalidFile = CreateMockFile("invalid.jpg");

        var files = new List<IFile> { validFile, invalidFile };

        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(files);

        var validator = Substitute.For<IValidator>();
        validator.Name.Returns("MockValidator");
        validator.Validate(validFile)
            .Returns(ValidationResult.Success("MockValidator"));
        validator.Validate(invalidFile)
            .Returns(ValidationResult.Fail("MockValidator", "Invalid file"));

        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator> { validator });

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert - ValidationError when files fail validation
        result.Should().Be((int)ExitCode.ValidationError);
    }

    [Test]
    public async Task ExecuteAsync_WithInvalidFiles_ReturnsValidationError_WhenMultipleValidatorsFailOnSameFile()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFile("photo.jpg")
        };

        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(files);

        var validator1 = Substitute.For<IValidator>();
        validator1.Name.Returns("Validator1");
        validator1.Validate(Arg.Any<IFile>())
            .Returns(ValidationResult.Fail("Validator1", "Reason 1"));

        var validator2 = Substitute.For<IValidator>();
        validator2.Name.Returns("Validator2");
        validator2.Validate(Arg.Any<IFile>())
            .Returns(ValidationResult.Fail("Validator2", "Reason 2"));

        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator> { validator1, validator2 });

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert - ValidationError when files fail validation
        result.Should().Be((int)ExitCode.ValidationError);
    }

    [Test]
    public async Task ExecuteAsync_OnIOError_ReturnsIOError_WhenIOExceptionThrown()
    {
        // Arrange
        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(_ => throw new IOException("Access denied"));

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert - IOError for I/O exceptions
        result.Should().Be((int)ExitCode.IOError);
    }

    #endregion

    #region ExecuteAsync_OnCancellation_ReturnsTwo

    [Test]
    public async Task ExecuteAsync_OnCancellation_ReturnsTwo_WhenCancelledBeforeProcessing()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var files = new List<IFile>
        {
            CreateMockFile("photo.jpg")
        };

        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(files);

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync(cts.Token);

        // Assert
        result.Should().Be(2);
    }

    [Test]
    public async Task ExecuteAsync_OnCancellation_ReturnsTwo_WhenCancelledDuringProcessing()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        var files = new List<IFile>
        {
            CreateMockFile("photo1.jpg"),
            CreateMockFile("photo2.jpg"),
            CreateMockFile("photo3.jpg")
        };

        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(files);

        var validator = Substitute.For<IValidator>();
        validator.Name.Returns("CancellingValidator");
        validator.Validate(Arg.Any<IFile>())
            .Returns(callInfo =>
            {
                cts.Cancel();
                return ValidationResult.Success("CancellingValidator");
            });

        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator> { validator });

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync(cts.Token);

        // Assert
        result.Should().Be(2);
    }

    [Test]
    public async Task ExecuteAsync_OnCancellation_ReturnsTwo_WhenOperationCancelledExceptionThrown()
    {
        // Arrange
        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(_ => throw new OperationCanceledException());

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(2);
    }

    #endregion

    #region ExecuteAsync_ValidatesSourcePath

    [Test]
    public async Task ExecuteAsync_ValidatesSourcePath_EnumeratesFilesFromConfiguredSource()
    {
        // Arrange
        var expectedSource = @"C:\TestSource\Photos";
        var config = new PhotoCopyConfig
        {
            Source = expectedSource,
            Destination = @"C:\Dest\{year}\{month}\{name}{ext}"
        };
        var options = Microsoft.Extensions.Options.Options.Create(config);

        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(new List<IFile>());

        var command = new ValidateCommand(
            _logger,
            options,
            _validatorFactory,
            _fileValidationService,
            fileSystem);

        // Act
        await command.ExecuteAsync();

        // Assert
        fileSystem.Received(1).EnumerateFiles(expectedSource);
    }

    [Test]
    public async Task ExecuteAsync_ValidatesSourcePath_LogsSourcePathInformation()
    {
        // Arrange
        var expectedSource = @"C:\MyPhotos";
        var config = new PhotoCopyConfig
        {
            Source = expectedSource,
            Destination = @"C:\Dest\{year}\{month}\{name}{ext}"
        };
        var options = Microsoft.Extensions.Options.Options.Create(config);

        var command = new ValidateCommand(
            _logger,
            options,
            _validatorFactory,
            _fileValidationService,
            _fileSystem);
        SharedLogs.Clear();

        // Act
        await command.ExecuteAsync();

        // Assert
        var logs = SharedLogs.Entries.ToList();
        logs.Should().Contain(entry =>
            entry.Message.Contains(expectedSource) &&
            entry.LogLevel == LogLevel.Information);
    }

    [Test]
    public async Task ExecuteAsync_ValidatesSourcePath_ProcessesAllFilesFromSource()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFile("photo1.jpg"),
            CreateMockFile("photo2.jpg"),
            CreateMockFile("photo3.jpg"),
            CreateMockFile("photo4.jpg"),
            CreateMockFile("photo5.jpg")
        };

        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(files);

        var validator = Substitute.For<IValidator>();
        validator.Name.Returns("CountingValidator");
        validator.Validate(Arg.Any<IFile>())
            .Returns(ValidationResult.Success("CountingValidator"));

        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator> { validator });

        var command = CreateCommand();

        // Act
        await command.ExecuteAsync();

        // Assert
        validator.Received(5).Validate(Arg.Any<IFile>());
    }

    #endregion

    #region ExecuteAsync_ValidatesDestinationFormat

    [Test]
    public async Task ExecuteAsync_ValidatesDestinationFormat_CreatesValidatorsFromConfig()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = @"C:\Source",
            Destination = @"C:\Dest\{year}\{month}\{day}\{name}{ext}",
            MinDate = new DateTime(2020, 1, 1),
            MaxDate = new DateTime(2025, 12, 31)
        };
        var options = Microsoft.Extensions.Options.Options.Create(config);

        var validatorFactory = Substitute.For<IValidatorFactory>();
        validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator>());

        var command = new ValidateCommand(
            _logger,
            options,
            validatorFactory,
            _fileValidationService,
            _fileSystem);

        // Act
        await command.ExecuteAsync();

        // Assert
        validatorFactory.Received(1).Create(config);
    }

    [Test]
    public async Task ExecuteAsync_ValidatesDestinationFormat_AppliesAllValidators()
    {
        // Arrange
        var file = CreateMockFile("photo.jpg");
        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(new List<IFile> { file });

        var validator1 = Substitute.For<IValidator>();
        validator1.Name.Returns("DateValidator");
        validator1.Validate(Arg.Any<IFile>())
            .Returns(ValidationResult.Success("DateValidator"));

        var validator2 = Substitute.For<IValidator>();
        validator2.Name.Returns("SizeValidator");
        validator2.Validate(Arg.Any<IFile>())
            .Returns(ValidationResult.Success("SizeValidator"));

        var validator3 = Substitute.For<IValidator>();
        validator3.Name.Returns("FormatValidator");
        validator3.Validate(Arg.Any<IFile>())
            .Returns(ValidationResult.Success("FormatValidator"));

        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator> { validator1, validator2, validator3 });

        var command = CreateCommand();

        // Act
        await command.ExecuteAsync();

        // Assert
        validator1.Received(1).Validate(file);
        validator2.Received(1).Validate(file);
        validator3.Received(1).Validate(file);
    }

    [Test]
    public async Task ExecuteAsync_ValidatesDestinationFormat_ReportsValidationSummary()
    {
        // Arrange
        var validFile = CreateMockFile("valid.jpg");
        var invalidFile = CreateMockFile("invalid.jpg");

        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(new List<IFile> { validFile, invalidFile });

        var validator = Substitute.For<IValidator>();
        validator.Name.Returns("TestValidator");
        validator.Validate(validFile)
            .Returns(ValidationResult.Success("TestValidator"));
        validator.Validate(invalidFile)
            .Returns(ValidationResult.Fail("TestValidator", "Invalid"));

        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator> { validator });

        var command = CreateCommand();
        SharedLogs.Clear();

        // Act
        await command.ExecuteAsync();

        // Assert
        var logs = SharedLogs.Entries.ToList();
        logs.Should().Contain(entry =>
            entry.Message.Contains("Validation Summary"));
    }

    [Test]
    public async Task ExecuteAsync_ValidatesDestinationFormat_LogsValidationFailures()
    {
        // Arrange
        var invalidFile = CreateMockFile("corrupted.jpg");

        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(new List<IFile> { invalidFile });

        var validator = Substitute.For<IValidator>();
        validator.Name.Returns("IntegrityValidator");
        validator.Validate(Arg.Any<IFile>())
            .Returns(ValidationResult.Fail("IntegrityValidator", "File is corrupted"));

        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator> { validator });

        var command = CreateCommand();
        SharedLogs.Clear();

        // Act
        await command.ExecuteAsync();

        // Assert
        var logs = SharedLogs.Entries.ToList();
        logs.Should().Contain(entry =>
            entry.Message.Contains("Validation Failures"));
    }

    #endregion

    #region Additional Edge Cases

    [Test]
    public async Task ExecuteAsync_WithEmptySource_ReturnsZero()
    {
        // Arrange
        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(new List<IFile>());

        var validator = CreateMockValidator();
        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator> { validator });

        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
    }

    [Test]
    public async Task ExecuteAsync_LogsTotalFilesCount()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFile("photo1.jpg"),
            CreateMockFile("photo2.jpg"),
            CreateMockFile("photo3.jpg")
        };

        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(files);

        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator>());

        var command = CreateCommand();
        SharedLogs.Clear();

        // Act
        await command.ExecuteAsync();

        // Assert
        var logs = SharedLogs.Entries.ToList();
        logs.Should().Contain(entry =>
            entry.Message.Contains("Total files") &&
            entry.LogLevel == LogLevel.Information);
    }

    [Test]
    public async Task ExecuteAsync_LogsValidFilesCount()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFile("photo.jpg")
        };

        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(files);

        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator>());

        var command = CreateCommand();
        SharedLogs.Clear();

        // Act
        await command.ExecuteAsync();

        // Assert
        var logs = SharedLogs.Entries.ToList();
        logs.Should().Contain(entry =>
            entry.Message.Contains("Valid files") &&
            entry.LogLevel == LogLevel.Information);
    }

    [Test]
    public async Task ExecuteAsync_LogsInvalidFilesCount()
    {
        // Arrange
        var files = new List<IFile>
        {
            CreateMockFile("photo.jpg")
        };

        _fileSystem.EnumerateFiles(Arg.Any<string>())
            .Returns(files);

        _validatorFactory.Create(Arg.Any<PhotoCopyConfig>())
            .Returns(new List<IValidator>());

        var command = CreateCommand();
        SharedLogs.Clear();

        // Act
        await command.ExecuteAsync();

        // Assert
        var logs = SharedLogs.Entries.ToList();
        logs.Should().Contain(entry =>
            entry.Message.Contains("Invalid files") &&
            entry.LogLevel == LogLevel.Information);
    }

    #endregion
}
