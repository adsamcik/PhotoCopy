using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Configuration;
using PhotoCopy.Files;

namespace PhotoCopy.Tests.Integration;

[Property("Category", "Integration")]
public class FileMetadataExtractorIntegrationTests
{
    private readonly FileMetadataExtractor _extractor;
    private readonly string _baseTestDirectory;
    private readonly IOptions<PhotoCopyConfig> _options;

    public FileMetadataExtractorIntegrationTests()
    {
        _baseTestDirectory = Path.Combine(Path.GetTempPath(), "FileMetadataExtractorTests");
        
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        _options = Substitute.For<IOptions<PhotoCopyConfig>>();
        _options.Value.Returns(new PhotoCopyConfig
        {
            LogLevel = OutputLevel.Verbose,
            AllowedExtensions = [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".heic"]
        });
        
        _extractor = new FileMetadataExtractor(logger, _options);
        
        if (!Directory.Exists(_baseTestDirectory))
        {
            Directory.CreateDirectory(_baseTestDirectory);
        }
    }

    private string CreateUniqueTestDirectory()
    {
        var uniquePath = Path.Combine(_baseTestDirectory, Guid.NewGuid().ToString());
        Directory.CreateDirectory(uniquePath);
        return uniquePath;
    }

    private void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Directory.Delete(path, true);
            }
        }
        catch (IOException) { }
    }

    [Test]
    public async Task GetDateTime_WithTextFile_ReturnsFileSystemDates()
    {
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var filePath = Path.Combine(testDir, "test.txt");
            File.WriteAllText(filePath, "test content");
            var fileInfo = new FileInfo(filePath);

            var result = _extractor.GetDateTime(fileInfo);

            await Assert.That(result.Created).IsNotEqualTo(default(DateTime));
            await Assert.That(result.Modified).IsNotEqualTo(default(DateTime));
            await Assert.That(result.Taken).IsEqualTo(default(DateTime));
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task GetDateTime_WithJpgWithoutExif_ReturnsFileSystemDatesAndDefaultTaken()
    {
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var filePath = Path.Combine(testDir, "fake.jpg");
            File.WriteAllBytes(filePath, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 });
            var fileInfo = new FileInfo(filePath);

            var result = _extractor.GetDateTime(fileInfo);

            await Assert.That(result.Created).IsNotEqualTo(default(DateTime));
            await Assert.That(result.Modified).IsNotEqualTo(default(DateTime));
            // Taken will be default since there's no EXIF data
            await Assert.That(result.Taken).IsEqualTo(default(DateTime));
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task GetDateTime_FileTimesMatchFileSystem()
    {
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var filePath = Path.Combine(testDir, "dated.txt");
            File.WriteAllText(filePath, "content");
            var fileInfo = new FileInfo(filePath);
            
            var expectedCreated = fileInfo.CreationTime;
            var expectedModified = fileInfo.LastWriteTime;

            var result = _extractor.GetDateTime(fileInfo);

            await Assert.That(result.Created).IsEqualTo(expectedCreated);
            await Assert.That(result.Modified).IsEqualTo(expectedModified);
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task GetCoordinates_WithTextFile_ReturnsNull()
    {
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var filePath = Path.Combine(testDir, "test.txt");
            File.WriteAllText(filePath, "test content");
            var fileInfo = new FileInfo(filePath);

            var result = _extractor.GetCoordinates(fileInfo);

            await Assert.That(result).IsNull();
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task GetCoordinates_WithJpgWithoutGps_ReturnsNull()
    {
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var filePath = Path.Combine(testDir, "no_gps.jpg");
            File.WriteAllBytes(filePath, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 });
            var fileInfo = new FileInfo(filePath);

            var result = _extractor.GetCoordinates(fileInfo);

            await Assert.That(result).IsNull();
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task GetCoordinates_WithNonImageExtension_ReturnsNull()
    {
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var filePath = Path.Combine(testDir, "document.pdf");
            File.WriteAllBytes(filePath, new byte[] { 0x25, 0x50, 0x44, 0x46 });
            var fileInfo = new FileInfo(filePath);

            var result = _extractor.GetCoordinates(fileInfo);

            await Assert.That(result).IsNull();
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    [Arguments(".jpg")]
    [Arguments(".png")]
    [Arguments(".gif")]
    [Arguments(".bmp")]
    public async Task GetDateTime_WithVariousImageTypes_HandlesGracefully(string extension)
    {
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var filePath = Path.Combine(testDir, $"test{extension}");
            File.WriteAllBytes(filePath, new byte[] { 0x00, 0x01, 0x02, 0x03 });
            var fileInfo = new FileInfo(filePath);

            var result = _extractor.GetDateTime(fileInfo);

            await Assert.That(result.Created).IsNotEqualTo(default(DateTime));
            await Assert.That(result.Modified).IsNotEqualTo(default(DateTime));
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task GetDateTime_WithEmptyFile_HandlesGracefully()
    {
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var filePath = Path.Combine(testDir, "empty.jpg");
            File.WriteAllBytes(filePath, Array.Empty<byte>());
            var fileInfo = new FileInfo(filePath);

            var result = _extractor.GetDateTime(fileInfo);

            await Assert.That(result.Created).IsNotEqualTo(default(DateTime));
            await Assert.That(result.Modified).IsNotEqualTo(default(DateTime));
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }
}
