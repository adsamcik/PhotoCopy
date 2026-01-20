using AwesomeAssertions;
using NSubstitute;
using PhotoCopy.Files;
using PhotoCopy.Statistics;
using System;
using System.IO;

namespace PhotoCopy.Tests.Statistics;

public class CopyStatisticsTests
{
    #region RecordFileProcessed Tests

    [Test]
    public void RecordFileProcessed_SingleFile_IncrementsCountAndBytes()
    {
        // Arrange
        var stats = new CopyStatistics();
        var file = CreateMockFile("photo.jpg", new DateTime(2023, 6, 15), 1024);
        
        // Act
        stats.RecordFileProcessed(file, 1024);
        
        // Assert
        stats.TotalFiles.Should().Be(1);
        stats.TotalBytesProcessed.Should().Be(1024);
    }

    [Test]
    public void RecordFileProcessed_PhotoFile_IncrementsPhotosCount()
    {
        // Arrange
        var stats = new CopyStatistics();
        var jpgFile = CreateMockFile("photo.jpg", new DateTime(2023, 6, 15), 1024);
        var pngFile = CreateMockFile("image.png", new DateTime(2023, 6, 15), 2048);
        var heicFile = CreateMockFile("IMG_1234.HEIC", new DateTime(2023, 6, 15), 4096);
        
        // Act
        stats.RecordFileProcessed(jpgFile, 1024);
        stats.RecordFileProcessed(pngFile, 2048);
        stats.RecordFileProcessed(heicFile, 4096);
        
        // Assert
        stats.PhotosCount.Should().Be(3);
        stats.VideosCount.Should().Be(0);
    }

    [Test]
    public void RecordFileProcessed_VideoFile_IncrementsVideosCount()
    {
        // Arrange
        var stats = new CopyStatistics();
        var mp4File = CreateMockFile("video.mp4", new DateTime(2023, 6, 15), 1024);
        var movFile = CreateMockFile("movie.mov", new DateTime(2023, 6, 15), 2048);
        var aviFile = CreateMockFile("clip.avi", new DateTime(2023, 6, 15), 4096);
        
        // Act
        stats.RecordFileProcessed(mp4File, 1024);
        stats.RecordFileProcessed(movFile, 2048);
        stats.RecordFileProcessed(aviFile, 4096);
        
        // Assert
        stats.VideosCount.Should().Be(3);
        stats.PhotosCount.Should().Be(0);
    }

    [Test]
    public void RecordFileProcessed_MixedFiles_TracksCorrectly()
    {
        // Arrange
        var stats = new CopyStatistics();
        var photo = CreateMockFile("photo.jpg", new DateTime(2023, 6, 15), 1024);
        var video = CreateMockFile("video.mp4", new DateTime(2023, 6, 15), 2048);
        
        // Act
        stats.RecordFileProcessed(photo, 1024);
        stats.RecordFileProcessed(video, 2048);
        
        // Assert
        stats.TotalFiles.Should().Be(2);
        stats.PhotosCount.Should().Be(1);
        stats.VideosCount.Should().Be(1);
        stats.TotalBytesProcessed.Should().Be(3072);
    }

    [Test]
    public void RecordFileProcessed_WithLocation_TracksLocationStats()
    {
        // Arrange
        var stats = new CopyStatistics();
        var location = new LocationData("Manhattan", "New York", "New York County", "New York", "US");
        var file = CreateMockFileWithLocation("photo.jpg", new DateTime(2023, 6, 15), 1024, location);
        
        // Act
        stats.RecordFileProcessed(file, 1024);
        
        // Assert
        stats.FilesWithLocation.Should().Be(1);
        stats.UniqueCountriesCount.Should().Be(1);
        stats.UniqueCitiesCount.Should().Be(1);
        stats.UniqueCountries.Should().Contain("US");
        stats.UniqueCities.Should().Contain("New York");
    }

    [Test]
    public void RecordFileProcessed_MultipleLocations_TracksUniqueValues()
    {
        // Arrange
        var stats = new CopyStatistics();
        var nyLocation = new LocationData("Manhattan", "New York", "New York County", "New York", "US");
        var laLocation = new LocationData("Hollywood", "Los Angeles", "Los Angeles County", "California", "US");
        var londonLocation = new LocationData("Westminster", "London", "London", "England", "GB");
        
        var file1 = CreateMockFileWithLocation("photo1.jpg", new DateTime(2023, 6, 15), 1024, nyLocation);
        var file2 = CreateMockFileWithLocation("photo2.jpg", new DateTime(2023, 6, 16), 1024, nyLocation); // same as file1
        var file3 = CreateMockFileWithLocation("photo3.jpg", new DateTime(2023, 6, 17), 1024, laLocation);
        var file4 = CreateMockFileWithLocation("photo4.jpg", new DateTime(2023, 6, 18), 1024, londonLocation);
        
        // Act
        stats.RecordFileProcessed(file1, 1024);
        stats.RecordFileProcessed(file2, 1024);
        stats.RecordFileProcessed(file3, 1024);
        stats.RecordFileProcessed(file4, 1024);
        
        // Assert
        stats.FilesWithLocation.Should().Be(4);
        stats.UniqueCountriesCount.Should().Be(2); // US and GB
        stats.UniqueCitiesCount.Should().Be(3); // New York, Los Angeles, London
    }

