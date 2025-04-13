using PhotoCopy.Validators;
using NSubstitute;
using IFile = PhotoCopy.Files.IFile;
using PhotoCopy.Abstractions;
using PhotoCopy.Files;
using Microsoft.Extensions.Logging;
using PhotoCopy.Directories;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace PhotoCopy.Tests;

public class DirectoryCopierTests : IClassFixture<ApplicationStateFixture>
{
    private readonly ApplicationStateFixture _fixture;
    private readonly IFileSystem _fileSystem;
    private readonly IFileOperation _fileOperation;
    private readonly ILogger _logger;
    private readonly DirectoryCopier _directoryCopier;
    private readonly string _sourcePath;
    private readonly string _destinationPath;

    public DirectoryCopierTests(ApplicationStateFixture fixture)
    {
        _fixture = fixture;
        ApplicationState.Options = new Options();
        _fileSystem = Substitute.For<IFileSystem>();
        _fileOperation = Substitute.For<IFileOperation>();
        _logger = Substitute.For<ILogger>();
        _directoryCopier = new DirectoryCopier(_fileSystem, _fileOperation, _logger);
        _sourcePath = Path.GetTempFileName();
        _destinationPath = Path.GetTempPath();
        ApplicationState.Options = new Options
        {
            Log = Options.LogLevel.verbose
        };
    }

    [Fact]
    public void Copy_DoesNotCopy_WhenValidatorFails()
    {
        // Arrange: Create a fake file and a validator that fails.
        var fakeFile = CreateFakeFile("dummy.jpg", new DateTime(2020, 1, 1), "checksum");
        _fileSystem.EnumerateFiles(Arg.Any<string>(), Arg.Any<Options>())
                   .Returns(new List<IFile> { fakeFile });

        var validator = Substitute.For<IValidator>();
        validator.Validate(Arg.Any<IFile>()).Returns(false);
        var validators = new List<IValidator> { validator };

        var options = CreateDefaultOptions();

        // Act
        _directoryCopier.Copy(options, validators);

        // Assert: No copy or move should occur and a filtered log message should be written.
        _fileOperation.DidNotReceiveWithAnyArgs().CopyFile(default, default, default);
        _fileOperation.DidNotReceiveWithAnyArgs().MoveFile(default, default, default);
        _logger.Received(1).Log<string>(
             logLevel: LogLevel.Trace, 
             eventId: Arg.Any<EventId>(),
             state: Arg.Is<string>(s =>
                 s.ToLowerInvariant().Contains("filtered by")
             ),
             exception: Arg.Any<Exception?>(),
             formatter: Arg.Any<Func<string, Exception?, string>>()
         );

    }

    [Fact]
    public void Copy_CopiesFile_WhenValidatorPasses_AndModeIsCopy()
    {
        // Arrange: Create a fake file with a passing validator.
        var fakeFile = CreateFakeFile("dummy.jpg", new DateTime(2020, 1, 1), "checksum");
        _fileSystem.EnumerateFiles(Arg.Any<string>(), Arg.Any<Options>())
                   .Returns(new List<IFile> { fakeFile });
        var validator = Substitute.For<IValidator>();
        validator.Validate(Arg.Any<IFile>()).Returns(true);
        var validators = new List<IValidator> { validator };

        var options = CreateDefaultOptions();
        options.Mode = Options.OperationMode.copy;
        options.DryRun = false;

        // Act
        _directoryCopier.Copy(options, validators);

        // Assert: The file operation should be a copy and any directory should be created
        _fileOperation.Received(1).CopyFile(fakeFile, Arg.Any<string>(), false);
        _fileSystem.Received(1).CreateDirectory(Arg.Any<string>());
    }

    [Fact]
    public void Copy_MovesFile_WhenValidatorPasses_AndModeIsMove()
    {
        // Arrange: Create a fake file with a passing validator.
        var fakeFile = CreateFakeFile("dummy.jpg", new DateTime(2020, 1, 1), "checksum");
        _fileSystem.EnumerateFiles(Arg.Any<string>(), Arg.Any<Options>())
                   .Returns(new List<IFile> { fakeFile });
        var validator = Substitute.For<IValidator>();
        validator.Validate(Arg.Any<IFile>()).Returns(true);
        var validators = new List<IValidator> { validator };

        var options = CreateDefaultOptions();
        options.Mode = Options.OperationMode.move;
        options.DryRun = false;

        // Simulate that the destination directory does not exist.
        var expectedDir = Path.GetDirectoryName(_directoryCopier.GeneratePath(options, fakeFile));
        _fileSystem.DirectoryExists(expectedDir).Returns(false);

        // Act
        _directoryCopier.Copy(options, validators);

        // Assert: The file operation should be a move and the directory should be created.
        _fileOperation.Received(1).MoveFile(fakeFile, Arg.Any<string>(), false);
        _fileSystem.Received(1).CreateDirectory(expectedDir);
    }

