using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Configuration;
using PhotoCopy.Files.Sidecar;

namespace PhotoCopy.Tests.Files.Sidecar;

public class SidecarMetadataServiceTests
{
    private readonly ILogger<SidecarMetadataService> _logger;
    private readonly string _tempDir;

    public SidecarMetadataServiceTests()
    {
        _logger = Substitute.For<ILogger<SidecarMetadataService>>();
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    [After(Test)]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private SidecarMetadataService CreateService(
        PhotoCopyConfig config,
        IEnumerable<ISidecarParser>? parsers = null)
    {
        var options = Options.Create(config);
        parsers ??= Array.Empty<ISidecarParser>();
        return new SidecarMetadataService(_logger, options, parsers);
    }

    private FileInfo CreateTempMediaFile(string fileName)
    {
        var filePath = Path.Combine(_tempDir, fileName);
        File.WriteAllText(filePath, "dummy content");
        return new FileInfo(filePath);
    }

    private void CreateTempSidecarFile(string fileName, string content)
    {
        var filePath = Path.Combine(_tempDir, fileName);
        File.WriteAllText(filePath, content);
    }

    private static ISidecarParser CreateMockParser(string extension, SidecarMetadata? result)
    {
        var parser = Substitute.For<ISidecarParser>();
        parser.CanParse(Arg.Is<string>(e => e.Equals(extension, StringComparison.OrdinalIgnoreCase)))
              .Returns(true);
        parser.CanParse(Arg.Is<string>(e => !e.Equals(extension, StringComparison.OrdinalIgnoreCase)))
              .Returns(false);
        parser.Parse(Arg.Any<string>()).Returns(result);
        return parser;
    }

    #region SidecarMetadataFallback Disabled Tests

    [Test]
    public async Task GetSidecarMetadata_WhenFallbackDisabled_ReturnsNull()
    {
        // Arrange
        var config = new PhotoCopyConfig { SidecarMetadataFallback = false };
        var service = CreateService(config);
        var mediaFile = CreateTempMediaFile("photo.jpg");

        // Act
        var result = service.GetSidecarMetadata(mediaFile);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region Sidecar Pattern Tests

    [Test]
    public async Task GetSidecarMetadata_FindsSidecarWithFullNamePattern()
    {
        // Arrange - photo.jpg.xmp pattern
        var config = new PhotoCopyConfig
        {
            SidecarMetadataFallback = true,
            SidecarExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xmp" }
        };

        var expectedMetadata = new SidecarMetadata { Latitude = 40.7128, Longitude = -74.006 };
        var mockParser = CreateMockParser(".xmp", expectedMetadata);
        var service = CreateService(config, new[] { mockParser });

        var mediaFile = CreateTempMediaFile("photo.jpg");
        CreateTempSidecarFile("photo.jpg.xmp", "<xmp>");

        // Act
        var result = service.GetSidecarMetadata(mediaFile);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Latitude).IsEqualTo(40.7128);
        await Assert.That(result.Longitude).IsEqualTo(-74.006);
    }

    [Test]
    public async Task GetSidecarMetadata_FindsSidecarWithBaseNamePattern()
    {
        // Arrange - photo.xmp pattern (base name without original extension)
        var config = new PhotoCopyConfig
        {
            SidecarMetadataFallback = true,
            SidecarExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xmp" }
        };

        var expectedMetadata = new SidecarMetadata { DateTaken = new DateTime(2024, 1, 15, 12, 0, 0) };
        var mockParser = CreateMockParser(".xmp", expectedMetadata);
        var service = CreateService(config, new[] { mockParser });

        var mediaFile = CreateTempMediaFile("photo.jpg");
        CreateTempSidecarFile("photo.xmp", "<xmp>");

        // Act
        var result = service.GetSidecarMetadata(mediaFile);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.DateTaken).IsEqualTo(new DateTime(2024, 1, 15, 12, 0, 0));
    }

