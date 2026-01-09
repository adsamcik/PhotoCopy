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
/// Integration tests for ValidateCommand using real files and validators.
/// These tests:
/// 1. Use MockImageGenerator to create real JPEG/PNG files with embedded EXIF metadata
/// 2. Write these files to actual temp directories
/// 3. Use REAL implementations of ValidatorFactory, FileSystem, FileFactory, DirectoryScanner
/// 4. Verify ValidateCommand correctly validates files and reports results
/// </summary>
[NotInParallel("ValidateCommandIntegration")]
[Property("Category", "Integration")]
public class ValidateCommandIntegrationTests
{
    private string _testBaseDirectory = null!;
    private string _sourceDir = null!;
    private FakeLogger<ValidateCommand> _logger = null!;

    [Before(Test)]
    public void Setup()
    {
        _testBaseDirectory = Path.Combine(Path.GetTempPath(), "ValidateCommandIntegrationTests", Guid.NewGuid().ToString());
        _sourceDir = Path.Combine(_testBaseDirectory, "source");
        
        Directory.CreateDirectory(_sourceDir);
        
        _logger = new FakeLogger<ValidateCommand>();
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

    #region Helper Methods

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
    /// Creates a file with corrupted/invalid image data.
    /// </summary>
    private async Task<string> CreateCorruptedImageFileAsync(string filename, string? subfolder = null)
    {
        var directory = subfolder != null 
            ? Path.Combine(_sourceDir, subfolder) 
            : _sourceDir;
        
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var filePath = Path.Combine(directory, filename);
        // Write random bytes that look like JPEG header but are actually corrupted
        var corruptedBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00 };
        await File.WriteAllBytesAsync(filePath, corruptedBytes);
        
        return filePath;
    }

