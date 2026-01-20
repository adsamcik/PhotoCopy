using System.IO;
using NSubstitute;
using PhotoCopy.Files;
using PhotoCopy.Files.Metadata;

namespace PhotoCopy.Tests.Files.Metadata;

public class AlbumMetadataEnrichmentStepTests
{
    #region Basic Album Enrichment Tests

    [Test]
    public async Task Enrich_WithAlbumFromExtractor_SetsAlbumProperty()
    {
        // Arrange
        var expectedAlbum = "Summer Vacation 2024";
        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetAlbum(Arg.Any<FileInfo>()).Returns(expectedAlbum);

        var step = new AlbumMetadataEnrichmentStep(metadataExtractor);
        var tempFile = CreateTempFile("photo_with_album.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            await Assert.That(context.Metadata.Album).IsEqualTo(expectedAlbum);
            metadataExtractor.Received(1).GetAlbum(Arg.Any<FileInfo>());
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public async Task Enrich_WithNullAlbum_SetsNullAlbumProperty()
    {
        // Arrange
        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetAlbum(Arg.Any<FileInfo>()).Returns((string?)null);

        var step = new AlbumMetadataEnrichmentStep(metadataExtractor);
        var tempFile = CreateTempFile("photo_no_album.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            await Assert.That(context.Metadata.Album).IsNull();
            metadataExtractor.Received(1).GetAlbum(Arg.Any<FileInfo>());
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public async Task Enrich_WithEmptyAlbum_SetsEmptyAlbumProperty()
    {
        // Arrange
        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetAlbum(Arg.Any<FileInfo>()).Returns(string.Empty);

        var step = new AlbumMetadataEnrichmentStep(metadataExtractor);
        var tempFile = CreateTempFile("photo_empty_album.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            await Assert.That(context.Metadata.Album).IsEqualTo(string.Empty);
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    #endregion

    #region Album Name Variations Tests

    [Test]
    public async Task Enrich_WithXmpAlbum_SetsAlbumProperty()
    {
        // Arrange
        var expectedAlbum = "Family Photos";
        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetAlbum(Arg.Any<FileInfo>()).Returns(expectedAlbum);

        var step = new AlbumMetadataEnrichmentStep(metadataExtractor);
        var tempFile = CreateTempFile("xmp_album.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            await Assert.That(context.Metadata.Album).IsEqualTo(expectedAlbum);
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public async Task Enrich_WithSpecialCharactersInAlbum_PreservesValidCharacters()
    {
        // Arrange - album name that was already sanitized by GetAlbum
        var sanitizedAlbum = "Summer 2024 - Beach Trip";
        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetAlbum(Arg.Any<FileInfo>()).Returns(sanitizedAlbum);

        var step = new AlbumMetadataEnrichmentStep(metadataExtractor);
        var tempFile = CreateTempFile("special_chars.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            await Assert.That(context.Metadata.Album).IsEqualTo(sanitizedAlbum);
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public async Task Enrich_WithLongAlbumName_PreservesFullName()
    {
        // Arrange
        var longAlbum = "This Is A Very Long Album Name That Describes The Photos From Our Amazing European Vacation";
        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetAlbum(Arg.Any<FileInfo>()).Returns(longAlbum);

        var step = new AlbumMetadataEnrichmentStep(metadataExtractor);
        var tempFile = CreateTempFile("long_album.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert
            await Assert.That(context.Metadata.Album).IsEqualTo(longAlbum);
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public async Task Enrich_WithWhitespaceOnlyAlbum_ReturnsWhitespace()
    {
        // Arrange - whitespace album returned by extractor (not sanitized in step)
        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetAlbum(Arg.Any<FileInfo>()).Returns("   ");

        var step = new AlbumMetadataEnrichmentStep(metadataExtractor);
        var tempFile = CreateTempFile("whitespace_album.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);

            // Assert - extractor should have sanitized this, but step just passes through
            await Assert.That(context.Metadata.Album).IsEqualTo("   ");
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    #endregion

    #region Integration with Other Metadata Tests

    [Test]
    public async Task Enrich_DoesNotAffectOtherMetadataProperties()
    {
        // Arrange
        var album = "Test Album";
        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetAlbum(Arg.Any<FileInfo>()).Returns(album);

        var step = new AlbumMetadataEnrichmentStep(metadataExtractor);
        var tempFile = CreateTempFile("metadata_test.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));
            var originalDateTime = context.Metadata.DateTime;
            context.Metadata.Camera = "Canon EOS R5";
            context.Metadata.Location = new LocationData("District", "City", "County", "State", "US");

            // Act
            step.Enrich(context);

            // Assert - other properties should remain unchanged
            await Assert.That(context.Metadata.Album).IsEqualTo(album);
            await Assert.That(context.Metadata.Camera).IsEqualTo("Canon EOS R5");
            await Assert.That(context.Metadata.DateTime).IsEqualTo(originalDateTime);
            await Assert.That(context.Metadata.Location).IsNotNull();
            await Assert.That(context.Metadata.Location!.City).IsEqualTo("City");
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Test]
    public async Task Enrich_CalledMultipleTimes_OverwritesAlbum()
    {
        // Arrange
        var firstAlbum = "First Album";
        var secondAlbum = "Second Album";
        var metadataExtractor = Substitute.For<IFileMetadataExtractor>();
        metadataExtractor.GetAlbum(Arg.Any<FileInfo>()).Returns(firstAlbum, secondAlbum);

        var step = new AlbumMetadataEnrichmentStep(metadataExtractor);
        var tempFile = CreateTempFile("multiple_calls.jpg");

        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));

            // Act
            step.Enrich(context);
            await Assert.That(context.Metadata.Album).IsEqualTo(firstAlbum);

            step.Enrich(context);
            await Assert.That(context.Metadata.Album).IsEqualTo(secondAlbum);
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    #endregion

    #region Helper Methods

    private static string CreateTempFile(string fileName)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "AlbumEnrichmentTests", fileName);
        var directory = Path.GetDirectoryName(tempPath)!;

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(tempPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }); // Minimal JPEG header
        return tempPath;
    }

    private static void CleanupFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #endregion
}
