using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using NSubstitute;
using PhotoCopy.Commands;
using PhotoCopy.Configuration;

namespace PhotoCopy.Tests.Commands;

public class ConfigurationDiagnosticsTests
{
    private ConfigurationDiagnostics _sut;
    private PhotoCopyConfig _config;

    public ConfigurationDiagnosticsTests()
    {
        _sut = new ConfigurationDiagnostics();
        _config = new PhotoCopyConfig
        {
            Source = @"C:\Photos\Source",
            Destination = @"C:\Photos\Dest\{year}\{month}",
            DryRun = true,
            SkipExisting = false,
            Overwrite = false,
            LogLevel = OutputLevel.Verbose
        };
    }

    #region GetSources_ReturnsAllSources

    [Test]
    public void GetSources_ReturnsAllSources_WhenMultipleSourcesRecorded()
    {
        // Arrange
        _sut.RecordSource("Source", @"C:\Photos", ConfigSourceType.CommandLine, "--source");
        _sut.RecordSource("Destination", @"C:\Dest", ConfigSourceType.ConfigFile, "appsettings.json");
        _sut.RecordSource("DryRun", "true", ConfigSourceType.EnvironmentVariable, "PHOTOCOPY_DRYRUN");

        // Act
        var sources = _sut.Sources;

        // Assert
        sources.Should().HaveCount(3);
        sources.Should().ContainKey("Source");
        sources.Should().ContainKey("Destination");
        sources.Should().ContainKey("DryRun");
    }

    [Test]
    public void GetSources_ReturnsEmptyDictionary_WhenNoSourcesRecorded()
    {
        // Act
        var sources = _sut.Sources;

        // Assert
        sources.Should().BeEmpty();
    }

    [Test]
    public void GetSources_ReturnsSingleSource_WhenOneSourceRecorded()
    {
        // Arrange
        _sut.RecordSource("Source", @"C:\Photos", ConfigSourceType.CommandLine, "--source");

        // Act
        var sources = _sut.Sources;

        // Assert
        sources.Should().HaveCount(1);
        sources.Should().ContainKey("Source");
    }

    [Test]
    public void GetSources_IsCaseInsensitive_WhenAccessingByKey()
    {
        // Arrange
        _sut.RecordSource("Source", @"C:\Photos", ConfigSourceType.CommandLine, "--source");

        // Act
        var sources = _sut.Sources;

        // Assert
        sources.Should().ContainKey("source");
        sources.Should().ContainKey("SOURCE");
        sources.Should().ContainKey("Source");
    }

    #endregion

    #region SetSource_RecordsPropertySource

    [Test]
    public void SetSource_RecordsPropertySource_WithCommandLineSource()
    {
        // Arrange & Act
        _sut.RecordSource("Source", @"C:\Photos", ConfigSourceType.CommandLine, "--source");

        // Assert
        var source = _sut.Sources["Source"];
        source.PropertyName.Should().Be("Source");
        source.ResolvedValue.Should().Be(@"C:\Photos");
        source.Source.Should().Be(ConfigSourceType.CommandLine);
        source.SourceDetail.Should().Be("--source");
    }

    [Test]
    public void SetSource_RecordsPropertySource_WithConfigFileSource()
    {
        // Arrange & Act
        _sut.RecordSource("Destination", @"C:\Dest\{year}", ConfigSourceType.ConfigFile, "appsettings.yaml");

        // Assert
        var source = _sut.Sources["Destination"];
        source.PropertyName.Should().Be("Destination");
        source.ResolvedValue.Should().Be(@"C:\Dest\{year}");
        source.Source.Should().Be(ConfigSourceType.ConfigFile);
        source.SourceDetail.Should().Be("appsettings.yaml");
    }

    [Test]
    public void SetSource_RecordsPropertySource_WithEnvironmentVariableSource()
    {
        // Arrange & Act
        _sut.RecordSource("DryRun", "true", ConfigSourceType.EnvironmentVariable, "PHOTOCOPY_DRYRUN");

        // Assert
        var source = _sut.Sources["DryRun"];
        source.PropertyName.Should().Be("DryRun");
        source.ResolvedValue.Should().Be("true");
        source.Source.Should().Be(ConfigSourceType.EnvironmentVariable);
        source.SourceDetail.Should().Be("PHOTOCOPY_DRYRUN");
    }

