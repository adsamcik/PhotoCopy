using System;
using AwesomeAssertions;
using PhotoCopy.Files;

namespace PhotoCopy.Tests.Files;

public class GpsLocationIndexTests
{
    #region AddLocation Tests

    [Test]
    public void AddLocation_StoresLocationInIndex()
    {
        // Arrange
        var index = new GpsLocationIndex();
        var timestamp = new DateTime(2024, 7, 20, 10, 30, 0);

        // Act
        index.AddLocation(timestamp, 48.8566, 2.3522); // Paris

        // Assert
        index.Count.Should().Be(1);
    }

    [Test]
    public void AddLocation_MultipleLocations_StoresAll()
    {
        // Arrange
        var index = new GpsLocationIndex();

        // Act
        index.AddLocation(new DateTime(2024, 7, 20, 10, 0, 0), 48.8566, 2.3522);
        index.AddLocation(new DateTime(2024, 7, 20, 11, 0, 0), 51.5074, -0.1278);
        index.AddLocation(new DateTime(2024, 7, 20, 12, 0, 0), 40.7128, -74.0060);

        // Assert
        index.Count.Should().Be(3);
    }

    #endregion

    #region FindNearest Tests

    [Test]
    public void FindNearest_ExactMatch_ReturnsLocation()
    {
        // Arrange
        var index = new GpsLocationIndex();
        var timestamp = new DateTime(2024, 7, 20, 10, 30, 0);
        index.AddLocation(timestamp, 48.8566, 2.3522);

        // Act
        var result = index.FindNearest(timestamp, TimeSpan.FromMinutes(5));

        // Assert
        result.Should().NotBeNull();
        result!.Value.Latitude.Should().BeApproximately(48.8566, 0.0001);
        result!.Value.Longitude.Should().BeApproximately(2.3522, 0.0001);
    }

    [Test]
    public void FindNearest_WithinWindow_ReturnsNearest()
    {
        // Arrange
        var index = new GpsLocationIndex();
        index.AddLocation(new DateTime(2024, 7, 20, 10, 0, 0), 48.8566, 2.3522);

        // Search for 10:02, should find 10:00 within 5 minute window
        var searchTime = new DateTime(2024, 7, 20, 10, 2, 0);

        // Act
        var result = index.FindNearest(searchTime, TimeSpan.FromMinutes(5));

        // Assert
        result.Should().NotBeNull();
        result!.Value.Latitude.Should().BeApproximately(48.8566, 0.0001);
    }