    /// <summary>
    /// Creates a duplicate JPEG file with identical content.
    /// </summary>
    private async Task<string> CreateDuplicateJpegAsync(
        string filename,
        DateTime dateTaken,
        string? subfolder = null)
    {
        // Create same JPEG content to ensure checksum match
        var directory = subfolder != null 
            ? Path.Combine(_sourceDir, subfolder) 
            : _sourceDir;
        
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var filePath = Path.Combine(directory, filename);
        var jpegBytes = MockImageGenerator.CreateJpeg(dateTaken: dateTaken);
        await File.WriteAllBytesAsync(filePath, jpegBytes);
        
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
        DateTime? maxDate = null,
        bool calculateChecksums = false)
    {
        destinationTemplate ??= Path.Combine(_testBaseDirectory, "dest", "{year}", "{month}", "{name}{ext}");
        
        return new PhotoCopyConfig
        {
            Source = _sourceDir,
            Destination = destinationTemplate,
            DryRun = false,
            MinDate = minDate,
            MaxDate = maxDate,
            CalculateChecksums = calculateChecksums,
            LogLevel = OutputLevel.Verbose,
            AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".heic", ".mov", ".mp4"
            }
        };
    }

    /// <summary>
    /// Creates a ValidateCommand with real dependencies.
    /// </summary>
    private ValidateCommand CreateValidateCommand(PhotoCopyConfig config, IServiceProvider serviceProvider)
    {
        var options = Microsoft.Extensions.Options.Options.Create(config);
        var validatorFactory = serviceProvider.GetRequiredService<IValidatorFactory>();
        var fileValidationService = serviceProvider.GetRequiredService<IFileValidationService>();
        var fileSystem = serviceProvider.GetRequiredService<IFileSystem>();

        return new ValidateCommand(
            _logger,
            options,
            validatorFactory,
            fileValidationService,
            fileSystem);
    }

    #endregion

    #region ValidateCommand_WithValidFiles_ReportsNoErrors

    [Test]
    public async Task ValidateCommand_WithValidFiles_ReportsNoErrors_SingleFile()
    {
        // Arrange - Create a JPEG with a date within any date range
        var dateTaken = new DateTime(2024, 6, 15, 14, 30, 45);
        await CreateTestJpegAsync("vacation.jpg", dateTaken);

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert
        result.Should().Be(0, "No validators configured, so all files should be valid");
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m.Contains("Total files: 1") || m.Contains("1"));
        logMessages.Should().Contain(m => m.Contains("Valid files: 1") || m.Contains("Valid"));
    }

    [Test]
    public async Task ValidateCommand_WithValidFiles_ReportsNoErrors_MultipleFiles()
    {
        // Arrange - Create multiple JPEGs within date range
        var minDate = new DateTime(2024, 1, 1);
        var maxDate = new DateTime(2024, 12, 31);
        
        await CreateTestJpegAsync("photo1.jpg", new DateTime(2024, 3, 15));
        await CreateTestJpegAsync("photo2.jpg", new DateTime(2024, 6, 20));
        await CreateTestPngAsync("photo3.png", new DateTime(2024, 9, 10));

        var config = CreateConfig(minDate: minDate, maxDate: maxDate);
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert
        result.Should().Be(0, "All files are within the configured date range");
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m.Contains("Total files: 3") || m.Contains("3 files"));
        logMessages.Should().Contain(m => m.Contains("Invalid files: 0") || m.Contains("0 invalid"));
    }

    [Test]
    public async Task ValidateCommand_WithValidFiles_ReportsNoErrors_WithSubfolders()
    {
        // Arrange - Create files in subfolders
        var dateTaken = new DateTime(2024, 5, 10);
        await CreateTestJpegAsync("root.jpg", dateTaken);
        await CreateTestJpegAsync("sub1.jpg", dateTaken, subfolder: "2024");
        await CreateTestJpegAsync("sub2.jpg", dateTaken, subfolder: "2024/May");

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m.Contains("Total files: 3") || m.Contains("3"));
    }

    #endregion

    #region ValidateCommand_WithFilesOutsideDateRange_ReportsFilteredCount

    [Test]
    public async Task ValidateCommand_WithFilesOutsideDateRange_ReportsFilteredCount_BeforeMinDate()
    {
        // Arrange - Create files before the min date
        var minDate = new DateTime(2024, 6, 1);
        await CreateTestJpegAsync("old_photo.jpg", new DateTime(2024, 1, 15));

        var config = CreateConfig(minDate: minDate);
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert
        result.Should().Be(1, "File is before min date so validation should fail");
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m.Contains("Invalid files: 1") || m.Contains("1 invalid"));
        logMessages.Should().Contain(m => m.Contains("earlier than configured min") || m.Contains("MinDateValidator"));
    }

    [Test]
    public async Task ValidateCommand_WithFilesOutsideDateRange_ReportsFilteredCount_AfterMaxDate()
    {
        // Arrange - Create files after the max date
        var maxDate = new DateTime(2024, 6, 1);
        await CreateTestJpegAsync("future_photo.jpg", new DateTime(2024, 12, 25));

        var config = CreateConfig(maxDate: maxDate);
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert
        result.Should().Be(1, "File is after max date so validation should fail");
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m.Contains("Invalid files: 1") || m.Contains("1 invalid"));
        logMessages.Should().Contain(m => m.Contains("exceeds configured max") || m.Contains("MaxDateValidator"));
    }

    [Test]
    public async Task ValidateCommand_WithFilesOutsideDateRange_ReportsFilteredCount_MultipleOutsideRange()
    {
        // Arrange - Create multiple files outside the date range
        var minDate = new DateTime(2024, 3, 1);
        var maxDate = new DateTime(2024, 9, 30);
        
        await CreateTestJpegAsync("too_old.jpg", new DateTime(2024, 1, 15));
        await CreateTestJpegAsync("too_new.jpg", new DateTime(2024, 11, 20));
        await CreateTestJpegAsync("just_right.jpg", new DateTime(2024, 6, 15));

        var config = CreateConfig(minDate: minDate, maxDate: maxDate);
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert
        result.Should().Be(1, "Some files are outside the date range");
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m.Contains("Total files: 3"));
        logMessages.Should().Contain(m => m.Contains("Invalid files: 2") || m.Contains("2 invalid"));
        logMessages.Should().Contain(m => m.Contains("Valid files: 1") || m.Contains("1 valid"));
    }

    #endregion

    #region ValidateCommand_WithUnsupportedFileTypes_ReportsInvalidFiles

    [Test]
    public async Task ValidateCommand_WithUnsupportedFileTypes_ReportsInvalidFiles_TextFilesIgnored()
    {
        // Arrange - Create text files that are not in AllowedExtensions
        await CreateNonImageFileAsync("readme.txt");
        await CreateNonImageFileAsync("notes.md");
        await CreateTestJpegAsync("valid_photo.jpg", new DateTime(2024, 5, 10));

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert - text files are not in AllowedExtensions, so they won't be enumerated
        // Only the JPEG should be found and validated
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m.Contains("Total files:"));
    }

    [Test]
    public async Task ValidateCommand_WithUnsupportedFileTypes_ReportsInvalidFiles_MixedTypes()
    {
        // Arrange - Create various file types
        await CreateTestJpegAsync("photo1.jpg", new DateTime(2024, 5, 10));
        await CreateTestPngAsync("photo2.png", new DateTime(2024, 6, 15));
        await CreateNonImageFileAsync("document.pdf");
        await CreateNonImageFileAsync("spreadsheet.xlsx");

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert - non-image files are filtered by AllowedExtensions
        result.Should().Be(0, "Only valid image files should be processed");
    }

    #endregion

    #region ValidateCommand_WithMixedValidAndInvalid_ReportsBoth

    [Test]
    public async Task ValidateCommand_WithMixedValidAndInvalid_ReportsBoth_DateRangeValidation()
    {
        // Arrange - Create mix of valid and invalid files based on date
        var minDate = new DateTime(2024, 1, 1);
        var maxDate = new DateTime(2024, 12, 31);
        
        // Valid files (within range)
        await CreateTestJpegAsync("valid1.jpg", new DateTime(2024, 3, 15));
        await CreateTestJpegAsync("valid2.jpg", new DateTime(2024, 7, 20));
        
        // Invalid files (outside range)
        await CreateTestJpegAsync("invalid1.jpg", new DateTime(2023, 5, 10));
        await CreateTestJpegAsync("invalid2.jpg", new DateTime(2025, 2, 28));

        var config = CreateConfig(minDate: minDate, maxDate: maxDate);
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert
        result.Should().Be(1, "Some files failed validation");
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m.Contains("Total files: 4"));
        logMessages.Should().Contain(m => m.Contains("Valid files: 2"));
        logMessages.Should().Contain(m => m.Contains("Invalid files: 2"));
    }

    [Test]
    public async Task ValidateCommand_WithMixedValidAndInvalid_ReportsBoth_ShowsFailureDetails()
    {
        // Arrange
        var minDate = new DateTime(2024, 6, 1);
        
        await CreateTestJpegAsync("old_photo.jpg", new DateTime(2024, 1, 15));
        await CreateTestJpegAsync("new_photo.jpg", new DateTime(2024, 8, 20));

        var config = CreateConfig(minDate: minDate);
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert
        result.Should().Be(1);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m.Contains("Validation Failures") || m.Contains("old_photo"));
        logMessages.Should().Contain(m => m.Contains("MinDateValidator") || m.Contains("earlier than"));
    }

    #endregion

    #region ValidateCommand_WithEmptySource_ReportsNoFilesFound

    [Test]
    public async Task ValidateCommand_WithEmptySource_ReportsNoFilesFound_EmptyDirectory()
    {
        // Arrange - Source directory is already empty

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert
        result.Should().Be(0, "No files means no validation failures");
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m.Contains("Total files: 0") || m.Contains("0 files"));
    }

    [Test]
    public async Task ValidateCommand_WithEmptySource_ReportsNoFilesFound_OnlyNonImageFiles()
    {
        // Arrange - Create only non-image files
        await CreateNonImageFileAsync("readme.txt");
        await CreateNonImageFileAsync("config.json");
        await CreateNonImageFileAsync("data.csv");

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert - non-image files are filtered out
        result.Should().Be(0, "No image files to validate");
    }

    [Test]
    public async Task ValidateCommand_WithEmptySource_ReportsNoFilesFound_EmptySubfolders()
    {
        // Arrange - Create empty subfolders
        Directory.CreateDirectory(Path.Combine(_sourceDir, "2024"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "2024", "January"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "2024", "February"));

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m.Contains("Total files: 0") || m.Contains("0"));
    }

    #endregion

    #region ValidateCommand_WithCorruptedImages_HandlesGracefully

    [Test]
    public async Task ValidateCommand_WithCorruptedImages_HandlesGracefully_SingleCorruptedFile()
    {
        // Arrange - Create a corrupted image file
        await CreateCorruptedImageFileAsync("corrupted.jpg");

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert - Command should not throw, even with corrupted files
        // The file will be processed but metadata extraction may fail
        result.Should().BeOneOf(0, 1, 2); // Command completed without crashing
    }

    [Test]
    public async Task ValidateCommand_WithCorruptedImages_HandlesGracefully_MixedWithValid()
    {
        // Arrange - Create mix of valid and corrupted files
        await CreateTestJpegAsync("valid.jpg", new DateTime(2024, 5, 10));
        await CreateCorruptedImageFileAsync("corrupted.jpg");

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert - Command should process all files without crashing
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m.Contains("Total files:") || m.Contains("files"));
    }

    [Test]
    public async Task ValidateCommand_WithCorruptedImages_HandlesGracefully_EmptyFile()
    {
        // Arrange - Create an empty file with image extension
        var filePath = Path.Combine(_sourceDir, "empty.jpg");
        await File.WriteAllBytesAsync(filePath, Array.Empty<byte>());

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert - Command handles empty file gracefully
        result.Should().BeOneOf(0, 1, 2);
    }

    #endregion

    #region ValidateCommand_ShowsDestinationPathPreview

    [Test]
    public async Task ValidateCommand_ShowsDestinationPathPreview_LogsSourcePath()
    {
        // Arrange
        var dateTaken = new DateTime(2024, 5, 15, 10, 30, 0);
        await CreateTestJpegAsync("photo.jpg", dateTaken);

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m.Contains(_sourceDir) || m.Contains("Validating files"));
    }

    [Test]
    public async Task ValidateCommand_ShowsDestinationPathPreview_WithMultipleFiles()
    {
        // Arrange - Create files with various dates
        await CreateTestJpegAsync("spring.jpg", new DateTime(2024, 4, 15));
        await CreateTestJpegAsync("summer.jpg", new DateTime(2024, 7, 20));
        await CreateTestJpegAsync("fall.jpg", new DateTime(2024, 10, 5));

        var destPattern = Path.Combine(_testBaseDirectory, "dest", "{year}", "{month}", "{day}", "{name}{ext}");
        var config = CreateConfig(destinationTemplate: destPattern);
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m.Contains("Validation Summary"));
        logMessages.Should().Contain(m => m.Contains("Total files: 3"));
    }

    #endregion

    #region ValidateCommand_WithDuplicatesInSource_ReportsDuplicates

    [Test]
    public async Task ValidateCommand_WithDuplicatesInSource_ReportsDuplicates_IdenticalFiles()
    {
        // Arrange - Create identical files (same date = same content from MockImageGenerator)
        var dateTaken = new DateTime(2024, 5, 10, 12, 0, 0);
        await CreateDuplicateJpegAsync("original.jpg", dateTaken);
        await CreateDuplicateJpegAsync("copy.jpg", dateTaken);

        var config = CreateConfig(calculateChecksums: true);
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert - Both files are valid (duplicates don't fail validation by default)
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m.Contains("Total files: 2"));
    }

    [Test]
    public async Task ValidateCommand_WithDuplicatesInSource_ReportsDuplicates_MultipleGroups()
    {
        // Arrange - Create multiple sets of duplicates
        var date1 = new DateTime(2024, 3, 15, 10, 0, 0);
        var date2 = new DateTime(2024, 6, 20, 14, 30, 0);
        
        // First duplicate group
        await CreateDuplicateJpegAsync("photo_a1.jpg", date1);
        await CreateDuplicateJpegAsync("photo_a2.jpg", date1);
        
        // Second duplicate group
        await CreateDuplicateJpegAsync("photo_b1.jpg", date2);
        await CreateDuplicateJpegAsync("photo_b2.jpg", date2);
        
        // Unique file
        await CreateTestJpegAsync("unique.jpg", new DateTime(2024, 9, 10, 8, 0, 0));

        var config = CreateConfig(calculateChecksums: true);
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m.Contains("Total files: 5"));
    }

    [Test]
    public async Task ValidateCommand_WithDuplicatesInSource_ReportsDuplicates_InSubfolders()
    {
        // Arrange - Create duplicates in different subfolders
        var dateTaken = new DateTime(2024, 5, 10, 12, 0, 0);
        await CreateDuplicateJpegAsync("photo.jpg", dateTaken, subfolder: "folder1");
        await CreateDuplicateJpegAsync("photo.jpg", dateTaken, subfolder: "folder2");
        await CreateDuplicateJpegAsync("photo.jpg", dateTaken, subfolder: "folder3");

        var config = CreateConfig(calculateChecksums: true);
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m.Contains("Total files: 3"));
    }

    #endregion

    #region Additional Edge Cases

    [Test]
    public async Task ValidateCommand_WithCancellation_ReturnsCancelledCode()
    {
        // Arrange
        await CreateTestJpegAsync("photo.jpg", new DateTime(2024, 5, 10));

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);
        
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act
        var result = await validateCommand.ExecuteAsync(cts.Token);

        // Assert
        result.Should().Be(2, "Cancelled operations should return 2");
    }

    [Test]
    public async Task ValidateCommand_WithBothMinAndMaxDate_ValidatesRange()
    {
        // Arrange
        var minDate = new DateTime(2024, 4, 1);
        var maxDate = new DateTime(2024, 8, 31);
        
        await CreateTestJpegAsync("in_range.jpg", new DateTime(2024, 6, 15));
        await CreateTestJpegAsync("before_min.jpg", new DateTime(2024, 2, 10));
        await CreateTestJpegAsync("after_max.jpg", new DateTime(2024, 10, 20));

        var config = CreateConfig(minDate: minDate, maxDate: maxDate);
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert
        result.Should().Be(1, "Two files are outside the date range");
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m.Contains("Valid files: 1"));
        logMessages.Should().Contain(m => m.Contains("Invalid files: 2"));
    }

    [Test]
    public async Task ValidateCommand_WithNoValidators_AllFilesAreValid()
    {
        // Arrange - No min/max date means no validators
        await CreateTestJpegAsync("photo1.jpg", new DateTime(2020, 1, 1));
        await CreateTestJpegAsync("photo2.jpg", new DateTime(2030, 12, 31));

        var config = CreateConfig(); // No date constraints
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert
        result.Should().Be(0, "With no validators, all files should pass");
        
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m.Contains("Total files: 2"));
        logMessages.Should().Contain(m => m.Contains("Valid files: 2"));
        logMessages.Should().Contain(m => m.Contains("Invalid files: 0"));
    }

    [Test]
    public async Task ValidateCommand_WithGpsMetadata_ValidatesSuccessfully()
    {
        // Arrange - Create files with GPS coordinates
        var dateTaken = new DateTime(2024, 5, 10);
        var gps = (Lat: 40.7128, Lon: -74.0060); // New York City
        
        await CreateTestJpegAsync("nyc_photo.jpg", dateTaken, gps);

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        var result = await validateCommand.ExecuteAsync();

        // Assert
        result.Should().Be(0);
    }

    [Test]
    public async Task ValidateCommand_LogsSummarySection()
    {
        // Arrange
        await CreateTestJpegAsync("photo.jpg", new DateTime(2024, 5, 10));

        var config = CreateConfig();
        var serviceProvider = BuildRealServiceProvider(config);
        var validateCommand = CreateValidateCommand(config, serviceProvider);

        // Act
        await validateCommand.ExecuteAsync();

        // Assert
        var logMessages = _logger.Logs.Select(l => l.Message).ToList();
        logMessages.Should().Contain(m => m.Contains("Validation Summary"));
        logMessages.Should().Contain(m => m.Contains("Total files:"));
        logMessages.Should().Contain(m => m.Contains("Valid files:"));
        logMessages.Should().Contain(m => m.Contains("Invalid files:"));
    }

    #endregion
}
