using AwesomeAssertions;
using PhotoCopy.Statistics;
using System;
using System.Collections.Generic;

namespace PhotoCopy.Tests.Statistics;

public class StatisticsReporterTests
{
    private readonly StatisticsReporter _reporter = new();

    #region GenerateReport Tests

    [Test]
    public void GenerateReport_EmptyStatistics_GeneratesValidReport()
    {
        // Arrange
        var snapshot = CreateEmptySnapshot();
        
        // Act
        var report = _reporter.GenerateReport(snapshot);
        
        // Assert
        report.Should().Contain("Copy Operation Summary");
        report.Should().Contain("Files processed:");
        report.Should().Contain("0");
    }

    [Test]
    public void GenerateReport_WithFiles_ShowsFilesCounts()
    {
        // Arrange
        var snapshot = CreateSnapshot(totalFiles: 100, photos: 80, videos: 20);
        
        // Act
        var report = _reporter.GenerateReport(snapshot);
        
        // Assert
        report.Should().Contain("100");
        report.Should().Contain("80");
        report.Should().Contain("20");
        report.Should().Contain("Photos:");
        report.Should().Contain("Videos:");
    }

    [Test]
    public void GenerateReport_WithLocation_ShowsLocationStats()
    {
        // Arrange
        var snapshot = CreateSnapshot(
            totalFiles: 100, 
            filesWithLocation: 75,
            countries: 5, 
            cities: 50);
        
        // Act
        var report = _reporter.GenerateReport(snapshot);
        
        // Assert
        report.Should().Contain("Location data:");
        report.Should().Contain("75");
        report.Should().Contain("75.0%"); // percentage
        report.Should().Contain("Countries:");
        report.Should().Contain("5");
        report.Should().Contain("Cities:");
        report.Should().Contain("50");
    }

    [Test]
    public void GenerateReport_WithDateRange_ShowsDates()
    {
        // Arrange
        var snapshot = CreateSnapshot(
            totalFiles: 100,
            earliestDate: new DateTime(2019, 3, 15),
            latestDate: new DateTime(2024, 12, 28));
        
        // Act
        var report = _reporter.GenerateReport(snapshot);
        
        // Assert
        report.Should().Contain("Date range:");
        report.Should().Contain("2019-03-15");
        report.Should().Contain("2024-12-28");
    }

    [Test]
    public void GenerateReport_WithBytes_FormatsSize()
    {
        // Arrange
        var snapshot = CreateSnapshot(
            totalFiles: 100,
            totalBytes: 96_000_000_000); // ~89.4 GB
        
        // Act
        var report = _reporter.GenerateReport(snapshot);
        
        // Assert
        report.Should().Contain("Total size:");
        report.Should().Contain("GB");
    }

    [Test]
    public void GenerateReport_WithSkipsAndErrors_ShowsAllCounts()
    {
        // Arrange
        var snapshot = CreateSnapshot(
            totalFiles: 100,
            duplicatesSkipped: 234,
            existingSkipped: 45,
            errorCount: 3);
        
        // Act
        var report = _reporter.GenerateReport(snapshot);
        
        // Assert
        report.Should().Contain("Duplicates skipped:");
        report.Should().Contain("234");
        report.Should().Contain("Already existing:");
        report.Should().Contain("45");
        report.Should().Contain("Errors:");
        report.Should().Contain("3");
    }

    [Test]
    public void GenerateReport_ContainsBorders()
    {
        // Arrange
        var snapshot = CreateEmptySnapshot();
        
        // Act
        var report = _reporter.GenerateReport(snapshot);
        
        // Assert
        // Check for border characters
        report.Should().Contain("═");
    }

    #endregion

    #region GenerateCompactSummary Tests

    [Test]
    public void GenerateCompactSummary_ReturnsOneLine()
    {
        // Arrange
        var snapshot = CreateSnapshot(
            totalFiles: 5277,
            photos: 4892,
            videos: 385,
            filesWithLocation: 4156,
            totalBytes: 96_000_000_000,
            duplicatesSkipped: 234,
            existingSkipped: 45,
            errorCount: 0);
        
        // Act
        var summary = _reporter.GenerateCompactSummary(snapshot);
        
        // Assert
        summary.Should().NotContain("\n");
        summary.Should().Contain("5,277");
        summary.Should().Contain("4,892");
        summary.Should().Contain("385");
    }

    #endregion

    #region FormatBytes Tests

    [Test]
    public void FormatBytes_ZeroBytes_ReturnsZeroB()
    {
        // Act
        var result = StatisticsReporter.FormatBytes(0);
        
        // Assert
        result.Should().Be("0 B");
    }

    [Test]
    public void FormatBytes_SmallBytes_ReturnsBytes()
    {
        // Act
        var result = StatisticsReporter.FormatBytes(512);
        
        // Assert
        result.Should().Be("512 B");
    }

