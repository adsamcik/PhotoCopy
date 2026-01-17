using System;
using System.Collections.Generic;
using System.IO;
using PhotoCopy.Files;

namespace PhotoCopy.Tests.TestingImplementation;

/// <summary>
/// A fluent builder for creating complex test scenarios with an in-memory file system.
/// Provides easy setup of source/destination directories, photos, videos, and other files.
/// </summary>
public class InMemoryScenarioBuilder
{
    private readonly InMemoryFileSystem _fileSystem = new();
    private string _sourceDirectory = TestPaths.Source;
    private string _destinationDirectory = TestPaths.Dest;
    private readonly List<FileEntry> _files = new();

    /// <summary>
    /// Represents a file entry to be added to the file system.
    /// </summary>
    private class FileEntry
    {
        public string Path { get; set; } = string.Empty;
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public InMemoryFile? MetadataFile { get; set; }
        public DateTime? CreationTime { get; set; }
        public DateTime? LastWriteTime { get; set; }
    }

    /// <summary>
    /// Sets the source directory path.
    /// </summary>
    /// <param name="path">The source directory path.</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithSourceDirectory(string path)
    {
        _sourceDirectory = path;
        return this;
    }

    /// <summary>
    /// Sets the destination directory path.
    /// </summary>
    /// <param name="path">The destination directory path.</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithDestinationDirectory(string path)
    {
        _destinationDirectory = path;
        return this;
    }

    /// <summary>
    /// Adds a photo to the source directory with the specified metadata.
    /// </summary>
    /// <param name="name">The file name (e.g., "photo.jpg").</param>
    /// <param name="taken">The date the photo was taken.</param>
    /// <param name="location">Optional location data.</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithPhoto(string name, DateTime taken, LocationData? location = null)
    {
        return WithPhotoInDirectory(_sourceDirectory, name, taken, location);
    }

    /// <summary>
    /// Adds a photo to the source directory with GPS coordinates.
    /// </summary>
    /// <param name="name">The file name (e.g., "photo.jpg").</param>
    /// <param name="taken">The date the photo was taken.</param>
    /// <param name="gps">GPS coordinates as (latitude, longitude).</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithPhoto(string name, DateTime taken, (double Latitude, double Longitude) gps)
    {
        return WithPhotoInDirectory(_sourceDirectory, name, taken, gps);
    }

    /// <summary>
    /// Adds a photo to a specific directory with the specified metadata.
    /// </summary>
    /// <param name="directory">The directory path.</param>
    /// <param name="name">The file name (e.g., "photo.jpg").</param>
    /// <param name="taken">The date the photo was taken.</param>
    /// <param name="location">Optional location data.</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithPhotoInDirectory(string directory, string name, DateTime taken, LocationData? location = null)
    {
        var path = Path.Combine(directory, name);
        var content = MockImageGenerator.CreateJpeg(taken);
        var fileDateTime = new FileDateTime(taken, taken, taken);
        var checksum = GenerateChecksum(path, taken);
        var metadataFile = new InMemoryFile(path, fileDateTime, location, checksum);

        _files.Add(new FileEntry
        {
            Path = path,
            Content = content,
            MetadataFile = metadataFile,
            CreationTime = taken,
            LastWriteTime = taken
        });

        return this;
    }

    /// <summary>
    /// Adds a photo to a specific directory with GPS coordinates.
    /// </summary>
    /// <param name="directory">The directory path.</param>
    /// <param name="name">The file name (e.g., "photo.jpg").</param>
    /// <param name="taken">The date the photo was taken.</param>
    /// <param name="gps">GPS coordinates as (latitude, longitude).</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithPhotoInDirectory(string directory, string name, DateTime taken, (double Latitude, double Longitude) gps)
    {
        var path = Path.Combine(directory, name);
        var content = MockImageGenerator.CreateJpeg(taken, (gps.Latitude, gps.Longitude));
        var fileDateTime = new FileDateTime(taken, taken, taken);
        var checksum = GenerateChecksum(path, taken);
        // Note: LocationData requires city/state/country, not lat/lon
        // GPS coordinates are stored in the image EXIF, not in LocationData
        var metadataFile = new InMemoryFile(path, fileDateTime, null, checksum);

        _files.Add(new FileEntry
        {
            Path = path,
            Content = content,
            MetadataFile = metadataFile,
            CreationTime = taken,
            LastWriteTime = taken
        });

        return this;
    }