    [Test]
    public void SetSource_RecordsPropertySource_WithNullSourceDetail()
    {
        // Arrange & Act
        _sut.RecordSource("SkipExisting", "false", ConfigSourceType.Default, null);

        // Assert
        var source = _sut.Sources["SkipExisting"];
        source.PropertyName.Should().Be("SkipExisting");
        source.ResolvedValue.Should().Be("false");
        source.Source.Should().Be(ConfigSourceType.Default);
        source.SourceDetail.Should().BeNull();
    }

    [Test]
    public void SetSource_RecordsPropertySource_WithNullValue()
    {
        // Arrange & Act
        _sut.RecordSource("GeonamesPath", null, ConfigSourceType.Default, null);

        // Assert
        var source = _sut.Sources["GeonamesPath"];
        source.PropertyName.Should().Be("GeonamesPath");
        source.ResolvedValue.Should().BeNull();
        source.Source.Should().Be(ConfigSourceType.Default);
    }

    [Test]
    public void SetSource_OverwritesPreviousSource_WhenCalledTwiceForSameProperty()
    {
        // Arrange
        _sut.RecordSource("Source", @"C:\OldPath", ConfigSourceType.ConfigFile, "appsettings.json");

        // Act
        _sut.RecordSource("Source", @"C:\NewPath", ConfigSourceType.CommandLine, "--source");

        // Assert
        _sut.Sources.Should().HaveCount(1);
        var source = _sut.Sources["Source"];
        source.ResolvedValue.Should().Be(@"C:\NewPath");
        source.Source.Should().Be(ConfigSourceType.CommandLine);
        source.SourceDetail.Should().Be("--source");
    }

    #endregion

    #region GetSource_ForUnsetProperty_ReturnsDefault

    [Test]
    public void GetSource_ForUnsetProperty_ReturnsDefault_InGeneratedReport()
    {
        // Arrange - Don't record any sources, so all properties will be default

        // Act
        var report = _sut.GenerateReport(_config);

        // Assert
        var sourceValue = report.Values.FirstOrDefault(v => v.PropertyName == "Source");
        sourceValue.Should().NotBeNull();
        sourceValue!.Source.Should().Be(ConfigSourceType.Default);
    }

    [Test]
    public void GetSource_ForUnsetProperty_ReturnsNull_WhenAccessingDirectly()
    {
        // Arrange - Don't record any sources

        // Act
        var hasKey = _sut.Sources.ContainsKey("NonExistentProperty");

        // Assert
        hasKey.Should().BeFalse();
    }

    [Test]
    public void GetSource_ForPartiallySetProperties_ReturnsDefaultForUnset()
    {
        // Arrange - Only record source for "Source" property
        _sut.RecordSource("Source", @"C:\Photos", ConfigSourceType.CommandLine, "--source");

        // Act
        var report = _sut.GenerateReport(_config);

        // Assert
        var sourceValue = report.Values.FirstOrDefault(v => v.PropertyName == "Source");
        var destValue = report.Values.FirstOrDefault(v => v.PropertyName == "Destination");

        sourceValue!.Source.Should().Be(ConfigSourceType.CommandLine);
        destValue!.Source.Should().Be(ConfigSourceType.Default);
    }

    #endregion

    #region GenerateReport_CreatesValidReport

    [Test]
    public void GenerateReport_CreatesValidReport_WithRecordedSources()
    {
        // Arrange
        _sut.RecordSource("Source", @"C:\Photos", ConfigSourceType.CommandLine, "--source");
        _sut.RecordSource("Destination", @"C:\Dest", ConfigSourceType.ConfigFile, "appsettings.json");

        // Act
        var report = _sut.GenerateReport(_config);

        // Assert
        report.Should().NotBeNull();
        report.Values.Should().NotBeNull();
        report.Values.Should().NotBeEmpty();
    }

    [Test]
    public void GenerateReport_CreatesValidReport_WithCorrectSourceTypes()
    {
        // Arrange
        _sut.RecordSource("Source", @"C:\Photos", ConfigSourceType.CommandLine, "--source");

        // Act
        var report = _sut.GenerateReport(_config);

        // Assert
        var sourceValue = report.Values.FirstOrDefault(v => v.PropertyName == "Source");
        sourceValue.Should().NotBeNull();
        sourceValue!.Source.Should().Be(ConfigSourceType.CommandLine);
        sourceValue.SourceDetail.Should().Be("--source");
    }

