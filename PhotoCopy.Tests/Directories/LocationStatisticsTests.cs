using AwesomeAssertions;
using PhotoCopy.Directories;
using PhotoCopy.Files;

namespace PhotoCopy.Tests.Directories;

public class LocationStatisticsTests
{
    #region RecordFile Tests

    [Test]
    public void RecordFile_SingleLocation_IncrementsCount()
    {
        // Arrange
        var stats = new LocationStatistics();
        // LocationData: (District, City, County, State, Country)
        var location = new LocationData("Manhattan", "New York", "New York County", "New York", "US");
        
        // Act
        stats.RecordFile(location);
        
        // Assert
        stats.GetCount("city", "New York").Should().Be(1);
        stats.GetCount("country", "US").Should().Be(1);
        stats.GetCount("state", "New York").Should().Be(1);
        stats.GetCount("county", "New York County").Should().Be(1);
        stats.GetCount("district", "Manhattan").Should().Be(1);
    }

    [Test]
    public void RecordFile_MultipleFilesFromSameCity_IncrementsCorrectly()
    {
        // Arrange
        var stats = new LocationStatistics();
        // LocationData: (District, City, County, State, Country)
        var location = new LocationData("Manhattan", "New York", "New York County", "New York", "US");
        
        // Act
        stats.RecordFile(location);
        stats.RecordFile(location);
        stats.RecordFile(location);
        
        // Assert
        stats.GetCount("city", "New York").Should().Be(3);
    }

    [Test]
    public void RecordFile_NullLocation_DoesNotThrow()
    {
        // Arrange
        var stats = new LocationStatistics();
        
        // Act
        Action act = () => stats.RecordFile((LocationData?)null);
        
        // Assert
        act.Should().NotThrow();
    }

    [Test]
    public void RecordFile_DifferentCities_TrackedSeparately()
    {
        // Arrange
        var stats = new LocationStatistics();
        // LocationData: (District, City, County, State, Country)
        var london = new LocationData("Westminster", "London", "London", "England", "GB");
        var paris = new LocationData("1st arrondissement", "Paris", "Paris", "Île-de-France", "FR");
        
        // Act
        stats.RecordFile(london);
        stats.RecordFile(london);
        stats.RecordFile(paris);
        
        // Assert
        stats.GetCount("city", "London").Should().Be(2);
        stats.GetCount("city", "Paris").Should().Be(1);
    }

    [Test]
    public void RecordFile_NullCityValue_DoesNotTrackNullValues()
    {
        // Arrange
        var stats = new LocationStatistics();
        // LocationData: (District, City, County, State, Country)
        var location = new LocationData("District", null, null, null, "US");
        
        // Act
        stats.RecordFile(location);
        
        // Assert - Null values are not tracked, only non-empty values are counted
        stats.GetCount("city", "").Should().Be(0);
        stats.GetCount("country", "US").Should().Be(1);
        stats.GetCount("district", "District").Should().Be(1);
    }

    [Test]
    public void RecordFile_IncrementsTotalCount()
    {
        // Arrange
        var stats = new LocationStatistics();
        // LocationData: (District, City, County, State, Country)
        var location = new LocationData("Manhattan", "New York", "New York County", "New York", "US");
        
        // Act
        stats.RecordFile(location);
        stats.RecordFile(location);
        stats.RecordFile((LocationData?)null);
        
        // Assert
        stats.TotalFiles.Should().Be(3);
    }

    #endregion

    #region GetCount Tests

    [Test]
    public void GetCount_UnknownValue_ReturnsZero()
    {
        // Arrange
        var stats = new LocationStatistics();
        
        // Act
        var count = stats.GetCount("city", "NonExistent");
        
        // Assert
        count.Should().Be(0);
    }

    [Test]
    public void GetCount_CaseInsensitiveVariableName_ReturnsCount()
    {
        // Arrange
        var stats = new LocationStatistics();
        // LocationData: (District, City, County, State, Country)
        var location = new LocationData("Manhattan", "New York", "New York County", "New York", "US");
        stats.RecordFile(location);
        
        // Act & Assert
        stats.GetCount("CITY", "New York").Should().Be(1);
        stats.GetCount("City", "New York").Should().Be(1);
        stats.GetCount("city", "New York").Should().Be(1);
    }

