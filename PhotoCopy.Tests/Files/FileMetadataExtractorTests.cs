using System.IO;
using PhotoCopy.Files;

namespace PhotoCopy.Tests.Files;

public class FileMetadataExtractorTests : IClassFixture<ApplicationStateFixture>
{
    [Fact]
    public void GetDateTime_WithNoExifData_ReturnsFileCreationTime()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "Test content");
        var fileInfo = new FileInfo(tempFile);

        try
        {
            // Act
            var result = FileMetadataExtractor.GetDateTime(fileInfo);

            // Assert
            Assert.Equal(DateTimeSource.FileCreation, result.DateTimeSource);
            Assert.Equal(fileInfo.CreationTime, result.DateTime);
        }
        finally
        {
            // Cleanup
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetDateTime_WhenCreationTimeIsNewerThanModified_ReturnsModificationTime()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "Test content");
        var fileInfo = new FileInfo(tempFile);
        
        try
        {
            // Set creation time to be newer than last write time
            var modifiedTime = fileInfo.LastWriteTime;
            fileInfo.CreationTime = modifiedTime.AddDays(1);

            // Act
            var result = FileMetadataExtractor.GetDateTime(fileInfo);

            // Assert
            Assert.Equal(DateTimeSource.FileModification, result.DateTimeSource);
            Assert.Equal(modifiedTime, result.DateTime);
        }
        finally
        {
            // Cleanup
            File.Delete(tempFile);
        }
    }
}