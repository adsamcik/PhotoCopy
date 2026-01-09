using System;
using AwesomeAssertions;
using PhotoCopy.Progress;

namespace PhotoCopy.Tests.Progress;

public class CopyProgressTests
{
    #region PercentComplete Tests

    [Test]
    public void PercentComplete_WithNoFiles_ReturnsZero()
    {
        // Arrange
        var progress = new CopyProgress(
            CurrentFile: 0,
            TotalFiles: 0,
            BytesProcessed: 0,
            TotalBytes: 0,
            CurrentFileName: "",
            Elapsed: TimeSpan.Zero);

        // Act
        var result = progress.PercentComplete;

        // Assert
        result.Should().Be(0);
    }

    [Test]
    public void PercentComplete_WithHalfComplete_ReturnsFifty()
    {
        // Arrange
        var progress = new CopyProgress(
            CurrentFile: 50,
            TotalFiles: 100,
            BytesProcessed: 5000,
            TotalBytes: 10000,
            CurrentFileName: "test.jpg",
            Elapsed: TimeSpan.FromSeconds(30));

        // Act
        var result = progress.PercentComplete;

        // Assert
        result.Should().Be(50);
    }

    [Test]
    public void PercentComplete_WithAllComplete_ReturnsHundred()
    {
        // Arrange
        var progress = new CopyProgress(
            CurrentFile: 100,
            TotalFiles: 100,
            BytesProcessed: 10000,
            TotalBytes: 10000,
            CurrentFileName: "final.jpg",
            Elapsed: TimeSpan.FromMinutes(1));

        // Act
        var result = progress.PercentComplete;

        // Assert
        result.Should().Be(100);
    }

    [Test]
    public void PercentComplete_CalculatesCorrectly()
    {
        // Arrange
        var progress = new CopyProgress(
            CurrentFile: 25,
            TotalFiles: 200,
            BytesProcessed: 2500,
            TotalBytes: 20000,
            CurrentFileName: "photo.jpg",
            Elapsed: TimeSpan.FromSeconds(15));

        // Act
        var result = progress.PercentComplete;

        // Assert
        result.Should().Be(12.5);
    }

    #endregion

    #region RemainingFiles Tests

    [Test]
    public void RemainingFiles_ReturnsCorrectCount()
    {
        // Arrange
        var progress = new CopyProgress(
            CurrentFile: 30,
            TotalFiles: 100,
            BytesProcessed: 3000,
            TotalBytes: 10000,
            CurrentFileName: "image.jpg",
            Elapsed: TimeSpan.FromSeconds(30));

        // Act
        var remainingFiles = progress.TotalFiles - progress.CurrentFile;

        // Assert
        remainingFiles.Should().Be(70);
    }

    [Test]
    public void RemainingFiles_WhenAllComplete_ReturnsZero()
    {
        // Arrange
        var progress = new CopyProgress(
            CurrentFile: 50,
            TotalFiles: 50,
            BytesProcessed: 5000,
            TotalBytes: 5000,
            CurrentFileName: "last.jpg",
            Elapsed: TimeSpan.FromMinutes(2));

        // Act
        var remainingFiles = progress.TotalFiles - progress.CurrentFile;

        // Assert
        remainingFiles.Should().Be(0);
    }

    [Test]
    public void RemainingFiles_WhenNoneProcessed_ReturnsTotalFiles()
    {
        // Arrange
        var progress = new CopyProgress(
            CurrentFile: 0,
            TotalFiles: 150,
            BytesProcessed: 0,
            TotalBytes: 15000,
            CurrentFileName: "",
            Elapsed: TimeSpan.Zero);

        // Act
        var remainingFiles = progress.TotalFiles - progress.CurrentFile;

        // Assert
        remainingFiles.Should().Be(150);
    }

    #endregion

    #region BytesPerSecond (Speed) Tests

    [Test]
    public void Speed_CalculatesCorrectly()
    {
        // Arrange
        var progress = new CopyProgress(
            CurrentFile: 10,
            TotalFiles: 100,
            BytesProcessed: 10000,
            TotalBytes: 100000,
            CurrentFileName: "data.jpg",
            Elapsed: TimeSpan.FromSeconds(10));

        // Act
        var result = progress.BytesPerSecond;

        // Assert
        result.Should().Be(1000); // 10000 bytes / 10 seconds = 1000 bytes/second
    }

    [Test]
    public void Speed_WithZeroElapsedTime_ReturnsZero()
    {
        // Arrange
        var progress = new CopyProgress(
            CurrentFile: 0,
            TotalFiles: 100,
            BytesProcessed: 0,
            TotalBytes: 100000,
            CurrentFileName: "",
            Elapsed: TimeSpan.Zero);

        // Act
        var result = progress.BytesPerSecond;

        // Assert
        result.Should().Be(0);
    }