    [Test]
    public void GetCount_UnknownVariableName_ReturnsZero()
    {
        // Arrange
        var stats = new LocationStatistics();
        // LocationData: (District, City, County, State, Country)
        var location = new LocationData("Manhattan", "New York", "New York County", "New York", "US");
        stats.RecordFile(location);
        
        // Act
        var count = stats.GetCount("unknown", "New York");
        
        // Assert
        count.Should().Be(0);
    }

    #endregion

    #region MeetsMinimumThreshold Tests

    [Test]
    public void MeetsMinimumThreshold_CountEqualsMinimum_ReturnsTrue()
    {
        // Arrange
        var stats = new LocationStatistics();
        // LocationData: (District, City, County, State, Country)
        var location = new LocationData("Manhattan", "New York", "New York County", "New York", "US");
        for (int i = 0; i < 10; i++)
            stats.RecordFile(location);
        
        // Act
        var result = stats.MeetsMinimumThreshold("city", "New York", 10);
        
        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void MeetsMinimumThreshold_CountBelowMinimum_ReturnsFalse()
    {
        // Arrange
        var stats = new LocationStatistics();
        // LocationData: (District, City, County, State, Country)
        var location = new LocationData("Manhattan", "New York", "New York County", "New York", "US");
        for (int i = 0; i < 5; i++)
            stats.RecordFile(location);
        
        // Act
        var result = stats.MeetsMinimumThreshold("city", "New York", 10);
        
        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void MeetsMinimumThreshold_CountAboveMinimum_ReturnsTrue()
    {
        // Arrange
        var stats = new LocationStatistics();
        // LocationData: (District, City, County, State, Country)
        var location = new LocationData("Manhattan", "New York", "New York County", "New York", "US");
        for (int i = 0; i < 15; i++)
            stats.RecordFile(location);
        
        // Act
        var result = stats.MeetsMinimumThreshold("city", "New York", 10);
        
        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region MeetsMaximumThreshold Tests

    [Test]
    public void MeetsMaximumThreshold_CountEqualsMaximum_ReturnsTrue()
    {
        // Arrange
        var stats = new LocationStatistics();
        // LocationData: (District, City, County, State, Country)
        var location = new LocationData("Manhattan", "New York", "New York County", "New York", "US");
        for (int i = 0; i < 10; i++)
            stats.RecordFile(location);
        
        // Act
        var result = stats.MeetsMaximumThreshold("city", "New York", 10);
        
        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void MeetsMaximumThreshold_CountBelowMaximum_ReturnsTrue()
    {
        // Arrange
        var stats = new LocationStatistics();
        // LocationData: (District, City, County, State, Country)
        var location = new LocationData("Manhattan", "New York", "New York County", "New York", "US");
        for (int i = 0; i < 5; i++)
            stats.RecordFile(location);
        
        // Act
        var result = stats.MeetsMaximumThreshold("city", "New York", 10);
        
        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void MeetsMaximumThreshold_CountAboveMaximum_ReturnsFalse()
    {
        // Arrange
        var stats = new LocationStatistics();
        // LocationData: (District, City, County, State, Country)
        var location = new LocationData("Manhattan", "New York", "New York County", "New York", "US");
        for (int i = 0; i < 15; i++)
            stats.RecordFile(location);
        
        // Act
        var result = stats.MeetsMaximumThreshold("city", "New York", 10);
        
        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Thread Safety Tests

    [Test]
    public void RecordFile_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var stats = new LocationStatistics();
        // LocationData: (District, City, County, State, Country)
        var location = new LocationData("Manhattan", "New York", "New York County", "New York", "US");
        const int threadCount = 10;
        const int filesPerThread = 100;
        
        // Act
        var tasks = Enumerable.Range(0, threadCount)
            .Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < filesPerThread; i++)
                    stats.RecordFile(location);
            }))
            .ToArray();
        
        Task.WaitAll(tasks);
        
        // Assert
        stats.GetCount("city", "New York").Should().Be(threadCount * filesPerThread);
        stats.TotalFiles.Should().Be(threadCount * filesPerThread);
    }

    #endregion
}
