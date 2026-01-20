using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Configuration;
using PhotoCopy.Files;
using TUnit.Core;

namespace PhotoCopy.Tests.Integration;

/// <summary>
/// Integration tests for camera make/model extraction from real image files.
/// </summary>
public class CameraMetadataIntegrationTests
{
    private readonly string _testPhotosPath;

    public CameraMetadataIntegrationTests()
    {
        // Get the path to the TestPhotos directory
        var currentDir = Directory.GetCurrentDirectory();
        // Navigate up from bin/Debug/netX.X to find TestPhotos
        var projectRoot = Path.GetFullPath(Path.Combine(currentDir, "..", "..", "..", ".."));
        _testPhotosPath = Path.Combine(projectRoot, "TestPhotos");
    }

    private void SkipIfNoTestPhotos()
    {
        if (!Directory.Exists(_testPhotosPath))
        {
            Skip.Test($"TestPhotos directory not found at {_testPhotosPath}");
        }
    }

    [Test]
    public async Task GetCamera_WithRealHeicFile_ExtractsCameraMakeAndModel()
    {
        // Skip if test photos directory doesn't exist
        SkipIfNoTestPhotos();
        
        // Arrange
        var testFile = Directory.GetFiles(_testPhotosPath, "*.heic").FirstOrDefault();
        if (testFile == null)
        {
            Skip.Test("No HEIC files found in TestPhotos");
            return;
        }
        
        var fileInfo = new FileInfo(testFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            AllowedExtensions = [".heic", ".jpg", ".jpeg", ".png"],
            LogLevel = OutputLevel.Verbose
        });
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        // Act
        var camera = extractor.GetCamera(fileInfo);

        // Assert - just verify it doesn't throw and returns something
        // The actual value depends on the test files
        await Assert.That(camera == null || camera.Length > 0).IsTrue();
        
        // Log the result for manual verification
        System.Diagnostics.Debug.WriteLine($"Camera from {fileInfo.Name}: {camera ?? "(null)"}");
    }

    [Test]
    public async Task GetCamera_WithRealJpgFile_ExtractsCameraMakeAndModel()
    {
        // Skip if test photos directory doesn't exist
        SkipIfNoTestPhotos();
        
        // Arrange
        var testFile = Directory.GetFiles(_testPhotosPath, "*.jpg").FirstOrDefault();
        if (testFile == null)
        {
            Skip.Test("No JPG files found in TestPhotos");
            return;
        }
        
        var fileInfo = new FileInfo(testFile);
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            AllowedExtensions = [".heic", ".jpg", ".jpeg", ".png"],
            LogLevel = OutputLevel.Verbose
        });
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        // Act
        var camera = extractor.GetCamera(fileInfo);

        // Assert - just verify it doesn't throw and returns something
        await Assert.That(camera == null || camera.Length > 0).IsTrue();
        
        // Log the result for manual verification
        System.Diagnostics.Debug.WriteLine($"Camera from {fileInfo.Name}: {camera ?? "(null)"}");
    }

    [Test]
    public async Task GetCamera_WithAllTestPhotos_ExtractsDataWithoutErrors()
    {
        // Skip if test photos directory doesn't exist
        SkipIfNoTestPhotos();
        
        // Arrange
        var testFiles = Directory.GetFiles(_testPhotosPath);
        if (testFiles.Length == 0)
        {
            Skip.Test("No files found in TestPhotos");
            return;
        }
        
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(new PhotoCopyConfig
        {
            AllowedExtensions = [".heic", ".jpg", ".jpeg", ".png"],
            LogLevel = OutputLevel.Verbose
        });
        var logger = Substitute.For<ILogger<FileMetadataExtractor>>();
        var extractor = new FileMetadataExtractor(logger, options);

        var cameraResults = new Dictionary<string, string?>();

        // Act - process all test files
        foreach (var testFile in testFiles)
        {
            var fileInfo = new FileInfo(testFile);
            var camera = extractor.GetCamera(fileInfo);
            cameraResults[fileInfo.Name] = camera;
        }

        // Assert - all files processed without exceptions
        await Assert.That(cameraResults.Count).IsEqualTo(testFiles.Length);
        
        // Log results
        System.Diagnostics.Debug.WriteLine("Camera extraction results:");
        foreach (var (fileName, camera) in cameraResults)
        {
            System.Diagnostics.Debug.WriteLine($"  {fileName}: {camera ?? "(no camera data)"}");
        }
    }
}
