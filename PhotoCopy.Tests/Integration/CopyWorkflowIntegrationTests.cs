using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Directories;
using PhotoCopy.Files;
using PhotoCopy.Files.Metadata;
using PhotoCopy.Rollback;
using PhotoCopy.Tests.TestingImplementation;
using PhotoCopy.Validators;

namespace PhotoCopy.Tests.Integration;

[NotInParallel("FileOperations")]
[Property("Category", "Integration")]
public class CopyWorkflowIntegrationTests
{
    private string _sourceDir = null!;
    private string _destDir = null!;

    private void SetupDirectories()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "CopyWorkflowTests", Guid.NewGuid().ToString());
        _sourceDir = Path.Combine(basePath, "source");
        _destDir = Path.Combine(basePath, "dest");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_destDir);
    }

    private void CleanupDirectories()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (Directory.Exists(Path.GetDirectoryName(_sourceDir)!))
                Directory.Delete(Path.GetDirectoryName(_sourceDir)!, true);
        }
        catch (IOException) { }
    }

    private static void CreateTestFile(string path, DateTime? fileDateTime = null)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllBytes(path, TestSampleImages.JpegWithNoExif);

        if (fileDateTime.HasValue)
        {
            File.SetCreationTime(path, fileDateTime.Value);
            File.SetLastWriteTime(path, fileDateTime.Value);
        }
    }

    private DirectoryCopier CreateCopier(string destTemplate, bool dryRun = false, 
        bool skipExisting = false, OperationMode mode = OperationMode.Copy)
    {
        var config = new PhotoCopyConfig
        {
            Source = _sourceDir, Destination = destTemplate, DryRun = dryRun,
            SkipExisting = skipExisting, Mode = mode, CalculateChecksums = false,
            DuplicatesFormat = "_{number}",
            AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".jpg", ".jpeg", ".png", ".mov", ".mp4" }
        };
        var options = Microsoft.Extensions.Options.Options.Create(config);

        var metadataExtractor = new FileMetadataExtractor(
            Substitute.For<ILogger<FileMetadataExtractor>>(), options);
        var metadataEnricher = new MetadataEnricher(new IMetadataEnrichmentStep[]
        {
            new DateTimeMetadataEnrichmentStep(metadataExtractor),
            new LocationMetadataEnrichmentStep(metadataExtractor, 
                Substitute.For<IReverseGeocodingService>()),
            new ChecksumMetadataEnrichmentStep(new Sha256ChecksumCalculator(), options)
        });
        var fileFactory = new FileFactory(metadataEnricher, 
            Substitute.For<ILogger<FileWithMetadata>>(), options);
        var scanner = new DirectoryScanner(
            Substitute.For<ILogger<DirectoryScanner>>(), options, fileFactory);
        var fileSystem = new FileSystem(
            Substitute.For<ILogger<FileSystem>>(), scanner);

        return new DirectoryCopier(
            Substitute.For<ILogger<DirectoryCopier>>(), fileSystem, options, Substitute.For<ITransactionLogger>(), new FileValidationService());
    }

    [Test]
    public async Task CopySingleFile_WithYearMonthTemplate_CreatesCorrectStructure()
    {
        SetupDirectories();
        try
        {
            CreateTestFile(Path.Combine(_sourceDir, "photo1.jpg"), new DateTime(2024, 3, 15));
            var copier = CreateCopier(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));

            copier.Copy(Array.Empty<IValidator>());

            await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "03", "photo1.jpg"))).IsTrue();
        }
        finally { CleanupDirectories(); }
    }

    [Test]
    public async Task CopyMultipleFiles_ToOrganizedStructure_CreatesAllFiles()
    {
        SetupDirectories();
        try
        {
            CreateTestFile(Path.Combine(_sourceDir, "jan.jpg"), new DateTime(2024, 1, 10));
            CreateTestFile(Path.Combine(_sourceDir, "feb.jpg"), new DateTime(2024, 2, 20));
            CreateTestFile(Path.Combine(_sourceDir, "march.jpg"), new DateTime(2024, 3, 30));
            var copier = CreateCopier(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));

            copier.Copy(Array.Empty<IValidator>());

            await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "01", "jan.jpg"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "02", "feb.jpg"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "03", "march.jpg"))).IsTrue();
        }
        finally { CleanupDirectories(); }
    }

    [Test]
    public async Task CopyDuplicateFile_AddsNumberSuffix()
    {
        SetupDirectories();
        try
        {
            CreateTestFile(Path.Combine(_sourceDir, "photo.jpg"), new DateTime(2024, 5, 1));
            var destFolder = Path.Combine(_destDir, "2024", "05");
            Directory.CreateDirectory(destFolder);
            CreateTestFile(Path.Combine(destFolder, "photo.jpg"));
            var copier = CreateCopier(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));

            copier.Copy(Array.Empty<IValidator>());

            await Assert.That(File.Exists(Path.Combine(destFolder, "photo.jpg"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(destFolder, "photo_1.jpg"))).IsTrue();
        }
        finally { CleanupDirectories(); }
    }

    [Test]
    public async Task DryRunMode_DoesNotCopyFiles()
    {
        SetupDirectories();
        try
        {
            CreateTestFile(Path.Combine(_sourceDir, "photo.jpg"), new DateTime(2024, 6, 1));
            var copier = CreateCopier(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"), 
                dryRun: true);

            copier.Copy(Array.Empty<IValidator>());

            await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "06", "photo.jpg"))).IsFalse();
            await Assert.That(Directory.GetFiles(_destDir, "*", SearchOption.AllDirectories).Length)
                .IsEqualTo(0);
        }
        finally { CleanupDirectories(); }
    }

    [Test]
    public async Task SkipExisting_DoesNotOverwriteFile()
    {
        SetupDirectories();
        try
        {
            CreateTestFile(Path.Combine(_sourceDir, "photo.jpg"), new DateTime(2024, 7, 1));
            var destFolder = Path.Combine(_destDir, "2024", "07");
            Directory.CreateDirectory(destFolder);
            var existingFile = Path.Combine(destFolder, "photo.jpg");
            var originalContent = new byte[] { 0x01, 0x02, 0x03 };
            File.WriteAllBytes(existingFile, originalContent);
            var copier = CreateCopier(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"), 
                skipExisting: true);

            copier.Copy(Array.Empty<IValidator>());

            await Assert.That(File.ReadAllBytes(existingFile)).IsEquivalentTo(originalContent);
            await Assert.That(File.Exists(Path.Combine(destFolder, "photo_1.jpg"))).IsFalse();
        }
        finally { CleanupDirectories(); }
    }

    [Test]
    public async Task MoveMode_DeletesSourceFile()
    {
        SetupDirectories();
        try
        {
            var sourceFile = Path.Combine(_sourceDir, "photo.jpg");
            CreateTestFile(sourceFile, new DateTime(2024, 8, 1));
            var copier = CreateCopier(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"), 
                mode: OperationMode.Move);

            copier.Copy(Array.Empty<IValidator>());

            await Assert.That(File.Exists(sourceFile)).IsFalse();
            await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "08", "photo.jpg"))).IsTrue();
        }
        finally { CleanupDirectories(); }
    }

    [Test]
    public async Task CopyWithDayTemplate_CreatesFullDateStructure()
    {
        SetupDirectories();
        try
        {
            CreateTestFile(Path.Combine(_sourceDir, "vacation.jpg"), new DateTime(2024, 12, 25));
            var copier = CreateCopier(
                Path.Combine(_destDir, "{year}", "{month}", "{day}", "{name}{ext}"));

            copier.Copy(Array.Empty<IValidator>());

            await Assert.That(File.Exists(
                Path.Combine(_destDir, "2024", "12", "25", "vacation.jpg"))).IsTrue();
        }
        finally { CleanupDirectories(); }
    }

    [Test]
    public async Task CopyFromSubdirectory_PreservesSourceStructure()
    {
        SetupDirectories();
        try
        {
            var subDir = Path.Combine(_sourceDir, "vacation", "beach");
            Directory.CreateDirectory(subDir);
            CreateTestFile(Path.Combine(subDir, "sunset.jpg"), new DateTime(2024, 7, 4));
            var copier = CreateCopier(Path.Combine(_destDir, "{year}", "{directory}", "{name}{ext}"));

            copier.Copy(Array.Empty<IValidator>());

            await Assert.That(File.Exists(
                Path.Combine(_destDir, "2024", "vacation", "beach", "sunset.jpg"))).IsTrue();
        }
        finally { CleanupDirectories(); }
    }

    [Test]
    public async Task MultipleDuplicates_CreatesSequentialSuffixes()
    {
        SetupDirectories();
        try
        {
            CreateTestFile(Path.Combine(_sourceDir, "photo.jpg"), new DateTime(2024, 9, 1));
            var destFolder = Path.Combine(_destDir, "2024", "09");
            Directory.CreateDirectory(destFolder);
            CreateTestFile(Path.Combine(destFolder, "photo.jpg"));
            CreateTestFile(Path.Combine(destFolder, "photo_1.jpg"));
            CreateTestFile(Path.Combine(destFolder, "photo_2.jpg"));
            var copier = CreateCopier(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));

            copier.Copy(Array.Empty<IValidator>());

            await Assert.That(File.Exists(Path.Combine(destFolder, "photo_3.jpg"))).IsTrue();
        }
        finally { CleanupDirectories(); }
    }

    [Test]
    public async Task CopyWithFlatStructure_AllFilesInSameFolder()
    {
        SetupDirectories();
        try
        {
            CreateTestFile(Path.Combine(_sourceDir, "a.jpg"), new DateTime(2024, 1, 1));
            CreateTestFile(Path.Combine(_sourceDir, "b.jpg"), new DateTime(2024, 6, 1));
            CreateTestFile(Path.Combine(_sourceDir, "c.jpg"), new DateTime(2024, 12, 1));
            var copier = CreateCopier(Path.Combine(_destDir, "photos", "{name}{ext}"));

            copier.Copy(Array.Empty<IValidator>());

            var photosDir = Path.Combine(_destDir, "photos");
            await Assert.That(File.Exists(Path.Combine(photosDir, "a.jpg"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(photosDir, "b.jpg"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(photosDir, "c.jpg"))).IsTrue();
        }
        finally { CleanupDirectories(); }
    }

    [Test]
    public async Task MoveMode_DryRun_DoesNotDeleteSource()
    {
        SetupDirectories();
        try
        {
            var sourceFile = Path.Combine(_sourceDir, "photo.jpg");
            CreateTestFile(sourceFile, new DateTime(2024, 10, 1));
            var copier = CreateCopier(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"), 
                dryRun: true, mode: OperationMode.Move);

            copier.Copy(Array.Empty<IValidator>());

            await Assert.That(File.Exists(sourceFile)).IsTrue();
            await Assert.That(File.Exists(Path.Combine(_destDir, "2024", "10", "photo.jpg"))).IsFalse();
        }
        finally { CleanupDirectories(); }
    }

    [Test]
    public async Task CopyEmptySource_CompletesWithoutError()
    {
        SetupDirectories();
        try
        {
            var copier = CreateCopier(Path.Combine(_destDir, "{year}", "{month}", "{name}{ext}"));
            Exception? exception = null;

            try { copier.Copy(Array.Empty<IValidator>()); }
            catch (Exception ex) { exception = ex; }

            await Assert.That(exception).IsNull();
            await Assert.That(Directory.GetFiles(_destDir, "*", SearchOption.AllDirectories).Length)
                .IsEqualTo(0);
        }
        finally { CleanupDirectories(); }
    }
}
