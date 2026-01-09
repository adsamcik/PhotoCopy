using System;
using System.IO;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PhotoCopy.Configuration;
using PhotoCopy.Files;
using PhotoCopy.Validators;

namespace PhotoCopy.Tests.Integration;

[Property("Category", "Integration")]
public class ValidatorsIntegrationTests
{
    private readonly string _baseTestDirectory;

    public ValidatorsIntegrationTests()
    {
        _baseTestDirectory = Path.Combine(Path.GetTempPath(), "ValidatorsIntegrationTests");
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
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Directory.Delete(path, true);
            }
        }
        catch (IOException) { }
    }

    private FileWithMetadata CreateFileWithDate(string directory, string fileName, DateTime dateTime)
    {
        var filePath = Path.Combine(directory, fileName);
        File.WriteAllText(filePath, "test content");
        var fileInfo = new FileInfo(filePath);
        var fileDateTime = new FileDateTime(dateTime, DateTimeSource.FileCreation);
        var logger = Substitute.For<ILogger<FileWithMetadata>>();
        return new FileWithMetadata(fileInfo, fileDateTime, logger);
    }

    [Test]
    public async Task MinDateValidator_FileBeforeMinDate_ReturnsFailure()
    {
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var minDate = new DateTime(2024, 6, 1);
            var fileDate = new DateTime(2024, 1, 15);
            var file = CreateFileWithDate(testDir, "old_photo.jpg", fileDate);
            var validator = new MinDateValidator(minDate);

            var result = validator.Validate(file);

            await Assert.That(result.IsValid).IsFalse();
            await Assert.That(result.ValidatorName).IsEqualTo("MinDateValidator");
            await Assert.That(result.Reason).Contains("earlier than configured min");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task MinDateValidator_FileAfterMinDate_ReturnsSuccess()
    {
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var minDate = new DateTime(2024, 1, 1);
            var fileDate = new DateTime(2024, 6, 15);
            var file = CreateFileWithDate(testDir, "recent_photo.jpg", fileDate);
            var validator = new MinDateValidator(minDate);

            var result = validator.Validate(file);

            await Assert.That(result.IsValid).IsTrue();
            await Assert.That(result.ValidatorName).IsEqualTo("MinDateValidator");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task MaxDateValidator_FileAfterMaxDate_ReturnsFailure()
    {
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var maxDate = new DateTime(2024, 6, 1);
            var fileDate = new DateTime(2024, 12, 25);
            var file = CreateFileWithDate(testDir, "future_photo.jpg", fileDate);
            var validator = new MaxDateValidator(maxDate);

            var result = validator.Validate(file);

            await Assert.That(result.IsValid).IsFalse();
            await Assert.That(result.ValidatorName).IsEqualTo("MaxDateValidator");
            await Assert.That(result.Reason).Contains("exceeds configured max");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task MaxDateValidator_FileBeforeMaxDate_ReturnsSuccess()
    {
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var maxDate = new DateTime(2024, 12, 31);
            var fileDate = new DateTime(2024, 3, 10);
            var file = CreateFileWithDate(testDir, "older_photo.jpg", fileDate);
            var validator = new MaxDateValidator(maxDate);

            var result = validator.Validate(file);

            await Assert.That(result.IsValid).IsTrue();
            await Assert.That(result.ValidatorName).IsEqualTo("MaxDateValidator");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task ValidatorFactory_WithMinAndMaxDate_CreatesBothValidators()
    {
        var logger = Substitute.For<ILogger<ValidatorFactory>>();
        var factory = new ValidatorFactory(logger);
        var config = new PhotoCopyConfig
        {
            MinDate = new DateTime(2024, 1, 1),
            MaxDate = new DateTime(2024, 12, 31)
        };

        var validators = factory.Create(config);

        await Assert.That(validators.Count).IsEqualTo(2);
        await Assert.That(validators.Any(v => v.Name == "MinDateValidator")).IsTrue();
        await Assert.That(validators.Any(v => v.Name == "MaxDateValidator")).IsTrue();
    }

    [Test]
    public async Task ValidatorFactory_WithNoDates_CreatesNoValidators()
    {
        var logger = Substitute.For<ILogger<ValidatorFactory>>();
        var factory = new ValidatorFactory(logger);
        var config = new PhotoCopyConfig();

        var validators = factory.Create(config);

        await Assert.That(validators.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CombinedValidators_FileWithinRange_AllPass()
    {
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var fileDate = new DateTime(2024, 6, 15);
            var file = CreateFileWithDate(testDir, "valid_photo.jpg", fileDate);
            var minValidator = new MinDateValidator(new DateTime(2024, 1, 1));
            var maxValidator = new MaxDateValidator(new DateTime(2024, 12, 31));

            var minResult = minValidator.Validate(file);
            var maxResult = maxValidator.Validate(file);

            await Assert.That(minResult.IsValid).IsTrue();
            await Assert.That(maxResult.IsValid).IsTrue();
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task CombinedValidators_FileOutsideRange_OneFails()
    {
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var fileDate = new DateTime(2023, 6, 15);
            var file = CreateFileWithDate(testDir, "out_of_range.jpg", fileDate);
            var minValidator = new MinDateValidator(new DateTime(2024, 1, 1));
            var maxValidator = new MaxDateValidator(new DateTime(2024, 12, 31));

            var minResult = minValidator.Validate(file);
            var maxResult = maxValidator.Validate(file);

            await Assert.That(minResult.IsValid).IsFalse();
            await Assert.That(maxResult.IsValid).IsTrue();
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task Validator_FileOnExactBoundaryDate_ReturnsSuccess()
    {
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var boundaryDate = new DateTime(2024, 6, 1);
            var file = CreateFileWithDate(testDir, "boundary_photo.jpg", boundaryDate);
            var minValidator = new MinDateValidator(boundaryDate);
            var maxValidator = new MaxDateValidator(boundaryDate);

            var minResult = minValidator.Validate(file);
            var maxResult = maxValidator.Validate(file);

            await Assert.That(minResult.IsValid).IsTrue();
            await Assert.That(maxResult.IsValid).IsTrue();
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }
}
