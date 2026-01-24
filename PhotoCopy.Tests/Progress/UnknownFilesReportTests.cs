using System.IO;
using System.Linq;
using AwesomeAssertions;
using PhotoCopy.Files;
using PhotoCopy.Progress;

namespace PhotoCopy.Tests.Progress;

public class UnknownFilesReportTests
{
    [Test]
    public void AddEntry_WithNoneReason_DoesNotAddEntry()
    {
        // Arrange
        var report = new UnknownFilesReport();

        // Act
        report.AddEntry("test.jpg", UnknownFileReason.None);

        // Assert
        report.Count.Should().Be(0);
    }

    [Test]
    public void AddEntry_WithNoGpsDataReason_AddsEntry()
    {
        // Arrange
        var report = new UnknownFilesReport();
        var filePath = Path.Combine("Photos", "test.jpg");

        // Act
        report.AddEntry(filePath, UnknownFileReason.NoGpsData);

        // Assert
        report.Count.Should().Be(1);
        var entries = report.GetEntries();
        entries[0].FilePath.Should().Be(filePath);
        entries[0].FileName.Should().Be("test.jpg");
        entries[0].Extension.Should().Be(".jpg");
        entries[0].Reason.Should().Be(UnknownFileReason.NoGpsData);
    }

    [Test]
    public void AddEntry_WithGpsExtractionErrorReason_AddsEntry()
    {
        // Arrange
        var report = new UnknownFilesReport();

        // Act
        report.AddEntry("photo.heic", UnknownFileReason.GpsExtractionError, "Corrupt EXIF data");

        // Assert
        report.Count.Should().Be(1);
        var entries = report.GetEntries();
        entries[0].Reason.Should().Be(UnknownFileReason.GpsExtractionError);
        entries[0].AdditionalInfo.Should().Be("Corrupt EXIF data");
    }

    [Test]
    public void AddEntry_WithGeocodingFailedReason_AddsEntry()
    {
        // Arrange
        var report = new UnknownFilesReport();

        // Act
        report.AddEntry("video.mp4", UnknownFileReason.GeocodingFailed);

        // Assert
        report.Count.Should().Be(1);
        var entries = report.GetEntries();
        entries[0].Reason.Should().Be(UnknownFileReason.GeocodingFailed);
    }

    [Test]
    public void AddEntry_WithNoExtension_SetsNoExtensionMarker()
    {
        // Arrange
        var report = new UnknownFilesReport();

        // Act
        report.AddEntry("README", UnknownFileReason.NoGpsData);

        // Assert
        var entries = report.GetEntries();
        entries[0].Extension.Should().Be("(no extension)");
    }

    [Test]
    public void Clear_RemovesAllEntries()
    {
        // Arrange
        var report = new UnknownFilesReport();
        report.AddEntry("file1.jpg", UnknownFileReason.NoGpsData);
        report.AddEntry("file2.jpg", UnknownFileReason.GeocodingFailed);
        report.Count.Should().Be(2);

        // Act
        report.Clear();

        // Assert
        report.Count.Should().Be(0);
        report.GetEntries().Should().BeEmpty();
    }

    [Test]
    public void GenerateSummary_WithNoFiles_ReturnsSummaryWithZeroCount()
    {
        // Arrange
        var report = new UnknownFilesReport();

        // Act
        var summary = report.GenerateSummary();

        // Assert
        summary.TotalCount.Should().Be(0);
        summary.ByReason.Should().BeEmpty();
        summary.ByExtension.Should().BeEmpty();
        summary.Files.Should().BeEmpty();
    }

    [Test]
    public void GenerateSummary_WithMultipleFiles_GroupsByReason()
    {
        // Arrange
        var report = new UnknownFilesReport();
        report.AddEntry("file1.jpg", UnknownFileReason.NoGpsData);
        report.AddEntry("file2.jpg", UnknownFileReason.NoGpsData);
        report.AddEntry("file3.jpg", UnknownFileReason.GeocodingFailed);

        // Act
        var summary = report.GenerateSummary();

        // Assert
        summary.TotalCount.Should().Be(3);
        summary.ByReason.Should().HaveCount(2);
        summary.ByReason[UnknownFileReason.NoGpsData].Should().Be(2);
        summary.ByReason[UnknownFileReason.GeocodingFailed].Should().Be(1);
    }

    [Test]
    public void GenerateSummary_WithMultipleFiles_GroupsByExtension()
    {
        // Arrange
        var report = new UnknownFilesReport();
        report.AddEntry("file1.jpg", UnknownFileReason.NoGpsData);
        report.AddEntry("file2.jpg", UnknownFileReason.NoGpsData);
        report.AddEntry("file3.png", UnknownFileReason.NoGpsData);
        report.AddEntry("file4.heic", UnknownFileReason.NoGpsData);

        // Act
        var summary = report.GenerateSummary();

        // Assert
        summary.ByExtension.Should().HaveCount(3);
        summary.ByExtension[".jpg"].Should().Be(2);
        summary.ByExtension[".png"].Should().Be(1);
        summary.ByExtension[".heic"].Should().Be(1);
    }

