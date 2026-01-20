using System;
using System.IO;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Configuration;
using PhotoCopy.Files;
using PhotoCopy.Files.Metadata;
using PhotoCopy.Files.Sidecar;

namespace PhotoCopy.Tests.Files.Sidecar;

public class SidecarMetadataEnrichmentStepTests
{
    private readonly string _tempDir;

    public SidecarMetadataEnrichmentStepTests()
    {
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

    private FileInfo CreateTempFile()
    {
        var filePath = Path.Combine(_tempDir, "photo.jpg");
        File.WriteAllText(filePath, "dummy");
        return new FileInfo(filePath);
    }

    private SidecarMetadataEnrichmentStep CreateStep(
        PhotoCopyConfig config,
        ISidecarMetadataService? sidecarService = null)
    {
        var options = Options.Create(config);
        return new SidecarMetadataEnrichmentStep(options, sidecarService);
    }

    #region SidecarMetadataFallback Disabled Tests

    [Test]
    public async Task Enrich_WhenFallbackDisabled_DoesNotModifyMetadata()
    {
        // Arrange
        var config = new PhotoCopyConfig { SidecarMetadataFallback = false };
        var sidecarService = Substitute.For<ISidecarMetadataService>();
        var step = CreateStep(config, sidecarService);
        
        var fileInfo = CreateTempFile();
        var context = new FileMetadataContext(fileInfo);
        var originalDateTime = context.Metadata.DateTime;

        // Act
        step.Enrich(context);

        // Assert
        await Assert.That(context.Metadata.DateTime).IsEqualTo(originalDateTime);
        sidecarService.DidNotReceive().GetSidecarMetadata(Arg.Any<FileInfo>());
    }

    [Test]
    public async Task Enrich_WhenSidecarServiceNull_DoesNotThrow()
    {
        // Arrange
        var config = new PhotoCopyConfig { SidecarMetadataFallback = true };
        var step = CreateStep(config, null);
        
        var fileInfo = CreateTempFile();
        var context = new FileMetadataContext(fileInfo);

        // Act & Assert - should not throw
        step.Enrich(context);
        await Assert.That(context.Metadata.DateTime.Source).IsEqualTo(DateTimeSource.FileCreation);
    }

    #endregion

    #region EmbeddedFirst Priority Tests

    [Test]
    public async Task Enrich_EmbeddedFirst_UsesEmbeddedDateWhenAvailable()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            SidecarMetadataFallback = true,
            SidecarPriority = SidecarMetadataPriority.EmbeddedFirst
        };

        var sidecarMetadata = new SidecarMetadata
        {
            DateTaken = new DateTime(2024, 6, 15, 10, 0, 0)
        };

        var sidecarService = Substitute.For<ISidecarMetadataService>();
        sidecarService.GetSidecarMetadata(Arg.Any<FileInfo>()).Returns(sidecarMetadata);

        var step = CreateStep(config, sidecarService);
        
        var fileInfo = CreateTempFile();
        var context = new FileMetadataContext(fileInfo);
        // Set embedded metadata (non-default)
        context.Metadata.DateTime = new FileDateTime(new DateTime(2024, 1, 1, 12, 0, 0), DateTimeSource.ExifDateTimeOriginal);

        // Act
        step.Enrich(context);