    [Test]
    public void FindNearest_OutsideWindow_ReturnsNull()
    {
        // Arrange
        var index = new GpsLocationIndex();
        index.AddLocation(new DateTime(2024, 7, 20, 10, 0, 0), 48.8566, 2.3522);

        // Search for 10:10, outside 5 minute window from 10:00
        var searchTime = new DateTime(2024, 7, 20, 10, 10, 0);

        // Act
        var result = index.FindNearest(searchTime, TimeSpan.FromMinutes(5));

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void FindNearest_EmptyIndex_ReturnsNull()
    {
        // Arrange
        var index = new GpsLocationIndex();
        var searchTime = new DateTime(2024, 7, 20, 10, 0, 0);

        // Act
        var result = index.FindNearest(searchTime, TimeSpan.FromMinutes(5));

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void FindNearest_MultipleLocations_ReturnsClosest()
    {
        // Arrange
        var index = new GpsLocationIndex();
        index.AddLocation(new DateTime(2024, 7, 20, 10, 0, 0), 48.8566, 2.3522);   // Paris
        index.AddLocation(new DateTime(2024, 7, 20, 10, 5, 0), 51.5074, -0.1278);  // London
        index.AddLocation(new DateTime(2024, 7, 20, 10, 10, 0), 40.7128, -74.0060); // New York

        // Search for 10:04, closest to 10:05 (London)
        var searchTime = new DateTime(2024, 7, 20, 10, 4, 0);

        // Act
        var result = index.FindNearest(searchTime, TimeSpan.FromMinutes(5));

        // Assert
        result.Should().NotBeNull();
        result!.Value.Latitude.Should().BeApproximately(51.5074, 0.0001); // London
        result!.Value.Longitude.Should().BeApproximately(-0.1278, 0.0001);
    }

    [Test]
    public void FindNearest_BeforeFirst_ReturnsFirstIfWithinWindow()
    {
        // Arrange
        var index = new GpsLocationIndex();
        index.AddLocation(new DateTime(2024, 7, 20, 10, 5, 0), 48.8566, 2.3522);

        // Search for 10:02, 3 minutes before 10:05
        var searchTime = new DateTime(2024, 7, 20, 10, 2, 0);

        // Act
        var result = index.FindNearest(searchTime, TimeSpan.FromMinutes(5));

        // Assert
        result.Should().NotBeNull();
        result!.Value.Latitude.Should().BeApproximately(48.8566, 0.0001);
    }

    [Test]
    public void FindNearest_AfterLast_ReturnsLastIfWithinWindow()
    {
        // Arrange
        var index = new GpsLocationIndex();
        index.AddLocation(new DateTime(2024, 7, 20, 10, 0, 0), 48.8566, 2.3522);

        // Search for 10:03, 3 minutes after 10:00
        var searchTime = new DateTime(2024, 7, 20, 10, 3, 0);

        // Act
        var result = index.FindNearest(searchTime, TimeSpan.FromMinutes(5));

        // Assert
        result.Should().NotBeNull();
        result!.Value.Latitude.Should().BeApproximately(48.8566, 0.0001);
    }

    [Test]
    public void FindNearest_ExactWindowBoundary_ReturnsLocation()
    {
        // Arrange
        var index = new GpsLocationIndex();
        index.AddLocation(new DateTime(2024, 7, 20, 10, 0, 0), 48.8566, 2.3522);

        // Search for exactly 5 minutes after
        var searchTime = new DateTime(2024, 7, 20, 10, 5, 0);

        // Act
        var result = index.FindNearest(searchTime, TimeSpan.FromMinutes(5));

        // Assert
        result.Should().NotBeNull();
    }

    [Test]
    public void FindNearest_JustOutsideWindow_ReturnsNull()
    {
        // Arrange
        var index = new GpsLocationIndex();
        index.AddLocation(new DateTime(2024, 7, 20, 10, 0, 0), 48.8566, 2.3522);

        // Search for 5 minutes and 1 second after
        var searchTime = new DateTime(2024, 7, 20, 10, 5, 1);

        // Act
        var result = index.FindNearest(searchTime, TimeSpan.FromMinutes(5));

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void FindNearest_UnsortedInserts_StillFindsNearest()
    {
        // Arrange
        var index = new GpsLocationIndex();
        // Insert out of order
        index.AddLocation(new DateTime(2024, 7, 20, 10, 10, 0), 40.7128, -74.0060); // New York
        index.AddLocation(new DateTime(2024, 7, 20, 10, 0, 0), 48.8566, 2.3522);   // Paris
        index.AddLocation(new DateTime(2024, 7, 20, 10, 5, 0), 51.5074, -0.1278);  // London

        // Search for 10:06, closest to 10:05 (London)
        var searchTime = new DateTime(2024, 7, 20, 10, 6, 0);

        // Act
        var result = index.FindNearest(searchTime, TimeSpan.FromMinutes(5));

        // Assert
        result.Should().NotBeNull();
        result!.Value.Latitude.Should().BeApproximately(51.5074, 0.0001); // London
    }

    #endregion

    #region Clear Tests

    [Test]
    public void Clear_RemovesAllLocations()
    {
        // Arrange
        var index = new GpsLocationIndex();
        index.AddLocation(new DateTime(2024, 7, 20, 10, 0, 0), 48.8566, 2.3522);
        index.AddLocation(new DateTime(2024, 7, 20, 11, 0, 0), 51.5074, -0.1278);

        // Act
        index.Clear();

        // Assert
        index.Count.Should().Be(0);
        index.FindNearest(new DateTime(2024, 7, 20, 10, 0, 0), TimeSpan.FromMinutes(60)).Should().BeNull();
    }

    #endregion

    #region Edge Cases

    [Test]
    public void FindNearest_LargeDataset_FindsCorrectNearest()
    {
        // Arrange
        var index = new GpsLocationIndex();
        var baseTime = new DateTime(2024, 7, 20, 0, 0, 0);
        
        // Add 1000 entries, 1 minute apart
        for (int i = 0; i < 1000; i++)
        {
            index.AddLocation(baseTime.AddMinutes(i), 40.0 + i * 0.001, -74.0 + i * 0.001);
        }

        // Search for entry 500 (at 8:20)
        var searchTime = baseTime.AddMinutes(500);

        // Act
        var result = index.FindNearest(searchTime, TimeSpan.FromMinutes(1));

        // Assert
        result.Should().NotBeNull();
        result!.Value.Latitude.Should().BeApproximately(40.0 + 500 * 0.001, 0.0001);
    }

    [Test]
    public void FindNearest_NegativeTimeDifference_FindsBefore()
    {
        // Arrange
        var index = new GpsLocationIndex();
        index.AddLocation(new DateTime(2024, 7, 20, 10, 10, 0), 48.8566, 2.3522);

        // Search for 10:08, 2 minutes BEFORE the stored location
        var searchTime = new DateTime(2024, 7, 20, 10, 8, 0);

        // Act
        var result = index.FindNearest(searchTime, TimeSpan.FromMinutes(5));

        // Assert
        result.Should().NotBeNull();
        result!.Value.Latitude.Should().BeApproximately(48.8566, 0.0001);
    }

    #endregion
}
