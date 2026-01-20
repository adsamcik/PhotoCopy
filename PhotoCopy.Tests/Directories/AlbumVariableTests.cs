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
/// Tests for the {album} destination variable functionality.
/// </summary>
public class AlbumVariableTests
{
    private readonly ILogger<DirectoryCopier> _logger;
    private readonly IFileSystem _fileSystem;
    private readonly PhotoCopyConfig _config;
    private readonly IOptions<PhotoCopyConfig> _options;
    private readonly ITransactionLogger _transactionLogger;
    private readonly IFileValidationService _fileValidationService;

    public AlbumVariableTests()
    {
        _logger = Substitute.For<ILogger<DirectoryCopier>>();
        _fileSystem = Substitute.For<IFileSystem>();
        _transactionLogger = Substitute.For<ITransactionLogger>();
        _fileValidationService = new FileValidationService();
        
        _config = new PhotoCopyConfig
        {
            Source = TestPaths.Source,
            Destination = TestPaths.DestPattern("{year}", "{album}", "{name}{ext}"),
            DryRun = true,
            DuplicatesFormat = "-{number}"
        };
        
        _options = Microsoft.Extensions.Options.Options.Create(_config);
    }

    #region GeneratePath with {album} Variable Tests

    [Test]
    public void GeneratePath_WithAlbumVariable_ReplacesWithAlbumName()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{album}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithAlbum("photo.jpg", new DateTime(2024, 6, 15), "Summer Vacation");

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("2024", "Summer Vacation", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithNullAlbum_ReplacesWithUnknownFallback()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{album}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithAlbum("photo.jpg", new DateTime(2024, 6, 15), null);

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("2024", "Unknown", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithEmptyAlbum_ReplacesWithUnknownFallback()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{album}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithAlbum("photo.jpg", new DateTime(2024, 6, 15), "");

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("2024", "Unknown", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithCustomAlbumFallback_UsesCustomFallback()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{album}", "{name}{ext}");
        _config.UnknownAlbumFallback = "No Album";
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithAlbum("photo.jpg", new DateTime(2024, 6, 15), null);

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("2024", "No Album", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithAlbumAndCamera_ReplacesAllVariables()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{album}", "{camera}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithAlbumAndCamera(
            "photo.jpg", 
            new DateTime(2024, 6, 15), 
            "Family Photos",
            "Apple iPhone 15 Pro");

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("2024", "Family Photos", "Apple iPhone 15 Pro", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithAlbumInlineFallback_UsesInlineFallback()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{album|NoAlbum}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithAlbum("photo.jpg", new DateTime(2024, 6, 15), null);

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("2024", "NoAlbum", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithAlbumOnlyPattern_ReplacesCorrectly()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{album}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithAlbum("vacation.jpg", new DateTime(2024, 7, 4), "Beach Trip 2024");

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("Beach Trip 2024", "vacation.jpg"));
    }

    [Test]
    public void GeneratePath_WithAlbumContainingSpaces_PreservesSpaces()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{album}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithAlbum("photo.jpg", new DateTime(2024, 6, 15), "My Family Album Photos");

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("My Family Album Photos", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithAlbumCasingUppercase_AppliesUppercase()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{album}", "{name}{ext}");
        _config.PathCasing = PathCasing.Uppercase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithAlbum("photo.jpg", new DateTime(2024, 6, 15), "Summer Vacation");

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("SUMMER VACATION", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithAlbumCasingLowercase_AppliesLowercase()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{album}", "{name}{ext}");
        _config.PathCasing = PathCasing.Lowercase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithAlbum("photo.jpg", new DateTime(2024, 6, 15), "Beach Trip");

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("beach trip", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithAlbumFallbackInheritingFromLocationFallback_UsesLocationFallback()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{album}", "{name}{ext}");
        _config.UnknownLocationFallback = "NoData";
        _config.UnknownAlbumFallback = null; // Should inherit from UnknownLocationFallback
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithAlbum("photo.jpg", new DateTime(2024, 6, 15), null);

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("2024", "NoData", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithAlbumAndLocation_ReplacesAllVariables()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{country}", "{album}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithAlbumAndLocation(
            "photo.jpg", 
            new DateTime(2024, 6, 15), 
            "Tokyo Trip",
            new LocationData("Tokyo", "Tokyo", null, "Tokyo", "JP"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("2024", "JP", "Tokyo Trip", "photo.jpg"));
    }

    #endregion

    #region Helper Methods

    private static IFile CreateMockFileWithAlbum(string name, DateTime dateTime, string? album)
    {
        var file = Substitute.For<IFile>();
        var fileInfo = new FileInfo(Path.Combine(Path.GetTempPath(), name));
        
        file.File.Returns(fileInfo);
        file.FileDateTime.Returns(new FileDateTime(dateTime, DateTimeSource.ExifDateTimeOriginal));
        file.Location.Returns((LocationData?)null);
        file.Camera.Returns((string?)null);
        file.Album.Returns(album);
        
        return file;
    }

    private static IFile CreateMockFileWithAlbumAndCamera(string name, DateTime dateTime, string? album, string? camera)
    {
        var file = Substitute.For<IFile>();
        var fileInfo = new FileInfo(Path.Combine(Path.GetTempPath(), name));
        
        file.File.Returns(fileInfo);
        file.FileDateTime.Returns(new FileDateTime(dateTime, DateTimeSource.ExifDateTimeOriginal));
        file.Location.Returns((LocationData?)null);
        file.Camera.Returns(camera);
        file.Album.Returns(album);
        
        return file;
    }

    private static IFile CreateMockFileWithAlbumAndLocation(string name, DateTime dateTime, string? album, LocationData location)
    {
        var file = Substitute.For<IFile>();
        var fileInfo = new FileInfo(Path.Combine(Path.GetTempPath(), name));
        
        file.File.Returns(fileInfo);
        file.FileDateTime.Returns(new FileDateTime(dateTime, DateTimeSource.ExifDateTimeOriginal));
        file.Location.Returns(location);
        file.Camera.Returns((string?)null);
        file.Album.Returns(album);
        
        return file;
    }

    #endregion
}
