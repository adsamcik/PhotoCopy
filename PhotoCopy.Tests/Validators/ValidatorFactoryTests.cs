using Microsoft.Extensions.Logging;
using NSubstitute;
using PhotoCopy.Validators;
using PhotoCopy.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PhotoCopy.Tests.Validators;

public class ValidatorFactoryTests
{
    [Test]
    public async Task Create_ReturnsBothValidators_WhenBothMaxAndMinDatesAreProvided()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = "dummy",
            Destination = "dummy",
            MaxDate = new DateTime(2023, 12, 31),
            MinDate = new DateTime(2023, 1, 1)
        };
        
        var logger = Substitute.For<ILogger<ValidatorFactory>>();

        // Act
        IReadOnlyCollection<IValidator> validators = new ValidatorFactory(logger).Create(config);

        // Assert
        await Assert.That(validators.Count).IsEqualTo(2);
        await Assert.That(validators.Any(v => v is MaxDateValidator)).IsTrue();
        await Assert.That(validators.Any(v => v is MinDateValidator)).IsTrue();
    }

    [Test]
    public async Task Create_ReturnsOnlyMaxDateValidator_WhenOnlyMaxDateIsProvided()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = "dummy",
            Destination = "dummy",
            MaxDate = new DateTime(2023, 12, 31),
            MinDate = null
        };
        
        var logger = Substitute.For<ILogger<ValidatorFactory>>();

        // Act
        IReadOnlyCollection<IValidator> validators = new ValidatorFactory(logger).Create(config);

        // Assert
        await Assert.That(validators.Count).IsEqualTo(1);
        await Assert.That(validators.Any(v => v is MaxDateValidator)).IsTrue();
    }

    [Test]
    public async Task Create_ReturnsOnlyMinDateValidator_WhenOnlyMinDateIsProvided()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = "dummy",
            Destination = "dummy",
            MaxDate = null,
            MinDate = new DateTime(2023, 1, 1)
        };
        
        var logger = Substitute.For<ILogger<ValidatorFactory>>();

        // Act
        IReadOnlyCollection<IValidator> validators = new ValidatorFactory(logger).Create(config);

        // Assert
        await Assert.That(validators.Count).IsEqualTo(1);
        await Assert.That(validators.Any(v => v is MinDateValidator)).IsTrue();
    }

    [Test]
    public async Task Create_ReturnsEmptyCollection_WhenNeitherMaxNorMinDatesAreProvided()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = "dummy",
            Destination = "dummy",
            MaxDate = null,
            MinDate = null
        };
        
        var logger = Substitute.For<ILogger<ValidatorFactory>>();

        // Act
        IReadOnlyCollection<IValidator> validators = new ValidatorFactory(logger).Create(config);

        // Assert
        await Assert.That(validators).IsEmpty();
    }

    #region ExcludePatternMatcher Tests

    [Test]
    public async Task Create_ReturnsExcludePatternMatcher_WhenExcludePatternsProvided()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = "C:\\Photos",
            Destination = "C:\\Output",
            ExcludePatterns = new List<string> { "*.aae", "*_thumb*" }
        };
        
        var logger = Substitute.For<ILogger<ValidatorFactory>>();

        // Act
        IReadOnlyCollection<IValidator> validators = new ValidatorFactory(logger).Create(config);

        // Assert
        await Assert.That(validators.Count).IsEqualTo(1);
        await Assert.That(validators.Any(v => v is ExcludePatternMatcher)).IsTrue();
    }

    [Test]
    public async Task Create_DoesNotReturnExcludePatternMatcher_WhenExcludePatternsEmpty()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = "C:\\Photos",
            Destination = "C:\\Output",
            ExcludePatterns = new List<string>()
        };
        
        var logger = Substitute.For<ILogger<ValidatorFactory>>();

        // Act
        IReadOnlyCollection<IValidator> validators = new ValidatorFactory(logger).Create(config);

        // Assert
        await Assert.That(validators.Any(v => v is ExcludePatternMatcher)).IsFalse();
    }

    [Test]
    public async Task Create_ReturnsExcludePatternMatcher_WithDateValidators()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = "C:\\Photos",
            Destination = "C:\\Output",
            ExcludePatterns = new List<string> { "*.aae" },
            MinDate = new DateTime(2023, 1, 1),
            MaxDate = new DateTime(2023, 12, 31)
        };
        
        var logger = Substitute.For<ILogger<ValidatorFactory>>();

        // Act
        IReadOnlyCollection<IValidator> validators = new ValidatorFactory(logger).Create(config);

        // Assert
        await Assert.That(validators.Count).IsEqualTo(3);
        await Assert.That(validators.Any(v => v is ExcludePatternMatcher)).IsTrue();
        await Assert.That(validators.Any(v => v is MinDateValidator)).IsTrue();
        await Assert.That(validators.Any(v => v is MaxDateValidator)).IsTrue();
    }

    [Test]
    public async Task Create_DoesNotReturnExcludePatternMatcher_WhenExcludePatternsNull()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            Source = "C:\\Photos",
            Destination = "C:\\Output"
        };
        // ExcludePatterns defaults to empty list
        
        var logger = Substitute.For<ILogger<ValidatorFactory>>();

        // Act
        IReadOnlyCollection<IValidator> validators = new ValidatorFactory(logger).Create(config);

        // Assert
        await Assert.That(validators.Any(v => v is ExcludePatternMatcher)).IsFalse();
    }

    #endregion
}