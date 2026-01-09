using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Configuration;
using PhotoCopy.Files;
using PhotoCopy.Files.Metadata;

namespace PhotoCopy.Tests.Integration;

[Property("Category", "Integration")]
public class FileFactoryIntegrationTests
{
    private readonly string _baseTestDirectory;

    public FileFactoryIntegrationTests()
    {
        _baseTestDirectory = Path.Combine(Path.GetTempPath(), "FileFactoryIntegrationTests");
        
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

    private FileFactory CreateFactory(PhotoCopyConfig? config = null)
    {
        config ??= new PhotoCopyConfig
        {
            LogLevel = OutputLevel.Verbose,
            AllowedExtensions = [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".heic"]
        };

        var options = Microsoft.Extensions.Options.Options.Create(config);
        var extractorLogger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var fileLogger = Substitute.For<ILogger<FileWithMetadata>>();
        
        var extractor = new FileMetadataExtractor(extractorLogger, options);
        var checksumCalculator = new Sha256ChecksumCalculator();
        
        var steps = new IMetadataEnrichmentStep[]
        {
            new DateTimeMetadataEnrichmentStep(extractor),
            new ChecksumMetadataEnrichmentStep(checksumCalculator, options)
        };
        
        var enricher = new MetadataEnricher(steps);
        
        return new FileFactory(enricher, fileLogger, options);
    }

    [Test]
    public async Task CreateFile_WithJpgExtension_ReturnsFileWithMetadata()
    {
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var filePath = Path.Combine(testDir, "photo.jpg");
            File.WriteAllBytes(filePath, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 });
            var fileInfo = new FileInfo(filePath);

            var factory = CreateFactory();
            var result = factory.Create(fileInfo);

            await Assert.That(result).IsTypeOf<FileWithMetadata>();
            await Assert.That(result.File.Name).IsEqualTo("photo.jpg");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task CreateFile_WithTxtExtension_ReturnsGenericFile()
    {
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var filePath = Path.Combine(testDir, "document.txt");
            File.WriteAllText(filePath, "test content");
            var fileInfo = new FileInfo(filePath);

            var factory = CreateFactory();
            var result = factory.Create(fileInfo);

            await Assert.That(result).IsTypeOf<GenericFile>();
            await Assert.That(result.File.Name).IsEqualTo("document.txt");
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task CreateFile_ExtractsDateTimeFromFile()
    {
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var filePath = Path.Combine(testDir, "test.txt");
            File.WriteAllText(filePath, "test content");
            var fileInfo = new FileInfo(filePath);

            var factory = CreateFactory();
            var result = factory.Create(fileInfo);

            await Assert.That(result.FileDateTime.Created).IsNotEqualTo(default(DateTime));
            await Assert.That(result.FileDateTime.Modified).IsNotEqualTo(default(DateTime));
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    [Test]
    public async Task CreateFile_WithCustomAllowedExtensions_RespectsConfiguration()
    {
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var config = new PhotoCopyConfig
            {
                LogLevel = OutputLevel.Verbose,
                AllowedExtensions = [".custom", ".special"]
            };

            var customFilePath = Path.Combine(testDir, "file.custom");
            File.WriteAllText(customFilePath, "custom content");
            var customFileInfo = new FileInfo(customFilePath);

            var txtFilePath = Path.Combine(testDir, "file.txt");
            File.WriteAllText(txtFilePath, "txt content");
            var txtFileInfo = new FileInfo(txtFilePath);

            var factory = CreateFactory(config);
            
            var customResult = factory.Create(customFileInfo);
            var txtResult = factory.Create(txtFileInfo);

            await Assert.That(customResult).IsTypeOf<FileWithMetadata>();
            await Assert.That(txtResult).IsTypeOf<GenericFile>();
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }

    /// <summary>
    /// Helper method to check if result matches expected type without using reflection.
    /// </summary>
    private static bool IsExpectedType(IFile result, Type expectedType)
    {
        return expectedType == typeof(FileWithMetadata) 
            ? result is FileWithMetadata 
            : result is GenericFile;
    }

    [Test]
    [Arguments(".jpg", typeof(FileWithMetadata))]
    [Arguments(".jpeg", typeof(FileWithMetadata))]
    [Arguments(".png", typeof(FileWithMetadata))]
    [Arguments(".gif", typeof(FileWithMetadata))]
    [Arguments(".bmp", typeof(FileWithMetadata))]
    [Arguments(".tiff", typeof(FileWithMetadata))]
    [Arguments(".heic", typeof(FileWithMetadata))]
    [Arguments(".doc", typeof(GenericFile))]
    [Arguments(".pdf", typeof(GenericFile))]
    public async Task CreateFile_WithVariousImageFormats_ReturnsCorrectType(string extension, Type expectedType)
    {
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var filePath = Path.Combine(testDir, $"file{extension}");
            File.WriteAllBytes(filePath, new byte[] { 0x00, 0x01, 0x02, 0x03 });
            var fileInfo = new FileInfo(filePath);

            var factory = CreateFactory();
            var result = factory.Create(fileInfo);

            await Assert.That(IsExpectedType(result, expectedType)).IsTrue();
        }
        finally
        {
            SafeDeleteDirectory(testDir);
        }
    }
}
