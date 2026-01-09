using System;
using System.IO;
using System.Linq;
using PhotoCopy.Configuration;
using PhotoCopy.Validators;

namespace PhotoCopy.Tests.Validators;

/// <summary>
/// Tests for configuration validation to ensure invalid configurations are properly detected and reported.
/// </summary>
public class ConfigurationValidationTests : IDisposable
{
    private readonly ConfigurationValidator _sut;
    private readonly string _tempDir;

    public ConfigurationValidationTests()
    {
        _sut = new ConfigurationValidator();
        _tempDir = Path.Combine(Path.GetTempPath(), $"PhotoCopyTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    #region Destination Pattern Validation

    [Test]
    public async Task InvalidDestinationPattern_WithUnknownVariable_ReturnsError()
    {
        // Arrange
        var config = CreateValidConfig();
        config.Destination = @"C:\Photos\{year}\{invalid_variable}\{name}";

        // Act
        var errors = _sut.Validate(config);

        // Assert
        await Assert.That(errors).IsNotEmpty();
        var error = errors.FirstOrDefault(e => e.PropertyName == nameof(PhotoCopyConfig.Destination));
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.ErrorMessage).Contains("Unknown destination pattern variable");
        await Assert.That(error.ErrorMessage).Contains("{invalid_variable}");
    }

    [Test]
    public async Task InvalidDestinationPattern_WithMultipleUnknownVariables_ReturnsMultipleErrors()
    {
        // Arrange
        var config = CreateValidConfig();
        config.Destination = @"C:\Photos\{foo}\{bar}\{name}";

        // Act
        var errors = _sut.Validate(config);

        // Assert
        var destinationErrors = errors.Where(e => e.PropertyName == nameof(PhotoCopyConfig.Destination)).ToList();
        await Assert.That(destinationErrors.Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(destinationErrors.Any(e => e.ErrorMessage.Contains("{foo}"))).IsTrue();
        await Assert.That(destinationErrors.Any(e => e.ErrorMessage.Contains("{bar}"))).IsTrue();
    }

    [Test]
    [Property("Category", "Validation")]
    public async Task ValidDestinationPattern_WithAllKnownVariables_ReturnsNoPatternErrors()
    {
        // Arrange
        var config = CreateValidConfig();
        config.Destination = @"C:\Photos\{year}\{month}\{day}\{city}\{state}\{country}\{name}";

        // Act
        var errors = _sut.Validate(config);

        // Assert
        var patternErrors = errors.Where(e => 
            e.PropertyName == nameof(PhotoCopyConfig.Destination) && 
            e.ErrorMessage.Contains("Unknown destination pattern variable"));
        await Assert.That(patternErrors).IsEmpty();
    }

    #endregion

    #region Source Path Validation

    [Test]
    public async Task MissingSourcePath_ReturnsError()
    {
        // Arrange
        var config = CreateValidConfig();
        config.Source = string.Empty;

        // Act
        var errors = _sut.Validate(config);

        // Assert
        await Assert.That(errors).IsNotEmpty();
        var error = errors.FirstOrDefault(e => e.PropertyName == nameof(PhotoCopyConfig.Source));
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.ErrorMessage).Contains("Source path is required");
    }

    [Test]
    public async Task MissingSourcePath_WithNullValue_ReturnsError()
    {
        // Arrange
        var config = CreateValidConfig();
        config.Source = null!;

        // Act
        var errors = _sut.Validate(config);

        // Assert
        await Assert.That(errors).IsNotEmpty();
        var error = errors.FirstOrDefault(e => e.PropertyName == nameof(PhotoCopyConfig.Source));
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.ErrorMessage).Contains("Source path is required");
    }

    [Test]
    public async Task MissingSourcePath_WithWhitespaceOnly_ReturnsError()
    {
        // Arrange
        var config = CreateValidConfig();
        config.Source = "   ";

        // Act
        var errors = _sut.Validate(config);

        // Assert
        await Assert.That(errors).IsNotEmpty();
        var error = errors.FirstOrDefault(e => e.PropertyName == nameof(PhotoCopyConfig.Source));
        await Assert.That(error).IsNotNull();
    }

