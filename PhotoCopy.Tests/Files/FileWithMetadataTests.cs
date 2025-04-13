using System;
using System.Collections.Generic;
using System.IO;
using PhotoCopy.Files;
using Xunit;

namespace PhotoCopy.Tests.Files;

public class FileWithMetadataTests : IClassFixture<ApplicationStateFixture>
{
    private readonly ApplicationStateFixture _fixture;
    private readonly string _testDirectory;

    public FileWithMetadataTests(ApplicationStateFixture fixture)
    {
        _fixture = fixture;
        ApplicationState.Options = new Options();
        _testDirectory = Path.Combine(Path.GetTempPath(), "FileWithMetadataTests");
        Directory.CreateDirectory(_testDirectory);
    }

    [Fact]
    public void AddRelatedFiles_InStrictMode_FindsRelatedFiles()
    {
        // Arrange
        var mainFile = Path.Combine(_testDirectory, "photo.jpg");
        var relatedFile = Path.Combine(_testDirectory, "photo.jpg.xmp");
        File.WriteAllText(mainFile, "test content");
        File.WriteAllText(relatedFile, "metadata content");

        try
        {
            var mainFileInfo = new FileInfo(mainFile);
            var relatedFileInfo = new FileInfo(relatedFile);
            var fileWithMetadata = new FileWithMetadata(mainFileInfo, 
                new FileDateTime(DateTime.Now, DateTimeSource.FileCreation));
            var relatedGenericFiles = new List<GenericFile>
            {
                new GenericFile(relatedFileInfo, new FileDateTime(DateTime.Now, DateTimeSource.FileCreation))
            };

            // Act
            fileWithMetadata.AddRelatedFiles(relatedGenericFiles, Options.RelatedFileLookup.strict);

            // Assert
            Assert.Single(fileWithMetadata.RelatedFileList);
            Assert.Equal(relatedFile, fileWithMetadata.RelatedFileList[0].File.FullName);
            Assert.Empty(relatedGenericFiles); // Related file should be removed from the list
        }
        finally
        {
            // Cleanup
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public void AddRelatedFiles_InLooseMode_FindsRelatedFiles()
    {
        // Arrange
        var mainFile = Path.Combine(_testDirectory, "IMG_001.jpg");
        var relatedFile = Path.Combine(_testDirectory, "IMG_001_metadata.xmp");
        File.WriteAllText(mainFile, "test content");
        File.WriteAllText(relatedFile, "metadata content");

        try
        {
            var mainFileInfo = new FileInfo(mainFile);
            var relatedFileInfo = new FileInfo(relatedFile);
            var fileWithMetadata = new FileWithMetadata(mainFileInfo, 
                new FileDateTime(DateTime.Now, DateTimeSource.FileCreation));
            var relatedGenericFiles = new List<GenericFile>
            {
                new GenericFile(relatedFileInfo, new FileDateTime(DateTime.Now, DateTimeSource.FileCreation))
            };

            // Act
            fileWithMetadata.AddRelatedFiles(relatedGenericFiles, Options.RelatedFileLookup.loose);

            // Assert
            Assert.Single(fileWithMetadata.RelatedFileList);
            Assert.Equal(relatedFile, fileWithMetadata.RelatedFileList[0].File.FullName);
        }
        finally
        {
            // Cleanup
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public void AddRelatedFiles_InNoneMode_DoesNotFindRelatedFiles()
    {
        // Arrange
        var mainFile = Path.Combine(_testDirectory, "photo.jpg");
        var relatedFile = Path.Combine(_testDirectory, "photo.jpg.xmp");
        File.WriteAllText(mainFile, "test content");
        File.WriteAllText(relatedFile, "metadata content");

        try
        {
            var mainFileInfo = new FileInfo(mainFile);
            var relatedFileInfo = new FileInfo(relatedFile);
            var fileWithMetadata = new FileWithMetadata(mainFileInfo, 
                new FileDateTime(DateTime.Now, DateTimeSource.FileCreation));
            var relatedGenericFiles = new List<GenericFile>
            {
                new GenericFile(relatedFileInfo, new FileDateTime(DateTime.Now, DateTimeSource.FileCreation))
            };

            // Act
            fileWithMetadata.AddRelatedFiles(relatedGenericFiles, Options.RelatedFileLookup.none);

            // Assert
            Assert.Empty(fileWithMetadata.RelatedFileList);
            Assert.Single(relatedGenericFiles); // List should remain unchanged
        }
        finally
        {
            // Cleanup
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public void CopyTo_CopiesMainFileAndRelatedFiles()
    {
        // Arrange
        var mainFile = Path.Combine(_testDirectory, "photo.jpg");
        var relatedFile = Path.Combine(_testDirectory, "photo.jpg.xmp");
        var destDir = Path.Combine(_testDirectory, "dest");
        Directory.CreateDirectory(destDir);
        File.WriteAllText(mainFile, "test content");
        File.WriteAllText(relatedFile, "metadata content");

        try
        {
            var mainFileInfo = new FileInfo(mainFile);
            var relatedFileInfo = new FileInfo(relatedFile);
            var fileWithMetadata = new FileWithMetadata(mainFileInfo, 
                new FileDateTime(DateTime.Now, DateTimeSource.FileCreation));
            var relatedGenericFiles = new List<GenericFile>
            {
                new GenericFile(relatedFileInfo, new FileDateTime(DateTime.Now, DateTimeSource.FileCreation))
            };
            fileWithMetadata.AddRelatedFiles(relatedGenericFiles, Options.RelatedFileLookup.strict);

            // Act
            var destPath = Path.Combine(destDir, "photo.jpg");
            fileWithMetadata.CopyTo(destPath, false);

            // Assert
            Assert.True(File.Exists(destPath));
            Assert.True(File.Exists(Path.Combine(destDir, "photo.jpg.xmp")));
        }
        finally
        {
            // Cleanup
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public void MoveTo_MovesMainFileAndRelatedFiles()
    {
        // Arrange
        var mainFile = Path.Combine(_testDirectory, "photo.jpg");
        var relatedFile = Path.Combine(_testDirectory, "photo.jpg.xmp");
        var destDir = Path.Combine(_testDirectory, "dest");
        Directory.CreateDirectory(destDir);
        File.WriteAllText(mainFile, "test content");
        File.WriteAllText(relatedFile, "metadata content");

        try
        {
            var mainFileInfo = new FileInfo(mainFile);
            var relatedFileInfo = new FileInfo(relatedFile);
            var fileWithMetadata = new FileWithMetadata(mainFileInfo, 
                new FileDateTime(DateTime.Now, DateTimeSource.FileCreation));
            var relatedGenericFiles = new List<GenericFile>
            {
                new GenericFile(relatedFileInfo, new FileDateTime(DateTime.Now, DateTimeSource.FileCreation))
            };
            fileWithMetadata.AddRelatedFiles(relatedGenericFiles, Options.RelatedFileLookup.strict);

            // Act
            var destPath = Path.Combine(destDir, "photo.jpg");
            fileWithMetadata.MoveTo(destPath, false);

            // Assert
            Assert.False(File.Exists(mainFile));
            Assert.False(File.Exists(relatedFile));
            Assert.True(File.Exists(destPath));
            Assert.True(File.Exists(Path.Combine(destDir, "photo.jpg.xmp")));
        }
        finally
        {
            // Cleanup
            Directory.Delete(_testDirectory, true);
        }
    }
}