    [Fact]
    public void GeneratePath_ReplacesTokensCorrectly()
    {
        // Arrange: Create a fake file with known values.
        var fakeFile = CreateFakeFile("test.jpg", new DateTime(2021, 12, 31), "checksum");
        var options = CreateDefaultOptions();
        options.Source = @"C:\Source";
        // Assume the Destination template uses tokens defined in Options.DestinationVariables.
        // For example: "{year}/{month}/{day}/{directory}/{name}"
        options.Destination = $"{Options.DestinationVariables.Year}/{Options.DestinationVariables.Month}/{Options.DestinationVariables.Day}/{Options.DestinationVariables.Directory}/{Options.DestinationVariables.Name}";

        // Act
        var generatedPath = _directoryCopier.GeneratePath(options, fakeFile);

        // Assert: Build the expected string.
        var expected = $"{fakeFile.FileDateTime.DateTime.Year}/" +
                       $"{fakeFile.FileDateTime.DateTime.Month}/" +
                       $"{fakeFile.FileDateTime.DateTime.Day}/" +
                       $"{Path.GetRelativePath(options.Source, Path.GetDirectoryName(fakeFile.File.FullName) ?? string.Empty)}/" +
                       $"{fakeFile.File.Name}";
        Assert.Equal(expected, generatedPath);
    }

    [Fact]
    public void ResolveDuplicate_SkipsFile_WhenSkipExistingIsTrue()
    {
        // Arrange: Create a temporary file to simulate an existing duplicate.
        var tempDir = Path.GetTempPath();
        var duplicateFilePath = Path.Combine(tempDir, "duplicate.jpg");
        File.WriteAllText(duplicateFilePath, "dummy content");

        try
        {
            var fakeFile = CreateFakeFile("duplicate.jpg", new DateTime(2020, 1, 1), "checksum");
            _fileSystem.EnumerateFiles(Arg.Any<string>(), Arg.Any<Options>())
                       .Returns(new List<IFile> { fakeFile });
            var validator = Substitute.For<IValidator>();
            validator.Validate(Arg.Any<IFile>()).Returns(true);
            var validators = new List<IValidator> { validator };

            var options = CreateDefaultOptions();
            options.Destination = duplicateFilePath;
            options.SkipExisting = true;
            options.Mode = Options.OperationMode.copy;
            options.DryRun = false;

            // Act
            _directoryCopier.Copy(options, validators);

            // Assert: No file operation should be executed because a duplicate is skipped.
            _fileOperation.DidNotReceiveWithAnyArgs().CopyFile(default, default, default);
            _fileOperation.DidNotReceiveWithAnyArgs().MoveFile(default, default, default);
        }
        finally
        {
            // Clean up the temporary file.
            if (File.Exists(duplicateFilePath))
                File.Delete(duplicateFilePath);
        }
    }

    [Fact]
    public void ResolveDuplicate_AppendsSuffix_WhenDuplicateExistsAndSkipExistingIsFalse()
    {
        // Arrange: Create a temporary file to simulate an existing file.
        var tempDir = Path.GetTempPath();
        var originalFilePath = Path.Combine(tempDir, "test.jpg");
        File.WriteAllText(originalFilePath, "dummy content");

        try
        {
            var fakeFile = CreateFakeFile("test.jpg", new DateTime(2020, 1, 1), "checksum");
            _fileSystem.EnumerateFiles(Arg.Any<string>(), Arg.Any<Options>())
                       .Returns(new List<IFile> { fakeFile });
            var validator = Substitute.For<IValidator>();
            validator.Validate(Arg.Any<IFile>()).Returns(true);
            var validators = new List<IValidator> { validator };

            var options = CreateDefaultOptions();
            options.Destination = originalFilePath;
            options.SkipExisting = false;
            options.NoDuplicateSkip = false;
            options.Mode = Options.OperationMode.copy;
            options.DryRun = false;
            // Set duplicate format to append a number (e.g. "-{number}")
            options.DuplicatesFormat = "-{number}";

            // Act
            _directoryCopier.Copy(options, validators);

            // Assert: The copy operation should use a new file name that is not the original.
            _fileOperation.Received(1).CopyFile(fakeFile, Arg.Is<string>(s => !s.Equals(originalFilePath)), false);
        }
        finally
        {
            if (File.Exists(originalFilePath))
                File.Delete(originalFilePath);
        }
    }

    // --- Helper Methods ---

    /// <summary>
    /// Creates a fake IFile with a FileInfo and FileDateTime.
    /// </summary>
    private IFile CreateFakeFile(string fileName, DateTime dateTime, string checksum)
    {
        var fakeFile = Substitute.For<IFile>();
        var tempDir = Path.GetTempPath();
        var filePath = Path.Combine(tempDir, fileName);
        var fileInfo = new FileInfo(filePath);

        fakeFile.File.Returns(fileInfo);
        fakeFile.FileDateTime.Returns(new FileDateTime(dateTime, DateTimeSource.FileCreation));
        fakeFile.Checksum.Returns(checksum);
        return fakeFile;
    }

    /// <summary>
    /// Creates a default Options instance with common default values.
    /// </summary>
    private Options CreateDefaultOptions()
    {
        return new Options
        {
            Source = Path.GetTempPath(), // Set source to temp path so relative paths work correctly
            Destination = "{year}/{month}/{day}/{directory}/{name}",
            DryRun = true,
            SkipExisting = false,
            NoDuplicateSkip = false,
            Mode = Options.OperationMode.copy,
            DuplicatesFormat = "-{number}"
        };
    }
}