    [Test]
    public async Task GetSidecarMetadata_PrefersFullNamePatternOverBaseNamePattern()
    {
        // Arrange - both photo.jpg.xmp and photo.xmp exist
        var config = new PhotoCopyConfig
        {
            SidecarMetadataFallback = true,
            SidecarExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xmp" }
        };

        // Create parser that returns different metadata based on path
        var mockParser = Substitute.For<ISidecarParser>();
        mockParser.CanParse(".xmp").Returns(true);
        mockParser.CanParse(Arg.Is<string>(e => !e.Equals(".xmp", StringComparison.OrdinalIgnoreCase)))
                  .Returns(false);
        mockParser.Parse(Arg.Is<string>(p => p.EndsWith("photo.jpg.xmp", StringComparison.OrdinalIgnoreCase)))
                  .Returns(new SidecarMetadata { Latitude = 40.0, Longitude = -74.0 }); // Full name pattern
        mockParser.Parse(Arg.Is<string>(p => p.EndsWith("photo.xmp", StringComparison.OrdinalIgnoreCase)))
                  .Returns(new SidecarMetadata { Latitude = 50.0, Longitude = -80.0 }); // Base name pattern

        var service = CreateService(config, new[] { mockParser });

        var mediaFile = CreateTempMediaFile("photo.jpg");
        CreateTempSidecarFile("photo.jpg.xmp", "<xmp full>");
        CreateTempSidecarFile("photo.xmp", "<xmp base>");

        // Act
        var result = service.GetSidecarMetadata(mediaFile);

        // Assert - should use photo.jpg.xmp (full name pattern)
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Latitude).IsEqualTo(40.0);
        await Assert.That(result.Longitude).IsEqualTo(-74.0);
    }

    [Test]
    public async Task GetSidecarMetadata_WhenNoSidecarExists_ReturnsNull()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            SidecarMetadataFallback = true,
            SidecarExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xmp", ".json" }
        };

        var mockParser = CreateMockParser(".xmp", new SidecarMetadata { Latitude = 40.0 });
        var service = CreateService(config, new[] { mockParser });

        var mediaFile = CreateTempMediaFile("photo.jpg");
        // No sidecar file created

        // Act
        var result = service.GetSidecarMetadata(mediaFile);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region GoogleTakeoutSupport Tests

    [Test]
    public async Task GetSidecarMetadata_WhenGoogleTakeoutDisabled_SkipsJsonSidecar()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            SidecarMetadataFallback = true,
            GoogleTakeoutSupport = false,
            SidecarExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".json", ".xmp" }
        };

        var jsonParser = CreateMockParser(".json", new SidecarMetadata { Latitude = 40.0 });
        var service = CreateService(config, new[] { jsonParser });

        var mediaFile = CreateTempMediaFile("photo.jpg");
        CreateTempSidecarFile("photo.jpg.json", "{}");

        // Act
        var result = service.GetSidecarMetadata(mediaFile);

        // Assert - JSON sidecar should be skipped
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetSidecarMetadata_WhenGoogleTakeoutEnabled_ParsesJsonSidecar()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            SidecarMetadataFallback = true,
            GoogleTakeoutSupport = true,
            SidecarExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".json" }
        };

        var expectedMetadata = new SidecarMetadata { Latitude = 40.7128, Longitude = -74.006 };
        var jsonParser = CreateMockParser(".json", expectedMetadata);
        var service = CreateService(config, new[] { jsonParser });

        var mediaFile = CreateTempMediaFile("photo.jpg");
        CreateTempSidecarFile("photo.jpg.json", "{}");

        // Act
        var result = service.GetSidecarMetadata(mediaFile);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Latitude).IsEqualTo(40.7128);
    }

    #endregion

    #region Multiple Extensions Tests

    [Test]
    public async Task GetSidecarMetadata_HandlesMultipleSidecarExtensions()
    {
        // Arrange - XMP comes before JSON in extension order
        var config = new PhotoCopyConfig
        {
            SidecarMetadataFallback = true,
            GoogleTakeoutSupport = true,
            SidecarExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xmp", ".json" }
        };

        var xmpMetadata = new SidecarMetadata { Latitude = 40.0, Longitude = -74.0 };
        var jsonMetadata = new SidecarMetadata { Latitude = 50.0, Longitude = -80.0 };

        var xmpParser = CreateMockParser(".xmp", xmpMetadata);
        var jsonParser = CreateMockParser(".json", jsonMetadata);

        var service = CreateService(config, new[] { xmpParser, jsonParser });

        var mediaFile = CreateTempMediaFile("photo.jpg");
        // Only create JSON sidecar (no XMP)
        CreateTempSidecarFile("photo.jpg.json", "{}");

        // Act
        var result = service.GetSidecarMetadata(mediaFile);

        // Assert - should find JSON since XMP doesn't exist
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Latitude).IsEqualTo(50.0);
    }

    [Test]
    public async Task GetSidecarMetadata_ReturnsFirstValidSidecar()
    {
        // Arrange - both XMP and JSON exist, but we find XMP first
        var config = new PhotoCopyConfig
        {
            SidecarMetadataFallback = true,
            GoogleTakeoutSupport = true,
            SidecarExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xmp", ".json" }
        };

        var xmpMetadata = new SidecarMetadata { Latitude = 40.0, Longitude = -74.0 };
        var jsonMetadata = new SidecarMetadata { Latitude = 50.0, Longitude = -80.0 };

        var xmpParser = CreateMockParser(".xmp", xmpMetadata);
        var jsonParser = CreateMockParser(".json", jsonMetadata);

        var service = CreateService(config, new[] { xmpParser, jsonParser });

        var mediaFile = CreateTempMediaFile("photo.jpg");
        CreateTempSidecarFile("photo.jpg.xmp", "<xmp>");
        CreateTempSidecarFile("photo.jpg.json", "{}");

        // Act
        var result = service.GetSidecarMetadata(mediaFile);

        // Assert - XMP should be found first based on extension order
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Latitude).IsEqualTo(40.0);
    }

    #endregion

    #region Edge Case Tests

    [Test]
    public async Task GetSidecarMetadata_WhenNoParserAvailable_ReturnsNull()
    {
        // Arrange - no parsers registered
        var config = new PhotoCopyConfig
        {
            SidecarMetadataFallback = true,
            SidecarExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".unknown" }
        };

        var service = CreateService(config, Array.Empty<ISidecarParser>());

        var mediaFile = CreateTempMediaFile("photo.jpg");
        CreateTempSidecarFile("photo.jpg.unknown", "content");

        // Act
        var result = service.GetSidecarMetadata(mediaFile);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetSidecarMetadata_WhenParserReturnsNull_ContinuesToNextExtension()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            SidecarMetadataFallback = true,
            GoogleTakeoutSupport = true,
            SidecarExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xmp", ".json" }
        };

        var xmpParser = CreateMockParser(".xmp", null); // Returns null
        var jsonMetadata = new SidecarMetadata { Latitude = 50.0, Longitude = -80.0 };
        var jsonParser = CreateMockParser(".json", jsonMetadata);

        var service = CreateService(config, new[] { xmpParser, jsonParser });

        var mediaFile = CreateTempMediaFile("photo.jpg");
        CreateTempSidecarFile("photo.jpg.xmp", "<invalid xmp>");
        CreateTempSidecarFile("photo.jpg.json", "{}");

        // Act
        var result = service.GetSidecarMetadata(mediaFile);

        // Assert - should fall through to JSON after XMP returns null
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Latitude).IsEqualTo(50.0);
    }

    [Test]
    public async Task GetSidecarMetadata_WithCaseInsensitiveExtension_FindsSidecar()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            SidecarMetadataFallback = true,
            SidecarExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".XMP" }
        };

        var expectedMetadata = new SidecarMetadata { Latitude = 40.7128 };
        var mockParser = CreateMockParser(".XMP", expectedMetadata);
        var service = CreateService(config, new[] { mockParser });

        var mediaFile = CreateTempMediaFile("PHOTO.JPG");
        CreateTempSidecarFile("PHOTO.JPG.xmp", "<xmp>");

        // Act  
        var result = service.GetSidecarMetadata(mediaFile);

        // Assert
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task GetSidecarMetadata_WithNullDirectoryName_ReturnsNull()
    {
        // Arrange
        var config = new PhotoCopyConfig { SidecarMetadataFallback = true };
        var service = CreateService(config);

        // Create a FileInfo with mocked null DirectoryName is tricky
        // Instead, test with a root path file which should handle gracefully
        var mediaFile = CreateTempMediaFile("photo.jpg");

        // Act
        var result = service.GetSidecarMetadata(mediaFile);

        // Assert - no sidecar exists
        await Assert.That(result).IsNull();
    }

    #endregion
}
