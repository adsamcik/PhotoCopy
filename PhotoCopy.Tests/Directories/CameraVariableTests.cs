using System;
using System.IO;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Directories;
using PhotoCopy.Files;
using PhotoCopy.Rollback;
using PhotoCopy.Tests.TestingImplementation;
using PhotoCopy.Validators;

namespace PhotoCopy.Tests.Directories;

/// <summary>
/// Tests for the {camera} destination variable functionality.
/// </summary>
public class CameraVariableTests
{
    private readonly ILogger<DirectoryCopier> _logger;
    private readonly IFileSystem _fileSystem;
    private readonly PhotoCopyConfig _config;
    private readonly IOptions<PhotoCopyConfig> _options;
    private readonly ITransactionLogger _transactionLogger;
    private readonly IFileValidationService _fileValidationService;

    public CameraVariableTests()
    {
        _logger = Substitute.For<ILogger<DirectoryCopier>>();
        _fileSystem = Substitute.For<IFileSystem>();
        _transactionLogger = Substitute.For<ITransactionLogger>();
        _fileValidationService = new FileValidationService();
        
        _config = new PhotoCopyConfig
        {
            Source = TestPaths.Source,
            Destination = TestPaths.DestPattern("{year}", "{camera}", "{name}{ext}"),
            DryRun = true,
            DuplicatesFormat = "-{number}"
        };
        
        _options = Microsoft.Extensions.Options.Options.Create(_config);
    }

    #region GeneratePath with {camera} Variable Tests

    [Test]
    public void GeneratePath_WithCameraVariable_ReplacesWithCameraMakeModel()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{camera}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithCamera("photo.jpg", new DateTime(2024, 6, 15), "Apple iPhone 15 Pro");

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("2024", "Apple iPhone 15 Pro", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithCameraVariable_HandlesCanonCamera()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{camera}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithCamera("photo.jpg", new DateTime(2024, 6, 15), "Canon EOS R5");

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("Canon EOS R5", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithNullCamera_ReplacesWithUnknownFallback()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{camera}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithCamera("photo.jpg", new DateTime(2024, 6, 15), null);

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("2024", "Unknown", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithEmptyCamera_ReplacesWithUnknownFallback()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{camera}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithCamera("photo.jpg", new DateTime(2024, 6, 15), "");

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("2024", "Unknown", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithCustomCameraFallback_UsesCustomFallback()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{camera}", "{name}{ext}");
        _config.UnknownCameraFallback = "No Camera Info";
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithCamera("photo.jpg", new DateTime(2024, 6, 15), null);

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("2024", "No Camera Info", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithCameraAndLocation_ReplacesAllVariables()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{country}", "{camera}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithCameraAndLocation(
            "photo.jpg", 
            new DateTime(2024, 6, 15), 
            "Sony A7 IV",
            new LocationData("Tokyo", "Tokyo", null, "Tokyo", "JP"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("2024", "JP", "Sony A7 IV", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithCameraInlineFallback_UsesInlineFallback()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{camera|NoCamera}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithCamera("photo.jpg", new DateTime(2024, 6, 15), null);

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("2024", "NoCamera", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithCameraOnlyPattern_ReplacesCorrectly()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{camera}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithCamera("vacation.jpg", new DateTime(2024, 7, 4), "Nikon Z8");

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("Nikon Z8", "vacation.jpg"));
    }

    [Test]
    public void GeneratePath_WithCameraContainingSpaces_PreservesSpaces()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{camera}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithCamera("photo.jpg", new DateTime(2024, 6, 15), "Apple iPhone 15 Pro Max");

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("Apple iPhone 15 Pro Max", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithCameraCasingUppercase_AppliesUppercase()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{camera}", "{name}{ext}");
        _config.PathCasing = PathCasing.Uppercase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithCamera("photo.jpg", new DateTime(2024, 6, 15), "Apple iPhone 15 Pro");

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("APPLE IPHONE 15 PRO", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithCameraCasingLowercase_AppliesLowercase()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{camera}", "{name}{ext}");
        _config.PathCasing = PathCasing.Lowercase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithCamera("photo.jpg", new DateTime(2024, 6, 15), "Canon EOS R5");

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("canon eos r5", "photo.jpg"));
    }

    #endregion

    #region Helper Methods

    private static IFile CreateMockFileWithCamera(string name, DateTime dateTime, string? camera)
    {
        var file = Substitute.For<IFile>();
        var fileInfo = new FileInfo(Path.Combine(Path.GetTempPath(), name));
        
        file.File.Returns(fileInfo);
        file.FileDateTime.Returns(new FileDateTime(dateTime, DateTimeSource.ExifDateTimeOriginal));
        file.Location.Returns((LocationData?)null);
        file.Camera.Returns(camera);
        
        return file;
    }

    private static IFile CreateMockFileWithCameraAndLocation(string name, DateTime dateTime, string? camera, LocationData location)
    {
        var file = Substitute.For<IFile>();
        var fileInfo = new FileInfo(Path.Combine(Path.GetTempPath(), name));
        
        file.File.Returns(fileInfo);
        file.FileDateTime.Returns(new FileDateTime(dateTime, DateTimeSource.ExifDateTimeOriginal));
        file.Location.Returns(location);
        file.Camera.Returns(camera);
        
        return file;
    }

    #endregion
}