        // Assert - should keep embedded date
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(new DateTime(2024, 1, 1, 12, 0, 0));
        await Assert.That(context.Metadata.DateTime.Source).IsEqualTo(DateTimeSource.ExifDateTimeOriginal);
    }

    [Test]
    public async Task Enrich_EmbeddedFirst_UsesSidecarDateWhenEmbeddedMissing()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            SidecarMetadataFallback = true,
            SidecarPriority = SidecarMetadataPriority.EmbeddedFirst
        };

        var sidecarMetadata = new SidecarMetadata
        {
            DateTaken = new DateTime(2024, 6, 15, 10, 0, 0)
        };

        var sidecarService = Substitute.For<ISidecarMetadataService>();
        sidecarService.GetSidecarMetadata(Arg.Any<FileInfo>()).Returns(sidecarMetadata);

        var step = CreateStep(config, sidecarService);
        
        var fileInfo = CreateTempFile();
        var context = new FileMetadataContext(fileInfo);
        // Set default (missing) date
        context.Metadata.DateTime = new FileDateTime(default, DateTimeSource.FileCreation);

        // Act
        step.Enrich(context);

        // Assert - should use sidecar date
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(new DateTime(2024, 6, 15, 10, 0, 0));
        await Assert.That(context.Metadata.DateTime.Source).IsEqualTo(DateTimeSource.Sidecar);
    }

    [Test]
    public async Task Enrich_EmbeddedFirst_UsesSidecarGpsWhenEmbeddedMissing()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            SidecarMetadataFallback = true,
            SidecarPriority = SidecarMetadataPriority.EmbeddedFirst
        };

        var sidecarMetadata = new SidecarMetadata
        {
            Latitude = 40.7128,
            Longitude = -74.006
        };

        var sidecarService = Substitute.For<ISidecarMetadataService>();
        sidecarService.GetSidecarMetadata(Arg.Any<FileInfo>()).Returns(sidecarMetadata);

        var step = CreateStep(config, sidecarService);
        
        var fileInfo = CreateTempFile();
        var context = new FileMetadataContext(fileInfo);
        // No coordinates set (null)

        // Act
        step.Enrich(context);

        // Assert - should use sidecar coordinates
        await Assert.That(context.Coordinates).IsNotNull();
        await Assert.That(context.Coordinates!.Value.Latitude).IsEqualTo(40.7128);
        await Assert.That(context.Coordinates!.Value.Longitude).IsEqualTo(-74.006);
    }

    [Test]
    public async Task Enrich_EmbeddedFirst_KeepsEmbeddedGpsWhenAvailable()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            SidecarMetadataFallback = true,
            SidecarPriority = SidecarMetadataPriority.EmbeddedFirst
        };

        var sidecarMetadata = new SidecarMetadata
        {
            Latitude = 40.7128,
            Longitude = -74.006
        };

        var sidecarService = Substitute.For<ISidecarMetadataService>();
        sidecarService.GetSidecarMetadata(Arg.Any<FileInfo>()).Returns(sidecarMetadata);

        var step = CreateStep(config, sidecarService);
        
        var fileInfo = CreateTempFile();
        var context = new FileMetadataContext(fileInfo);
        // Set embedded coordinates
        context.Coordinates = (51.5074, -0.1278); // London

        // Act
        step.Enrich(context);

        // Assert - should keep embedded coordinates
        await Assert.That(context.Coordinates!.Value.Latitude).IsEqualTo(51.5074);
        await Assert.That(context.Coordinates!.Value.Longitude).IsEqualTo(-0.1278);
    }

    #endregion

    #region SidecarFirst Priority Tests

    [Test]
    public async Task Enrich_SidecarFirst_OverridesEmbeddedDateWithSidecar()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            SidecarMetadataFallback = true,
            SidecarPriority = SidecarMetadataPriority.SidecarFirst
        };

        var sidecarMetadata = new SidecarMetadata
        {
            DateTaken = new DateTime(2024, 6, 15, 10, 0, 0)
        };

        var sidecarService = Substitute.For<ISidecarMetadataService>();
        sidecarService.GetSidecarMetadata(Arg.Any<FileInfo>()).Returns(sidecarMetadata);

        var step = CreateStep(config, sidecarService);
        
        var fileInfo = CreateTempFile();
        var context = new FileMetadataContext(fileInfo);
        // Set embedded date
        context.Metadata.DateTime = new FileDateTime(new DateTime(2024, 1, 1, 12, 0, 0), DateTimeSource.ExifDateTimeOriginal);

        // Act
        step.Enrich(context);

        // Assert - should override with sidecar date
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(new DateTime(2024, 6, 15, 10, 0, 0));
        await Assert.That(context.Metadata.DateTime.Source).IsEqualTo(DateTimeSource.Sidecar);
    }

    [Test]
    public async Task Enrich_SidecarFirst_OverridesEmbeddedGpsWithSidecar()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            SidecarMetadataFallback = true,
            SidecarPriority = SidecarMetadataPriority.SidecarFirst
        };

        var sidecarMetadata = new SidecarMetadata
        {
            Latitude = 40.7128,
            Longitude = -74.006
        };

        var sidecarService = Substitute.For<ISidecarMetadataService>();
        sidecarService.GetSidecarMetadata(Arg.Any<FileInfo>()).Returns(sidecarMetadata);

        var step = CreateStep(config, sidecarService);
        
        var fileInfo = CreateTempFile();
        var context = new FileMetadataContext(fileInfo);
        // Set embedded coordinates (will be overridden)
        context.Coordinates = (51.5074, -0.1278); // London

        // Act
        step.Enrich(context);

        // Assert - should override with sidecar coordinates
        await Assert.That(context.Coordinates!.Value.Latitude).IsEqualTo(40.7128);
        await Assert.That(context.Coordinates!.Value.Longitude).IsEqualTo(-74.006);
    }

    [Test]
    public async Task Enrich_SidecarFirst_KeepsEmbeddedWhenSidecarLacksData()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            SidecarMetadataFallback = true,
            SidecarPriority = SidecarMetadataPriority.SidecarFirst
        };

        var sidecarMetadata = new SidecarMetadata(); // No data

        var sidecarService = Substitute.For<ISidecarMetadataService>();
        sidecarService.GetSidecarMetadata(Arg.Any<FileInfo>()).Returns(sidecarMetadata);

        var step = CreateStep(config, sidecarService);
        
        var fileInfo = CreateTempFile();
        var context = new FileMetadataContext(fileInfo);
        var originalDate = new DateTime(2024, 1, 1, 12, 0, 0);
        context.Metadata.DateTime = new FileDateTime(originalDate, DateTimeSource.ExifDateTimeOriginal);
        context.Coordinates = (51.5074, -0.1278);

        // Act
        step.Enrich(context);

        // Assert - should keep embedded data since sidecar has nothing
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(originalDate);
        await Assert.That(context.Coordinates!.Value.Latitude).IsEqualTo(51.5074);
    }

    #endregion

    #region MergePreferEmbedded Priority Tests

    [Test]
    public async Task Enrich_MergePreferEmbedded_FillsMissingFieldsFromSidecar()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            SidecarMetadataFallback = true,
            SidecarPriority = SidecarMetadataPriority.MergePreferEmbedded
        };

        var sidecarMetadata = new SidecarMetadata
        {
            DateTaken = new DateTime(2024, 6, 15, 10, 0, 0),
            Latitude = 40.7128,
            Longitude = -74.006
        };

        var sidecarService = Substitute.For<ISidecarMetadataService>();
        sidecarService.GetSidecarMetadata(Arg.Any<FileInfo>()).Returns(sidecarMetadata);

        var step = CreateStep(config, sidecarService);
        
        var fileInfo = CreateTempFile();
        var context = new FileMetadataContext(fileInfo);
        // Set embedded date but no coordinates
        context.Metadata.DateTime = new FileDateTime(new DateTime(2024, 1, 1, 12, 0, 0), DateTimeSource.ExifDateTimeOriginal);
        context.Coordinates = null;

        // Act
        step.Enrich(context);

        // Assert - should keep embedded date, add sidecar coordinates
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(new DateTime(2024, 1, 1, 12, 0, 0));
        await Assert.That(context.Metadata.DateTime.Source).IsEqualTo(DateTimeSource.ExifDateTimeOriginal);
        await Assert.That(context.Coordinates).IsNotNull();
        await Assert.That(context.Coordinates!.Value.Latitude).IsEqualTo(40.7128);
    }

    #endregion

    #region No Sidecar Found Tests

    [Test]
    public async Task Enrich_WhenNoSidecarFound_DoesNotModifyMetadata()
    {
        // Arrange
        var config = new PhotoCopyConfig
        {
            SidecarMetadataFallback = true,
            SidecarPriority = SidecarMetadataPriority.SidecarFirst
        };

        var sidecarService = Substitute.For<ISidecarMetadataService>();
        sidecarService.GetSidecarMetadata(Arg.Any<FileInfo>()).Returns((SidecarMetadata?)null);

        var step = CreateStep(config, sidecarService);
        
        var fileInfo = CreateTempFile();
        var context = new FileMetadataContext(fileInfo);
        var originalDate = new DateTime(2024, 1, 1, 12, 0, 0);
        context.Metadata.DateTime = new FileDateTime(originalDate, DateTimeSource.ExifDateTimeOriginal);

        // Act
        step.Enrich(context);

        // Assert - should not modify anything
        await Assert.That(context.Metadata.DateTime.DateTime).IsEqualTo(originalDate);
        await Assert.That(context.Coordinates).IsNull();
    }

    #endregion
}