    /// <summary>
    /// Adds a photo with full metadata including location details.
    /// </summary>
    /// <param name="name">The file name (e.g., "photo.jpg").</param>
    /// <param name="taken">The date the photo was taken.</param>
    /// <param name="gps">GPS coordinates as (latitude, longitude).</param>
    /// <param name="city">The city name.</param>
    /// <param name="state">The state or region name (optional).</param>
    /// <param name="country">The country name.</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithPhotoWithLocation(
        string name,
        DateTime taken,
        (double Latitude, double Longitude) gps,
        string city,
        string? state,
        string country)
    {
        var path = Path.Combine(_sourceDirectory, name);
        var content = MockImageGenerator.CreateJpeg(taken, (gps.Latitude, gps.Longitude));
        var fileDateTime = new FileDateTime(taken, taken, taken);
        var location = new LocationData(city, city, null, state, country);
        var checksum = GenerateChecksum(path, taken);
        var metadataFile = new InMemoryFile(path, fileDateTime, location, checksum);

        _files.Add(new FileEntry
        {
            Path = path,
            Content = content,
            MetadataFile = metadataFile,
            CreationTime = taken,
            LastWriteTime = taken
        });

        return this;
    }

    /// <summary>
    /// Adds a video to the source directory with the specified metadata.
    /// </summary>
    /// <param name="name">The file name (e.g., "video.mp4").</param>
    /// <param name="taken">The date the video was taken.</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithVideo(string name, DateTime taken)
    {
        return WithVideoInDirectory(_sourceDirectory, name, taken);
    }

    /// <summary>
    /// Adds a video to a specific directory with the specified metadata.
    /// </summary>
    /// <param name="directory">The directory path.</param>
    /// <param name="name">The file name (e.g., "video.mp4").</param>
    /// <param name="taken">The date the video was taken.</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithVideoInDirectory(string directory, string name, DateTime taken)
    {
        var path = Path.Combine(directory, name);
        // Videos don't have EXIF metadata like photos, so we use a simple byte array
        var content = GenerateVideoContent(name);
        var fileDateTime = new FileDateTime(taken, taken, taken);
        var checksum = GenerateChecksum(path, taken);
        var metadataFile = new InMemoryFile(path, fileDateTime, null, checksum);

        _files.Add(new FileEntry
        {
            Path = path,
            Content = content,
            MetadataFile = metadataFile,
            CreationTime = taken,
            LastWriteTime = taken
        });

        return this;
    }

    /// <summary>
    /// Adds a generic file to the source directory.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <param name="content">The file content.</param>
    /// <param name="creationTime">Optional creation time.</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithFile(string name, byte[] content, DateTime? creationTime = null)
    {
        return WithFileInDirectory(_sourceDirectory, name, content, creationTime);
    }

    /// <summary>
    /// Adds a generic file to the source directory with string content.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <param name="content">The file content as a string.</param>
    /// <param name="creationTime">Optional creation time.</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithFile(string name, string content, DateTime? creationTime = null)
    {
        return WithFile(name, System.Text.Encoding.UTF8.GetBytes(content), creationTime);
    }

    /// <summary>
    /// Adds a generic file to a specific directory.
    /// </summary>
    /// <param name="directory">The directory path.</param>
    /// <param name="name">The file name.</param>
    /// <param name="content">The file content.</param>
    /// <param name="creationTime">Optional creation time.</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithFileInDirectory(string directory, string name, byte[] content, DateTime? creationTime = null)
    {
        var path = Path.Combine(directory, name);
        var time = creationTime ?? DateTime.Now;

        _files.Add(new FileEntry
        {
            Path = path,
            Content = content,
            CreationTime = time,
            LastWriteTime = time
        });

        return this;
    }

    /// <summary>
    /// Adds an existing file to the destination directory (simulates pre-existing files).
    /// </summary>
    /// <param name="relativePath">The relative path within the destination directory.</param>
    /// <param name="content">The file content (optional, defaults to empty).</param>
    /// <param name="creationTime">Optional creation time.</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithExistingDestinationFile(string relativePath, byte[]? content = null, DateTime? creationTime = null)
    {
        var path = Path.Combine(_destinationDirectory, relativePath);
        var time = creationTime ?? DateTime.Now;

        _files.Add(new FileEntry
        {
            Path = path,
            Content = content ?? Array.Empty<byte>(),
            CreationTime = time,
            LastWriteTime = time
        });

        return this;
    }

    /// <summary>
    /// Adds an existing photo file to the destination directory (simulates pre-existing photos).
    /// </summary>
    /// <param name="relativePath">The relative path within the destination directory.</param>
    /// <param name="taken">The date the photo was taken.</param>
    /// <param name="location">Optional location data.</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithExistingDestinationPhoto(string relativePath, DateTime taken, LocationData? location = null)
    {
        var path = Path.Combine(_destinationDirectory, relativePath);
        var content = MockImageGenerator.CreateJpeg(taken);
        var fileDateTime = new FileDateTime(taken, taken, taken);
        var checksum = GenerateChecksum(path, taken);
        var metadataFile = new InMemoryFile(path, fileDateTime, location, checksum);

        _files.Add(new FileEntry
        {
            Path = path,
            Content = content,
            MetadataFile = metadataFile,
            CreationTime = taken,
            LastWriteTime = taken
        });

        return this;
    }

    /// <summary>
    /// Adds a PNG photo to the source directory.
    /// </summary>
    /// <param name="name">The file name (e.g., "photo.png").</param>
    /// <param name="taken">The date the photo was taken.</param>
    /// <param name="gps">Optional GPS coordinates as (latitude, longitude).</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithPngPhoto(string name, DateTime taken, (double Latitude, double Longitude)? gps = null)
    {
        var path = Path.Combine(_sourceDirectory, name);
        var content = MockImageGenerator.CreatePng(taken, gps.HasValue ? (gps.Value.Latitude, gps.Value.Longitude) : null);
        var fileDateTime = new FileDateTime(taken, taken, taken);
        var checksum = GenerateChecksum(path, taken);
        var metadataFile = new InMemoryFile(path, fileDateTime, null, checksum);

        _files.Add(new FileEntry
        {
            Path = path,
            Content = content,
            MetadataFile = metadataFile,
            CreationTime = taken,
            LastWriteTime = taken
        });

        return this;
    }

    /// <summary>
    /// Adds multiple photos with sequential dates.
    /// </summary>
    /// <param name="baseName">The base file name (e.g., "photo" will create "photo_001.jpg", "photo_002.jpg", etc.).</param>
    /// <param name="startDate">The date for the first photo.</param>
    /// <param name="count">The number of photos to create.</param>
    /// <param name="daysBetween">Days between each photo (default: 1).</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithPhotoSequence(string baseName, DateTime startDate, int count, int daysBetween = 1)
    {
        for (int i = 0; i < count; i++)
        {
            var name = $"{baseName}_{(i + 1):D3}.jpg";
            var date = startDate.AddDays(i * daysBetween);
            WithPhoto(name, date);
        }
        return this;
    }

    /// <summary>
    /// Adds photos for an entire year, one per month.
    /// </summary>
    /// <param name="year">The year for the photos.</param>
    /// <param name="baseName">The base file name (default: "monthly_photo").</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithMonthlyPhotos(int year, string baseName = "monthly_photo")
    {
        for (int month = 1; month <= 12; month++)
        {
            var name = $"{baseName}_{month:D2}.jpg";
            var date = new DateTime(year, month, 15, 12, 0, 0);
            WithPhoto(name, date);
        }
        return this;
    }

    /// <summary>
    /// Adds a subdirectory structure with photos.
    /// </summary>
    /// <param name="subdirectory">The subdirectory path relative to source.</param>
    /// <param name="photoCount">Number of photos to add.</param>
    /// <param name="baseDate">The base date for photos.</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithSubdirectoryPhotos(string subdirectory, int photoCount, DateTime baseDate)
    {
        var fullPath = Path.Combine(_sourceDirectory, subdirectory);
        for (int i = 0; i < photoCount; i++)
        {
            var name = $"photo_{(i + 1):D3}.jpg";
            var date = baseDate.AddDays(i);
            WithPhotoInDirectory(fullPath, name, date);
        }
        return this;
    }

    /// <summary>
    /// Adds a duplicate photo (same content, different name or location).
    /// </summary>
    /// <param name="originalName">The original photo name to duplicate.</param>
    /// <param name="duplicateName">The name for the duplicate.</param>
    /// <param name="directory">Optional different directory for the duplicate.</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithDuplicatePhoto(string originalName, string duplicateName, string? directory = null)
    {
        var originalEntry = _files.Find(f => 
            Path.GetFileName(f.Path).Equals(originalName, StringComparison.OrdinalIgnoreCase));
        
        if (originalEntry == null)
        {
            throw new InvalidOperationException($"Original file '{originalName}' not found. Add it first using WithPhoto.");
        }

        var targetDirectory = directory ?? _sourceDirectory;
        var path = Path.Combine(targetDirectory, duplicateName);

        _files.Add(new FileEntry
        {
            Path = path,
            Content = originalEntry.Content.ToArray(), // Copy content
            MetadataFile = originalEntry.MetadataFile != null 
                ? new InMemoryFile(path, originalEntry.MetadataFile.FileDateTime, originalEntry.MetadataFile.Location, originalEntry.MetadataFile.Checksum)
                : null,
            CreationTime = originalEntry.CreationTime,
            LastWriteTime = originalEntry.LastWriteTime
        });

        return this;
    }

    /// <summary>
    /// Adds an InMemoryFile directly (for advanced scenarios).
    /// </summary>
    /// <param name="file">The InMemoryFile to add.</param>
    /// <param name="content">Optional content for the file.</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithInMemoryFile(InMemoryFile file, byte[]? content = null)
    {
        var taken = file.FileDateTime.Taken;
        
        _files.Add(new FileEntry
        {
            Path = file.File.FullName,
            Content = content ?? MockImageGenerator.CreateJpeg(taken),
            MetadataFile = file,
            CreationTime = file.FileDateTime.Created,
            LastWriteTime = file.FileDateTime.Modified
        });

        return this;
    }

    /// <summary>
    /// Creates an empty directory.
    /// </summary>
    /// <param name="path">The directory path.</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithDirectory(string path)
    {
        _fileSystem.CreateDirectory(path);
        return this;
    }

    /// <summary>
    /// Creates a subdirectory under the source directory.
    /// </summary>
    /// <param name="relativePath">The relative path under the source directory.</param>
    /// <returns>The builder for method chaining.</returns>
    public InMemoryScenarioBuilder WithSourceSubdirectory(string relativePath)
    {
        var fullPath = Path.Combine(_sourceDirectory, relativePath);
        _fileSystem.CreateDirectory(fullPath);
        return this;
    }

    /// <summary>
    /// Builds and returns the configured InMemoryFileSystem.
    /// </summary>
    /// <returns>The configured InMemoryFileSystem.</returns>
    public InMemoryFileSystem Build()
    {
        // Create source and destination directories
        _fileSystem.CreateDirectory(_sourceDirectory);
        _fileSystem.CreateDirectory(_destinationDirectory);

        // Add all files
        foreach (var entry in _files)
        {
            _fileSystem.AddFile(entry.Path, entry.Content, entry.CreationTime, entry.LastWriteTime);

            if (entry.MetadataFile != null)
            {
                _fileSystem.AddIFile(entry.MetadataFile);
            }
        }

        return _fileSystem;
    }

    /// <summary>
    /// Builds and returns the configured scenario with additional information.
    /// </summary>
    /// <returns>A ScenarioResult containing the file system and configuration details.</returns>
    public ScenarioResult BuildWithDetails()
    {
        return new ScenarioResult(
            Build(),
            _sourceDirectory,
            _destinationDirectory,
            _files.Count);
    }

    /// <summary>
    /// Gets the configured source directory path.
    /// </summary>
    public string SourceDirectory => _sourceDirectory;

    /// <summary>
    /// Gets the configured destination directory path.
    /// </summary>
    public string DestinationDirectory => _destinationDirectory;

    private static string GenerateChecksum(string path, DateTime date)
    {
        var input = $"{path}_{date:yyyyMMddHHmmss}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static byte[] GenerateVideoContent(string name)
    {
        // Create a minimal video-like content marker
        // In a real scenario, this would be actual video data
        var header = new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70 }; // ftyp box header
        var marker = System.Text.Encoding.UTF8.GetBytes($"VIDEO:{name}");
        var result = new byte[header.Length + marker.Length];
        header.CopyTo(result, 0);
        marker.CopyTo(result, header.Length);
        return result;
    }
}

/// <summary>
/// Contains the result of building a scenario, including the file system and configuration details.
/// </summary>
public class ScenarioResult
{
    /// <summary>
    /// The configured in-memory file system.
    /// </summary>
    public InMemoryFileSystem FileSystem { get; }

    /// <summary>
    /// The source directory path.
    /// </summary>
    public string SourceDirectory { get; }

    /// <summary>
    /// The destination directory path.
    /// </summary>
    public string DestinationDirectory { get; }

    /// <summary>
    /// The total number of files in the scenario.
    /// </summary>
    public int FileCount { get; }

    public ScenarioResult(
        InMemoryFileSystem fileSystem,
        string sourceDirectory,
        string destinationDirectory,
        int fileCount)
    {
        FileSystem = fileSystem;
        SourceDirectory = sourceDirectory;
        DestinationDirectory = destinationDirectory;
        FileCount = fileCount;
    }

    /// <summary>
    /// Deconstructs the result for tuple-style assignment.
    /// </summary>
    public void Deconstruct(
        out InMemoryFileSystem fileSystem,
        out string sourceDirectory,
        out string destinationDirectory)
    {
        fileSystem = FileSystem;
        sourceDirectory = SourceDirectory;
        destinationDirectory = DestinationDirectory;
    }
}
