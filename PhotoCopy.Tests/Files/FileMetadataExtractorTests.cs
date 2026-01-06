using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Files;
using PhotoCopy.Configuration;
using Xunit;

namespace PhotoCopy.Tests.Files;

public class FileMetadataExtractorTests : TestBase
{
    [Fact]
    public void GetDateTime_WithNoExifData_ReturnsFileCreationTime()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "Test content");
        var fileInfo = new FileInfo(tempFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(DefaultConfig);
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        try
        {
            // Act
            var result = extractor.GetDateTime(fileInfo);

            // Assert
            Assert.Equal(fileInfo.CreationTime, result.Created);
            Assert.Equal(fileInfo.CreationTime, result.DateTime); // Main DateTime should be creation time
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
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(DefaultConfig);
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);
        
        try
        {
            // Set creation time to be newer than last write time
            var modifiedTime = fileInfo.LastWriteTime;
            fileInfo.CreationTime = modifiedTime.AddDays(1);

            // Act
            var result = extractor.GetDateTime(fileInfo);

            // Assert
            Assert.Equal(fileInfo.CreationTime, result.Created);
            Assert.Equal(fileInfo.LastWriteTime, result.Modified);
        }
        finally
        {
            // Cleanup
            File.Delete(tempFile);
        }
    }
}