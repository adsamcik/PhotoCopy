using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Files;
using PhotoCopy.Configuration;

namespace PhotoCopy.Tests.Files;

public class FileMetadataExtractorTests : TestBase
{
    [Test]
    public async Task GetDateTime_WithNoExifData_ReturnsFileCreationTime()
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
            await Assert.That(result.Created).IsEqualTo(fileInfo.CreationTime);
            await Assert.That(result.DateTime).IsEqualTo(fileInfo.CreationTime); // Main DateTime should be creation time
        }
        finally
        {
            // Cleanup
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task GetDateTime_WhenCreationTimeIsNewerThanModified_ReturnsModificationTime()
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
            await Assert.That(result.Created).IsEqualTo(fileInfo.CreationTime);
            await Assert.That(result.Modified).IsEqualTo(fileInfo.LastWriteTime);
        }
        finally
        {
            // Cleanup
            File.Delete(tempFile);
        }
    }
}