    [Test]
    public void GenerateReport_CreatesValidReport_WithResolvedValues()
    {
        // Arrange
        _sut.RecordSource("DryRun", "true", ConfigSourceType.Default, null);

        // Act
        var report = _sut.GenerateReport(_config);

        // Assert
        var dryRunValue = report.Values.FirstOrDefault(v => v.PropertyName == "DryRun");
        dryRunValue.Should().NotBeNull();
        dryRunValue!.ResolvedValue.Should().Be("True");
    }

    [Test]
    public void GenerateReport_CreatesValidReport_WhenNoSourcesRecorded()
    {
        // Act
        var report = _sut.GenerateReport(_config);

        // Assert
        report.Should().NotBeNull();
        report.Values.Should().NotBeEmpty();
        report.Values.Should().OnlyContain(v => v.Source == ConfigSourceType.Default);
    }

    [Test]
    public void GenerateReport_CreatesValidReport_WithNullPropertyValues()
    {
        // Arrange
        var configWithNull = new PhotoCopyConfig
        {
            Source = @"C:\Photos",
            Destination = @"C:\Dest",
            GeonamesPath = null,
            MinDate = null,
            MaxDate = null
        };

        // Act
        var report = _sut.GenerateReport(configWithNull);

        // Assert
        var geonamesValue = report.Values.FirstOrDefault(v => v.PropertyName == "GeonamesPath");
        geonamesValue.Should().NotBeNull();
        geonamesValue!.ResolvedValue.Should().BeNull();
    }

    [Test]
    public void GenerateReport_ToJson_ReturnsValidJson()
    {
        // Arrange
        _sut.RecordSource("Source", @"C:\Photos", ConfigSourceType.CommandLine, "--source");

        // Act
        var report = _sut.GenerateReport(_config);
        var json = report.ToJson();

        // Assert
        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("propertyName");
        json.Should().Contain("Source");
    }

    #endregion

    #region GenerateReport_IncludesAllProperties

    [Test]
    public void GenerateReport_IncludesAllProperties_FromPhotoCopyConfig()
    {
        // Arrange - explicit list of PhotoCopyConfig property names (avoids reflection)
        var expectedProperties = new List<string>
        {
            "Source", "Destination", "DryRun", "SkipExisting", "Overwrite", "NoDuplicateSkip",
            "Mode", "LogLevel", "RelatedFileMode", "DuplicatesFormat", "MinDate", "MaxDate",
            "GeonamesPath", "CalculateChecksums", "Parallelism", "ShowProgress", "UseAsync",
            "DuplicateHandling", "EnableRollback", "MaxDepth", "AllowedExtensions",
            "MinimumPopulation", "LocationGranularity", "UseFullCountryNames", "UnknownLocationFallback",
            "PathCasing", "UnknownReport"
        };

        // Act
        var report = _sut.GenerateReport(_config);

        // Assert
        var reportedProperties = report.Values.Select(v => v.PropertyName).ToList();
        reportedProperties.Should().HaveCount(expectedProperties.Count);

        foreach (var expectedProp in expectedProperties)
        {
            reportedProperties.Should().Contain(expectedProp);
        }
    }

    [Test]
    public void GenerateReport_IncludesAllProperties_WithCorrectResolvedValues()
    {
        // Act
        var report = _sut.GenerateReport(_config);

        // Assert
        var sourceValue = report.Values.FirstOrDefault(v => v.PropertyName == "Source");
        var destValue = report.Values.FirstOrDefault(v => v.PropertyName == "Destination");
        var dryRunValue = report.Values.FirstOrDefault(v => v.PropertyName == "DryRun");
        var logLevelValue = report.Values.FirstOrDefault(v => v.PropertyName == "LogLevel");

        sourceValue!.ResolvedValue.Should().Be(_config.Source);
        destValue!.ResolvedValue.Should().Be(_config.Destination);
        dryRunValue!.ResolvedValue.Should().Be("True");
        logLevelValue!.ResolvedValue.Should().Be("Verbose");
    }

