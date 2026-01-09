using AwesomeAssertions;
using PhotoCopy.Configuration;

namespace PhotoCopy.Tests.Configuration;

/// <summary>
/// Unit tests for the LocationGranularity enum.
/// </summary>
public class LocationGranularityTests
{
    [Test]
    public void LocationGranularity_City_HasLowestValue()
    {
        // Assert - City is most detailed, should be the lowest enum value
        ((int)LocationGranularity.City).Should().Be(0);
    }

    [Test]
    public void LocationGranularity_County_IsAfterCity()
    {
        // Assert
        ((int)LocationGranularity.County).Should().Be(1);
    }

    [Test]
    public void LocationGranularity_State_IsAfterCounty()
    {
        // Assert
        ((int)LocationGranularity.State).Should().Be(2);
    }

    [Test]
    public void LocationGranularity_Country_HasHighestValue()
    {
        // Assert - Country is least detailed, should be the highest enum value
        ((int)LocationGranularity.Country).Should().Be(3);
    }

    [Test]
    public void LocationGranularity_ComparisonOrder_IsCorrect()
    {
        // Assert - Higher granularity (less detail) should have higher numeric value
        (LocationGranularity.City < LocationGranularity.County).Should().BeTrue();
        (LocationGranularity.County < LocationGranularity.State).Should().BeTrue();
        (LocationGranularity.State < LocationGranularity.Country).Should().BeTrue();
    }

    [Test]
    public void LocationGranularity_AllValuesAreDefined()
    {
        // Assert
        Enum.GetValues<LocationGranularity>().Should().HaveCount(4);
    }

    [Test]
    public void LocationGranularity_CanParseFromString()
    {
        // Act & Assert
        Enum.Parse<LocationGranularity>("City").Should().Be(LocationGranularity.City);
        Enum.Parse<LocationGranularity>("County").Should().Be(LocationGranularity.County);
        Enum.Parse<LocationGranularity>("State").Should().Be(LocationGranularity.State);
        Enum.Parse<LocationGranularity>("Country").Should().Be(LocationGranularity.Country);
    }

    [Test]
    public void LocationGranularity_ToString_ReturnsCorrectName()
    {
        // Act & Assert
        LocationGranularity.City.ToString().Should().Be("City");
        LocationGranularity.County.ToString().Should().Be("County");
        LocationGranularity.State.ToString().Should().Be("State");
        LocationGranularity.Country.ToString().Should().Be("Country");
    }
}
