using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Directories;
using PhotoCopy.Files;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Files.Metadata;

namespace PhotoCopy.Tests.Integration;

[Property("Category", "Integration")]
public class FileSystemIntegrationTests
{
    private readonly PhotoCopy.Files.FileSystem _fileSystem;
    private readonly string _baseTestDirectory;
    private readonly ILogger<FileSystem> _logger;
    private readonly IOptions<PhotoCopyConfig> _options;
    private readonly IDirectoryScanner _directoryScanner;

    public FileSystemIntegrationTests()
    {
        _baseTestDirectory = Path.Combine(Path.GetTempPath(), "FileSystemIntegrationTests");
        
        // Create test dependencies directly
        _logger = Substitute.For<ILogger<FileSystem>>();
        _options = Substitute.For<IOptions<PhotoCopyConfig>>();
        _options.Value.Returns(new PhotoCopyConfig
        {
            Source = "test-source",
            Destination = "test-destination",
            RelatedFileMode = RelatedFileLookup.None
        });
        
        // Create real dependencies for integration tests
        var metadataExtractorLogger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var metadataExtractor = new FileMetadataExtractor(metadataExtractorLogger, _options);
        
        var reverseGeocodingService = Substitute.For<IReverseGeocodingService>();
        var fileWithMetadataLogger = Substitute.For<ILogger<FileWithMetadata>>();
        var checksumCalculator = new Sha256ChecksumCalculator();
        var metadataEnricher = new MetadataEnricher(new IMetadataEnrichmentStep[]
        {
            new DateTimeMetadataEnrichmentStep(metadataExtractor),
            new LocationMetadataEnrichmentStep(metadataExtractor, reverseGeocodingService),
            new ChecksumMetadataEnrichmentStep(checksumCalculator, _options)
        });
        var fileFactory = new FileFactory(metadataEnricher, fileWithMetadataLogger, _options);
        
        var scannerLogger = Substitute.For<ILogger<DirectoryScanner>>();
        _directoryScanner = new DirectoryScanner(scannerLogger, _options, fileFactory);
        
        _fileSystem = new PhotoCopy.Files.FileSystem(_logger, _directoryScanner);
        
        // Create base test directory if it doesn't exist
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
                // Give Windows a moment to release any locks
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Directory.Delete(path, true);
            }
        }
        catch (IOException)
        {
            // If we can't delete now, don't fail the test
            // The directory will be cleaned up on the next test run
        }
    }

    [Test]
    public async Task EnumerateFiles_WithValidDirectory_ReturnsFiles()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var testFile = Path.Combine(testDirectory, "test.txt");
            File.WriteAllText(testFile, "test content");

            // Act
            var files = _fileSystem.EnumerateFiles(testDirectory).ToList();

            // Assert
            await Assert.That(files.Count).IsEqualTo(1);
            await Assert.That(files[0].File.FullName).IsEqualTo(testFile);
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task CreateDirectory_CreatesNewDirectory()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Arrange
            var newDir = Path.Combine(testDirectory, "newDir");

            // Act
            _fileSystem.CreateDirectory(newDir);

            // Assert
            await Assert.That(Directory.Exists(newDir)).IsTrue();
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task DirectoryExists_WithExistingDirectory_ReturnsTrue()
    {
        var testDirectory = CreateUniqueTestDirectory();
        try
        {
            // Act
            var exists = _fileSystem.DirectoryExists(testDirectory);

            // Assert
            await Assert.That(exists).IsTrue();
        }
        finally
        {
            SafeDeleteDirectory(testDirectory);
        }
    }

    [Test]
    public async Task DirectoryExists_WithNonExistentDirectory_ReturnsFalse()
    {
        var testDirectory = Path.Combine(_baseTestDirectory, "NonExistentDirectory");
        
        // Act
        var exists = _fileSystem.DirectoryExists(testDirectory);

        // Assert
        await Assert.That(exists).IsFalse();
    }
}
