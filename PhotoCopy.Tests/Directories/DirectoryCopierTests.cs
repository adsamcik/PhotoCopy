using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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

public class DirectoryCopierTests
{
    private readonly ILogger<DirectoryCopier> _logger;
    private readonly IFileSystem _fileSystem;
    private readonly PhotoCopyConfig _config;
    private readonly IOptions<PhotoCopyConfig> _options;
    private readonly ITransactionLogger _transactionLogger;
    private readonly IFileValidationService _fileValidationService;

    public DirectoryCopierTests()
    {
        _logger = Substitute.For<ILogger<DirectoryCopier>>();
        _fileSystem = Substitute.For<IFileSystem>();
        _transactionLogger = Substitute.For<ITransactionLogger>();
        _fileValidationService = new FileValidationService();
        
        _config = new PhotoCopyConfig
        {
            Source = TestPaths.Source,
            Destination = Path.Combine(TestPaths.Dest, "{year}", "{month}", "{day}", "{name}{ext}"),
            DryRun = true,
            DuplicatesFormat = "-{number}"
        };
        
        _options = Microsoft.Extensions.Options.Options.Create(_config);
    }

    #region GeneratePath Tests - Various Templates

    [Test]
    public void GeneratePath_WithYearTemplate_ReplacesYearCorrectly()
    {
        // Arrange
        _config.Destination = Path.Combine(TestPaths.Dest, "{year}", "photo.jpg");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("2023", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithMonthTemplate_ReplacesMonthWithPadding()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{month}", "photo.jpg");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("06", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithDayTemplate_ReplacesDayWithPadding()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{day}", "photo.jpg");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 5));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("05", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithNameTemplate_ReplacesWithFileNameWithoutExtension()
    {
        // Arrange
        _config.Destination = Path.Combine(TestPaths.Dest, "{name}.jpg");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("vacation_photo.jpg", new DateTime(2023, 6, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("vacation_photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithExtensionTemplate_ReplacesWithFileExtension()
    {
        // Arrange
        _config.Destination = Path.Combine(TestPaths.Dest, "photo{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.png", new DateTime(2023, 6, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("photo.png"));
    }

    [Test]
    public void GeneratePath_WithDirectoryTemplate_ReplacesWithRelativeDirectory()
    {
        // Arrange
        _config.Source = TestPaths.Source;
        _config.Destination = TestPaths.DestPattern("{directory}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithPath(TestPaths.InSource("Vacation", "2023", "test.jpg"), new DateTime(2023, 6, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("Vacation", "2023", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithAllDateTemplates_ReplacesAllCorrectly()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{month}", "{day}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("photo.jpg", new DateTime(2023, 12, 25));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("2023", "12", "25", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithNameNoExtTemplate_ReplacesWithFileNameWithoutExtension()
    {
        // Arrange
        _config.Destination = Path.Combine(TestPaths.Dest, "{namenoext}_backup{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("photo.jpg", new DateTime(2023, 6, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("photo_backup.jpg"));
    }

    [Test]
    public void GeneratePath_WithFilenameTemplate_ReplacesWithFullFilename()
    {
        // Arrange
        _config.Destination = Path.Combine(TestPaths.Dest, "{filename}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("photo.jpg", new DateTime(2023, 6, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("photo.jpg"));
    }

    #endregion

    #region GeneratePath Tests - Location Data

    [Test]
    public void GeneratePath_WithCityTemplate_ReplacesCityFromLocation()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{city}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15), 
            new LocationData("New York", "New York", null, "NY", "USA"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("New York", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithStateTemplate_ReplacesStateFromLocation()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{state}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15), 
            new LocationData("Los Angeles", "Los Angeles", null, "California", "USA"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("California", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithCountryTemplate_ReplacesCountryFromLocation()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15), 
            new LocationData("Paris", "Paris", null, "Île-de-France", "France"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("France", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithAllLocationTemplates_ReplacesAllCorrectly()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{city}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("vacation.jpg", new DateTime(2023, 6, 15), 
            new LocationData("Miami", "Miami", null, "Florida", "USA"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("USA", "Florida", "Miami", "vacation.jpg"));
    }

    [Test]
    public void GeneratePath_WithLocationData_IncludesLocationInfo()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{city}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("beach.jpg", new DateTime(2023, 7, 4), 
            new LocationData("San Diego", "San Diego", null, "California", "USA"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("2023", "San Diego", "beach.jpg"));
    }

    [Test]
    public void GeneratePath_WithNullState_UsesUnknownForState()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{state}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15), 
            new LocationData("Singapore", "Singapore", null, null, "Singapore"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("Unknown", "test.jpg"));
    }

    #endregion

    #region GeneratePath Tests - Null Location

    [Test]
    public void GeneratePath_WithNullLocation_UsesCityAsUnknown()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{city}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("Unknown", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithNullLocation_UsesStateAsUnknown()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{state}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("Unknown", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithNullLocation_UsesCountryAsUnknown()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("Unknown", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithNullLocation_UsesUnknownForAllLocationFields()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{city}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("Unknown", "Unknown", "Unknown", "test.jpg"));
    }

    #endregion

    #region GeneratePath Tests - County Variable

    [Test]
    public void GeneratePath_WithCountyTemplate_ReplacesCountyFromLocation()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{county}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15), 
            new LocationData("Manhattan", "Manhattan", "New York County", "NY", "USA"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("New York County", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithNullCounty_UsesUnknown()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{county}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15), 
            new LocationData("London", "London", null, "ENG", "GB"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("Unknown", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithAllLocationIncludingCounty_ReplacesAllCorrectly()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{county}", "{city}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("vacation.jpg", new DateTime(2023, 6, 15), 
            new LocationData("Manhattan", "Manhattan", "New York County", "NY", "USA"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("USA", "NY", "New York County", "Manhattan", "vacation.jpg"));
    }

    #endregion

    #region GeneratePath Tests - Location Granularity

    [Test]
    public void GeneratePath_WithCountryGranularity_OnlyShowsCountry()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{city}", "{name}{ext}");
        _config.LocationGranularity = LocationGranularity.Country;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15), 
            new LocationData("New York", "New York", "New York County", "NY", "USA"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("USA", "Unknown", "Unknown", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithStateGranularity_ShowsStateAndCountry()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{city}", "{name}{ext}");
        _config.LocationGranularity = LocationGranularity.State;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15), 
            new LocationData("New York", "New York", "New York County", "NY", "USA"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("USA", "NY", "Unknown", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithCountyGranularity_ShowsCountyStateAndCountry()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{county}", "{city}", "{name}{ext}");
        _config.LocationGranularity = LocationGranularity.County;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15), 
            new LocationData("Manhattan", "Manhattan", "New York County", "NY", "USA"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("USA", "NY", "New York County", "Unknown", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithCityGranularity_ShowsAllFields()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{county}", "{city}", "{name}{ext}");
        _config.LocationGranularity = LocationGranularity.City;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15), 
            new LocationData("Manhattan", "Manhattan", "New York County", "NY", "USA"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("USA", "NY", "New York County", "Manhattan", "test.jpg"));
    }

    #endregion

    #region GeneratePath Tests - Full Country Names

    [Test]
    public void GeneratePath_WithFullCountryNames_UsesFullName()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{city}", "{name}{ext}");
        _config.UseFullCountryNames = true;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15), 
            new LocationData("New York", "New York", null, "NY", "US"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("United States", "New York", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithoutFullCountryNames_UsesCode()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{city}", "{name}{ext}");
        _config.UseFullCountryNames = false;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15), 
            new LocationData("Paris", "Paris", null, "IDF", "FR"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("FR", "Paris", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithFullCountryNames_HandlesUnknownCode()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{name}{ext}");
        _config.UseFullCountryNames = true;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15), 
            new LocationData("Unknown City", "Unknown City", null, null, "XX")); // Unknown country code

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("XX", "test.jpg")); // Returns code as-is when not found
    }

    #endregion

    #region GeneratePath Tests - Custom Fallback

    [Test]
    public void GeneratePath_WithCustomFallback_UsesCustomText()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{city}", "{name}{ext}");
        _config.UnknownLocationFallback = "NoLocation";
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("NoLocation", "NoLocation", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithCustomFallbackAndGranularity_UsesFallbackForMaskedFields()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{city}", "{name}{ext}");
        _config.UnknownLocationFallback = "NA"; // Avoid slashes which get sanitized
        _config.LocationGranularity = LocationGranularity.Country;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15), 
            new LocationData("Paris", "Paris", null, "IDF", "FR"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - State and City should be masked with custom fallback
        result.Should().Be(TestPaths.InDest("FR", "NA", "NA", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithEmptyFallback_UsesEmptyString()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{city}", "{name}{ext}");
        _config.UnknownLocationFallback = "";
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Empty fallbacks are now cleaned up by path normalization
        // Consecutive separators from empty variables are collapsed
        result.Should().Be(TestPaths.Dest + Path.DirectorySeparatorChar + "test.jpg");
    }

    #endregion

    #region GeneratePath Tests - Inline Fallback Syntax

    [Test]
    public void GeneratePath_WithInlineFallback_UsesInlineFallbackWhenLocationNull()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country|NoCountry}", "{city|NoCity}", "{name}{ext}");
        _config.UnknownLocationFallback = "Unknown"; // Should be overridden by inline fallback
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15)); // No location

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Inline fallbacks are used instead of global fallback
        result.Should().Be(TestPaths.InDest("NoCountry", "NoCity", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithInlineFallback_UsesActualValueWhenLocationExists()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country|NoCountry}", "{city|NoCity}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("Prague", "Prague", null, null, "CZ"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Actual values are used, not fallbacks
        result.Should().Be(TestPaths.InDest("CZ", "Prague", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithEmptyInlineFallback_UsesEmptyString()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country|}", "{city|}", "{name}{ext}");
        _config.UnknownLocationFallback = "Unknown"; // Should be overridden by empty inline fallback
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15)); // No location

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Empty inline fallbacks result in normalized path (consecutive separators removed)
        result.Should().Be(TestPaths.Dest + Path.DirectorySeparatorChar + "test.jpg");
    }

    [Test]
    public void GeneratePath_WithPartialInlineFallback_MixesInlineAndGlobalFallback()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country|NoCountry}", "{city}", "{name}{ext}");
        _config.UnknownLocationFallback = "Unknown";
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15)); // No location

        // Act
        var result = copier.GeneratePath(file);

        // Assert - {country} uses inline fallback, {city} uses global fallback
        result.Should().Be(TestPaths.InDest("NoCountry", "Unknown", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithInlineFallbackAndCasing_AppliesCasingToFallback()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country|no-country}", "{city|no-city}", "{name}{ext}");
        _config.PathCasing = PathCasing.Uppercase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15)); // No location

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Inline fallbacks have casing applied
        result.Should().Be(TestPaths.InDest("NO-COUNTRY", "NO-CITY", "test.jpg"));
    }

    #endregion

    #region GeneratePath Tests - Path Normalization

    [Test]
    public void GeneratePath_WithConsecutiveSeparators_NormalizesToSingleSeparator()
    {
        // Arrange - Empty fallback creates consecutive separators that get normalized
        _config.Destination = TestPaths.Dest + Path.DirectorySeparatorChar + "{country}" + Path.DirectorySeparatorChar + "{city}" + Path.DirectorySeparatorChar + "{name}{ext}";
        _config.UnknownLocationFallback = "";
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Consecutive separators are collapsed
        result.Should().Be(TestPaths.Dest + Path.DirectorySeparatorChar + "test.jpg");
    }

    [Test]
    public void GeneratePath_WithHyphenSeparatedEmptyVariables_RemovesOrphanedHyphens()
    {
        // Arrange - Pattern like {country}-{city} with both empty
        _config.Destination = TestPaths.DestPattern("{year}", "{country}-{city}", "{name}{ext}");
        _config.UnknownLocationFallback = "";
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15)); // No location

        // Act
        var result = copier.GeneratePath(file);

        // Assert - The "-" segment (from empty-{empty}) is removed
        result.Should().Be(TestPaths.InDest("2023", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithOneEmptyVariableInHyphenPair_RemovesLeadingHyphen()
    {
        // Arrange - Pattern like {country}-{city} with only country empty
        _config.Destination = TestPaths.DestPattern("{year}", "{country}-{city}", "{name}{ext}");
        _config.UnknownLocationFallback = "";
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("Prague", "Prague", null, null, "")); // Empty country, valid city

        // Act
        var result = copier.GeneratePath(file);

        // Assert - The leading hyphen before Prague is removed
        result.Should().Be(TestPaths.InDest("2023", "Prague", "test.jpg"));
    }

    #endregion

    #region GeneratePath Tests - Conditional Variables

    [Test]
    public void GeneratePath_WithMinCondition_UsesValueWhenConditionMet()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{city?min=5|Unknown}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("Central London", "London", "London", "England", "GB"));
        
        // Create statistics with 10 files for London
        var stats = new LocationStatistics();
        for (int i = 0; i < 10; i++)
            stats.RecordFile(new LocationData("Central London", "London", "London", "England", "GB"));
        var context = new PathGeneratorContext(stats, 10);

        // Act
        var result = copier.GeneratePath(file, context);

        // Assert - Condition met (10 >= 5), use actual value
        result.Should().Be(TestPaths.InDest("2023", "London", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithMinCondition_UsesFallbackWhenConditionNotMet()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{city?min=20|Unknown}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("Central London", "London", "London", "England", "GB"));
        
        // Create statistics with only 5 files for London
        var stats = new LocationStatistics();
        for (int i = 0; i < 5; i++)
            stats.RecordFile(new LocationData("Central London", "London", "London", "England", "GB"));
        var context = new PathGeneratorContext(stats, 5);

        // Act
        var result = copier.GeneratePath(file, context);

        // Assert - Condition NOT met (5 < 20), use fallback
        result.Should().Be(TestPaths.InDest("2023", "Unknown", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithMaxCondition_UsesValueWhenConditionMet()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{city?max=50}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("Central London", "London", "London", "England", "GB"));
        
        // Create statistics with 10 files for London
        var stats = new LocationStatistics();
        for (int i = 0; i < 10; i++)
            stats.RecordFile(new LocationData("Central London", "London", "London", "England", "GB"));
        var context = new PathGeneratorContext(stats, 10);

        // Act
        var result = copier.GeneratePath(file, context);

        // Assert - Condition met (10 <= 50), use actual value
        result.Should().Be(TestPaths.InDest("2023", "London", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithMaxCondition_UsesFallbackWhenConditionNotMet()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{city?max=5|TooMany}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("Central London", "London", "London", "England", "GB"));
        
        // Create statistics with 10 files for London - exceeds max
        var stats = new LocationStatistics();
        for (int i = 0; i < 10; i++)
            stats.RecordFile(new LocationData("Central London", "London", "London", "England", "GB"));
        var context = new PathGeneratorContext(stats, 10);

        // Act
        var result = copier.GeneratePath(file, context);

        // Assert - Condition NOT met (10 > 5), use fallback
        result.Should().Be(TestPaths.InDest("2023", "TooMany", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithMinAndMaxConditions_UsesValueWhenBothMet()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{city?min=5,max=50}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("Central London", "London", "London", "England", "GB"));
        
        // Create statistics with 10 files for London (within range 5-50)
        var stats = new LocationStatistics();
        for (int i = 0; i < 10; i++)
            stats.RecordFile(new LocationData("Central London", "London", "London", "England", "GB"));
        var context = new PathGeneratorContext(stats, 10);

        // Act
        var result = copier.GeneratePath(file, context);

        // Assert - Both conditions met, use actual value
        result.Should().Be(TestPaths.InDest("2023", "London", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithConditionButNoContext_UsesValueIgnoringConditions()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{city?min=5|Unknown}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("Central London", "London", "London", "England", "GB"));

        // Act - No context provided (backward compatibility)
        var result = copier.GeneratePath(file); // No context

        // Assert - Without context, conditions can't be evaluated, so value is used (backward compatible)
        result.Should().Be(TestPaths.InDest("2023", "London", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithVariableFallback_UsesOtherLocationVariable()
    {
        // Arrange - Use country as fallback when city condition fails
        _config.Destination = TestPaths.DestPattern("{year}", "{city?min=20|country}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("Central London", "London", "London", "England", "GB"));
        
        // Create statistics with only 5 files for London (fails min=20)
        var stats = new LocationStatistics();
        for (int i = 0; i < 5; i++)
            stats.RecordFile(new LocationData("Central London", "London", "London", "England", "GB"));
        var context = new PathGeneratorContext(stats, 5);

        // Act
        var result = copier.GeneratePath(file, context);

        // Assert - Condition NOT met, fallback to country value "GB"
        result.Should().Be(TestPaths.InDest("2023", "GB", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithEmptyConditionAndNoFallback_UsesEmptyString()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{city?min=20}", "{name}{ext}");
        _config.UnknownLocationFallback = "";
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("Central London", "London", "London", "England", "GB"));
        
        // Create statistics with only 5 files for London (fails min=20)
        var stats = new LocationStatistics();
        for (int i = 0; i < 5; i++)
            stats.RecordFile(new LocationData("Central London", "London", "London", "England", "GB"));
        var context = new PathGeneratorContext(stats, 5);

        // Act
        var result = copier.GeneratePath(file, context);

        // Assert - Condition NOT met, no fallback, uses empty (normalized path)
        result.Should().Be(TestPaths.InDest("2023", "test.jpg"));
    }

    #endregion

    #region GeneratePath Tests - Location Feature Combinations

    [Test]
    public void GeneratePath_WithFullCountryNamesAndCountryGranularity_OnlyShowsFullCountryName()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{city}", "{name}{ext}");
        _config.UseFullCountryNames = true;
        _config.LocationGranularity = LocationGranularity.Country;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15), 
            new LocationData("Berlin", "Berlin", null, "BE", "DE"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Country becomes full name, others masked
        result.Should().Be(TestPaths.InDest("Germany", "Unknown", "Unknown", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithFullCountryNamesAndStateGranularity_ShowsFullCountryAndState()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{city}", "{name}{ext}");
        _config.UseFullCountryNames = true;
        _config.LocationGranularity = LocationGranularity.State;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15), 
            new LocationData("Munich", "Munich", "Oberbayern", "BY", "DE"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Country full name, State shown, City masked
        result.Should().Be(TestPaths.InDest("Germany", "BY", "Unknown", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithAllFeaturesCombined_AppliesAllCorrectly()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{county}", "{city}", "{name}{ext}");
        _config.UseFullCountryNames = true;
        _config.LocationGranularity = LocationGranularity.County;
        _config.UnknownLocationFallback = "NoCity";
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("vacation.jpg", new DateTime(2023, 6, 15), 
            new LocationData("San Francisco", "San Francisco", "San Francisco County", "CA", "US"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Full country name, State shown, County shown, City masked with custom fallback
        result.Should().Be(TestPaths.InDest("United States", "CA", "San Francisco County", "NoCity", "vacation.jpg"));
    }

    [Test]
    public void GeneratePath_WithFullCountryNamesAndCustomFallback_NoLocation()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{city}", "{name}{ext}");
        _config.UseFullCountryNames = true;
        _config.UnknownLocationFallback = "NoGPS";
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("photo.jpg", new DateTime(2023, 6, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Both use custom fallback since no location data
        result.Should().Be(TestPaths.InDest("NoGPS", "NoGPS", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithJapanCountryCode_ReturnsCorrectFullName()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{city}", "{name}{ext}");
        _config.UseFullCountryNames = true;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("sakura.jpg", new DateTime(2023, 4, 1), 
            new LocationData("Tokyo", "Tokyo", null, "TK", "JP"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("Japan", "Tokyo", "sakura.jpg"));
    }

    [Test]
    public void GeneratePath_WithUnitedKingdom_ReturnsCorrectFullName()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{city}", "{name}{ext}");
        _config.UseFullCountryNames = true;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("bigben.jpg", new DateTime(2023, 7, 15), 
            new LocationData("London", "London", "Greater London", "ENG", "GB"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("United Kingdom", "London", "bigben.jpg"));
    }

    [Test]
    public void GeneratePath_WithAustralia_ReturnsCorrectFullName()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{city}", "{name}{ext}");
        _config.UseFullCountryNames = true;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("opera.jpg", new DateTime(2023, 1, 26), 
            new LocationData("Sydney", "Sydney", null, "NSW", "AU"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("Australia", "NSW", "Sydney", "opera.jpg"));
    }

    [Test]
    public void GeneratePath_WithCountyOnlyInPath_WorksWithCityGranularity()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{county}", "{name}{ext}");
        _config.LocationGranularity = LocationGranularity.City; // Most detailed
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15), 
            new LocationData("Oakland", "Oakland", "Alameda County", "CA", "US"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("Alameda County", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithCountyOnlyInPath_MaskedWithCountryGranularity()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{county}", "{name}{ext}");
        _config.LocationGranularity = LocationGranularity.Country; // Only country visible
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15), 
            new LocationData("Oakland", "Oakland", "Alameda County", "CA", "US"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - County should be masked
        result.Should().Be(TestPaths.InDest("Unknown", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithMissingCountyData_UsesDefaultFallback()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{county}", "{city}", "{name}{ext}");
        _config.LocationGranularity = LocationGranularity.City;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15), 
            new LocationData("Rome", "Rome", null, "Lazio", "IT"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - County should be Unknown since it's null in data
        result.Should().Be(TestPaths.InDest("IT", "Unknown", "Rome", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithPopulationData_StillProcessesLocation()
    {
        // Arrange - Test that population data doesn't break anything
        _config.Destination = TestPaths.DestPattern("{country}", "{city}", "{name}{ext}");
        _config.UseFullCountryNames = true;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15), 
            new LocationData("New York", "New York", "New York County", "NY", "US", 8336817));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("United States", "New York", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithSpecialCharactersInCounty_PreservesCharacters()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{county}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15), 
            new LocationData("Chicago", "Chicago", "Cook County", "IL", "US"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("Cook County", "test.jpg"));
    }

    #endregion

    #region GeneratePath Tests - PathCasing

    [Test]
    public void GeneratePath_WithOriginalCasing_PreservesOriginalLocationValues()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.Original;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("New York", "New York", "New York County", "New York", "United States"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("United States", "New York", "New York", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithLowercaseCasing_ConvertsAllLocationVariablesToLowercase()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.Lowercase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("New York", "New York", "New York County", "New York", "United States"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("united states", "new york", "new york", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithUppercaseCasing_ConvertsAllLocationVariablesToUppercase()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.Uppercase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("New York", "New York", "New York County", "New York", "United States"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("UNITED STATES", "NEW YORK", "NEW YORK", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithTitleCaseCasing_ConvertsAllLocationVariablesToTitleCase()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.TitleCase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("los angeles", "los angeles", "Los Angeles County", "california", "united states"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("United States", "California", "Los Angeles", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithPascalCaseCasing_ConvertsAllLocationVariablesToPascalCase()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.PascalCase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("New York", "New York", "New York County", "New York", "United States"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("UnitedStates", "NewYork", "NewYork", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithCamelCaseCasing_ConvertsAllLocationVariablesToCamelCase()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.CamelCase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("New York", "New York", "New York County", "New York", "United States"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("unitedStates", "newYork", "newYork", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithSnakeCaseCasing_ConvertsAllLocationVariablesToSnakeCase()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.SnakeCase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("New York", "New York", "New York County", "New York", "United States"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("united_states", "new_york", "new_york", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithKebabCaseCasing_ConvertsAllLocationVariablesToKebabCase()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.KebabCase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("New York", "New York", "New York County", "New York", "United States"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("united-states", "new-york", "new-york", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithScreamingSnakeCaseCasing_ConvertsAllLocationVariablesToScreamingSnakeCase()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.ScreamingSnakeCase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("New York", "New York", "New York County", "New York", "United States"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("UNITED_STATES", "NEW_YORK", "NEW_YORK", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithKebabCase_AppliesCasingToDistrictVariable()
    {
        // Arrange - Use multi-word district to test hyphenation
        _config.Destination = TestPaths.DestPattern("{district}", "{name}{ext}");
        _config.PathCasing = PathCasing.KebabCase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("Old Town", "Praha", "Prague", "Central Bohemia", "Czechia"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Verifies hyphen insertion between words
        result.Should().Be(TestPaths.InDest("old-town", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithKebabCase_AppliesCasingToCountyVariable()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{county}", "{name}{ext}");
        _config.PathCasing = PathCasing.KebabCase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("Chicago", "Chicago", "Cook County", "Illinois", "United States"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("cook-county", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithLowercaseCasing_AppliesCasingToAllFiveLocationVariables()
    {
        // Arrange - Test all location variables: district, city, county, state, country
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{county}", "{city}", "{district}", "{name}{ext}");
        _config.PathCasing = PathCasing.Lowercase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("Manhattan", "New York", "New York County", "New York", "United States"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("united states", "new york", "new york county", "new york", "manhattan", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithKebabCase_DoesNotAffectNonLocationVariables()
    {
        // Arrange - Year, month, day, name should not be affected
        _config.Destination = TestPaths.DestPattern("{year}", "{month}", "{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.KebabCase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("MyPhoto_2023.jpg", new DateTime(2023, 6, 15),
            new LocationData("Los Angeles", "Los Angeles", null, "California", "United States"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Year/month stay as is, city is kebab-cased, filename stays as is
        result.Should().Be(TestPaths.InDest("2023", "06", "los-angeles", "MyPhoto_2023.jpg"));
    }

    [Test]
    public void GeneratePath_WithKebabCase_AppliesCasingToUnknownLocationFallback()
    {
        // Arrange - When location is null, fallback should be cased
        _config.Destination = TestPaths.DestPattern("{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.KebabCase;
        _config.UnknownLocationFallback = "No Location Data";
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("no-location-data", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithSnakeCase_AppliesCasingToUnknownLocationFallback()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.SnakeCase;
        _config.UnknownLocationFallback = "Unknown Location";
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("unknown_location", "unknown_location", "unknown_location", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithKebabCase_PreservesDiacriticsInLocationNames()
    {
        // Arrange - Test that diacritics are preserved (CasingFormatter doesn't normalize them)
        _config.Destination = TestPaths.DestPattern("{country}", "{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.KebabCase;
        _config.UseFullCountryNames = true;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("München", "München", null, "Bavaria", "DE"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Diacritics are preserved, only case is changed
        result.Should().Be(TestPaths.InDest("germany", "münchen", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithTitleCase_ConvertsUppercaseLocationsToTitleCase()
    {
        // Arrange - Uppercase input is converted to Title Case (not preserved)
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{name}{ext}");
        _config.PathCasing = PathCasing.TitleCase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("Washington", "Washington", null, "DC", "USA"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("Usa", "Dc", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithPascalCase_HandlesMultiWordCityNames()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.PascalCase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("San Francisco", "San Francisco", "San Francisco County", "California", "United States"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("SanFrancisco", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithCamelCase_HandlesMultiWordStateNames()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{state}", "{name}{ext}");
        _config.PathCasing = PathCasing.CamelCase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("Raleigh", "Raleigh", "Wake County", "North Carolina", "United States"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("northCarolina", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithSnakeCase_HandlesCountryWithFullName()
    {
        // Arrange - Test that full country name is also converted
        _config.Destination = TestPaths.DestPattern("{country}", "{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.SnakeCase;
        _config.UseFullCountryNames = true;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("London", "London", "Greater London", "England", "GB"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("united_kingdom", "london", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithKebabCase_AndLocationGranularityState_AppliesCasingToVisibleVariables()
    {
        // Arrange - With State granularity, city/district use fallback
        _config.Destination = TestPaths.DestPattern("{country}", "{state}", "{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.KebabCase;
        _config.LocationGranularity = LocationGranularity.State;
        _config.UnknownLocationFallback = "Not Available";
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("New York", "New York", "New York County", "New York", "United States"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - State and Country visible, City masked with fallback
        result.Should().Be(TestPaths.InDest("united-states", "new-york", "not-available", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithScreamingSnakeCase_HandlesMultiWordLocations()
    {
        // Arrange - Use multi-word locations to test underscore joining
        _config.Destination = TestPaths.DestPattern("{country}", "{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.ScreamingSnakeCase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("Los Angeles", "Los Angeles", null, "California", "United States"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Verifies underscore insertion and uppercase
        result.Should().Be(TestPaths.InDest("UNITED_STATES", "LOS_ANGELES", "test.jpg"));
    }

    [Test]
    [Arguments(PathCasing.Original, "New York")]
    [Arguments(PathCasing.Lowercase, "new york")]
    [Arguments(PathCasing.Uppercase, "NEW YORK")]
    [Arguments(PathCasing.TitleCase, "New York")]
    [Arguments(PathCasing.PascalCase, "NewYork")]
    [Arguments(PathCasing.CamelCase, "newYork")]
    [Arguments(PathCasing.SnakeCase, "new_york")]
    [Arguments(PathCasing.KebabCase, "new-york")]
    [Arguments(PathCasing.ScreamingSnakeCase, "NEW_YORK")]
    public void GeneratePath_WithVariousCasings_ProducesExpectedCityOutput(PathCasing casing, string expectedCity)
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{city}", "{name}{ext}");
        _config.PathCasing = casing;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("New York", "New York", "New York County", "New York", "United States"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest(expectedCity, "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithOriginalCasing_PreservesMixedCaseInput()
    {
        // Arrange - Verify Original casing truly preserves mixed-case exactly
        _config.Destination = TestPaths.DestPattern("{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.Original;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("nEw YoRk", "nEw YoRk", null, "NY", "USA"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Mixed case is preserved as-is
        result.Should().Be(TestPaths.InDest("nEw YoRk", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithKebabCase_HandlesLocationWithExistingHyphens()
    {
        // Arrange - Location name already has hyphens
        _config.Destination = TestPaths.DestPattern("{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.KebabCase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("Saint-Tropez", "Saint-Tropez", null, "Provence", "France"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Should not produce double hyphens
        result.Should().Be(TestPaths.InDest("saint-tropez", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithSnakeCase_HandlesLocationWithExistingUnderscores()
    {
        // Arrange - Location name already has underscores
        _config.Destination = TestPaths.DestPattern("{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.SnakeCase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("Some_City", "Some_City", null, "State", "Country"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Should not produce double underscores
        result.Should().Be(TestPaths.InDest("some_city", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithDefaultPathCasing_UsesOriginalCasing()
    {
        // Arrange - PathCasing not explicitly set, should default to Original
        _config.Destination = TestPaths.DestPattern("{city}", "{name}{ext}");
        // PathCasing is NOT set - relies on default value
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("New York", "New York", null, "NY", "USA"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Default should be Original, preserving case
        result.Should().Be(TestPaths.InDest("New York", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithKebabCase_AndNullCityFallsBackToDistrict_AppliesCasing()
    {
        // Arrange - City is null, should fallback to District and apply casing
        _config.Destination = TestPaths.DestPattern("{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.KebabCase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("Old Town", null, null, "State", "Country"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Falls back to district "Old Town" and applies kebab-case
        result.Should().Be(TestPaths.InDest("old-town", "test.jpg"));
    }

    [Test]
    public void GeneratePath_WithKebabCase_HandlesSingleWordLocation()
    {
        // Arrange - Single word location (edge case for hyphen-based casings)
        _config.Destination = TestPaths.DestPattern("{city}", "{name}{ext}");
        _config.PathCasing = PathCasing.KebabCase;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFileWithLocation("test.jpg", new DateTime(2023, 6, 15),
            new LocationData("Tokyo", "Tokyo", null, "Tokyo", "Japan"));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Single word just lowercased, no hyphen needed
        result.Should().Be(TestPaths.InDest("tokyo", "test.jpg"));
    }

    #endregion

    #region ResolveDuplicate Tests - No Duplicate

    [Test]
    public void ResolveDuplicate_WithNoDuplicate_ReturnsOriginalPath()
    {
        // Arrange
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var destinationPath = TestPaths.InDest("2023", "06", "photo.jpg");
        _fileSystem.FileExists(destinationPath).Returns(false);

        // Act
        var result = copier.ResolveDuplicate(destinationPath);

        // Assert
        result.Should().Be(destinationPath);
    }

    [Test]
    public void ResolveDuplicate_WithNonExistentFile_DoesNotModifyPath()
    {
        // Arrange
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var destinationPath = TestPaths.InDest("unique_photo.jpg");
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);

        // Act
        var result = copier.ResolveDuplicate(destinationPath);

        // Assert
        result.Should().Be(destinationPath);
        _fileSystem.Received(1).FileExists(destinationPath);
    }

    #endregion

    #region ResolveDuplicate Tests - Existing File

    [Test]
    public void ResolveDuplicate_WithExistingFile_AppendsNumber()
    {
        // Arrange
        _config.DuplicatesFormat = "-{number}";
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var originalPath = TestPaths.InDest("photo.jpg");
        var expectedPath = TestPaths.InDest("photo-1.jpg");
        
        _fileSystem.FileExists(originalPath).Returns(true);
        _fileSystem.FileExists(expectedPath).Returns(false);

        // Act
        var result = copier.ResolveDuplicate(originalPath);

        // Assert
        result.Should().Be(expectedPath);
    }

    [Test]
    public void ResolveDuplicate_WithExistingFile_UsesConfiguredFormat()
    {
        // Arrange
        _config.DuplicatesFormat = "_{number}";
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var originalPath = TestPaths.InDest("photo.jpg");
        var expectedPath = TestPaths.InDest("photo_1.jpg");
        
        _fileSystem.FileExists(originalPath).Returns(true);
        _fileSystem.FileExists(expectedPath).Returns(false);

        // Act
        var result = copier.ResolveDuplicate(originalPath);

        // Assert
        result.Should().Be(expectedPath);
    }

    [Test]
    public void ResolveDuplicate_WithExistingFile_PreservesExtension()
    {
        // Arrange
        _config.DuplicatesFormat = "-{number}";
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var originalPath = TestPaths.InDest("photo.png");
        var expectedPath = TestPaths.InDest("photo-1.png");
        
        _fileSystem.FileExists(originalPath).Returns(true);
        _fileSystem.FileExists(expectedPath).Returns(false);

        // Act
        var result = copier.ResolveDuplicate(originalPath);

        // Assert
        result.Should().Be(expectedPath);
    }

    #endregion

    #region ResolveDuplicate Tests - Multiple Duplicates

    [Test]
    public void ResolveDuplicate_WithMultipleDuplicates_IncrementsNumber()
    {
        // Arrange
        _config.DuplicatesFormat = "-{number}";
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var originalPath = TestPaths.InDest("photo.jpg");
        
        _fileSystem.FileExists(originalPath).Returns(true);
        _fileSystem.FileExists(TestPaths.InDest("photo-1.jpg")).Returns(true);
        _fileSystem.FileExists(TestPaths.InDest("photo-2.jpg")).Returns(true);
        _fileSystem.FileExists(TestPaths.InDest("photo-3.jpg")).Returns(false);

        // Act
        var result = copier.ResolveDuplicate(originalPath);

        // Assert
        result.Should().Be(TestPaths.InDest("photo-3.jpg"));
    }

    [Test]
    public void ResolveDuplicate_WithManyDuplicates_FindsNextAvailableNumber()
    {
        // Arrange
        _config.DuplicatesFormat = "-{number}";
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var originalPath = TestPaths.InDest("photo.jpg");
        
        _fileSystem.FileExists(Arg.Any<string>()).Returns(callInfo =>
        {
            var path = callInfo.Arg<string>();
            // Original and duplicates 1-9 exist
            if (path == originalPath) return true;
            for (int i = 1; i <= 9; i++)
            {
                if (path == TestPaths.InDest($"photo-{i}.jpg")) return true;
            }
            return false;
        });

        // Act
        var result = copier.ResolveDuplicate(originalPath);

        // Assert
        result.Should().Be(TestPaths.InDest("photo-10.jpg"));
    }

    [Test]
    public void ResolveDuplicate_WithSkipExisting_ReturnsNull()
    {
        // Arrange
        _config.SkipExisting = true;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var originalPath = TestPaths.InDest("photo.jpg");
        
        _fileSystem.FileExists(originalPath).Returns(true);

        // Act
        var result = copier.ResolveDuplicate(originalPath);

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void ResolveDuplicate_WithOverwrite_ReturnsOriginalPath()
    {
        // Arrange
        _config.Overwrite = true;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var originalPath = TestPaths.InDest("photo.jpg");
        
        _fileSystem.FileExists(originalPath).Returns(true);

        // Act
        var result = copier.ResolveDuplicate(originalPath);

        // Assert
        result.Should().Be(originalPath);
    }

    #endregion

    #region BuildCopyPlan Tests - Valid Files

    [Test]
    public void Copy_WithValidFiles_CreatesCorrectPlan()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{month}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file1 = CreateMockFile("photo1.jpg", new DateTime(2023, 6, 15));
        var file2 = CreateMockFile("photo2.jpg", new DateTime(2023, 7, 20));
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { file1, file2 });
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        
        var validators = Array.Empty<IValidator>();

        // Act
        copier.Copy(validators);

        // Assert - verify both files were processed (dry run logs operations)
        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("2 primary files")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public void Copy_WithValidFiles_GeneratesCorrectDestinationPaths()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{year}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("summer.jpg", new DateTime(2023, 8, 1));
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { file });
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);

        // Act - Use GeneratePath directly to verify destination path generation
        var generatedPath = copier.GeneratePath(file);

        // Assert - The path should be correctly generated based on the template
        generatedPath.Should().Be(TestPaths.InDest("2023", "summer.jpg"));
    }

    #endregion

    #region BuildCopyPlan Tests - Validation Failures

    [Test]
    public void Copy_WithValidationFailures_SkipsInvalidFiles()
    {
        // Arrange
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var validFile = CreateMockFile("valid.jpg", new DateTime(2023, 6, 15));
        var invalidFile = CreateMockFile("invalid.jpg", new DateTime(2019, 1, 1));
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { validFile, invalidFile });
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        
        var validator = Substitute.For<IValidator>();
        validator.Validate(validFile).Returns(ValidationResult.Success("TestValidator"));
        validator.Validate(invalidFile).Returns(ValidationResult.Fail("TestValidator", "File too old"));

        // Act
        copier.Copy(new[] { validator });

        // Assert - verify one file was skipped
        _logger.Received().Log(
            LogLevel.Debug,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("skipped")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public void Copy_WithAllFilesInvalid_SkipsAllFiles()
    {
        // Arrange
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file1 = CreateMockFile("old1.jpg", new DateTime(2015, 1, 1));
        var file2 = CreateMockFile("old2.jpg", new DateTime(2016, 1, 1));
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { file1, file2 });
        
        var validator = Substitute.For<IValidator>();
        validator.Validate(Arg.Any<IFile>()).Returns(ValidationResult.Fail("DateValidator", "File too old"));

        // Act
        copier.Copy(new[] { validator });

        // Assert - verify summary shows no primary files
        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("0 primary files")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public void Copy_WithMultipleValidators_AppliesAllValidatorsInOrder()
    {
        // Arrange
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { file });
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        
        var validator1 = Substitute.For<IValidator>();
        validator1.Validate(file).Returns(ValidationResult.Success("Validator1"));
        
        var validator2 = Substitute.For<IValidator>();
        validator2.Validate(file).Returns(ValidationResult.Success("Validator2"));

        // Act
        copier.Copy(new[] { validator1, validator2 });

        // Assert - both validators were called
        validator1.Received(1).Validate(file);
        validator2.Received(1).Validate(file);
    }

    [Test]
    public void Copy_WithFirstValidatorFailing_StopsValidationEarly()
    {
        // Arrange
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { file });
        
        var validator1 = Substitute.For<IValidator>();
        validator1.Validate(file).Returns(ValidationResult.Fail("Validator1", "Failed first check"));
        
        var validator2 = Substitute.For<IValidator>();

        // Act
        copier.Copy(new[] { validator1, validator2 });

        // Assert - second validator was never called
        validator1.Received(1).Validate(file);
        validator2.DidNotReceive().Validate(Arg.Any<IFile>());
    }

    #endregion

    #region ExecuteCopyPlan Tests - Dry Run Mode

    [Test]
    public void Copy_InDryRunMode_DoesNotCopyFiles()
    {
        // Arrange
        _config.DryRun = true;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { file });
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);

        // Act
        copier.Copy(Array.Empty<IValidator>());

        // Assert - CopyFile was never called
        _fileSystem.DidNotReceive().CopyFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Test]
    public void Copy_InDryRunMode_DoesNotMoveFiles()
    {
        // Arrange
        _config.DryRun = true;
        _config.Mode = OperationMode.Move;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { file });
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);

        // Act
        copier.Copy(Array.Empty<IValidator>());

        // Assert - MoveFile was never called
        _fileSystem.DidNotReceive().MoveFile(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public void Copy_InDryRunMode_DoesNotCreateDirectories()
    {
        // Arrange
        _config.DryRun = true;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { file });
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(false);

        // Act
        copier.Copy(Array.Empty<IValidator>());

        // Assert - CreateDirectory was never called
        _fileSystem.DidNotReceive().CreateDirectory(Arg.Any<string>());
    }

    [Test]
    public void Copy_InDryRunMode_LogsDryRunMessage()
    {
        // Arrange
        _config.DryRun = true;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { file });
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);

        // Act
        copier.Copy(Array.Empty<IValidator>());

        // Assert - DryRun message was logged
        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("DryRun")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public void Copy_NotInDryRunMode_CopiesFiles()
    {
        // Arrange
        _config.DryRun = false;
        _config.Mode = OperationMode.Copy;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { file });
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(true);

        // Act
        copier.Copy(Array.Empty<IValidator>());

        // Assert - CopyFile was called
        _fileSystem.Received().CopyFile(Arg.Any<string>(), Arg.Any<string>(), true);
    }

    [Test]
    public void Copy_NotInDryRunMode_WithMoveMode_MovesFiles()
    {
        // Arrange
        _config.DryRun = false;
        _config.Mode = OperationMode.Move;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { file });
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(true);

        // Act
        copier.Copy(Array.Empty<IValidator>());

        // Assert - MoveFile was called
        _fileSystem.Received().MoveFile(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public void Copy_NotInDryRunMode_CreatesDirectoriesWhenNeeded()
    {
        // Arrange
        _config.DryRun = false;
        _config.Destination = TestPaths.DestPattern("{year}", "{month}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { file });
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(false);

        // Act
        copier.Copy(Array.Empty<IValidator>());

        // Assert - CreateDirectory was called
        _fileSystem.Received().CreateDirectory(Arg.Any<string>());
    }

    #endregion

    #region ExecuteCopyPlan Tests - Cancellation

    [Test]
    public void Copy_WithEmptyFileList_CompletesSuccessfully()
    {
        // Arrange
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        _fileSystem.EnumerateFiles(_config.Source).Returns(Array.Empty<IFile>());

        // Act
        copier.Copy(Array.Empty<IValidator>());

        // Assert - operation completes with 0 files
        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("0 primary files")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region Edge Cases

    [Test]
    public void GeneratePath_WithNullDestination_ReturnsEmptyString()
    {
        // Arrange
        _config.Destination = null!;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void GeneratePath_WithEmptyDestination_ReturnsEmptyString()
    {
        // Arrange
        _config.Destination = string.Empty;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void GeneratePath_WithNoTemplates_ReturnsDestinationAsIs()
    {
        // Arrange
        _config.Destination = TestPaths.InDest("fixed_path.jpg");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("fixed_path.jpg"));
    }

    [Test]
    public void GeneratePath_WithSingleDigitMonth_PadsWithZero()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{month}", "photo.jpg");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 1, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("01", "photo.jpg"));
    }

    [Test]
    public void GeneratePath_WithSingleDigitDay_PadsWithZero()
    {
        // Arrange
        _config.Destination = TestPaths.DestPattern("{day}", "photo.jpg");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 1));

        // Act
        var result = copier.GeneratePath(file);

        // Assert
        result.Should().Be(TestPaths.InDest("01", "photo.jpg"));
    }

    [Test]
    public void ResolveDuplicate_WithParenthesesFormat_HandlesCorrectly()
    {
        // Arrange
        _config.DuplicatesFormat = " ({number})";
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var originalPath = TestPaths.InDest("photo.jpg");
        
        _fileSystem.FileExists(originalPath).Returns(true);
        _fileSystem.FileExists(TestPaths.InDest("photo (1).jpg")).Returns(false);

        // Act
        var result = copier.ResolveDuplicate(originalPath);

        // Assert
        result.Should().Be(TestPaths.InDest("photo (1).jpg"));
    }

    [Test]
    public void Copy_WithExistingDirectory_DoesNotCreateDirectory()
    {
        // Arrange
        _config.DryRun = false;
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("test.jpg", new DateTime(2023, 6, 15));
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { file });
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(true);

        // Act
        copier.Copy(Array.Empty<IValidator>());

        // Assert - CreateDirectory was not called because directory exists
        _fileSystem.DidNotReceive().CreateDirectory(Arg.Any<string>());
    }

    #endregion

    #region Helper Methods

    private static IFile CreateMockFile(string name, DateTime dateTime)
    {
        var file = Substitute.For<IFile>();
        var fileInfo = new FileInfo(Path.Combine(Path.GetTempPath(), name));
        
        file.File.Returns(fileInfo);
        file.FileDateTime.Returns(new FileDateTime(dateTime, DateTimeSource.ExifDateTimeOriginal));
        file.Location.Returns((LocationData?)null);
        
        return file;
    }

    private static IFile CreateMockFileWithPath(string fullPath, DateTime dateTime)
    {
        var file = Substitute.For<IFile>();
        var fileInfo = new FileInfo(fullPath);
        
        file.File.Returns(fileInfo);
        file.FileDateTime.Returns(new FileDateTime(dateTime, DateTimeSource.ExifDateTimeOriginal));
        file.Location.Returns((LocationData?)null);
        
        return file;
    }

    private static IFile CreateMockFileWithLocation(string name, DateTime dateTime, LocationData location)
    {
        var file = Substitute.For<IFile>();
        var fileInfo = new FileInfo(Path.Combine(Path.GetTempPath(), name));
        
        file.File.Returns(fileInfo);
        file.FileDateTime.Returns(new FileDateTime(dateTime, DateTimeSource.ExifDateTimeOriginal));
        file.Location.Returns(location);
        
        return file;
    }

    private static IFile CreateMockFileWithSize(string name, long size)
    {
        var file = Substitute.For<IFile>();
        var fileInfo = new FileInfo(Path.Combine(Path.GetTempPath(), name));
        
        file.File.Returns(fileInfo);
        file.FileDateTime.Returns(new FileDateTime(DateTime.Now, DateTimeSource.FileCreation));
        file.Location.Returns((LocationData?)null);
        
        return file;
    }

    private static FileWithMetadata CreateFileWithMetadata(string name, DateTime dateTime, ILogger logger)
    {
        var fileInfo = new FileInfo(Path.Combine(Path.GetTempPath(), name));
        var fileDateTime = new FileDateTime(dateTime, DateTimeSource.ExifDateTimeOriginal);
        return new FileWithMetadata(fileInfo, fileDateTime, logger);
    }

    private static IFile CreateRelatedMockFile(string name)
    {
        var file = Substitute.For<IFile>();
        var fileInfo = new FileInfo(Path.Combine(Path.GetTempPath(), name));
        file.File.Returns(fileInfo);
        file.FileDateTime.Returns(new FileDateTime(DateTime.Now, DateTimeSource.FileCreation));
        file.Location.Returns((LocationData?)null);
        return file;
    }

    #endregion

    #region Related Files Copy Tests

    [Test]
    public void Copy_WithRelatedFiles_CopiesRelatedFilesToDestination()
    {
        // Arrange
        _config.DryRun = false;
        _config.Destination = TestPaths.DestPattern("{year}", "{month}", "{day}", "{name}{ext}");
        
        var logger = Substitute.For<ILogger>();
        var mainFile = CreateFileWithMetadata("photo.jpg", new DateTime(2023, 6, 15), logger);
        
        // Add related files
        var relatedXmp = CreateRelatedMockFile("photo.xmp");
        var relatedJson = CreateRelatedMockFile("photo.json");
        mainFile.AddRelatedFiles(new[] { relatedXmp, relatedJson }, RelatedFileLookup.Strict);
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { mainFile });
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(true);
        
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);

        // Act
        copier.Copy(Array.Empty<IValidator>());

        // Assert - main file was copied
        _fileSystem.Received(1).CopyFile(
            mainFile.File.FullName,
            TestPaths.InDest("2023", "06", "15", "photo.jpg"),
            true);
        
        // Assert - related files were copied
        _fileSystem.Received(1).CopyFile(
            relatedXmp.File.FullName,
            TestPaths.InDest("2023", "06", "15", "photo.xmp"),
            true);
        _fileSystem.Received(1).CopyFile(
            relatedJson.File.FullName,
            TestPaths.InDest("2023", "06", "15", "photo.json"),
            true);
    }

    [Test]
    public void Copy_WithRelatedFiles_PreservesRelativeStructure()
    {
        // Arrange
        _config.DryRun = false;
        _config.Destination = TestPaths.DestPattern("{year}", "{name}{ext}");
        
        var logger = Substitute.For<ILogger>();
        var mainFile = CreateFileWithMetadata("vacation.jpg", new DateTime(2023, 8, 20), logger);
        
        // Add related file with underscore suffix pattern
        var relatedEdit = CreateRelatedMockFile("vacation_edit.jpg");
        mainFile.AddRelatedFiles(new[] { relatedEdit }, RelatedFileLookup.Strict);
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { mainFile });
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(true);
        
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);

        // Act
        copier.Copy(Array.Empty<IValidator>());

        // Assert - main file was copied
        _fileSystem.Received(1).CopyFile(
            mainFile.File.FullName,
            TestPaths.InDest("2023", "vacation.jpg"),
            true);
        
        // Assert - related file preserves the suffix
        _fileSystem.Received(1).CopyFile(
            relatedEdit.File.FullName,
            TestPaths.InDest("2023", "vacation_edit.jpg"),
            true);
    }

    [Test]
    public void Copy_InDryRunMode_ReportsRelatedFilesButDoesNotCopy()
    {
        // Arrange
        _config.DryRun = true;
        _config.Destination = TestPaths.DestPattern("{year}", "{month}", "{day}", "{name}{ext}");
        
        var logger = Substitute.For<ILogger>();
        var mainFile = CreateFileWithMetadata("photo.jpg", new DateTime(2023, 6, 15), logger);
        
        var relatedXmp = CreateRelatedMockFile("photo.xmp");
        mainFile.AddRelatedFiles(new[] { relatedXmp }, RelatedFileLookup.Strict);
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { mainFile });
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);

        // Act
        copier.Copy(Array.Empty<IValidator>());

        // Assert - no actual copy operations were performed
        _fileSystem.DidNotReceive().CopyFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
        _fileSystem.DidNotReceive().MoveFile(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public void Copy_WithMoveMode_MovesRelatedFiles()
    {
        // Arrange
        _config.DryRun = false;
        _config.Mode = OperationMode.Move;
        _config.Destination = TestPaths.DestPattern("{year}", "{name}{ext}");
        
        var logger = Substitute.For<ILogger>();
        var mainFile = CreateFileWithMetadata("photo.jpg", new DateTime(2023, 6, 15), logger);
        
        var relatedXmp = CreateRelatedMockFile("photo.xmp");
        mainFile.AddRelatedFiles(new[] { relatedXmp }, RelatedFileLookup.Strict);
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { mainFile });
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(true);
        
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);

        // Act
        copier.Copy(Array.Empty<IValidator>());

        // Assert - main file was moved
        _fileSystem.Received(1).MoveFile(mainFile.File.FullName, TestPaths.InDest("2023", "photo.jpg"));
        
        // Assert - related file was also moved
        _fileSystem.Received(1).MoveFile(relatedXmp.File.FullName, TestPaths.InDest("2023", "photo.xmp"));
    }

    [Test]
    public void Copy_WithNoRelatedFiles_CopiesOnlyMainFile()
    {
        // Arrange
        _config.DryRun = false;
        _config.Destination = TestPaths.DestPattern("{year}", "{name}{ext}");
        
        var logger = Substitute.For<ILogger>();
        var mainFile = CreateFileWithMetadata("photo.jpg", new DateTime(2023, 6, 15), logger);
        // No related files added
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { mainFile });
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(true);
        
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);

        // Act
        copier.Copy(Array.Empty<IValidator>());

        // Assert - only one copy call for the main file
        _fileSystem.Received(1).CopyFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
        _fileSystem.Received(1).CopyFile(
            mainFile.File.FullName,
            TestPaths.InDest("2023", "photo.jpg"),
            true);
    }

    [Test]
    public void Copy_WithRelatedFiles_CreatesDirectoriesForRelatedFiles()
    {
        // Arrange
        _config.DryRun = false;
        _config.Destination = TestPaths.DestPattern("{year}", "{month}", "{name}{ext}");
        
        var logger = Substitute.For<ILogger>();
        var mainFile = CreateFileWithMetadata("photo.jpg", new DateTime(2023, 6, 15), logger);
        
        var relatedXmp = CreateRelatedMockFile("photo.xmp");
        mainFile.AddRelatedFiles(new[] { relatedXmp }, RelatedFileLookup.Strict);
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { mainFile });
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(false);
        
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);

        // Act
        copier.Copy(Array.Empty<IValidator>());

        // Assert - directory was created (may be called multiple times for main and related)
        _fileSystem.Received().CreateDirectory(TestPaths.InDest("2023", "06"));
    }

    [Test]
    public void Copy_WithMultipleRelatedFiles_CopiesAllRelatedFiles()
    {
        // Arrange
        _config.DryRun = false;
        _config.Destination = Path.Combine(TestPaths.Dest, "{name}{ext}");
        
        var logger = Substitute.For<ILogger>();
        var mainFile = CreateFileWithMetadata("IMG_1234.jpg", new DateTime(2023, 6, 15), logger);
        
        var relatedXmp = CreateRelatedMockFile("IMG_1234.xmp");
        var relatedJson = CreateRelatedMockFile("IMG_1234.json");
        var relatedRaw = CreateRelatedMockFile("IMG_1234.CR2");
        mainFile.AddRelatedFiles(new[] { relatedXmp, relatedJson, relatedRaw }, RelatedFileLookup.Strict);
        
        _fileSystem.EnumerateFiles(_config.Source).Returns(new[] { mainFile });
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(true);
        
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);

        // Act
        copier.Copy(Array.Empty<IValidator>());

        // Assert - 4 total copies (1 main + 3 related)
        _fileSystem.Received(4).CopyFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
        _fileSystem.Received(1).CopyFile(mainFile.File.FullName, TestPaths.InDest("IMG_1234.jpg"), true);
        _fileSystem.Received(1).CopyFile(relatedXmp.File.FullName, TestPaths.InDest("IMG_1234.xmp"), true);
        _fileSystem.Received(1).CopyFile(relatedJson.File.FullName, TestPaths.InDest("IMG_1234.json"), true);
        _fileSystem.Received(1).CopyFile(relatedRaw.File.FullName, TestPaths.InDest("IMG_1234.CR2"), true);
    }

    #endregion

    #region Path Security Tests

    [Test]
    public void GeneratePath_WithSafeLocation_GeneratesValidPath()
    {
        // Arrange - Normal location data should work fine
        _config.Destination = Path.Combine(TestPaths.Dest, "{year}", "{city}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        
        var location = new LocationData("Old Town", "Prague", "Prague", "Central Bohemia", "CZ");
        var file = CreateMockFileWithLocation("photo.jpg", new DateTime(2024, 6, 15), location);

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Path should be within destination
        result.Should().StartWith(TestPaths.Dest);
        result.Should().Contain("Prague");
        result.Should().NotContain("..");
    }

    [Test]
    public void GeneratePath_WithMaliciousLocation_SanitizesInput()
    {
        // Arrange - Location data with characters that might look like path traversal
        _config.Destination = Path.Combine(TestPaths.Dest, "{year}", "{city}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        
        // Create a file with a location that contains dots (but not as path traversal)
        var location = new LocationData("City..Name", "City..Name", null, "State", "CZ");
        var file = CreateMockFileWithLocation("photo.jpg", new DateTime(2024, 6, 15), location);

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Path should still be within destination (sanitized)
        result.Should().StartWith(TestPaths.Dest);
    }

    [Test]
    public void GeneratePath_DirectoryVariable_SanitizesPathTraversal()
    {
        // Arrange - The {directory} variable could potentially contain traversal sequences
        // if source files come from unusual paths
        _config.Source = TestPaths.Source;
        _config.Destination = Path.Combine(TestPaths.Dest, "{directory}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        
        // Create a mock file in a normal subdirectory
        var file = CreateMockFileWithPath(Path.Combine(TestPaths.Source, "subdir", "photo.jpg"), new DateTime(2024, 6, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Path should be within destination
        result.Should().StartWith(TestPaths.Dest);
        result.Should().NotContain("..");
    }

    [Test]
    public void GeneratePath_ValidatesPathWithinDestinationRoot()
    {
        // Arrange - Standard path generation
        _config.Destination = Path.Combine(TestPaths.Dest, "{year}", "{month}", "{name}{ext}");
        var copier = new DirectoryCopier(_logger, _fileSystem, _options, _transactionLogger, _fileValidationService);
        var file = CreateMockFile("photo.jpg", new DateTime(2024, 7, 15));

        // Act
        var result = copier.GeneratePath(file);

        // Assert - Generated path should be within the destination root
        result.Should().StartWith(TestPaths.Dest);
        result.Should().Be(TestPaths.InDest("2024", "07", "photo.jpg"));
    }

    #endregion
}

