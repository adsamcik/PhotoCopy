using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Configuration;
using PhotoCopy.Files;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using AwesomeAssertions;

namespace PhotoCopy.Tests.Files;

/// <summary>
/// Unit tests for LivePhotoEnricher that validates HEIF/HEIC Live Photo handling.
/// 
/// iPhone Live Photos consist of:
/// - A .heic still image with full EXIF metadata including GPS
/// - A companion .mov video (typically 2-3 seconds) that often lacks GPS metadata
/// 
/// These tests verify that the LivePhotoEnricher correctly:
/// - Pairs .heic photos with companion .mov videos by base name
/// - Transfers GPS metadata from photos to videos
/// - Handles various edge cases (missing files, already has GPS, etc.)
/// </summary>
[Property("Category", "Unit,LivePhoto")]
public class LivePhotoEnricherTests
{
    private string _testDirectory = null!;
    
    [Before(Test)]
    public void Setup()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "LivePhotoEnricherTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
    }
    
    [After(Test)]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    #region Helper Methods

    private static LivePhotoEnricher CreateEnricher(
        bool enableLivePhoto = true)
    {
        var config = new PhotoCopyConfig
        {
            EnableLivePhotoInheritance = enableLivePhoto
        };
        var options = Substitute.For<IOptions<PhotoCopyConfig>>();
        options.Value.Returns(config);

        var logger = Substitute.For<ILogger<LivePhotoEnricher>>();
        return new LivePhotoEnricher(options, logger);
    }

    private FileWithMetadata CreateFileWithMetadata(
        string fileName,
        DateTime dateTime,
        LocationData? location = null,
        DateTimeSource source = DateTimeSource.ExifDateTimeOriginal)
    {
        // Create file in shared test directory so files can be found as pairs
        var filePath = Path.Combine(_testDirectory, fileName);
        File.WriteAllText(filePath, "test content");

        var fileInfo = new FileInfo(filePath);
        var logger = Substitute.For<ILogger<FileWithMetadata>>();
        var fileDateTime = new FileDateTime(dateTime, source);
        
        var file = new FileWithMetadata(fileInfo, fileDateTime, logger)
        {
            Location = location
        };
        
        return file;
    }

    private static LocationData CreateLocationData(string city = "Tokyo", string country = "Japan")
    {
        return new LocationData(
            District: "District",
            City: city,
            County: null,
            State: "State",
            Country: country,
            Population: 1000000
        );
    }

    #endregion

    #region IsEnabled Tests

    [Test]
    public async Task IsEnabled_WhenConfigEnabled_ReturnsTrue()
    {
        // Arrange
        var enricher = CreateEnricher(enableLivePhoto: true);

        // Act & Assert
        await Assert.That(enricher.IsEnabled).IsTrue();
    }

    [Test]
    public async Task IsEnabled_WhenConfigDisabled_ReturnsFalse()
    {
        // Arrange
        var enricher = CreateEnricher(enableLivePhoto: false);

        // Act & Assert
        await Assert.That(enricher.IsEnabled).IsFalse();
    }

    #endregion

    #region Basic Pairing Tests

    [Test]
    public async Task EnrichFiles_HeicAndMovPair_TransfersGpsToMov()
    {
        // Arrange
        var enricher = CreateEnricher();
        var dateTime = new DateTime(2024, 1, 15, 10, 30, 0);
        var location = CreateLocationData("Kyoto", "Japan");

        // Create .heic with GPS
        var heicFile = CreateFileWithMetadata("IMG_1234.heic", dateTime, location);
        
        // Create .mov without GPS (same base name)
        var movFile = CreateFileWithMetadata("IMG_1234.mov", dateTime, location: null);
        movFile.UnknownReason = UnknownFileReason.NoGpsData;

        var files = new List<IFile> { heicFile, movFile };

        // Act
        enricher.EnrichFiles(files);

        // Assert
        await Assert.That(movFile.Location).IsNotNull();
        await Assert.That(movFile.Location!.City).IsEqualTo("Kyoto");
        await Assert.That(movFile.Location!.Country).IsEqualTo("Japan");
        await Assert.That(movFile.UnknownReason).IsEqualTo(UnknownFileReason.None);
    }

    [Test]
    public async Task EnrichFiles_JpegAndMovPair_TransfersGpsToMov()
    {
        // Arrange
        var enricher = CreateEnricher();
        var dateTime = new DateTime(2024, 2, 20, 14, 45, 0);
        var location = CreateLocationData("Paris", "France");

        // Some Live Photos use .jpg instead of .heic
        var jpgFile = CreateFileWithMetadata("IMG_5678.jpg", dateTime, location);
        var movFile = CreateFileWithMetadata("IMG_5678.mov", dateTime, location: null);
        movFile.UnknownReason = UnknownFileReason.NoGpsData;

        var files = new List<IFile> { jpgFile, movFile };

        // Act
        enricher.EnrichFiles(files);

        // Assert
        await Assert.That(movFile.Location).IsNotNull();
        await Assert.That(movFile.Location!.City).IsEqualTo("Paris");
    }

    [Test]
    public async Task EnrichFiles_HeifAndMovPair_TransfersGpsToMov()
    {
        // Arrange
        var enricher = CreateEnricher();
        var dateTime = new DateTime(2024, 3, 10, 9, 15, 0);
        var location = CreateLocationData("Berlin", "Germany");

        // Some devices use .heif instead of .heic
        var heifFile = CreateFileWithMetadata("IMG_9012.heif", dateTime, location);
        var movFile = CreateFileWithMetadata("IMG_9012.mov", dateTime, location: null);
        movFile.UnknownReason = UnknownFileReason.NoGpsData;

        var files = new List<IFile> { heifFile, movFile };

        // Act
        enricher.EnrichFiles(files);

        // Assert
        await Assert.That(movFile.Location).IsNotNull();
        await Assert.That(movFile.Location!.City).IsEqualTo("Berlin");
    }

    #endregion

    #region No Pairing Tests

    [Test]
    public async Task EnrichFiles_MovAlreadyHasGps_DoesNotOverwrite()
    {
        // Arrange
        var enricher = CreateEnricher();
        var dateTime = new DateTime(2024, 4, 5, 12, 0, 0);
        var heicLocation = CreateLocationData("Tokyo", "Japan");
        var movLocation = CreateLocationData("Osaka", "Japan");

        var heicFile = CreateFileWithMetadata("IMG_1234.heic", dateTime, heicLocation);
        var movFile = CreateFileWithMetadata("IMG_1234.mov", dateTime, movLocation);

        var files = new List<IFile> { heicFile, movFile };

        // Act
        enricher.EnrichFiles(files);

        // Assert - MOV keeps its original GPS
        await Assert.That(movFile.Location!.City).IsEqualTo("Osaka");
    }

    [Test]
    public async Task EnrichFiles_NoMatchingHeic_MovUnchanged()
    {
        // Arrange
        var enricher = CreateEnricher();
        var dateTime = new DateTime(2024, 5, 10, 8, 30, 0);

        // Create .heic and .mov with different base names
        var heicFile = CreateFileWithMetadata("IMG_1111.heic", dateTime, CreateLocationData());
        var movFile = CreateFileWithMetadata("IMG_2222.mov", dateTime, location: null);
        movFile.UnknownReason = UnknownFileReason.NoGpsData;

        var files = new List<IFile> { heicFile, movFile };

        // Act
        enricher.EnrichFiles(files);

        // Assert - MOV remains without GPS
        await Assert.That(movFile.Location).IsNull();
        await Assert.That(movFile.UnknownReason).IsEqualTo(UnknownFileReason.NoGpsData);
    }

    [Test]
    public async Task EnrichFiles_HeicWithoutGps_MovUnchanged()
    {
        // Arrange
        var enricher = CreateEnricher();
        var dateTime = new DateTime(2024, 6, 15, 16, 45, 0);

        // HEIC without GPS
        var heicFile = CreateFileWithMetadata("IMG_3333.heic", dateTime, location: null);
        heicFile.UnknownReason = UnknownFileReason.NoGpsData;
        
        var movFile = CreateFileWithMetadata("IMG_3333.mov", dateTime, location: null);
        movFile.UnknownReason = UnknownFileReason.NoGpsData;

        var files = new List<IFile> { heicFile, movFile };

        // Act
        enricher.EnrichFiles(files);

        // Assert - Neither has GPS to inherit
        await Assert.That(movFile.Location).IsNull();
    }

    [Test]
    public async Task EnrichFiles_DisabledConfig_NoEnrichment()
    {
        // Arrange
        var enricher = CreateEnricher(enableLivePhoto: false);
        var dateTime = new DateTime(2024, 7, 20, 11, 0, 0);

        var heicFile = CreateFileWithMetadata("IMG_4444.heic", dateTime, CreateLocationData());
        var movFile = CreateFileWithMetadata("IMG_4444.mov", dateTime, location: null);
        movFile.UnknownReason = UnknownFileReason.NoGpsData;

        var files = new List<IFile> { heicFile, movFile };

        // Act
        enricher.EnrichFiles(files);

        // Assert - Enrichment is disabled
        await Assert.That(movFile.Location).IsNull();
    }

    #endregion

    #region Case Sensitivity Tests

    [Test]
    public async Task EnrichFiles_DifferentCase_StillMatches()
    {
        // Arrange
        var enricher = CreateEnricher();
        var dateTime = new DateTime(2024, 8, 25, 13, 30, 0);
        var location = CreateLocationData("London", "UK");

        // Different casing in file names
        var heicFile = CreateFileWithMetadata("IMG_5555.HEIC", dateTime, location);
        var movFile = CreateFileWithMetadata("img_5555.mov", dateTime, location: null);
        movFile.UnknownReason = UnknownFileReason.NoGpsData;

        var files = new List<IFile> { heicFile, movFile };

        // Act
        enricher.EnrichFiles(files);

        // Assert - Should match despite case difference
        await Assert.That(movFile.Location).IsNotNull();
        await Assert.That(movFile.Location!.City).IsEqualTo("London");
    }

    #endregion

    #region Multiple Files Tests

    [Test]
    public async Task EnrichFiles_MultiplePairs_AllEnriched()
    {
        // Arrange
        var enricher = CreateEnricher();
        var dateTime1 = new DateTime(2024, 9, 1, 10, 0, 0);
        var dateTime2 = new DateTime(2024, 9, 2, 11, 0, 0);
        var dateTime3 = new DateTime(2024, 9, 3, 12, 0, 0);

        var location1 = CreateLocationData("Rome", "Italy");
        var location2 = CreateLocationData("Venice", "Italy");
        var location3 = CreateLocationData("Florence", "Italy");

        var heic1 = CreateFileWithMetadata("IMG_0001.heic", dateTime1, location1);
        var mov1 = CreateFileWithMetadata("IMG_0001.mov", dateTime1, location: null);
        mov1.UnknownReason = UnknownFileReason.NoGpsData;

        var heic2 = CreateFileWithMetadata("IMG_0002.heic", dateTime2, location2);
        var mov2 = CreateFileWithMetadata("IMG_0002.mov", dateTime2, location: null);
        mov2.UnknownReason = UnknownFileReason.NoGpsData;

        var heic3 = CreateFileWithMetadata("IMG_0003.heic", dateTime3, location3);
        var mov3 = CreateFileWithMetadata("IMG_0003.mov", dateTime3, location: null);
        mov3.UnknownReason = UnknownFileReason.NoGpsData;

        var files = new List<IFile> { heic1, mov1, heic2, mov2, heic3, mov3 };

        // Act
        enricher.EnrichFiles(files);

        // Assert - All MOVs should have GPS
        await Assert.That(mov1.Location!.City).IsEqualTo("Rome");
        await Assert.That(mov2.Location!.City).IsEqualTo("Venice");
        await Assert.That(mov3.Location!.City).IsEqualTo("Florence");
    }

    [Test]
    public async Task EnrichFiles_MixedFilesWithAndWithoutPairs_OnlyPairedEnriched()
    {
        // Arrange
        var enricher = CreateEnricher();
        var dateTime = new DateTime(2024, 10, 15, 14, 0, 0);
        var location = CreateLocationData("Madrid", "Spain");

        // Paired files
        var heic1 = CreateFileWithMetadata("IMG_1000.heic", dateTime, location);
        var mov1 = CreateFileWithMetadata("IMG_1000.mov", dateTime, location: null);
        mov1.UnknownReason = UnknownFileReason.NoGpsData;

        // Unpaired MOV
        var mov2 = CreateFileWithMetadata("IMG_2000.mov", dateTime, location: null);
        mov2.UnknownReason = UnknownFileReason.NoGpsData;

        // Regular JPG (not a Live Photo pair)
        var jpg = CreateFileWithMetadata("DSC_5000.jpg", dateTime, CreateLocationData("Barcelona", "Spain"));

        var files = new List<IFile> { heic1, mov1, mov2, jpg };

        // Act
        enricher.EnrichFiles(files);

        // Assert
        await Assert.That(mov1.Location).IsNotNull(); // Should be enriched
        await Assert.That(mov1.Location!.City).IsEqualTo("Madrid");
        await Assert.That(mov2.Location).IsNull(); // No pair, not enriched
    }

    #endregion

    #region Priority Tests

    [Test]
    public async Task EnrichFiles_MultiplePhotosWithSameBaseName_PrefersOneWithGps()
    {
        // Arrange
        var enricher = CreateEnricher();
        var dateTime = new DateTime(2024, 11, 5, 9, 0, 0);
        var location = CreateLocationData("Amsterdam", "Netherlands");

        // Two photos with same base name - one with GPS, one without
        var heicWithGps = CreateFileWithMetadata("IMG_7777.heic", dateTime, location);
        var jpgWithoutGps = CreateFileWithMetadata("IMG_7777.jpg", dateTime, location: null);
        
        var movFile = CreateFileWithMetadata("IMG_7777.mov", dateTime, location: null);
        movFile.UnknownReason = UnknownFileReason.NoGpsData;

        // Order matters - put the one without GPS first to test preference
        var files = new List<IFile> { jpgWithoutGps, heicWithGps, movFile };

        // Act
        enricher.EnrichFiles(files);

        // Assert - Should use the one with GPS
        await Assert.That(movFile.Location).IsNotNull();
        await Assert.That(movFile.Location!.City).IsEqualTo("Amsterdam");
    }

    #endregion

    #region Empty/Edge Cases

    [Test]
    public async Task EnrichFiles_EmptyList_NoException()
    {
        // Arrange
        var enricher = CreateEnricher();
        var files = new List<IFile>();

        // Act & Assert - Should not throw
        enricher.EnrichFiles(files);
        await Assert.That(files.Count).IsEqualTo(0);
    }

    [Test]
    public async Task EnrichFiles_OnlyPhotos_NoEnrichment()
    {
        // Arrange
        var enricher = CreateEnricher();
        var dateTime = new DateTime(2024, 12, 1, 15, 0, 0);

        var heic1 = CreateFileWithMetadata("IMG_8888.heic", dateTime, CreateLocationData());
        var heic2 = CreateFileWithMetadata("IMG_9999.heic", dateTime, CreateLocationData());

        var files = new List<IFile> { heic1, heic2 };

        // Act
        enricher.EnrichFiles(files);

        // Assert - No changes, just verify no exceptions
        await Assert.That(heic1.Location).IsNotNull();
        await Assert.That(heic2.Location).IsNotNull();
    }

    [Test]
    public async Task EnrichFiles_OnlyVideos_NoEnrichment()
    {
        // Arrange
        var enricher = CreateEnricher();
        var dateTime = new DateTime(2024, 12, 10, 16, 30, 0);

        var mov1 = CreateFileWithMetadata("IMG_0101.mov", dateTime, location: null);
        mov1.UnknownReason = UnknownFileReason.NoGpsData;
        
        var mov2 = CreateFileWithMetadata("IMG_0202.mov", dateTime, location: null);
        mov2.UnknownReason = UnknownFileReason.NoGpsData;

        var files = new List<IFile> { mov1, mov2 };

        // Act
        enricher.EnrichFiles(files);

        // Assert - No photos to inherit from
        await Assert.That(mov1.Location).IsNull();
        await Assert.That(mov2.Location).IsNull();
    }

    #endregion

    #region Non-Live Photo Video Tests

    [Test]
    public async Task EnrichFiles_Mp4Video_NotEnriched()
    {
        // Arrange
        var enricher = CreateEnricher();
        var dateTime = new DateTime(2024, 12, 15, 10, 0, 0);
        var location = CreateLocationData("Sydney", "Australia");

        // .mp4 is not a typical Live Photo companion format
        var heicFile = CreateFileWithMetadata("IMG_1234.heic", dateTime, location);
        var mp4File = CreateFileWithMetadata("IMG_1234.mp4", dateTime, location: null);
        mp4File.UnknownReason = UnknownFileReason.NoGpsData;

        var files = new List<IFile> { heicFile, mp4File };

        // Act
        enricher.EnrichFiles(files);

        // Assert - MP4 is not enriched (only .mov is considered Live Photo companion)
        await Assert.That(mp4File.Location).IsNull();
    }

    #endregion
}