    [Test]
    public void RecordFileProcessed_WithoutLocation_DoesNotIncrementLocationCount()
    {
        // Arrange
        var stats = new CopyStatistics();
        var file = CreateMockFile("photo.jpg", new DateTime(2023, 6, 15), 1024);
        
        // Act
        stats.RecordFileProcessed(file, 1024);
        
        // Assert
        stats.FilesWithLocation.Should().Be(0);
        stats.UniqueCountriesCount.Should().Be(0);
        stats.UniqueCitiesCount.Should().Be(0);
    }

    [Test]
    public void RecordFileProcessed_TracksDateRange()
    {
        // Arrange
        var stats = new CopyStatistics();
        var file1 = CreateMockFile("photo1.jpg", new DateTime(2020, 1, 15), 1024);
        var file2 = CreateMockFile("photo2.jpg", new DateTime(2023, 12, 25), 1024);
        var file3 = CreateMockFile("photo3.jpg", new DateTime(2022, 6, 10), 1024);
        
        // Act
        stats.RecordFileProcessed(file1, 1024);
        stats.RecordFileProcessed(file2, 1024);
        stats.RecordFileProcessed(file3, 1024);
        
        // Assert
        stats.EarliestDate.Should().Be(new DateTime(2020, 1, 15));
        stats.LatestDate.Should().Be(new DateTime(2023, 12, 25));
    }

    [Test]
    public void RecordFileProcessed_TracksExtensionBreakdown()
    {
        // Arrange
        var stats = new CopyStatistics();
        var jpg1 = CreateMockFile("photo1.jpg", new DateTime(2023, 6, 15), 1024);
        var jpg2 = CreateMockFile("photo2.jpg", new DateTime(2023, 6, 16), 1024);
        var png = CreateMockFile("image.png", new DateTime(2023, 6, 17), 1024);
        var mp4 = CreateMockFile("video.mp4", new DateTime(2023, 6, 18), 1024);
        
        // Act
        stats.RecordFileProcessed(jpg1, 1024);
        stats.RecordFileProcessed(jpg2, 1024);
        stats.RecordFileProcessed(png, 1024);
        stats.RecordFileProcessed(mp4, 1024);
        
        // Assert
        var breakdown = stats.ExtensionBreakdown;
        breakdown[".jpg"].Should().Be(2);
        breakdown[".png"].Should().Be(1);
        breakdown[".mp4"].Should().Be(1);
    }

    #endregion

    #region Skip/Error Recording Tests

    [Test]
    public void RecordDuplicateSkipped_IncrementsDuplicatesCount()
    {
        // Arrange
        var stats = new CopyStatistics();
        
        // Act
        stats.RecordDuplicateSkipped();
        stats.RecordDuplicateSkipped();
        
        // Assert
        stats.DuplicatesSkipped.Should().Be(2);
    }

    [Test]
    public void RecordExistingSkipped_IncrementsExistingCount()
    {
        // Arrange
        var stats = new CopyStatistics();
        
        // Act
        stats.RecordExistingSkipped();
        stats.RecordExistingSkipped();
        stats.RecordExistingSkipped();
        
        // Assert
        stats.ExistingSkipped.Should().Be(3);
    }

    [Test]
    public void RecordError_IncrementsErrorCount()
    {
        // Arrange
        var stats = new CopyStatistics();
        
        // Act
        stats.RecordError();
        
        // Assert
        stats.ErrorCount.Should().Be(1);
    }

    [Test]
    public void RecordErrors_IncrementsErrorCountByAmount()
    {
        // Arrange
        var stats = new CopyStatistics();
        
        // Act
        stats.RecordErrors(5);
        
        // Assert
        stats.ErrorCount.Should().Be(5);
    }

    #endregion

    #region Thread Safety Tests

    [Test]
    public async Task RecordFileProcessed_ConcurrentCalls_ThreadSafe()
    {
        // Arrange
        var stats = new CopyStatistics();
        var files = Enumerable.Range(1, 100)
            .Select(i => CreateMockFile($"photo{i}.jpg", new DateTime(2023, 6, 15), 1024))
            .ToList();
        
        // Act - process in parallel
        await Parallel.ForEachAsync(files, async (file, ct) =>
        {
            await Task.Yield();
            stats.RecordFileProcessed(file, 1024);
        });
        
        // Assert
        stats.TotalFiles.Should().Be(100);
        stats.TotalBytesProcessed.Should().Be(102400);
    }

    [Test]
    public async Task RecordError_ConcurrentCalls_ThreadSafe()
    {
        // Arrange
        var stats = new CopyStatistics();
        
        // Act - record errors in parallel
        var tasks = Enumerable.Range(1, 50).Select(_ => Task.Run(() => stats.RecordError()));
        await Task.WhenAll(tasks);
        
        // Assert
        stats.ErrorCount.Should().Be(50);
    }

