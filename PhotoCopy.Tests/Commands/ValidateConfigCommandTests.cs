using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Abstractions;
using PhotoCopy.Commands;
using PhotoCopy.Configuration;
using PhotoCopy.Tests.TestingImplementation;

namespace PhotoCopy.Tests.Commands;

// Run tests sequentially since they write to Console
[NotInParallel]
public class ValidateConfigCommandTests
{
    private FakeLogger<ValidateConfigCommand> _logger = null!;
    private PhotoCopyConfig _config = null!;
    private IOptions<PhotoCopyConfig> _options = null!;
    private IFileSystem _fileSystem = null!;
    private TextWriter _originalOut = null!;
    private StringWriter _testOutput = null!;

    [Before(Test)]
    public void Setup()
    {
        _logger = new FakeLogger<ValidateConfigCommand>();
        _config = new PhotoCopyConfig
        {
            Source = @"C:\Source",
            Destination = @"C:\Dest\{year}\{month}\{day}\{name}{ext}",
            DryRun = true,
            Parallelism = Environment.ProcessorCount
        };
        _options = Microsoft.Extensions.Options.Options.Create(_config);
        _fileSystem = Substitute.For<IFileSystem>();
        _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(true);

        // Clear shared logs
        SharedLogs.Clear();
        
        // Capture console output
        _originalOut = Console.Out;
        _testOutput = new StringWriter();
        Console.SetOut(_testOutput);
    }

    [After(Test)]
    public void Cleanup()
    {
        Console.SetOut(_originalOut);
        _testOutput?.Dispose();
    }

    private ValidateConfigCommand CreateCommand(PhotoCopyConfig? config = null)
    {
        var opts = config != null ? Microsoft.Extensions.Options.Options.Create(config) : _options;
        return new ValidateConfigCommand(_logger, opts, _fileSystem);
    }

    #region Source Path Validation

    [Test]
    public async Task ExecuteAsync_ReturnsError_WhenSourcePathDoesNotExist()
    {
        // Arrange
        _fileSystem.DirectoryExists(_config.Source).Returns(false);
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo((int)ExitCode.Error);
        await Assert.That(command.Errors.Any(e => e.Category == ValidationCategory.Source)).IsTrue();
    }

    [Test]
    public async Task ExecuteAsync_ReturnsWarning_WhenSourcePathNotSpecified()
    {
        // Arrange
        _config.Source = string.Empty;
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(command.Warnings.Any(w => w.Category == ValidationCategory.Source)).IsTrue();
    }

    [Test]
    public async Task ExecuteAsync_ReturnsSuccess_WhenSourcePathExists()
    {
        // Arrange
        _fileSystem.DirectoryExists(_config.Source).Returns(true);
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo((int)ExitCode.Success);
        await Assert.That(command.Errors.Count).IsEqualTo(0);
    }

    #endregion

    #region Destination Pattern Validation

