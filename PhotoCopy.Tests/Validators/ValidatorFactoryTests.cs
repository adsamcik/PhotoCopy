using Microsoft.Extensions.Logging;
using NSubstitute;
using PhotoCopy.Validators;
using PhotoCopy.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PhotoCopy.Tests.Validators;

public class ValidatorFactoryTests
{
    [Fact]
    public void Create_ReturnsBothValidators_WhenBothMaxAndMinDatesAreProvided()
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
        Assert.Equal(2, validators.Count);
        Assert.Contains(validators, v => v.GetType() == typeof(MaxDateValidator));
        Assert.Contains(validators, v => v.GetType() == typeof(MinDateValidator));
    }

    [Fact]
    public void Create_ReturnsOnlyMaxDateValidator_WhenOnlyMaxDateIsProvided()
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
        Assert.Single(validators);
        Assert.Contains(validators, v => v.GetType() == typeof(MaxDateValidator));
    }

    [Fact]
    public void Create_ReturnsOnlyMinDateValidator_WhenOnlyMinDateIsProvided()
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
        Assert.Single(validators);
        Assert.Contains(validators, v => v.GetType() == typeof(MinDateValidator));
    }

    [Fact]
    public void Create_ReturnsEmptyCollection_WhenNeitherMaxNorMinDatesAreProvided()
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
        Assert.Empty(validators);
    }
}