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
}