    [Test]
    public async Task SourcePathDoesNotExist_ReturnsError()
    {
        // Arrange
        var config = CreateValidConfig();
        config.Source = @"C:\NonExistent\Directory\That\Does\Not\Exist";

        // Act
        var errors = _sut.Validate(config);

        // Assert
        await Assert.That(errors).IsNotEmpty();
        var error = errors.FirstOrDefault(e => e.PropertyName == nameof(PhotoCopyConfig.Source));
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.ErrorMessage).Contains("does not exist");
    }

    [Test]
    public async Task SourcePathExists_ReturnsNoSourceErrors()
    {
        // Arrange
        var config = CreateValidConfig();
        config.Source = _tempDir; // Use the temp directory we created

        // Act
        var errors = _sut.Validate(config);

        // Assert
        var sourceErrors = errors.Where(e => e.PropertyName == nameof(PhotoCopyConfig.Source));
        await Assert.That(sourceErrors).IsEmpty();
    }

    #endregion

    #region Source Equals Destination Validation

    [Test]
    public async Task SourceEqualsDestination_ReturnsError()
    {
        // Arrange
        var config = CreateValidConfig();
        config.Source = _tempDir;
        config.Destination = _tempDir;

        // Act
        var errors = _sut.Validate(config);

        // Assert
        await Assert.That(errors).IsNotEmpty();
        var error = errors.FirstOrDefault(e => 
            e.PropertyName == nameof(PhotoCopyConfig.Destination) && 
            e.ErrorMessage.Contains("cannot be the same"));
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.ErrorMessage).Contains("infinite loop");
    }

    [Test]
    public async Task SourceEqualsDestination_WithTrailingSlash_ReturnsError()
    {
        // Arrange
        var config = CreateValidConfig();
        config.Source = _tempDir + Path.DirectorySeparatorChar;
        config.Destination = _tempDir;

        // Act
        var errors = _sut.Validate(config);

        // Assert
        var error = errors.FirstOrDefault(e => 
            e.PropertyName == nameof(PhotoCopyConfig.Destination) && 
            e.ErrorMessage.Contains("cannot be the same"));
        await Assert.That(error).IsNotNull();
    }

    [Test]
    public async Task DestinationInsideSource_ReturnsError()
    {
        // Arrange
        var subDir = Path.Combine(_tempDir, "SubFolder");
        Directory.CreateDirectory(subDir);
        
        var config = CreateValidConfig();
        config.Source = _tempDir;
        config.Destination = subDir + @"\{year}\{name}";

        // Act
        var errors = _sut.Validate(config);

        // Assert
        var error = errors.FirstOrDefault(e => 
            e.PropertyName == nameof(PhotoCopyConfig.Destination) && 
            e.ErrorMessage.Contains("inside the source path"));
        await Assert.That(error).IsNotNull();
    }

    [Test]
    public async Task DestinationDifferentFromSource_ReturnsNoError()
    {
        // Arrange
        var differentDir = Path.Combine(Path.GetTempPath(), $"PhotoCopyDest_{Guid.NewGuid():N}");
        
        var config = CreateValidConfig();
        config.Source = _tempDir;
        config.Destination = differentDir + @"\{year}\{name}";

        // Act
        var errors = _sut.Validate(config);

        // Assert
        var samePathErrors = errors.Where(e => 
            e.PropertyName == nameof(PhotoCopyConfig.Destination) && 
            (e.ErrorMessage.Contains("cannot be the same") || e.ErrorMessage.Contains("inside the source path")));
        await Assert.That(samePathErrors).IsEmpty();
    }

    #endregion

    #region Date Range Validation

    [Test]
    public async Task InvalidDateRange_FromAfterTo_ReturnsError()
    {
        // Arrange
        var config = CreateValidConfig();
        config.MinDate = new DateTime(2024, 6, 15);
        config.MaxDate = new DateTime(2024, 1, 1);

        // Act
        var errors = _sut.Validate(config);

        // Assert
        await Assert.That(errors).IsNotEmpty();
        var error = errors.FirstOrDefault(e => e.PropertyName == nameof(PhotoCopyConfig.MinDate));
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.ErrorMessage).Contains("cannot be after MaxDate");
    }

    [Test]
    public async Task ValidDateRange_FromBeforeTo_ReturnsNoDateErrors()
    {
        // Arrange
        var config = CreateValidConfig();
        config.MinDate = new DateTime(2024, 1, 1);
        config.MaxDate = new DateTime(2024, 12, 31);

        // Act
        var errors = _sut.Validate(config);

        // Assert
        var dateErrors = errors.Where(e => e.PropertyName == nameof(PhotoCopyConfig.MinDate));
        await Assert.That(dateErrors).IsEmpty();
    }

    [Test]
    public async Task ValidDateRange_SameDate_ReturnsNoDateErrors()
    {
        // Arrange
        var config = CreateValidConfig();
        var sameDate = new DateTime(2024, 6, 15);
        config.MinDate = sameDate;
        config.MaxDate = sameDate;

        // Act
        var errors = _sut.Validate(config);

        // Assert
        var dateErrors = errors.Where(e => e.PropertyName == nameof(PhotoCopyConfig.MinDate));
        await Assert.That(dateErrors).IsEmpty();
    }

    [Test]
    public async Task NullDateRange_ReturnsNoDateErrors()
    {
        // Arrange
        var config = CreateValidConfig();
        config.MinDate = null;
        config.MaxDate = null;

        // Act
        var errors = _sut.Validate(config);

        // Assert
        var dateErrors = errors.Where(e => e.PropertyName == nameof(PhotoCopyConfig.MinDate));
        await Assert.That(dateErrors).IsEmpty();
    }

    #endregion

    #region Destination Path Validation

    [Test]
    public async Task EmptyDestinationPath_ReturnsError()
    {
        // Arrange
        var config = CreateValidConfig();
        config.Destination = string.Empty;

        // Act
        var errors = _sut.Validate(config);

        // Assert
        await Assert.That(errors).IsNotEmpty();
        var error = errors.FirstOrDefault(e => e.PropertyName == nameof(PhotoCopyConfig.Destination));
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.ErrorMessage).Contains("Destination path is required");
    }

    [Test]
    public async Task NullDestinationPath_ReturnsError()
    {
        // Arrange
        var config = CreateValidConfig();
        config.Destination = null!;

        // Act
        var errors = _sut.Validate(config);

        // Assert
        await Assert.That(errors).IsNotEmpty();
        var error = errors.FirstOrDefault(e => e.PropertyName == nameof(PhotoCopyConfig.Destination));
        await Assert.That(error).IsNotNull();
    }

    [Test]
    public async Task WhitespaceDestinationPath_ReturnsError()
    {
        // Arrange
        var config = CreateValidConfig();
        config.Destination = "   ";

        // Act
        var errors = _sut.Validate(config);

        // Assert
        await Assert.That(errors).IsNotEmpty();
        var error = errors.FirstOrDefault(e => e.PropertyName == nameof(PhotoCopyConfig.Destination));
        await Assert.That(error).IsNotNull();
    }

    #endregion

    #region Parallelism Validation

    [Test]
    public async Task NegativeMaxDegreeOfParallelism_ReturnsError()
    {
        // Arrange
        var config = CreateValidConfig();
        config.Parallelism = -1;

        // Act
        var errors = _sut.Validate(config);

        // Assert
        await Assert.That(errors).IsNotEmpty();
        var error = errors.FirstOrDefault(e => e.PropertyName == nameof(PhotoCopyConfig.Parallelism));
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.ErrorMessage).Contains("must be a positive number");
    }

    [Test]
    public async Task ZeroParallelism_ReturnsError()
    {
        // Arrange
        var config = CreateValidConfig();
        config.Parallelism = 0;

        // Act
        var errors = _sut.Validate(config);

        // Assert
        await Assert.That(errors).IsNotEmpty();
        var error = errors.FirstOrDefault(e => e.PropertyName == nameof(PhotoCopyConfig.Parallelism));
        await Assert.That(error).IsNotNull();
    }

    [Test]
    public async Task PositiveParallelism_ReturnsNoParallelismErrors()
    {
        // Arrange
        var config = CreateValidConfig();
        config.Parallelism = 4;

        // Act
        var errors = _sut.Validate(config);

        // Assert
        var parallelismErrors = errors.Where(e => e.PropertyName == nameof(PhotoCopyConfig.Parallelism));
        await Assert.That(parallelismErrors).IsEmpty();
    }

    #endregion

    #region Duplicates Format Validation

    [Test]
    public async Task DuplicatesFormatWithoutNumber_ReturnsError()
    {
        // Arrange
        var config = CreateValidConfig();
        config.DuplicatesFormat = "-copy";

        // Act
        var errors = _sut.Validate(config);

        // Assert
        await Assert.That(errors).IsNotEmpty();
        var error = errors.FirstOrDefault(e => e.PropertyName == nameof(PhotoCopyConfig.DuplicatesFormat));
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.ErrorMessage).Contains("{number}");
    }

    [Test]
    public async Task DuplicatesFormatWithNumber_ReturnsNoErrors()
    {
        // Arrange
        var config = CreateValidConfig();
        config.DuplicatesFormat = "-{number}";

        // Act
        var errors = _sut.Validate(config);

        // Assert
        var formatErrors = errors.Where(e => e.PropertyName == nameof(PhotoCopyConfig.DuplicatesFormat));
        await Assert.That(formatErrors).IsEmpty();
    }

    [Test]
    public async Task EmptyDuplicatesFormat_ReturnsError()
    {
        // Arrange
        var config = CreateValidConfig();
        config.DuplicatesFormat = string.Empty;

        // Act
        var errors = _sut.Validate(config);

        // Assert
        await Assert.That(errors).IsNotEmpty();
        var error = errors.FirstOrDefault(e => e.PropertyName == nameof(PhotoCopyConfig.DuplicatesFormat));
        await Assert.That(error).IsNotNull();
    }

    #endregion

    #region Valid Configuration Tests

    [Test]
    [Property("Category", "Validation")]
    public async Task ValidConfiguration_PassesAllValidations()
    {
        // Arrange
        var config = CreateValidConfig();

        // Act
        var errors = _sut.Validate(config);

        // Assert
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task ValidConfiguration_WithAllVariables_PassesValidation()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = _tempDir,
            Destination = @"C:\Output\{year}\{month}\{day}\{city}\{state}\{country}\{namenoext}{ext}",
            DryRun = false,
            SkipExisting = true,
            Overwrite = false,
            Mode = OperationMode.Copy,
            LogLevel = OutputLevel.Important,
            MinDate = new DateTime(2020, 1, 1),
            MaxDate = new DateTime(2025, 12, 31),
            Parallelism = 8,
            DuplicatesFormat = "_{number}"
        };

        // Act
        var errors = _sut.Validate(config);

        // Assert
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task ValidConfiguration_MinimalSettings_PassesValidation()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = _tempDir,
            Destination = @"C:\Output\{name}",
            DuplicatesFormat = "-{number}",
            Parallelism = 1
        };

        // Act
        var errors = _sut.Validate(config);

        // Assert
        await Assert.That(errors).IsEmpty();
    }

    #endregion

    #region Multiple Errors Tests

    [Test]
    public async Task MultipleInvalidSettings_ReturnsAllErrors()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = string.Empty,
            Destination = string.Empty,
            MinDate = new DateTime(2025, 1, 1),
            MaxDate = new DateTime(2020, 1, 1),
            Parallelism = -5,
            DuplicatesFormat = "copy"
        };

        // Act
        var errors = _sut.Validate(config);

        // Assert
        await Assert.That(errors.Count).IsGreaterThanOrEqualTo(4);
        await Assert.That(errors.Any(e => e.PropertyName == nameof(PhotoCopyConfig.Source))).IsTrue();
        await Assert.That(errors.Any(e => e.PropertyName == nameof(PhotoCopyConfig.Destination))).IsTrue();
        await Assert.That(errors.Any(e => e.PropertyName == nameof(PhotoCopyConfig.MinDate))).IsTrue();
        await Assert.That(errors.Any(e => e.PropertyName == nameof(PhotoCopyConfig.Parallelism))).IsTrue();
    }

    #endregion

    #region Helper Methods

    private PhotoCopyConfig CreateValidConfig()
    {
        return new PhotoCopyConfig
        {
            Source = _tempDir,
            Destination = @"C:\Output\{year}\{month}\{name}",
            DryRun = true,
            SkipExisting = false,
            Overwrite = false,
            Mode = OperationMode.Copy,
            LogLevel = OutputLevel.Important,
            Parallelism = Environment.ProcessorCount,
            DuplicatesFormat = "-{number}"
        };
    }

    #endregion
}