    #endregion

    #region CreateSnapshot Tests

    [Test]
    public void CreateSnapshot_ReturnsImmutableCopy()
    {
        // Arrange
        var stats = new CopyStatistics();
        var file = CreateMockFile("photo.jpg", new DateTime(2023, 6, 15), 1024);
        stats.RecordFileProcessed(file, 1024);
        
        // Act
        var snapshot = stats.CreateSnapshot();
        stats.RecordFileProcessed(file, 1024); // Add another file after snapshot
        
        // Assert - snapshot should not change
        snapshot.TotalFiles.Should().Be(1);
        stats.TotalFiles.Should().Be(2);
    }

    [Test]
    public void CreateSnapshot_ContainsAllData()
    {
        // Arrange
        var stats = new CopyStatistics();
        var location = new LocationData("Manhattan", "New York", "New York County", "New York", "US");
        var file = CreateMockFileWithLocation("photo.jpg", new DateTime(2023, 6, 15), 1024, location);
        stats.RecordFileProcessed(file, 1024);
        stats.RecordDuplicateSkipped();
        stats.RecordExistingSkipped();
        stats.RecordError();
        
        // Act
        var snapshot = stats.CreateSnapshot();
        
        // Assert
        snapshot.TotalFiles.Should().Be(1);
        snapshot.PhotosCount.Should().Be(1);
        snapshot.VideosCount.Should().Be(0);
        snapshot.FilesWithLocation.Should().Be(1);
        snapshot.UniqueCountriesCount.Should().Be(1);
        snapshot.UniqueCitiesCount.Should().Be(1);
        snapshot.TotalBytesProcessed.Should().Be(1024);
        snapshot.EarliestDate.Should().Be(new DateTime(2023, 6, 15));
        snapshot.LatestDate.Should().Be(new DateTime(2023, 6, 15));
        snapshot.DuplicatesSkipped.Should().Be(1);
        snapshot.ExistingSkipped.Should().Be(1);
        snapshot.ErrorCount.Should().Be(1);
        snapshot.ExtensionBreakdown.Should().ContainKey(".jpg");
        snapshot.UniqueCountries.Should().Contain("US");
        snapshot.UniqueCities.Should().Contain("New York");
    }

    #endregion

    #region Reset Tests

    [Test]
    public void Reset_ClearsAllStatistics()
    {
        // Arrange
        var stats = new CopyStatistics();
        var location = new LocationData("Manhattan", "New York", "New York County", "New York", "US");
        var file = CreateMockFileWithLocation("photo.jpg", new DateTime(2023, 6, 15), 1024, location);
        stats.RecordFileProcessed(file, 1024);
        stats.RecordDuplicateSkipped();
        stats.RecordError();
        
        // Act
        stats.Reset();
        
        // Assert
        stats.TotalFiles.Should().Be(0);
        stats.PhotosCount.Should().Be(0);
        stats.VideosCount.Should().Be(0);
        stats.FilesWithLocation.Should().Be(0);
        stats.UniqueCountriesCount.Should().Be(0);
        stats.UniqueCitiesCount.Should().Be(0);
        stats.TotalBytesProcessed.Should().Be(0);
        stats.EarliestDate.Should().BeNull();
        stats.LatestDate.Should().BeNull();
        stats.DuplicatesSkipped.Should().Be(0);
        stats.ExistingSkipped.Should().Be(0);
        stats.ErrorCount.Should().Be(0);
    }

    #endregion

    #region Location with District Fallback Tests

    [Test]
    public void RecordFileProcessed_LocationWithNullCity_UsesDistrictAsCity()
    {
        // Arrange
        var stats = new CopyStatistics();
        var location = new LocationData("Small Village", null, null, null, "US");
        var file = CreateMockFileWithLocation("photo.jpg", new DateTime(2023, 6, 15), 1024, location);
        
        // Act
        stats.RecordFileProcessed(file, 1024);
        
        // Assert
        stats.UniqueCitiesCount.Should().Be(1);
        stats.UniqueCities.Should().Contain("Small Village");
    }

    #endregion

    #region Helper Methods

    private static IFile CreateMockFile(string name, DateTime date, long size)
    {
        var file = Substitute.For<IFile>();
        var fileInfo = new FileInfo(Path.Combine(Path.GetTempPath(), name));
        
        file.File.Returns(fileInfo);
        file.FileDateTime.Returns(new FileDateTime(date, DateTimeSource.ExifDateTime));
        file.Location.Returns((LocationData?)null);
        
        return file;
    }

    private static IFile CreateMockFileWithLocation(string name, DateTime date, long size, LocationData location)
    {
        var file = Substitute.For<IFile>();
        var fileInfo = new FileInfo(Path.Combine(Path.GetTempPath(), name));
        
        file.File.Returns(fileInfo);
        file.FileDateTime.Returns(new FileDateTime(date, DateTimeSource.ExifDateTime));
        file.Location.Returns(location);
        
        return file;
    }

    #endregion
}