    [Test]
    public async Task ExecuteAsync_ReturnsError_WhenDestinationHasUnknownVariable()
    {
        // Arrange
        _config.Destination = @"C:\Dest\{yeaar}\{month}\{name}{ext}";
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo((int)ExitCode.Error);
        var error = command.Errors.FirstOrDefault(e => e.Category == ValidationCategory.DestinationPattern);
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.Message).Contains("{yeaar}");
        await Assert.That(error.Message).Contains("year");
    }

    [Test]
    public async Task ExecuteAsync_ReturnsError_WhenDestinationHasUnbalancedBraces()
    {
        // Arrange
        _config.Destination = @"C:\Dest\{year\{month}\{name}{ext}";
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo((int)ExitCode.Error);
        var error = command.Errors.FirstOrDefault(e => e.Category == ValidationCategory.DestinationPattern);
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.Message).Contains("unbalanced");
    }

    [Test]
    public async Task ExecuteAsync_AcceptsValidDestinationVariables()
    {
        // Arrange
        _config.Destination = @"C:\Dest\{year}\{month}\{day}\{city}\{country}\{name}{ext}";
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo((int)ExitCode.Success);
        await Assert.That(command.Errors.Any(e => e.Category == ValidationCategory.DestinationPattern)).IsFalse();
    }

    [Test]
    public async Task ExecuteAsync_AcceptsInlineFallbackSyntax()
    {
        // Arrange
        _config.Destination = @"C:\Dest\{year}\{city:Unknown}\{name}{ext}";
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(command.Errors.Any(e => e.Category == ValidationCategory.DestinationPattern)).IsFalse();
    }

    [Test]
    public async Task ExecuteAsync_AcceptsConditionalSyntax()
    {
        // Arrange
        _config.Destination = @"C:\Dest\{year}\{city?hasGps:NoLocation}\{name}{ext}";
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(command.Errors.Any(e => e.Category == ValidationCategory.DestinationPattern)).IsFalse();
    }

    #endregion

    #region Conflicting Options Validation

    [Test]
    public async Task ExecuteAsync_ReturnsError_WhenSkipExistingAndOverwriteBothSet()
    {
        // Arrange
        _config.SkipExisting = true;
        _config.Overwrite = true;
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo((int)ExitCode.Error);
        var error = command.Errors.FirstOrDefault(e => e.Category == ValidationCategory.ConflictingOptions);
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.Message).Contains("skip-existing");
        await Assert.That(error.Message).Contains("overwrite");
    }

    [Test]
    public async Task ExecuteAsync_ReturnsSuccess_WhenOnlySkipExistingSet()
    {
        // Arrange
        _config.SkipExisting = true;
        _config.Overwrite = false;
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(command.Errors.Any(e => e.Category == ValidationCategory.ConflictingOptions)).IsFalse();
    }

    [Test]
    public async Task ExecuteAsync_ReturnsSuccess_WhenOnlyOverwriteSet()
    {
        // Arrange
        _config.SkipExisting = false;
        _config.Overwrite = true;
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(command.Errors.Any(e => e.Category == ValidationCategory.ConflictingOptions)).IsFalse();
    }

    #endregion

    #region Exclude Patterns Validation

    [Test]
    public async Task ExecuteAsync_AcceptsValidGlobPatterns()
    {
        // Arrange
        _config.ExcludePatterns = new List<string> { "*.aae", "*_thumb*", ".trashed-*", "**/*.tmp" };
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(command.Errors.Any(e => e.Category == ValidationCategory.ExcludePatterns)).IsFalse();
    }

    [Test]
    public async Task ExecuteAsync_WarnsOnEmptyExcludePattern()
    {
        // Arrange
        _config.ExcludePatterns = new List<string> { "*.aae", "", "*.tmp" };
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        var warning = command.Warnings.FirstOrDefault(w => w.Category == ValidationCategory.ExcludePatterns);
        await Assert.That(warning).IsNotNull();
        await Assert.That(warning!.Message).Contains("Empty");
    }

    #endregion

    #region Parallelism Validation

    [Test]
    public async Task ExecuteAsync_ReturnsError_WhenParallelismIsZero()
    {
        // Arrange
        _config.Parallelism = 0;
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo((int)ExitCode.Error);
        var error = command.Errors.FirstOrDefault(e => e.Category == ValidationCategory.Parallelism);
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.Message).Contains("parallelism");
    }

    [Test]
    public async Task ExecuteAsync_ReturnsError_WhenParallelismIsNegative()
    {
        // Arrange
        _config.Parallelism = -1;
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo((int)ExitCode.Error);
        await Assert.That(command.Errors.Any(e => e.Category == ValidationCategory.Parallelism)).IsTrue();
    }

    [Test]
    public async Task ExecuteAsync_WarnsOnHighParallelism()
    {
        // Arrange
        _config.Parallelism = Environment.ProcessorCount * 5;
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        var warning = command.Warnings.FirstOrDefault(w => w.Category == ValidationCategory.Parallelism);
        await Assert.That(warning).IsNotNull();
        await Assert.That(warning!.Message).Contains("High");
    }

    #endregion

    #region MaxDepth Validation

    [Test]
    public async Task ExecuteAsync_WarnsOnNegativeMaxDepth()
    {
        // Arrange
        _config.MaxDepth = -1;
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        var warning = command.Warnings.FirstOrDefault(w => w.Category == ValidationCategory.MaxDepth);
        await Assert.That(warning).IsNotNull();
        await Assert.That(warning!.Message).Contains("Negative");
    }

    [Test]
    public async Task ExecuteAsync_AcceptsZeroMaxDepth()
    {
        // Arrange
        _config.MaxDepth = 0;
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(command.Warnings.Any(w => w.Category == ValidationCategory.MaxDepth)).IsFalse();
        await Assert.That(command.Errors.Any(e => e.Category == ValidationCategory.MaxDepth)).IsFalse();
    }

    [Test]
    public async Task ExecuteAsync_AcceptsPositiveMaxDepth()
    {
        // Arrange
        _config.MaxDepth = 5;
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(command.Warnings.Any(w => w.Category == ValidationCategory.MaxDepth)).IsFalse();
        await Assert.That(command.Errors.Any(e => e.Category == ValidationCategory.MaxDepth)).IsFalse();
    }

    #endregion

    #region Date Range Validation

    [Test]
    public async Task ExecuteAsync_ReturnsError_WhenMinDateAfterMaxDate()
    {
        // Arrange
        _config.MinDate = new DateTime(2024, 12, 31);
        _config.MaxDate = new DateTime(2024, 1, 1);
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo((int)ExitCode.Error);
        var error = command.Errors.FirstOrDefault(e => e.Category == ValidationCategory.DateRange);
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.Message).Contains("MinDate");
        await Assert.That(error.Message).Contains("MaxDate");
    }

    [Test]
    public async Task ExecuteAsync_AcceptsValidDateRange()
    {
        // Arrange
        _config.MinDate = new DateTime(2024, 1, 1);
        _config.MaxDate = new DateTime(2024, 12, 31);
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(command.Errors.Any(e => e.Category == ValidationCategory.DateRange)).IsFalse();
    }

    [Test]
    public async Task ExecuteAsync_AcceptsSameDateForMinAndMax()
    {
        // Arrange
        _config.MinDate = new DateTime(2024, 6, 15);
        _config.MaxDate = new DateTime(2024, 6, 15);
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(command.Errors.Any(e => e.Category == ValidationCategory.DateRange)).IsFalse();
    }

    #endregion

    #region Duplicates Format Validation

    [Test]
    public async Task ExecuteAsync_ReturnsError_WhenDuplicatesFormatMissingNumber()
    {
        // Arrange
        _config.DuplicatesFormat = "-copy";
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo((int)ExitCode.Error);
        var error = command.Errors.FirstOrDefault(e => e.Category == ValidationCategory.DuplicatesFormat);
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.Message).Contains("{number}");
    }

    [Test]
    public async Task ExecuteAsync_AcceptsValidDuplicatesFormat()
    {
        // Arrange
        _config.DuplicatesFormat = "-{number}";
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(command.Errors.Any(e => e.Category == ValidationCategory.DuplicatesFormat)).IsFalse();
    }

    #endregion

    #region Multiple Errors

    [Test]
    public async Task ExecuteAsync_ReportsAllErrors_WhenMultipleIssuesExist()
    {
        // Arrange
        _config.Source = @"C:\NonExistent";
        _config.Destination = @"C:\Dest\{yeaar}\{name}{ext}";
        _config.SkipExisting = true;
        _config.Overwrite = true;
        _config.Parallelism = 0;
        _fileSystem.DirectoryExists(_config.Source).Returns(false);
        
        var command = CreateCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        await Assert.That(result).IsEqualTo((int)ExitCode.Error);
        await Assert.That(command.Errors.Count).IsGreaterThanOrEqualTo(4);
    }

    #endregion

    #region Console Output

    [Test]
    public async Task ExecuteAsync_PrintsSuccessMessage_WhenConfigurationIsValid()
    {
        // Arrange
        var command = CreateCommand();

        // Act
        await command.ExecuteAsync();

        // Assert
        var output = _testOutput.ToString();
        await Assert.That(output).Contains("valid");
    }

    [Test]
    public async Task ExecuteAsync_PrintsErrorMessages_WhenConfigurationIsInvalid()
    {
        // Arrange
        _config.Destination = @"C:\Dest\{yeaar}\{name}{ext}";
        var command = CreateCommand();

        // Act
        await command.ExecuteAsync();

        // Assert
        var output = _testOutput.ToString();
        await Assert.That(output).Contains("yeaar");
    }

    #endregion

    #region Typo Suggestions

    [Test]
    public async Task ExecuteAsync_SuggestsCorrectVariable_ForCommonTypos()
    {
        // Arrange - test various common typos
        var typoTests = new[]
        {
            ("{yeaar}", "year"),
            ("{mnth}", "month"),
            ("{dat}", "day"),
            ("{citi}", "city"),
            ("{countri}", "country"),
            ("{stat}", "state"),
            ("{nmae}", "name"),
        };

        foreach (var (typo, expected) in typoTests)
        {
            _config.Destination = $@"C:\Dest\{typo}\{{name}}{{ext}}";
            var command = CreateCommand();

            // Act
            await command.ExecuteAsync();

            // Assert
            var errorWithSuggestion = command.Errors.FirstOrDefault(e => 
                e.Category == ValidationCategory.DestinationPattern &&
                e.Message.Contains("did you mean"));
            
            await Assert.That(errorWithSuggestion).IsNotNull()
                .Because($"Should suggest correction for typo {typo}");
        }
    }

    #endregion
}
