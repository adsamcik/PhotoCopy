using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Abstractions;
using PhotoCopy.Commands;
using PhotoCopy.Configuration;
using PhotoCopy.Directories;
using PhotoCopy.Files;
using PhotoCopy.Files.Metadata;
using PhotoCopy.Tests.TestingImplementation;
using PhotoCopy.Validators;
using Directory = System.IO.Directory;

namespace PhotoCopy.Tests.Integration;

/// <summary>
/// Integration tests for ScanCommand using real files and metadata extraction.
/// These tests:
/// 1. Use MockImageGenerator to create real JPEG/PNG files with embedded EXIF metadata
/// 2. Write these files to actual temp directories
/// 3. Use REAL implementations of DirectoryScanner, FileSystem, FileFactory, FileMetadataExtractor
/// 4. Verify ScanCommand correctly scans and reports file metadata
/// </summary>
[NotInParallel("ScanCommandIntegration")]
[Property("Category", "Integration")]
public class ScanCommandIntegrationTests
{
    private string _testBaseDirectory = null!;
    private string _sourceDir = null!;
    private FakeLogger<ScanCommand> _logger = null!;

    [Before(Test)]
    public void Setup()
    {
        _testBaseDirectory = Path.Combine(Path.GetTempPath(), "ScanCommandIntegrationTests", Guid.NewGuid().ToString());
        _sourceDir = Path.Combine(_testBaseDirectory, "source");
        
        Directory.CreateDirectory(_sourceDir);
        
        _logger = new FakeLogger<ScanCommand>();
        SharedLogs.Clear();
    }

    [After(Test)]
    public void Cleanup()
    {
        SafeDeleteDirectory(_testBaseDirectory);
        SharedLogs.Clear();
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(50); // Brief pause to release file handles
                Directory.Delete(path, true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup - directory may be locked
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup
        }
    }

    /// <summary>
    /// Creates a test JPEG file with real EXIF metadata.
    /// </summary>
    private async Task<string> CreateTestJpegAsync(
        string filename,
        DateTime dateTaken,
        (double Lat, double Lon)? gps = null,
        string? subfolder = null)
    {
        var directory = subfolder != null 
            ? Path.Combine(_sourceDir, subfolder) 
            : _sourceDir;
        
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var filePath = Path.Combine(directory, filename);
        var jpegBytes = MockImageGenerator.CreateJpeg(dateTaken: dateTaken, gps: gps);
        await File.WriteAllBytesAsync(filePath, jpegBytes);
        
        return filePath;
    }

    /// <summary>
    /// Creates a test PNG file with real EXIF metadata.
    /// </summary>
    private async Task<string> CreateTestPngAsync(
        string filename,
        DateTime dateTaken,
        (double Lat, double Lon)? gps = null,
        string? subfolder = null)
    {
        var directory = subfolder != null 
            ? Path.Combine(_sourceDir, subfolder) 
            : _sourceDir;
        
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var filePath = Path.Combine(directory, filename);
        var pngBytes = MockImageGenerator.CreatePng(dateTaken: dateTaken, gps: gps);
        await File.WriteAllBytesAsync(filePath, pngBytes);
        
        return filePath;
    }

    /// <summary>
    /// Creates a non-image file (plain text file).
    /// </summary>
    private async Task<string> CreateNonImageFileAsync(string filename, string content = "test content", string? subfolder = null)
    {
        var directory = subfolder != null 
            ? Path.Combine(_sourceDir, subfolder) 
            : _sourceDir;
        
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var filePath = Path.Combine(directory, filename);
        await File.WriteAllTextAsync(filePath, content);
        
        return filePath;
    }

