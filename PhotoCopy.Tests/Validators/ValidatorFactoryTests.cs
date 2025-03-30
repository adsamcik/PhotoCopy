using PhotoCopy.Validators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PhotoCopy.Tests.Validators;

public class ValidatorFactoryTests
{
    [Fact]
    public void Create_ReturnsBothValidators_WhenBothMaxAndMinDatesAreProvided()
    {
        // Arrange
        var options = new Options
        {
            Source = "dummy",
            Destination = "dummy",
            MaxDate = new DateTime(2023, 12, 31),
            MinDate = new DateTime(2023, 1, 1)
        };

        // Act
        IReadOnlyCollection<IValidator> validators = ValidatorFactory.Create(options);

        // Assert
        Assert.Equal(2, validators.Count);
        Assert.Contains(validators, v => v.GetType() == typeof(MaxDateValidator));
        Assert.Contains(validators, v => v.GetType() == typeof(MinDateValidator));
    }

    [Fact]
    public void Create_ReturnsOnlyMaxDateValidator_WhenOnlyMaxDateIsProvided()
    {
        // Arrange
        var options = new Options
        {
            Source = "dummy",
            Destination = "dummy",
            MaxDate = new DateTime(2023, 12, 31),
            MinDate = null
        };

        // Act
        IReadOnlyCollection<IValidator> validators = ValidatorFactory.Create(options);

        // Assert
        Assert.Single(validators);
        Assert.Contains(validators, v => v.GetType() == typeof(MaxDateValidator));
    }

    [Fact]
    public void Create_ReturnsOnlyMinDateValidator_WhenOnlyMinDateIsProvided()
    {
        // Arrange
        var options = new Options
        {
            Source = "dummy",
            Destination = "dummy",
            MaxDate = null,
            MinDate = new DateTime(2023, 1, 1)
        };

        // Act
        IReadOnlyCollection<IValidator> validators = ValidatorFactory.Create(options);

        // Assert
        Assert.Single(validators);
        Assert.Contains(validators, v => v.GetType() == typeof(MinDateValidator));
    }

    [Fact]
    public void Create_ReturnsEmptyCollection_WhenNeitherMaxNorMinDatesAreProvided()
    {
        // Arrange
        var options = new Options
        {
            Source = "dummy",
            Destination = "dummy",
            MaxDate = null,
            MinDate = null
        };

        // Act
        IReadOnlyCollection<IValidator> validators = ValidatorFactory.Create(options);

        // Assert
        Assert.Empty(validators);
    }
}