    [Test]
    public void GenerateSummary_WithIncludeFilesFalse_DoesNotIncludeFiles()
    {
        // Arrange
        var report = new UnknownFilesReport();
        report.AddEntry("file1.jpg", UnknownFileReason.NoGpsData);

        // Act
        var summary = report.GenerateSummary(includeFiles: false);

        // Assert
        summary.Files.Should().BeEmpty();
    }

    [Test]
    public void GenerateSummary_WithIncludeFilesTrue_IncludesFiles()
    {
        // Arrange
        var report = new UnknownFilesReport();
        report.AddEntry("file1.jpg", UnknownFileReason.NoGpsData);

        // Act
        var summary = report.GenerateSummary(includeFiles: true);

        // Assert
        summary.Files.Should().HaveCount(1);
        summary.Files[0].FileName.Should().Be("file1.jpg");
    }

    [Test]
    public void GenerateReport_WithNoFiles_ReturnsEmptyMessage()
    {
        // Arrange
        var report = new UnknownFilesReport();

        // Act
        var reportText = report.GenerateReport();

        // Assert
        reportText.Should().Contain("No files were placed in the Unknown folder");
    }

    [Test]
    public void GenerateReport_WithFiles_ContainsHeader()
    {
        // Arrange
        var report = new UnknownFilesReport();
        report.AddEntry("file1.jpg", UnknownFileReason.NoGpsData);

        // Act
        var reportText = report.GenerateReport();

        // Assert
        reportText.Should().Contain("UNKNOWN FILES REPORT");
    }

    [Test]
    public void GenerateReport_WithFiles_ContainsTotalCount()
    {
        // Arrange
        var report = new UnknownFilesReport();
        report.AddEntry("file1.jpg", UnknownFileReason.NoGpsData);
        report.AddEntry("file2.jpg", UnknownFileReason.GeocodingFailed);

        // Act
        var reportText = report.GenerateReport();

        // Assert
        reportText.Should().Contain("Total files without location data: 2");
    }

    [Test]
    public void GenerateReport_WithFiles_ContainsReasonBreakdown()
    {
        // Arrange
        var report = new UnknownFilesReport();
        report.AddEntry("file1.jpg", UnknownFileReason.NoGpsData);
        report.AddEntry("file2.jpg", UnknownFileReason.GeocodingFailed);

        // Act
        var reportText = report.GenerateReport();

        // Assert
        reportText.Should().Contain("No GPS data in file");
        reportText.Should().Contain("Geocoding failed");
    }

    [Test]
    public void GenerateReport_WithFiles_ContainsExtensionBreakdown()
    {
        // Arrange
        var report = new UnknownFilesReport();
        report.AddEntry("file1.jpg", UnknownFileReason.NoGpsData);
        report.AddEntry("file2.png", UnknownFileReason.NoGpsData);

        // Act
        var reportText = report.GenerateReport();

        // Assert
        reportText.Should().Contain(".jpg");
        reportText.Should().Contain(".png");
    }

    [Test]
    public void GenerateReport_WithDetailedFileList_ContainsFileNames()
    {
        // Arrange
        var report = new UnknownFilesReport();
        report.AddEntry("vacation_photo.jpg", UnknownFileReason.NoGpsData);
        report.AddEntry("family_video.mp4", UnknownFileReason.GeocodingFailed);

        // Act
        var reportText = report.GenerateReport(includeDetailedFileList: true);

        // Assert
        reportText.Should().Contain("vacation_photo.jpg");
        reportText.Should().Contain("family_video.mp4");
        reportText.Should().Contain("[NO-GPS]");
        reportText.Should().Contain("[GEO-FAIL]");
    }

    [Test]
    public void GenerateReport_WithDetailedFileListAndAdditionalInfo_ContainsInfo()
    {
        // Arrange
        var report = new UnknownFilesReport();
        report.AddEntry("corrupt.jpg", UnknownFileReason.GpsExtractionError, "Invalid EXIF data");

        // Act
        var reportText = report.GenerateReport(includeDetailedFileList: true);

        // Assert
        reportText.Should().Contain("Invalid EXIF data");
    }

    [Test]
    public void GenerateReport_WithMaxFilesToList_TruncatesFileList()
    {
        // Arrange
        var report = new UnknownFilesReport();
        for (int i = 1; i <= 10; i++)
        {
            report.AddEntry($"file{i}.jpg", UnknownFileReason.NoGpsData);
        }

        // Act
        var reportText = report.GenerateReport(includeDetailedFileList: true, maxFilesToList: 5);

        // Assert
        reportText.Should().Contain("... and 5 more files");
    }

    [Test]
    public void AddEntry_ThreadSafe_CanAddFromMultipleThreads()
    {
        // Arrange
        var report = new UnknownFilesReport();
        var tasks = Enumerable.Range(1, 100)
            .Select(i => System.Threading.Tasks.Task.Run(() => 
                report.AddEntry($"file{i}.jpg", UnknownFileReason.NoGpsData)))
            .ToArray();

        // Act
        System.Threading.Tasks.Task.WaitAll(tasks);

        // Assert
        report.Count.Should().Be(100);
    }
}