    [Test]
    public void Speed_WithLargeTransfer_CalculatesCorrectly()
    {
        // Arrange - 1GB transferred in 100 seconds = 10MB/s
        var progress = new CopyProgress(
            CurrentFile: 50,
            TotalFiles: 100,
            BytesProcessed: 1_073_741_824, // 1 GB
            TotalBytes: 2_147_483_648, // 2 GB
            CurrentFileName: "large.mp4",
            Elapsed: TimeSpan.FromSeconds(100));

        // Act
        var result = progress.BytesPerSecond;

        // Assert
        result.Should().BeApproximately(10_737_418.24, 0.01); // ~10.24 MB/s
    }

    [Test]
    public void Speed_WithFractionalSeconds_CalculatesCorrectly()
    {
        // Arrange
        var progress = new CopyProgress(
            CurrentFile: 5,
            TotalFiles: 10,
            BytesProcessed: 5000,
            TotalBytes: 10000,
            CurrentFileName: "file.jpg",
            Elapsed: TimeSpan.FromMilliseconds(2500)); // 2.5 seconds

        // Act
        var result = progress.BytesPerSecond;

        // Assert
        result.Should().Be(2000); // 5000 bytes / 2.5 seconds = 2000 bytes/second
    }

    #endregion

    #region EstimatedTimeRemaining Tests

    [Test]
    public void TimeRemaining_EstimatesCorrectly()
    {
        // Arrange - 10 files done in 10 seconds, 90 remaining
        var progress = new CopyProgress(
            CurrentFile: 10,
            TotalFiles: 100,
            BytesProcessed: 1000,
            TotalBytes: 10000,
            CurrentFileName: "current.jpg",
            Elapsed: TimeSpan.FromSeconds(10));

        // Act
        var result = progress.EstimatedTimeRemaining;

        // Assert
        result.Should().NotBeNull();
        result!.Value.TotalSeconds.Should().Be(90); // 90 files * 1 second per file
    }

    [Test]
    public void TimeRemaining_WhenNoFilesProcessed_ReturnsNull()
    {
        // Arrange
        var progress = new CopyProgress(
            CurrentFile: 0,
            TotalFiles: 100,
            BytesProcessed: 0,
            TotalBytes: 10000,
            CurrentFileName: "",
            Elapsed: TimeSpan.Zero);

        // Act
        var result = progress.EstimatedTimeRemaining;

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void TimeRemaining_WhenElapsedIsZero_ReturnsNull()
    {
        // Arrange
        var progress = new CopyProgress(
            CurrentFile: 5,
            TotalFiles: 100,
            BytesProcessed: 500,
            TotalBytes: 10000,
            CurrentFileName: "test.jpg",
            Elapsed: TimeSpan.Zero);

        // Act
        var result = progress.EstimatedTimeRemaining;

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void TimeRemaining_WhenAllComplete_ReturnsZero()
    {
        // Arrange
        var progress = new CopyProgress(
            CurrentFile: 100,
            TotalFiles: 100,
            BytesProcessed: 10000,
            TotalBytes: 10000,
            CurrentFileName: "final.jpg",
            Elapsed: TimeSpan.FromMinutes(5));

        // Act
        var result = progress.EstimatedTimeRemaining;

        // Assert
        result.Should().NotBeNull();
        result!.Value.TotalSeconds.Should().Be(0);
    }

    [Test]
    public void TimeRemaining_WithVariedProgress_EstimatesCorrectly()
    {
        // Arrange - 25 files done in 50 seconds (2 seconds per file), 75 remaining
        var progress = new CopyProgress(
            CurrentFile: 25,
            TotalFiles: 100,
            BytesProcessed: 2500,
            TotalBytes: 10000,
            CurrentFileName: "photo.jpg",
            Elapsed: TimeSpan.FromSeconds(50));

        // Act
        var result = progress.EstimatedTimeRemaining;

        // Assert
        result.Should().NotBeNull();
        result!.Value.TotalSeconds.Should().Be(150); // 75 files * 2 seconds per file
    }

    #endregion

    #region Record Equality Tests

    [Test]
    public void CopyProgress_WithSameValues_AreEqual()
    {
        // Arrange
        var progress1 = new CopyProgress(10, 100, 1000, 10000, "test.jpg", TimeSpan.FromSeconds(5));
        var progress2 = new CopyProgress(10, 100, 1000, 10000, "test.jpg", TimeSpan.FromSeconds(5));

        // Act & Assert
        progress1.Should().Be(progress2);
    }

    [Test]
    public void CopyProgress_WithDifferentValues_AreNotEqual()
    {
        // Arrange
        var progress1 = new CopyProgress(10, 100, 1000, 10000, "test.jpg", TimeSpan.FromSeconds(5));
        var progress2 = new CopyProgress(20, 100, 2000, 10000, "test.jpg", TimeSpan.FromSeconds(10));

        // Act & Assert
        progress1.Should().NotBe(progress2);
    }

    #endregion
}
