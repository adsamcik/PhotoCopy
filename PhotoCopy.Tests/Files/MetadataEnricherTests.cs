using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;
using PhotoCopy.Files;
using PhotoCopy.Files.Metadata;
using Xunit;

namespace PhotoCopy.Tests.Files;

public class MetadataEnricherTests
{
    private readonly PhotoCopyConfig _config;
    private readonly IOptions<PhotoCopyConfig> _options;

    public MetadataEnricherTests()
    {
        _config = new PhotoCopyConfig
        {
            CalculateChecksums = true
        };
        _options = Microsoft.Extensions.Options.Options.Create(_config);
    }

    #region MetadataEnricher Core Tests

    [Fact]
    public void Enrich_WithNoSteps_ReturnsDefaultMetadata()
    {
        // Arrange
        var enricher = new MetadataEnricher(Array.Empty<IMetadataEnrichmentStep>());
        var tempFile = CreateTempFile("test.jpg");
        
        try
        {
            // Act
            var metadata = enricher.Enrich(new FileInfo(tempFile));
            
            // Assert
            metadata.Should().NotBeNull();
            metadata.DateTime.Should().NotBeNull();
            metadata.Location.Should().BeNull();
            metadata.Checksum.Should().BeNull();
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Fact]
    public void Enrich_WithMultipleSteps_ExecutesAllStepsInOrder()
    {
        // Arrange
        var executionOrder = new List<int>();
        
        var step1 = Substitute.For<IMetadataEnrichmentStep>();
        step1.When(x => x.Enrich(Arg.Any<FileMetadataContext>())).Do(_ => executionOrder.Add(1));
        
        var step2 = Substitute.For<IMetadataEnrichmentStep>();
        step2.When(x => x.Enrich(Arg.Any<FileMetadataContext>())).Do(_ => executionOrder.Add(2));
        
        var step3 = Substitute.For<IMetadataEnrichmentStep>();
        step3.When(x => x.Enrich(Arg.Any<FileMetadataContext>())).Do(_ => executionOrder.Add(3));
        
        var enricher = new MetadataEnricher(new[] { step1, step2, step3 });
        var tempFile = CreateTempFile("test.jpg");
        
        try
        {
            // Act
            enricher.Enrich(new FileInfo(tempFile));
            
            // Assert
            executionOrder.Should().BeEquivalentTo(new[] { 1, 2, 3 }, options => options.WithStrictOrdering());
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Fact]
    public void Enrich_StepsShareSameContext()
    {
        // Arrange
        FileMetadataContext? capturedContext1 = null;
        FileMetadataContext? capturedContext2 = null;
        
        var step1 = Substitute.For<IMetadataEnrichmentStep>();
        step1.When(x => x.Enrich(Arg.Any<FileMetadataContext>())).Do(ci => capturedContext1 = ci.Arg<FileMetadataContext>());
        
        var step2 = Substitute.For<IMetadataEnrichmentStep>();
        step2.When(x => x.Enrich(Arg.Any<FileMetadataContext>())).Do(ci => capturedContext2 = ci.Arg<FileMetadataContext>());
        
        var enricher = new MetadataEnricher(new[] { step1, step2 });
        var tempFile = CreateTempFile("test.jpg");
        
        try
        {
            // Act
            enricher.Enrich(new FileInfo(tempFile));
            
            // Assert
            capturedContext1.Should().NotBeNull();
            capturedContext2.Should().NotBeNull();
            capturedContext1.Should().BeSameAs(capturedContext2);
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Fact]
    public void Enrich_EarlierStepModifiesContext_LaterStepsSeesModification()
    {
        // Arrange
        var testLocation = new LocationData("TestCity", "TestState", "TestCountry");
        
        var step1 = Substitute.For<IMetadataEnrichmentStep>();
        step1.When(x => x.Enrich(Arg.Any<FileMetadataContext>())).Do(ci =>
        {
            var context = ci.Arg<FileMetadataContext>();
            context.Metadata.Location = testLocation;
        });
        
        LocationData? observedLocation = null;
        var step2 = Substitute.For<IMetadataEnrichmentStep>();
        step2.When(x => x.Enrich(Arg.Any<FileMetadataContext>())).Do(ci =>
        {
            var context = ci.Arg<FileMetadataContext>();
            observedLocation = context.Metadata.Location;
        });
        
        var enricher = new MetadataEnricher(new[] { step1, step2 });
        var tempFile = CreateTempFile("test.jpg");
        
        try
        {
            // Act
            enricher.Enrich(new FileInfo(tempFile));
            
            // Assert
            observedLocation.Should().BeSameAs(testLocation);
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    #endregion

    #region DateTimeMetadataEnrichmentStep Tests

    [Fact]
    public void DateTimeStep_UpdatesDateTimeFromExtractor()
    {
        // Arrange
        var expectedDateTime = new FileDateTime(new DateTime(2023, 6, 15, 14, 30, 0), DateTimeSource.ExifDateTimeOriginal);
        
        var extractor = Substitute.For<IFileMetadataExtractor>();
        extractor.GetDateTime(Arg.Any<FileInfo>()).Returns(expectedDateTime);
        
        var step = new DateTimeMetadataEnrichmentStep(extractor);
        var tempFile = CreateTempFile("photo.jpg");
        
        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));
            
            // Act
            step.Enrich(context);
            
            // Assert
            context.Metadata.DateTime.Should().Be(expectedDateTime);
            context.Metadata.DateTime.DateTime.Should().Be(new DateTime(2023, 6, 15, 14, 30, 0));
            context.Metadata.DateTime.Source.Should().Be(DateTimeSource.ExifDateTimeOriginal);
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Fact]
    public void DateTimeStep_PreservesExistingMetadataProperties()
    {
        // Arrange
        var testLocation = new LocationData("City", "State", "Country");
        var expectedDateTime = new FileDateTime(new DateTime(2023, 6, 15), DateTimeSource.ExifDateTimeOriginal);
        
        var extractor = Substitute.For<IFileMetadataExtractor>();
        extractor.GetDateTime(Arg.Any<FileInfo>()).Returns(expectedDateTime);
        
        var step = new DateTimeMetadataEnrichmentStep(extractor);
        var tempFile = CreateTempFile("photo.jpg");
        
        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));
            context.Metadata.Location = testLocation;
            context.Metadata.Checksum = "abc123";
            
            // Act
            step.Enrich(context);
            
            // Assert
            context.Metadata.Location.Should().BeSameAs(testLocation);
            context.Metadata.Checksum.Should().Be("abc123");
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    #endregion

    #region LocationMetadataEnrichmentStep Tests

    [Fact]
    public void LocationStep_WithCoordinates_SetsLocation()
    {
        // Arrange
        var coords = (Latitude: 40.7128, Longitude: -74.0060);
        var expectedLocation = new LocationData("New York", "NY", "USA");
        
        var extractor = Substitute.For<IFileMetadataExtractor>();
        extractor.GetCoordinates(Arg.Any<FileInfo>()).Returns(coords);
        
        var geocoding = Substitute.For<IReverseGeocodingService>();
        geocoding.ReverseGeocode(coords.Latitude, coords.Longitude).Returns(expectedLocation);
        
        var step = new LocationMetadataEnrichmentStep(extractor, geocoding);
        var tempFile = CreateTempFile("photo.jpg");
        
        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));
            
            // Act
            step.Enrich(context);
            
            // Assert
            context.Metadata.Location.Should().Be(expectedLocation);
            context.Metadata.Location!.City.Should().Be("New York");
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Fact]
    public void LocationStep_WithoutCoordinates_LeavesLocationNull()
    {
        // Arrange
        var extractor = Substitute.For<IFileMetadataExtractor>();
        extractor.GetCoordinates(Arg.Any<FileInfo>()).Returns((ValueTuple<double, double>?)null);
        
        var geocoding = Substitute.For<IReverseGeocodingService>();
        
        var step = new LocationMetadataEnrichmentStep(extractor, geocoding);
        var tempFile = CreateTempFile("photo.jpg");
        
        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));
            
            // Act
            step.Enrich(context);
            
            // Assert
            context.Metadata.Location.Should().BeNull();
            geocoding.DidNotReceive().ReverseGeocode(Arg.Any<double>(), Arg.Any<double>());
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    #endregion

    #region ChecksumMetadataEnrichmentStep Tests

    [Fact]
    public void ChecksumStep_WhenEnabled_CalculatesChecksum()
    {
        // Arrange
        var calculator = Substitute.For<IChecksumCalculator>();
        calculator.Calculate(Arg.Any<FileInfo>()).Returns("abc123def456");
        
        var config = new PhotoCopyConfig { CalculateChecksums = true };
        var options = Microsoft.Extensions.Options.Options.Create(config);
        
        var step = new ChecksumMetadataEnrichmentStep(calculator, options);
        var tempFile = CreateTempFile("photo.jpg");
        
        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));
            
            // Act
            step.Enrich(context);
            
            // Assert
            context.Metadata.Checksum.Should().Be("abc123def456");
            calculator.Received(1).Calculate(Arg.Any<FileInfo>());
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Fact]
    public void ChecksumStep_WhenDisabled_SkipsCalculation()
    {
        // Arrange
        var calculator = Substitute.For<IChecksumCalculator>();
        
        var config = new PhotoCopyConfig { CalculateChecksums = false };
        var options = Microsoft.Extensions.Options.Options.Create(config);
        
        var step = new ChecksumMetadataEnrichmentStep(calculator, options);
        var tempFile = CreateTempFile("photo.jpg");
        
        try
        {
            var context = new FileMetadataContext(new FileInfo(tempFile));
            
            // Act
            step.Enrich(context);
            
            // Assert
            context.Metadata.Checksum.Should().BeNull();
            calculator.DidNotReceive().Calculate(Arg.Any<FileInfo>());
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    #endregion

    #region FileMetadataContext Tests

    [Fact]
    public void FileMetadataContext_InitializesWithFileInfo()
    {
        // Arrange
        var tempFile = CreateTempFile("test.jpg");
        
        try
        {
            var fileInfo = new FileInfo(tempFile);
            
            // Act
            var context = new FileMetadataContext(fileInfo);
            
            // Assert
            context.FileInfo.Should().Be(fileInfo);
            context.Metadata.Should().NotBeNull();
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Fact]
    public void FileMetadataContext_InitializesWithDefaultDateTime()
    {
        // Arrange
        var tempFile = CreateTempFile("test.jpg");
        
        try
        {
            var fileInfo = new FileInfo(tempFile);
            
            // Act
            var context = new FileMetadataContext(fileInfo);
            
            // Assert
            context.Metadata.DateTime.Should().NotBeNull();
            context.Metadata.DateTime.Source.Should().Be(DateTimeSource.FileCreation);
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    #endregion

    #region Integration Tests - Full Pipeline

    [Fact]
    public void FullPipeline_AllStepsExecute_MetadataFullyPopulated()
    {
        // Arrange
        var expectedDateTime = new FileDateTime(new DateTime(2023, 6, 15), DateTimeSource.ExifDateTimeOriginal);
        var expectedLocation = new LocationData("Paris", "Île-de-France", "France");
        var expectedChecksum = "sha256hash";
        var coords = (Latitude: 48.8566, Longitude: 2.3522);
        
        var extractor = Substitute.For<IFileMetadataExtractor>();
        extractor.GetDateTime(Arg.Any<FileInfo>()).Returns(expectedDateTime);
        extractor.GetCoordinates(Arg.Any<FileInfo>()).Returns(coords);
        
        var geocoding = Substitute.For<IReverseGeocodingService>();
        geocoding.ReverseGeocode(coords.Latitude, coords.Longitude).Returns(expectedLocation);
        
        var calculator = Substitute.For<IChecksumCalculator>();
        calculator.Calculate(Arg.Any<FileInfo>()).Returns(expectedChecksum);
        
        var config = new PhotoCopyConfig { CalculateChecksums = true };
        var options = Microsoft.Extensions.Options.Options.Create(config);
        
        var steps = new IMetadataEnrichmentStep[]
        {
            new DateTimeMetadataEnrichmentStep(extractor),
            new LocationMetadataEnrichmentStep(extractor, geocoding),
            new ChecksumMetadataEnrichmentStep(calculator, options)
        };
        
        var enricher = new MetadataEnricher(steps);
        var tempFile = CreateTempFile("vacation.jpg");
        
        try
        {
            // Act
            var metadata = enricher.Enrich(new FileInfo(tempFile));
            
            // Assert
            metadata.DateTime.Should().Be(expectedDateTime);
            metadata.Location.Should().Be(expectedLocation);
            metadata.Checksum.Should().Be(expectedChecksum);
        }
        finally
        {
            CleanupFile(tempFile);
        }
    }

    [Fact]
    public void FullPipeline_WithMissingOptionalData_HandlesGracefully()
    {
        // Arrange
        var expectedDateTime = new FileDateTime(new DateTime(2023, 6, 15), DateTimeSource.FileModification);
        
        var extractor = Substitute.For<IFileMetadataExtractor>();
        extractor.GetDateTime(Arg.Any<FileInfo>()).Returns(expectedDateTime);
        extractor.GetCoordinates(Arg.Any<FileInfo>()).Returns((ValueTuple<double, double>?)null); // No GPS
        
        var geocoding = Substitute.For<IReverseGeocodingService>();
        
        var calculator = Substitute.For<IChecksumCalculator>();
        calculator.Calculate(Arg.Any<FileInfo>()).Returns("checksum");
        
        var config = new PhotoCopyConfig { CalculateChecksums = true };
        var options = Microsoft.Extensions.Options.Options.Create(config);
        
        var steps = new IMetadataEnrichmentStep[]
        {
            new DateTimeMetadataEnrichmentStep(extractor),
            new LocationMetadataEnrichmentStep(extractor, geocoding),
            new ChecksumMetadataEnrichmentStep(calculator, options)
        };
        
        var enricher = new MetadataEnricher(steps);
        var tempFile = CreateTempFile("screenshot.png");
        
        try
        {
            // Act
            var metadata = enricher.Enrich(new FileInfo(tempFile));
            
            // Assert
            metadata.DateTime.Should().Be(expectedDateTime);
            metadata.Location.Should().BeNull(); // No GPS data
            metadata.Checksum.Should().Be("checksum");
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
        var tempPath = Path.Combine(Path.GetTempPath(), "MetadataEnricherTests", fileName);
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