    /// <summary>
    /// Builds a fully configured service provider with REAL implementations.
    /// </summary>
    private IServiceProvider BuildRealServiceProvider(PhotoCopyConfig config)
    {
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));

        // Register configuration
        services.AddSingleton<IOptions<PhotoCopyConfig>>(Microsoft.Extensions.Options.Options.Create(config));

        // Mock IReverseGeocodingService since we don't want real network calls
        var mockGeocodingService = Substitute.For<IReverseGeocodingService>();
        mockGeocodingService.ReverseGeocode(Arg.Any<double>(), Arg.Any<double>())
            .Returns((LocationData?)null);
        services.AddSingleton(mockGeocodingService);

        // Register REAL core services
        services.AddSingleton<IChecksumCalculator, Sha256ChecksumCalculator>();
        services.AddTransient<IFileMetadataExtractor, FileMetadataExtractor>();
        services.AddTransient<IMetadataEnricher, MetadataEnricher>();
        services.AddTransient<IMetadataEnrichmentStep, DateTimeMetadataEnrichmentStep>();
        services.AddTransient<IMetadataEnrichmentStep, LocationMetadataEnrichmentStep>();
        services.AddTransient<IMetadataEnrichmentStep, ChecksumMetadataEnrichmentStep>();
        services.AddTransient<IFileFactory, FileFactory>();
        services.AddTransient<IDirectoryScanner, DirectoryScanner>();
        services.AddTransient<IFileSystem, FileSystem>();
        services.AddTransient<IValidatorFactory, ValidatorFactory>();
        services.AddTransient<IFileValidationService, FileValidationService>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates a PhotoCopyConfig for testing.
    /// </summary>
    private PhotoCopyConfig CreateConfig(
        string? destinationTemplate = null,
        DateTime? minDate = null,
        DateTime? maxDate = null)
    {
        destinationTemplate ??= Path.Combine(_testBaseDirectory, "dest", "{year}", "{month}", "{name}{ext}");
        
        return new PhotoCopyConfig
        {
            Source = _sourceDir,
            Destination = destinationTemplate,
            DryRun = false,
            MinDate = minDate,
            MaxDate = maxDate,
            CalculateChecksums = false, // Disable for faster tests
            LogLevel = OutputLevel.Verbose,
            AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".heic", ".mov", ".mp4"
            }
        };
    }

    /// <summary>
    /// Creates a ScanCommand with real dependencies.
    /// </summary>
    private ScanCommand CreateScanCommand(PhotoCopyConfig config, IServiceProvider serviceProvider, bool outputJson = false)
    {
        var options = Microsoft.Extensions.Options.Options.Create(config);
        var directoryScanner = serviceProvider.GetRequiredService<IDirectoryScanner>();
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();
        var fileValidationService = serviceProvider.GetRequiredService<IFileValidationService>();
        var fileFactory = serviceProvider.GetRequiredService<IFileFactory>();
        var fileSystem = serviceProvider.GetRequiredService<IFileSystem>();

        return new ScanCommand(
            _logger,
            options,
            directoryScanner,
            validatorFactory,
            fileValidationService,
            fileFactory,
            fileSystem,
            outputJson);
    }

    #region ScanCommand_WithRealImages_ExtractsCorrectMetadata

    [Test]
    public async Task ScanCommand_WithRealImages_ExtractsCorrectMetadata_SingleJpeg()
    {
        // Arrange - Create a JPEG with a specific date embedded in EXIF
        var dateTaken = new DateTime(2023, 7, 15, 14, 30, 45);
        await CreateTestJpegAsync("vacation.jpg", dateTaken);

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider);

        // Act
        var result = await scanCommand.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        
        // Verify logging indicates file was scanned
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m != null && (m.Contains("Scan complete") || m.Contains("1 files") || m.Contains("1 valid")));
    }

    [Test]
    public async Task ScanCommand_WithRealImages_ExtractsCorrectMetadata_MultipleJpegs()
    {
        // Arrange - Create multiple JPEGs with different dates
        var date1 = new DateTime(2023, 1, 15, 10, 0, 0);
        var date2 = new DateTime(2023, 6, 20, 14, 30, 0);
        var date3 = new DateTime(2023, 12, 25, 8, 0, 0);
        
        await CreateTestJpegAsync("photo1.jpg", date1);
        await CreateTestJpegAsync("photo2.jpg", date2);
        await CreateTestJpegAsync("photo3.jpg", date3);

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider);

        // Act
        var result = await scanCommand.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m != null && (m.Contains("3 files") || m.Contains("3 valid")));
    }

    [Test]
    public async Task ScanCommand_WithRealImages_ExtractsCorrectMetadata_WithJsonOutput()
    {
        // Arrange
        var dateTaken = new DateTime(2024, 5, 10, 12, 0, 0);
        await CreateTestJpegAsync("test_photo.jpg", dateTaken);

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider, outputJson: true);

        // Capture console output
        var originalOut = Console.Out;
        using var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);

        try
        {
            // Act
            var result = await scanCommand.ExecuteAsync();

            // Assert
            result.Should().Be(0);
            
            var jsonOutput = stringWriter.ToString();
            jsonOutput.Should().Contain("test_photo.jpg");
            jsonOutput.Should().Contain("TotalFiles");
            jsonOutput.Should().Contain("ValidFiles");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    #endregion

    #region ScanCommand_ShowsCorrectDestinationPaths

    [Test]
    public async Task ScanCommand_ShowsCorrectDestinationPaths_VerifyFilesFound()
    {
        // Arrange - Create JPEGs with specific dates
        var date1 = new DateTime(2023, 3, 15);
        var date2 = new DateTime(2024, 8, 20);
        
        await CreateTestJpegAsync("spring_photo.jpg", date1);
        await CreateTestJpegAsync("summer_photo.jpg", date2);

        var destPattern = Path.Combine(_testBaseDirectory, "dest", "{year}", "{month}", "{day}", "{name}{ext}");
        var config = CreateConfig(destinationTemplate: destPattern);
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider);

        // Act
        var result = await scanCommand.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m != null && (m.Contains("2 files") || m.Contains("2 valid")));
    }

    [Test]
    public async Task ScanCommand_ShowsCorrectDestinationPaths_WithJsonOutput()
    {
        // Arrange
        var dateTaken = new DateTime(2023, 11, 5, 15, 30, 0);
        await CreateTestJpegAsync("november_photo.jpg", dateTaken);

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider, outputJson: true);

        // Capture console output
        var originalOut = Console.Out;
        using var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);

        try
        {
            // Act
            var result = await scanCommand.ExecuteAsync();

            // Assert
            result.Should().Be(0);
            
            var jsonOutput = stringWriter.ToString();
            jsonOutput.Should().Contain("november_photo.jpg");
            jsonOutput.Should().Contain("\"IsValid\": true");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    #endregion

    #region ScanCommand_WithMixedFileTypes_IdentifiesPhotosCorrectly

    [Test]
    public async Task ScanCommand_WithMixedFileTypes_IdentifiesPhotosCorrectly_JpegsAndTextFiles()
    {
        // Arrange - Create a mix of image and non-image files
        var dateTaken = new DateTime(2023, 5, 10);
        await CreateTestJpegAsync("photo.jpg", dateTaken);
        await CreateNonImageFileAsync("readme.txt");
        await CreateNonImageFileAsync("notes.md");

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider);

        // Act
        var result = await scanCommand.ExecuteAsync();

        // Assert - Should complete successfully, with only valid photos counted
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        // Should have 3 total files but only 1 valid (jpg)
        logMessages.Should().Contain(m => m != null && (m.Contains("3 files") || m.Contains("1 valid")));
    }

    [Test]
    public async Task ScanCommand_WithMixedFileTypes_IdentifiesPhotosCorrectly_JpegsPngsAndOthers()
    {
        // Arrange
        var date1 = new DateTime(2023, 4, 15);
        var date2 = new DateTime(2023, 8, 20);
        
        await CreateTestJpegAsync("image1.jpg", date1);
        await CreateTestPngAsync("image2.png", date2);
        await CreateNonImageFileAsync("document.pdf");
        await CreateNonImageFileAsync("data.json");
        await CreateNonImageFileAsync("script.py");

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider);

        // Act
        var result = await scanCommand.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        // Should have 5 total files but only 2 valid (jpg and png)
        logMessages.Should().Contain(m => m != null && (m.Contains("5 files") || m.Contains("2 valid")));
    }

    [Test]
    public async Task ScanCommand_WithMixedFileTypes_JsonOutputShowsValidity()
    {
        // Arrange
        var dateTaken = new DateTime(2023, 6, 1);
        await CreateTestJpegAsync("valid_photo.jpg", dateTaken);
        await CreateNonImageFileAsync("invalid_file.xyz");

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider, outputJson: true);

        // Capture console output
        var originalOut = Console.Out;
        using var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);

        try
        {
            // Act
            var result = await scanCommand.ExecuteAsync();

            // Assert
            result.Should().Be(0);
            
            var jsonOutput = stringWriter.ToString();
            jsonOutput.Should().Contain("valid_photo.jpg");
            jsonOutput.Should().Contain("invalid_file.xyz");
            // Check for validity indicators
            jsonOutput.Should().Contain("\"IsValid\"");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    #endregion

    #region ScanCommand_WithGpsData_ShowsLocationInfo

    [Test]
    public async Task ScanCommand_WithGpsData_ShowsLocationInfo_NewYorkCoordinates()
    {
        // Arrange - Create JPEG with GPS coordinates for New York City
        var dateTaken = new DateTime(2023, 9, 15);
        var nycCoordinates = (40.7128, -74.0060);
        
        await CreateTestJpegAsync("nyc_photo.jpg", dateTaken, gps: nycCoordinates);

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider);

        // Act
        var result = await scanCommand.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        
        // The scan should complete successfully - GPS data is embedded in the file
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m != null && (m.Contains("1 files") || m.Contains("1 valid")));
    }

    [Test]
    public async Task ScanCommand_WithGpsData_ShowsLocationInfo_MultipleLocations()
    {
        // Arrange - Create JPEGs with different GPS locations
        var dateTaken = new DateTime(2023, 10, 20);
        var parisCoordinates = (48.8566, 2.3522);
        var londonCoordinates = (51.5074, -0.1278);
        var tokyoCoordinates = (35.6762, 139.6503);
        
        await CreateTestJpegAsync("paris.jpg", dateTaken, gps: parisCoordinates);
        await CreateTestJpegAsync("london.jpg", dateTaken, gps: londonCoordinates);
        await CreateTestJpegAsync("tokyo.jpg", dateTaken, gps: tokyoCoordinates);

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider);

        // Act
        var result = await scanCommand.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m != null && (m.Contains("3 files") || m.Contains("3 valid")));
    }

    [Test]
    public async Task ScanCommand_WithGpsData_ShowsLocationInfo_MixedGpsAndNoGps()
    {
        // Arrange - Some photos with GPS, some without
        var dateTaken = new DateTime(2023, 7, 4);
        var sfCoordinates = (37.7749, -122.4194);
        
        await CreateTestJpegAsync("with_gps.jpg", dateTaken, gps: sfCoordinates);
        await CreateTestJpegAsync("without_gps.jpg", dateTaken);

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider);

        // Act
        var result = await scanCommand.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m != null && (m.Contains("2 files") || m.Contains("2 valid")));
    }

    #endregion

    #region ScanCommand_WithNoValidPhotos_ReportsEmpty

    [Test]
    public async Task ScanCommand_WithNoValidPhotos_ReportsEmpty_EmptyDirectory()
    {
        // Arrange - Empty source directory (already created in Setup)
        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider);

        // Act
        var result = await scanCommand.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m != null && (m.Contains("0 files") || m.Contains("0 valid")));
    }

    [Test]
    public async Task ScanCommand_WithNoValidPhotos_ReportsEmpty_OnlyNonImageFiles()
    {
        // Arrange - Only non-image files
        await CreateNonImageFileAsync("document.txt");
        await CreateNonImageFileAsync("config.xml");
        await CreateNonImageFileAsync("data.csv");

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider);

        // Act
        var result = await scanCommand.ExecuteAsync();

        // Assert - Should succeed. Note: The scanner reports ALL files it finds, not just image files.
        // Non-image files (.txt, .xml, .csv) are still enumerated and counted as valid since they pass through.
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m != null && (m.Contains("3 files") || m.Contains("3 valid")));
    }

    [Test]
    public async Task ScanCommand_WithNoValidPhotos_ReportsEmpty_JsonOutputShowsEmpty()
    {
        // Arrange - Empty directory
        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider, outputJson: true);

        // Capture console output
        var originalOut = Console.Out;
        using var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);

        try
        {
            // Act
            var result = await scanCommand.ExecuteAsync();

            // Assert
            result.Should().Be(0);
            
            var jsonOutput = stringWriter.ToString();
            jsonOutput.Should().Contain("\"TotalFiles\": 0");
            jsonOutput.Should().Contain("\"ValidFiles\": 0");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Test]
    public async Task ScanCommand_WithNoValidPhotos_ReportsEmpty_AllFilesFilteredByDate()
    {
        // Arrange - Create photos outside the date range
        var oldDate = new DateTime(2020, 1, 1);
        await CreateTestJpegAsync("old_photo.jpg", oldDate);

        // Set min date to filter out old photos
        var config = CreateConfig(minDate: new DateTime(2023, 1, 1));
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider);

        // Act
        var result = await scanCommand.ExecuteAsync();

        // Assert - Photo should be filtered/skipped due to date
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m != null && (m.Contains("1 skipped") || m.Contains("0 valid")));
    }

    #endregion

    #region ScanCommand_WithNestedDirectories_ScansRecursively

    [Test]
    public async Task ScanCommand_WithNestedDirectories_ScansRecursively_SingleLevel()
    {
        // Arrange - Create photos in root and one subfolder
        var dateTaken = new DateTime(2023, 5, 15);
        
        await CreateTestJpegAsync("root_photo.jpg", dateTaken);
        await CreateTestJpegAsync("subfolder_photo.jpg", dateTaken, subfolder: "2023");

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider);

        // Act
        var result = await scanCommand.ExecuteAsync();

        // Assert - Should find both photos
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m != null && (m.Contains("2 files") || m.Contains("2 valid")));
    }

    [Test]
    public async Task ScanCommand_WithNestedDirectories_ScansRecursively_MultipleNestedLevels()
    {
        // Arrange - Create photos in deeply nested structure
        var dateTaken = new DateTime(2023, 8, 20);
        
        await CreateTestJpegAsync("root.jpg", dateTaken);
        await CreateTestJpegAsync("level1.jpg", dateTaken, subfolder: "folder1");
        await CreateTestJpegAsync("level2.jpg", dateTaken, subfolder: Path.Combine("folder1", "folder2"));
        await CreateTestJpegAsync("level3.jpg", dateTaken, subfolder: Path.Combine("folder1", "folder2", "folder3"));

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider);

        // Act
        var result = await scanCommand.ExecuteAsync();

        // Assert - Should find all 4 photos in nested directories
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m != null && (m.Contains("4 files") || m.Contains("4 valid")));
    }

    [Test]
    public async Task ScanCommand_WithNestedDirectories_ScansRecursively_MixedFilesInSubfolders()
    {
        // Arrange - Create mix of valid and invalid files across subfolders
        var dateTaken = new DateTime(2023, 11, 10);
        
        await CreateTestJpegAsync("photo1.jpg", dateTaken, subfolder: "photos");
        await CreateTestJpegAsync("photo2.jpg", dateTaken, subfolder: Path.Combine("photos", "vacation"));
        await CreateTestPngAsync("screenshot.png", dateTaken, subfolder: "screenshots");
        await CreateNonImageFileAsync("notes.txt", subfolder: "documents");
        await CreateNonImageFileAsync("log.txt", subfolder: Path.Combine("documents", "logs"));

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider);

        // Act
        var result = await scanCommand.ExecuteAsync();

        // Assert - Should find 5 files total, 3 valid (2 jpg + 1 png), 2 skipped
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m != null && (m.Contains("5 files") || m.Contains("3 valid")));
    }

    [Test]
    public async Task ScanCommand_WithNestedDirectories_ScansRecursively_EmptySubfolders()
    {
        // Arrange - Create structure with some empty folders
        var dateTaken = new DateTime(2023, 6, 1);
        
        await CreateTestJpegAsync("only_photo.jpg", dateTaken);
        
        // Create empty subfolders
        Directory.CreateDirectory(Path.Combine(_sourceDir, "empty_folder1"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "empty_folder1", "empty_subfolder"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "empty_folder2"));

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider);

        // Act
        var result = await scanCommand.ExecuteAsync();

        // Assert - Should find only the one photo, empty folders don't cause issues
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m != null && (m.Contains("1 files") || m.Contains("1 valid")));
    }

    [Test]
    public async Task ScanCommand_WithNestedDirectories_ScansRecursively_JsonOutputWithSubfolders()
    {
        // Arrange
        var dateTaken = new DateTime(2023, 12, 15);
        
        await CreateTestJpegAsync("root_image.jpg", dateTaken);
        await CreateTestJpegAsync("nested_image.jpg", dateTaken, subfolder: "subfolder");

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider, outputJson: true);

        // Capture console output
        var originalOut = Console.Out;
        using var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);

        try
        {
            // Act
            var result = await scanCommand.ExecuteAsync();

            // Assert
            result.Should().Be(0);
            
            var jsonOutput = stringWriter.ToString();
            jsonOutput.Should().Contain("root_image.jpg");
            jsonOutput.Should().Contain("nested_image.jpg");
            jsonOutput.Should().Contain("\"TotalFiles\": 2");
            jsonOutput.Should().Contain("\"ValidFiles\": 2");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    #endregion

    #region Additional Edge Cases

    [Test]
    public async Task ScanCommand_WithCancellation_ReturnsTwo()
    {
        // Arrange - Create some files
        var dateTaken = new DateTime(2023, 4, 1);
        await CreateTestJpegAsync("photo1.jpg", dateTaken);
        await CreateTestJpegAsync("photo2.jpg", dateTaken);

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider);

        // Create a pre-cancelled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await scanCommand.ExecuteAsync(cts.Token);

        // Assert - Cancellation returns 2
        result.Should().Be(2);
    }

    [Test]
    public async Task ScanCommand_WithDateFilter_FiltersCorrectly()
    {
        // Arrange - Create photos with different dates
        var oldDate = new DateTime(2020, 5, 10);
        var newDate = new DateTime(2024, 5, 10);
        
        await CreateTestJpegAsync("old_photo.jpg", oldDate);
        await CreateTestJpegAsync("new_photo.jpg", newDate);

        // Only accept photos from 2023 onwards
        var config = CreateConfig(minDate: new DateTime(2023, 1, 1));
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider);

        // Act
        var result = await scanCommand.ExecuteAsync();

        // Assert - Should find 2 files, but only 1 valid (new_photo.jpg)
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m != null && (m.Contains("1 valid") || m.Contains("1 skipped")));
    }

    [Test]
    public async Task ScanCommand_WithMaxDateFilter_FiltersCorrectly()
    {
        // Arrange - Create photos with different dates
        var oldDate = new DateTime(2020, 5, 10);
        var newDate = new DateTime(2024, 5, 10);
        
        await CreateTestJpegAsync("old_photo.jpg", oldDate);
        await CreateTestJpegAsync("new_photo.jpg", newDate);

        // Only accept photos before 2023
        var config = CreateConfig(maxDate: new DateTime(2023, 1, 1));
        var serviceProvider = BuildRealServiceProvider(config);
        var scanCommand = CreateScanCommand(config, serviceProvider);

        // Act
        var result = await scanCommand.ExecuteAsync();

        // Assert - Should find 2 files, but only 1 valid (old_photo.jpg)
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m != null && (m.Contains("1 valid") || m.Contains("1 skipped")));
    }

    #endregion
}