    [Test]
    public void GenerateReport_IncludesAllProperties_WithDateTimeFormatting()
    {
        // Arrange
        var configWithDates = new PhotoCopyConfig
        {
            Source = @"C:\Photos",
            Destination = @"C:\Dest",
            MinDate = new DateTime(2020, 6, 15, 10, 30, 0),
            MaxDate = new DateTime(2023, 12, 31, 23, 59, 59)
        };

        // Act
        var report = _sut.GenerateReport(configWithDates);

        // Assert
        var minDateValue = report.Values.FirstOrDefault(v => v.PropertyName == "MinDate");
        var maxDateValue = report.Values.FirstOrDefault(v => v.PropertyName == "MaxDate");

        minDateValue.Should().NotBeNull();
        maxDateValue.Should().NotBeNull();
        // DateTime values should be formatted in ISO 8601 format (O specifier)
        minDateValue!.ResolvedValue.Should().Contain("2020-06-15");
        maxDateValue!.ResolvedValue.Should().Contain("2023-12-31");
    }

    [Test]
    public void GenerateReport_IncludesAllProperties_WithCollectionFormatting()
    {
        // Arrange
        var configWithExtensions = new PhotoCopyConfig
        {
            Source = @"C:\Photos",
            Destination = @"C:\Dest",
            AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".png", ".heic" }
        };

        // Act
        var report = _sut.GenerateReport(configWithExtensions);

        // Assert
        var extensionsValue = report.Values.FirstOrDefault(v => v.PropertyName == "AllowedExtensions");
        extensionsValue.Should().NotBeNull();
        extensionsValue!.ResolvedValue.Should().NotBeNull();
        extensionsValue.ResolvedValue.Should().Contain(".jpg");
        extensionsValue.ResolvedValue.Should().Contain(".png");
        extensionsValue.ResolvedValue.Should().Contain(".heic");
    }

    [Test]
    public void GenerateReport_IncludesAllProperties_MixedSources()
    {
        // Arrange
        _sut.RecordSource("Source", @"C:\Photos", ConfigSourceType.CommandLine, "--source");
        _sut.RecordSource("Destination", @"C:\Dest", ConfigSourceType.ConfigFile, "appsettings.json");
        _sut.RecordSource("DryRun", "true", ConfigSourceType.EnvironmentVariable, "PHOTOCOPY_DRYRUN");

        // Act
        var report = _sut.GenerateReport(_config);

        // Assert
        var cliSources = report.Values.Where(v => v.Source == ConfigSourceType.CommandLine).ToList();
        var configFileSources = report.Values.Where(v => v.Source == ConfigSourceType.ConfigFile).ToList();
        var envVarSources = report.Values.Where(v => v.Source == ConfigSourceType.EnvironmentVariable).ToList();
        var defaultSources = report.Values.Where(v => v.Source == ConfigSourceType.Default).ToList();

        cliSources.Should().HaveCount(1);
        configFileSources.Should().HaveCount(1);
        envVarSources.Should().HaveCount(1);
        defaultSources.Should().NotBeEmpty(); // Remaining properties should be default
    }

    #endregion

    #region ConfigValueSource_Record_Tests

    [Test]
    public void ConfigValueSource_RecordEquality_WorksCorrectly()
    {
        // Arrange
        var source1 = new ConfigValueSource("Source", @"C:\Photos", ConfigSourceType.CommandLine, "--source");
        var source2 = new ConfigValueSource("Source", @"C:\Photos", ConfigSourceType.CommandLine, "--source");

        // Assert
        source1.Should().Be(source2);
    }

    [Test]
    public void ConfigValueSource_RecordWith_CreatesModifiedCopy()
    {
        // Arrange
        var original = new ConfigValueSource("Source", @"C:\Photos", ConfigSourceType.CommandLine, "--source");

        // Act
        var modified = original with { ResolvedValue = @"C:\NewPhotos" };

        // Assert
        modified.PropertyName.Should().Be("Source");
        modified.ResolvedValue.Should().Be(@"C:\NewPhotos");
        modified.Source.Should().Be(ConfigSourceType.CommandLine);
        modified.SourceDetail.Should().Be("--source");
    }

    #endregion

    #region ConfigSourceType_Enum_Tests

    [Test]
    public void ConfigSourceType_HasExpectedValues()
    {
        // Assert
        Enum.GetValues<ConfigSourceType>().Should().HaveCount(4);
        Enum.IsDefined(ConfigSourceType.Default).Should().BeTrue();
        Enum.IsDefined(ConfigSourceType.ConfigFile).Should().BeTrue();
        Enum.IsDefined(ConfigSourceType.EnvironmentVariable).Should().BeTrue();
        Enum.IsDefined(ConfigSourceType.CommandLine).Should().BeTrue();
    }

    #endregion
}