    [Test]
    public void FormatBytes_Kilobytes_ReturnsKB()
    {
        // Act
        var result = StatisticsReporter.FormatBytes(1536); // 1.5 KB
        
        // Assert
        result.Should().Be("1.5 KB");
    }

    [Test]
    public void FormatBytes_Megabytes_ReturnsMB()
    {
        // Act
        var result = StatisticsReporter.FormatBytes(1_572_864); // 1.5 MB
        
        // Assert
        result.Should().Be("1.5 MB");
    }

    [Test]
    public void FormatBytes_Gigabytes_ReturnsGB()
    {
        // Act
        var result = StatisticsReporter.FormatBytes(96_000_000_000); // ~89.4 GB
        
        // Assert
        result.Should().Contain("GB");
    }

    [Test]
    public void FormatBytes_Terabytes_ReturnsTB()
    {
        // Act
        var result = StatisticsReporter.FormatBytes(1_099_511_627_776); // 1 TB
        
        // Assert
        result.Should().Contain("TB");
    }

    [Test]
    public void FormatBytes_NegativeBytes_ReturnsZeroB()
    {
        // Act
        var result = StatisticsReporter.FormatBytes(-100);
        
        // Assert
        result.Should().Be("0 B");
    }

    #endregion

    #region FormatNumber Tests

    [Test]
    public void FormatNumber_SmallNumber_FormatsWithoutSeparator()
    {
        // Act
        var result = StatisticsReporter.FormatNumber(100);
        
        // Assert
        result.Should().Be("100");
    }

    [Test]
    public void FormatNumber_LargeNumber_FormatsWithThousandsSeparator()
    {
        // Act
        var result = StatisticsReporter.FormatNumber(1234567);
        
        // Assert
        result.Should().Contain(","); // thousands separator
    }

    #endregion

    #region GenerateFileTypeBreakdown Tests

    [Test]
    public void GenerateFileTypeBreakdown_EmptyBreakdown_ReturnsNoFilesMessage()
    {
        // Arrange
        var snapshot = CreateEmptySnapshot();
        
        // Act
        var breakdown = _reporter.GenerateFileTypeBreakdown(snapshot);
        
        // Assert
        breakdown.Should().Contain("No files processed");
    }

    [Test]
    public void GenerateFileTypeBreakdown_WithExtensions_ShowsBreakdown()
    {
        // Arrange
        var extensions = new Dictionary<string, int>
        {
            { ".jpg", 500 },
            { ".png", 100 },
            { ".mp4", 50 }
        };
        var snapshot = CreateSnapshot(
            totalFiles: 650,
            extensionBreakdown: extensions);
        
        // Act
        var breakdown = _reporter.GenerateFileTypeBreakdown(snapshot);
        
        // Assert
        breakdown.Should().Contain("File Type Breakdown:");
        breakdown.Should().Contain(".jpg");
        breakdown.Should().Contain("500");
        breakdown.Should().Contain(".png");
        breakdown.Should().Contain(".mp4");
    }

    #endregion

    #region Helper Methods

    private static CopyStatisticsSnapshot CreateEmptySnapshot()
    {
        return new CopyStatisticsSnapshot(
            TotalFiles: 0,
            PhotosCount: 0,
            VideosCount: 0,
            FilesWithLocation: 0,
            UniqueCountriesCount: 0,
            UniqueCitiesCount: 0,
            TotalBytesProcessed: 0,
            EarliestDate: null,
            LatestDate: null,
            DuplicatesSkipped: 0,
            ExistingSkipped: 0,
            ErrorCount: 0,
            ExtensionBreakdown: new Dictionary<string, int>(),
            UniqueCountries: Array.Empty<string>(),
            UniqueCities: Array.Empty<string>());
    }

    private static CopyStatisticsSnapshot CreateSnapshot(
        int totalFiles = 0,
        int photos = 0,
        int videos = 0,
        int filesWithLocation = 0,
        int countries = 0,
        int cities = 0,
        long totalBytes = 0,
        DateTime? earliestDate = null,
        DateTime? latestDate = null,
        int duplicatesSkipped = 0,
        int existingSkipped = 0,
        int errorCount = 0,
        Dictionary<string, int>? extensionBreakdown = null)
    {
        return new CopyStatisticsSnapshot(
            TotalFiles: totalFiles,
            PhotosCount: photos,
            VideosCount: videos,
            FilesWithLocation: filesWithLocation,
            UniqueCountriesCount: countries,
            UniqueCitiesCount: cities,
            TotalBytesProcessed: totalBytes,
            EarliestDate: earliestDate,
            LatestDate: latestDate,
            DuplicatesSkipped: duplicatesSkipped,
            ExistingSkipped: existingSkipped,
            ErrorCount: errorCount,
            ExtensionBreakdown: extensionBreakdown ?? new Dictionary<string, int>(),
            UniqueCountries: Array.Empty<string>(),
            UniqueCities: Array.Empty<string>());
    }

    #endregion
}
