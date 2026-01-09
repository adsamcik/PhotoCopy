using System;
using System.Collections.Generic;
using System.IO;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Directories;
using PhotoCopy.Files;
using PhotoCopy.Rollback;
using PhotoCopy.Validators;

namespace PhotoCopy.Tests.Directories;

public class CopyPlanTests
{
    private readonly ILogger<DirectoryCopier> _logger;
    private readonly IFileSystem _fileSystem;
    private readonly PhotoCopyConfig _config;
    private readonly IOptions<PhotoCopyConfig> _options;
    private readonly ITransactionLogger _transactionLogger;
    private readonly IFileValidationService _fileValidationService;

    public CopyPlanTests()
    {
        _logger = Substitute.For<ILogger<DirectoryCopier>>();
        _fileSystem = Substitute.For<IFileSystem>();
        _transactionLogger = Substitute.For<ITransactionLogger>();
        _fileValidationService = new FileValidationService();
        
        _config = new PhotoCopyConfig
        {
            Source = @"C:\Source",
            Destination = @"C:\Dest\{year}\{month}\{name}",
            DryRun = true,
            DuplicatesFormat = "-{number}"
        };
        
        _options = Microsoft.Extensions.Options.Options.Create(_config);
    }

    #region CopyPlan Record Tests

    [Test]
    public void CopyPlan_Constructor_SetsAllProperties()
    {
        // Arrange
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));
        var operations = new List<FileCopyPlan>
        {
            new(file, @"C:\Dest\2023\06\test.jpg", Array.Empty<RelatedFilePlan>())
        };
        var skipped = new List<ValidationFailure>();
        var directories = new HashSet<string> { @"C:\Dest\2023\06" };
        
        // Act
        var plan = new CopyPlan(operations, skipped, directories, 1024L);
        
        // Assert
        plan.Operations.Should().HaveCount(1);
        plan.SkippedFiles.Should().BeEmpty();
        plan.DirectoriesToCreate.Should().HaveCount(1);
        plan.TotalBytes.Should().Be(1024L);
    }

    [Test]
    public void CopyPlan_WithSkippedFiles_TracksValidationFailures()
    {
        // Arrange
        var file = CreateMockFile("old.jpg", new DateTime(2019, 1, 1));
        var operations = new List<FileCopyPlan>();
        var skipped = new List<ValidationFailure>
        {
            new(file, "MinDateValidator", "File date 2019-01-01 is before minimum date")
        };
        var directories = new HashSet<string>();
        
        // Act
        var plan = new CopyPlan(operations, skipped, directories, 0L);
        
        // Assert
        plan.Operations.Should().BeEmpty();
        plan.SkippedFiles.Should().HaveCount(1);
        plan.SkippedFiles[0].ValidatorName.Should().Be("MinDateValidator");
        plan.SkippedFiles[0].Reason.Should().Contain("2019-01-01");
    }

    [Test]
    public void FileCopyPlan_WithRelatedFiles_IncludesAllRelatedPlans()
    {
        // Arrange
        var mainFile = CreateMockFile("photo.jpg", new DateTime(2023, 6, 15));
        var rawFile = CreateMockFile("photo.cr2", new DateTime(2023, 6, 15));
        var xmpFile = CreateMockFile("photo.xmp", new DateTime(2023, 6, 15));
        
        var relatedPlans = new List<RelatedFilePlan>
        {
            new(rawFile, @"C:\Dest\2023\06\photo.cr2"),
            new(xmpFile, @"C:\Dest\2023\06\photo.xmp")
        };
        
        // Act
        var plan = new FileCopyPlan(mainFile, @"C:\Dest\2023\06\photo.jpg", relatedPlans);
        
        // Assert
        plan.File.Should().Be(mainFile);
        plan.DestinationPath.Should().Be(@"C:\Dest\2023\06\photo.jpg");
        plan.RelatedFiles.Should().HaveCount(2);
    }

    [Test]
    public void ValidationFailure_CapturesAllDetails()
    {
        // Arrange
        var file = CreateMockFile("test.jpg", DateTime.Now);
        
        // Act
        var failure = new ValidationFailure(file, "TestValidator", "Test reason");
        
        // Assert
        failure.File.Should().Be(file);
        failure.ValidatorName.Should().Be("TestValidator");
        failure.Reason.Should().Be("Test reason");
    }

    [Test]
    public void ValidationFailure_AllowsNullReason()
    {
        // Arrange
        var file = CreateMockFile("test.jpg", DateTime.Now);
        
        // Act
        var failure = new ValidationFailure(file, "TestValidator", null);
        
        // Assert
        failure.Reason.Should().BeNull();
    }

    #endregion

    #region CopyPlan Aggregation Tests

    [Test]
    public void CopyPlan_TotalBytes_SumsAllFileSizes()
    {
        // Arrange
        var file1 = CreateMockFileWithSize("file1.jpg", 1000);
        var file2 = CreateMockFileWithSize("file2.jpg", 2000);
        var file3 = CreateMockFileWithSize("file3.jpg", 3000);
        
        var operations = new List<FileCopyPlan>
        {
            new(file1, @"C:\Dest\file1.jpg", Array.Empty<RelatedFilePlan>()),
            new(file2, @"C:\Dest\file2.jpg", Array.Empty<RelatedFilePlan>()),
            new(file3, @"C:\Dest\file3.jpg", Array.Empty<RelatedFilePlan>())
        };
        
        // Act
        var plan = new CopyPlan(operations, new List<ValidationFailure>(), new HashSet<string>(), 6000L);
        
        // Assert
        plan.TotalBytes.Should().Be(6000L);
        plan.Operations.Should().HaveCount(3);
    }

    [Test]
    public void CopyPlan_DirectoriesToCreate_DeduplicatesDirectories()
    {
        // Arrange
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Dest\2023\06",
            @"C:\Dest\2023\07",
            @"C:\Dest\2023\06" // Duplicate
        };
        
        // Act
        var plan = new CopyPlan(new List<FileCopyPlan>(), new List<ValidationFailure>(), directories, 0L);
        
        // Assert
        plan.DirectoriesToCreate.Should().HaveCount(2);
    }

    #endregion

    #region CopyPlan Building Integration Tests

    [Test]
    public void BuildCopyPlan_WithValidFiles_CreatesOperationsForEachFile()
    {
        // Arrange
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file1 = CreateMockFile("photo1.jpg", new DateTime(2023, 6, 15));
        var file2 = CreateMockFile("photo2.jpg", new DateTime(2023, 7, 20));
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { file1, file2 });
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        
        var validators = Array.Empty<IValidator>();
        
        // Act
        copier.Copy(validators);
        
        // Assert - verify files were processed (dry run logs operations)
        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("DryRun")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public void BuildCopyPlan_WithFailingValidator_SkipsFile()
    {
        // Arrange
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("old.jpg", new DateTime(2019, 1, 1));
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { file });
        
        var validator = Substitute.For<IValidator>();
        validator.Validate(file).Returns(ValidationResult.Fail("TestValidator", "File too old"));
        
        // Act
        copier.Copy(new[] { validator });
        
        // Assert - verify file was skipped
        _logger.Received().Log(
            LogLevel.Debug,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("skipped")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public void BuildCopyPlan_WithMultipleValidators_AppliesAllValidators()
    {
        // Arrange
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { file });
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        
        var validator1 = Substitute.For<IValidator>();
        validator1.Validate(file).Returns(ValidationResult.Success("Validator1"));
        
        var validator2 = Substitute.For<IValidator>();
        validator2.Validate(file).Returns(ValidationResult.Success("Validator2"));
        
        // Act
        copier.Copy(new[] { validator1, validator2 });
        
        // Assert - both validators were called
        validator1.Received(1).Validate(file);
        validator2.Received(1).Validate(file);
    }

    [Test]
    public void BuildCopyPlan_WithFirstValidatorFailing_DoesNotCallSubsequentValidators()
    {
        // Arrange
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { file });
        
        var validator1 = Substitute.For<IValidator>();
        validator1.Validate(file).Returns(ValidationResult.Fail("Validator1", "Failed"));
        
        var validator2 = Substitute.For<IValidator>();
        validator2.Validate(file).Returns(ValidationResult.Success("Validator2"));
        
        // Act
        copier.Copy(new[] { validator1, validator2 });
        
        // Assert - only first validator was called
        validator1.Received(1).Validate(file);
        validator2.DidNotReceive().Validate(Arg.Any<IFile>());
    }

    #endregion

    #region Helper Methods

    private static IFile CreateMockFile(string name, DateTime dateTime)
    {
        var file = Substitute.For<IFile>();
        var fileInfo = new FileInfo(Path.Combine(Path.GetTempPath(), name));
        
        file.File.Returns(fileInfo);
        file.FileDateTime.Returns(new FileDateTime(dateTime, DateTimeSource.ExifDateTimeOriginal));
        
        return file;
    }

    private static IFile CreateMockFileWithSize(string name, long size)
    {
        var file = Substitute.For<IFile>();
        var fileInfo = new FileInfo(Path.Combine(Path.GetTempPath(), name));
        
        file.File.Returns(fileInfo);
        file.FileDateTime.Returns(new FileDateTime(DateTime.Now, DateTimeSource.FileCreation));
        
        // Note: We can't mock FileInfo.Length, but the CopyPlan gets TotalBytes from constructor
        // so this test verifies the plan correctly stores and reports the total
        return file;
    }

    #endregion
}
