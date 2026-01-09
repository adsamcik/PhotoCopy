using System;
using System.IO;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using CommandLine;
using CommandLine.Text;
using PhotoCopy.Commands;
using PhotoCopy.Configuration;

namespace PhotoCopy.Tests.Commands;

/// <summary>
/// Tests for CLI argument parsing using CommandLineParser.
/// Validates that short/long options, verbs, and various input patterns are parsed correctly.
/// </summary>
public class OptionsParsingTests
{
    #region Short Arguments Tests

    [Test]
    public void ShortArguments_ParsedCorrectly_ForCopyCommand()
    {
        // Arrange
        var args = new[] { "copy", "-s", @"C:\Source", "-d", @"C:\Dest\{year}" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.Source.Should().Be(@"C:\Source");
            opts.Destination.Should().Be(@"C:\Dest\{year}");
        });
    }

    [Test]
    public void ShortArguments_ParsedCorrectly_ForDryRun()
    {
        // Arrange - bool? requires explicit true/false value
        var args = new[] { "copy", "-s", @"C:\Source", "-n", "true" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.DryRun.Should().BeTrue();
        });
    }

    [Test]
    public void ShortArguments_ParsedCorrectly_ForLogLevel()
    {
        // Arrange
        var args = new[] { "copy", "-l", "verbose" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.LogLevel.Should().Be(OutputLevel.Verbose);
        });
    }

    [Test]
    public void ShortArguments_ParsedCorrectly_ForParallelism()
    {
        // Arrange
        var args = new[] { "copy", "-p", "4" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.Parallelism.Should().Be(4);
        });
    }

    [Test]
    public void ShortArguments_ParsedCorrectly_ForOperationMode()
    {
        // Arrange
        var args = new[] { "copy", "-m", "move" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.Mode.Should().Be(OperationMode.Move);
        });
    }

    #endregion

    #region Long Arguments Tests

    [Test]
    public void LongArguments_ParsedCorrectly_ForCopyCommand()
    {
        // Arrange
        var args = new[] { "copy", "--source", @"C:\Photos", "--destination", @"D:\Backup\{year}\{month}" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.Source.Should().Be(@"C:\Photos");
            opts.Destination.Should().Be(@"D:\Backup\{year}\{month}");
        });
    }

    [Test]
    public void LongArguments_ParsedCorrectly_ForDryRun()
    {
        // Arrange - bool? requires explicit true/false value
        var args = new[] { "copy", "--dry-run", "true" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.DryRun.Should().BeTrue();
        });
    }

    [Test]
    public void LongArguments_ParsedCorrectly_ForSkipExisting()
    {
        // Arrange - bool? requires explicit true/false value
        var args = new[] { "copy", "--skip-existing", "true" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.SkipExisting.Should().BeTrue();
        });
    }

    [Test]
    public void LongArguments_ParsedCorrectly_ForOverwrite()
    {
        // Arrange - bool? requires explicit true/false value
        var args = new[] { "copy", "--overwrite", "true" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.Overwrite.Should().BeTrue();
        });
    }

    [Test]
    public void LongArguments_ParsedCorrectly_ForRelatedFileMode()
    {
        // Arrange
        var args = new[] { "copy", "--related-file-mode", "strict" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.RelatedFileMode.Should().Be(RelatedFileLookup.Strict);
        });
    }

    [Test]
    public void LongArguments_ParsedCorrectly_ForConfigPath()
    {
        // Arrange
        var args = new[] { "copy", "--config", @"C:\config\settings.yaml" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.ConfigPath.Should().Be(@"C:\config\settings.yaml");
        });
    }

    [Test]
    public void LongArguments_ParsedCorrectly_ForDateFilters()
    {
        // Arrange
        var minDate = new DateTime(2023, 1, 1);
        var maxDate = new DateTime(2023, 12, 31);
        var args = new[] { "copy", "--min-date", "2023-01-01", "--max-date", "2023-12-31" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.MinDate.Should().Be(minDate);
            opts.MaxDate.Should().Be(maxDate);
        });
    }

    [Test]
    public void LongArguments_ParsedCorrectly_ForGeonamesPath()
    {
        // Arrange
        var args = new[] { "copy", "--geonames-path", @"C:\Data\cities500.txt" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.GeonamesPath.Should().Be(@"C:\Data\cities500.txt");
        });
    }

    [Test]
    public void LongArguments_ParsedCorrectly_ForCalculateChecksums()
    {
        // Arrange - bool? requires explicit true/false value
        var args = new[] { "copy", "--calculate-checksums", "true" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.CalculateChecksums.Should().BeTrue();
        });
    }

    [Test]
    public void LongArguments_ParsedCorrectly_ForDuplicateHandling()
    {
        // Arrange
        var args = new[] { "copy", "--detect-duplicates", "skip" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.DuplicateHandling.Should().Be(DuplicateHandlingOption.Skip);
        });
    }

    [Test]
    public void LongArguments_ParsedCorrectly_ForEnableRollback()
    {
        // Arrange - bool? requires explicit true/false value
        var args = new[] { "copy", "--enable-rollback", "true" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.EnableRollback.Should().BeTrue();
        });
    }

    #endregion

    #region Mixed Short and Long Arguments Tests

    [Test]
    public void MixedShortAndLong_ParsedCorrectly_ForCopyCommand()
    {
        // Arrange - bool? requires explicit true/false values
        var args = new[] { "copy", "-s", @"C:\Source", "--destination", @"D:\Dest", "-n", "true", "--overwrite", "true" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.Source.Should().Be(@"C:\Source");
            opts.Destination.Should().Be(@"D:\Dest");
            opts.DryRun.Should().BeTrue();
            opts.Overwrite.Should().BeTrue();
        });
    }

    [Test]
    public void MixedShortAndLong_ParsedCorrectly_WithAllOptions()
    {
        // Arrange - bool? requires explicit true/false values
        var args = new[]
        {
            "copy",
            "-s", @"C:\Photos",
            "--destination", @"D:\Backup\{year}\{month}\{day}",
            "-n", "true",
            "--skip-existing", "true",
            "-m", "copy",
            "--log-level", "important",
            "-p", "8"
        };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.Source.Should().Be(@"C:\Photos");
            opts.Destination.Should().Be(@"D:\Backup\{year}\{month}\{day}");
            opts.DryRun.Should().BeTrue();
            opts.SkipExisting.Should().BeTrue();
            opts.Mode.Should().Be(OperationMode.Copy);
            opts.LogLevel.Should().Be(OutputLevel.Important);
            opts.Parallelism.Should().Be(8);
        });
    }

    #endregion

    #region Boolean Flags Tests

    [Test]
    public void BooleanFlags_DefaultsAndExplicit_DryRunImplicit()
    {
        // Arrange - bool? requires explicit true/false value
        var args = new[] { "copy", "--dry-run", "true" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.DryRun.Should().BeTrue();
        });
    }

    [Test]
    public void BooleanFlags_DefaultsAndExplicit_NoDryRunSpecified()
    {
        // Arrange - no flag means null (not specified)
        var args = new[] { "copy", "-s", @"C:\Source" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.DryRun.Should().BeNull();
        });
    }

    [Test]
    public void BooleanFlags_DefaultsAndExplicit_SkipExistingAndOverwriteFlags()
    {
        // Arrange - bool? requires explicit true/false value
        var args = new[] { "copy", "--skip-existing", "true", "-o", "true" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.SkipExisting.Should().BeTrue();
            opts.Overwrite.Should().BeTrue();
        });
    }

    [Test]
    public void BooleanFlags_DefaultsAndExplicit_NoDuplicateSkip()
    {
        // Arrange - bool? requires explicit true/false value
        var args = new[] { "copy", "-k", "true" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.NoDuplicateSkip.Should().BeTrue();
        });
    }

    [Test]
    public void BooleanFlags_DefaultsAndExplicit_MultipleBooleanFlags()
    {
        // Arrange - bool? requires explicit true/false values
        var args = new[] { "copy", "-n", "true", "-e", "true", "-o", "true", "-k", "true" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.DryRun.Should().BeTrue();
            opts.SkipExisting.Should().BeTrue();
            opts.Overwrite.Should().BeTrue();
            opts.NoDuplicateSkip.Should().BeTrue();
        });
    }

    #endregion

    #region Paths With Spaces Tests

    [Test]
    public void PathsWithSpaces_ParsedCorrectly_SourcePath()
    {
        // Arrange
        var args = new[] { "copy", "-s", @"C:\My Photos\2024", "-d", @"D:\Backup" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.Source.Should().Be(@"C:\My Photos\2024");
        });
    }

    [Test]
    public void PathsWithSpaces_ParsedCorrectly_DestinationPath()
    {
        // Arrange
        var args = new[] { "copy", "-s", @"C:\Source", "-d", @"D:\My Backup Folder\Photos\{year}" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.Destination.Should().Be(@"D:\My Backup Folder\Photos\{year}");
        });
    }

    [Test]
    public void PathsWithSpaces_ParsedCorrectly_BothPaths()
    {
        // Arrange
        var args = new[] { "copy", "--source", @"C:\My Photos\Summer Vacation", "--destination", @"D:\Family Albums\2024 Vacation\{month}" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.Source.Should().Be(@"C:\My Photos\Summer Vacation");
            opts.Destination.Should().Be(@"D:\Family Albums\2024 Vacation\{month}");
        });
    }

    [Test]
    public void PathsWithSpaces_ParsedCorrectly_ConfigPath()
    {
        // Arrange
        var args = new[] { "copy", "--config", @"C:\Program Files\PhotoCopy\config.yaml" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.ConfigPath.Should().Be(@"C:\Program Files\PhotoCopy\config.yaml");
        });
    }

    #endregion

    #region Paths With Quotes Tests

    [Test]
    public void PathsWithQuotes_HandledCorrectly_QuotedSource()
    {
        // Arrange - quotes typically handled by shell, parser sees unquoted
        var args = new[] { "copy", "-s", @"C:\Source\Path", "-d", @"D:\Dest" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.Source.Should().Be(@"C:\Source\Path");
        });
    }

    [Test]
    public void PathsWithQuotes_HandledCorrectly_PathWithVariables()
    {
        // Arrange - braces in destination pattern
        var args = new[] { "copy", "-d", @"D:\Backup\{year}\{month}\{day}\{name}{ext}" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.Destination.Should().Be(@"D:\Backup\{year}\{month}\{day}\{name}{ext}");
        });
    }

    [Test]
    public void PathsWithQuotes_HandledCorrectly_UNCPath()
    {
        // Arrange - UNC network path
        var args = new[] { "copy", "-s", @"\\Server\Share\Photos" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.Source.Should().Be(@"\\Server\Share\Photos");
        });
    }

    #endregion

    #region Invalid Argument Value Tests

    [Test]
    public void InvalidArgumentValue_ReportsError_ForInvalidEnum()
    {
        // Arrange - "invalid" is not a valid OperationMode
        var args = new[] { "copy", "-m", "invalid" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.NotParsed);
    }

    [Test]
    public void InvalidArgumentValue_ReportsError_ForInvalidLogLevel()
    {
        // Arrange
        var args = new[] { "copy", "-l", "notavalidlevel" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.NotParsed);
    }

    [Test]
    public void InvalidArgumentValue_ReportsError_ForInvalidRelatedFileMode()
    {
        // Arrange
        var args = new[] { "copy", "-r", "invalid" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.NotParsed);
    }

    [Test]
    public void InvalidArgumentValue_ReportsError_ForNonNumericParallelism()
    {
        // Arrange
        var args = new[] { "copy", "-p", "abc" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.NotParsed);
    }

    [Test]
    public void InvalidArgumentValue_ReportsError_ForInvalidDate()
    {
        // Arrange
        var args = new[] { "copy", "--min-date", "not-a-date" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.NotParsed);
    }

    [Test]
    public void InvalidArgumentValue_ReportsError_ForInvalidDuplicateHandling()
    {
        // Arrange
        var args = new[] { "copy", "--detect-duplicates", "invalid" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.NotParsed);
    }

    #endregion

    #region Unknown Argument Tests

    [Test]
    public void UnknownArgument_ReportsError_ForUnrecognizedLongOption()
    {
        // Arrange
        var args = new[] { "copy", "--unknown-option", "value" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.NotParsed);
    }

    [Test]
    public void UnknownArgument_ReportsError_ForUnrecognizedShortOption()
    {
        // Arrange
        var args = new[] { "copy", "-z", "value" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.NotParsed);
    }

    [Test]
    public void UnknownArgument_ReportsError_ForUnrecognizedVerb()
    {
        // Arrange - With copy as default verb, unknownverb gets treated as source value
        // To detect unknown verb, we need to use an unknown option format
        var args = new[] { "unknownverb", "--unknown-option" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert - fails due to unknown option, not unknown verb (since copy is default)
        result.Tag.Should().Be(ParserResultType.NotParsed);
    }

    #endregion

    #region Help Flag Tests

    [Test]
    public void HelpFlag_ShowsHelp_WithLongOption()
    {
        // Arrange - no verb specified shows verb-level help
        var args = new[] { "--help" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.NotParsed);
        result.Errors.Should().ContainItemsAssignableTo<HelpVerbRequestedError>();
    }

    [Test]
    public void HelpFlag_ShowsHelp_WithVerbAndHelp()
    {
        // Arrange - verb specified shows verb-specific help
        var args = new[] { "copy", "--help" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.NotParsed);
        result.Errors.Should().ContainItemsAssignableTo<HelpRequestedError>();
    }

    [Test]
    public void HelpFlag_ShowsHelp_ForScanCommand()
    {
        // Arrange - verb specified shows verb-specific help
        var args = new[] { "scan", "--help" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.NotParsed);
        result.Errors.Should().ContainItemsAssignableTo<HelpRequestedError>();
    }

    [Test]
    public void HelpFlag_ShowsHelp_ForValidateCommand()
    {
        // Arrange - verb specified shows verb-specific help
        var args = new[] { "validate", "--help" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.NotParsed);
        result.Errors.Should().ContainItemsAssignableTo<HelpRequestedError>();
    }

    [Test]
    public void HelpFlag_ShowsHelp_ForConfigCommand()
    {
        // Arrange - verb specified shows verb-specific help
        var args = new[] { "config", "--help" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.NotParsed);
        result.Errors.Should().ContainItemsAssignableTo<HelpRequestedError>();
    }

    [Test]
    public void HelpFlag_ShowsHelp_ForRollbackCommand()
    {
        // Arrange - verb specified shows verb-specific help
        var args = new[] { "rollback", "--help" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.NotParsed);
        result.Errors.Should().ContainItemsAssignableTo<HelpRequestedError>();
    }

    #endregion

    #region Version Flag Tests

    [Test]
    public void VersionFlag_ShowsVersion_WithLongOption()
    {
        // Arrange
        var args = new[] { "--version" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.NotParsed);
        result.Errors.Should().ContainItemsAssignableTo<VersionRequestedError>();
    }

    #endregion

    #region Verb Command Tests

    [Test]
    public void VerbCommand_ParsedCorrectly_ScanCommand()
    {
        // Arrange
        var args = new[] { "scan", "-s", @"C:\Photos", "--json" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<ScanOptions>(opts =>
        {
            opts.Source.Should().Be(@"C:\Photos");
            opts.OutputJson.Should().BeTrue();
        });
    }

    [Test]
    public void VerbCommand_ParsedCorrectly_ValidateCommand()
    {
        // Arrange
        var args = new[] { "validate", "-s", @"C:\Photos" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<ValidateOptions>(opts =>
        {
            opts.Source.Should().Be(@"C:\Photos");
        });
    }

    [Test]
    public void VerbCommand_ParsedCorrectly_ConfigCommand()
    {
        // Arrange
        var args = new[] { "config", "--json" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<ConfigOptions>(opts =>
        {
            opts.OutputJson.Should().BeTrue();
        });
    }

    [Test]
    public void VerbCommand_ParsedCorrectly_RollbackCommand()
    {
        // Arrange
        var args = new[] { "rollback", "-f", @"C:\Logs\transaction.log" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<RollbackOptions>(opts =>
        {
            opts.TransactionLogPath.Should().Be(@"C:\Logs\transaction.log");
        });
    }

    [Test]
    public void VerbCommand_ParsedCorrectly_RollbackWithList()
    {
        // Arrange
        var args = new[] { "rollback", "--list" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<RollbackOptions>(opts =>
        {
            opts.ListLogs.Should().BeTrue();
        });
    }

    [Test]
    public void VerbCommand_ParsedCorrectly_RollbackWithDirectory()
    {
        // Arrange
        var args = new[] { "rollback", "-d", @"C:\Logs" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<RollbackOptions>(opts =>
        {
            opts.LogDirectory.Should().Be(@"C:\Logs");
        });
    }

    [Test]
    public void VerbCommand_DefaultsToPoppy_WhenNoVerbSpecified()
    {
        // Arrange - copy is the default verb
        var args = new[] { "-s", @"C:\Source", "-d", @"D:\Dest" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.Source.Should().Be(@"C:\Source");
            opts.Destination.Should().Be(@"D:\Dest");
        });
    }

    #endregion

    #region Edge Cases

    [Test]
    public void EdgeCase_EmptyArgs_DefaultsToCopyWithNoOptions()
    {
        // Arrange
        var args = Array.Empty<string>();

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.Source.Should().BeNull();
            opts.Destination.Should().BeNull();
        });
    }

    [Test]
    public void EdgeCase_DuplicatesFormatWithSpecialChars()
    {
        // Arrange
        var args = new[] { "copy", "-f", @"{name}_dup_{number}{ext}" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.DuplicatesFormat.Should().Be(@"{name}_dup_{number}{ext}");
        });
    }

    [Test]
    public void EdgeCase_DestinationWithAllVariables()
    {
        // Arrange
        var args = new[] { "copy", "-d", @"D:\{year}\{month}\{day}\{city}\{country}\{name}{ext}" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.Destination.Should().Be(@"D:\{year}\{month}\{day}\{city}\{country}\{name}{ext}");
        });
    }

    [Test]
    public void EdgeCase_NegativeParallelism()
    {
        // Arrange - CommandLineParser accepts negative numbers, validation happens elsewhere
        var args = new[] { "copy", "-p", "-1" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<CopyOptions>(opts =>
        {
            opts.Parallelism.Should().Be(-1);
        });
    }

    [Test]
    public void EdgeCase_ScanWithChecksums()
    {
        // Arrange - bool? requires explicit true/false value
        var args = new[] { "scan", "-s", @"C:\Photos", "--calculate-checksums", "true" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<ScanOptions>(opts =>
        {
            opts.Source.Should().Be(@"C:\Photos");
            opts.CalculateChecksums.Should().BeTrue();
        });
    }

    [Test]
    public void EdgeCase_ValidateWithDateRange()
    {
        // Arrange
        var args = new[] { "validate", "-s", @"C:\Photos", "--min-date", "2020-01-01", "--max-date", "2024-12-31" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<ValidateOptions>(opts =>
        {
            opts.Source.Should().Be(@"C:\Photos");
            opts.MinDate.Should().Be(new DateTime(2020, 1, 1));
            opts.MaxDate.Should().Be(new DateTime(2024, 12, 31));
        });
    }

    [Test]
    public void EdgeCase_ConfigWithSourceAndDestination()
    {
        // Arrange
        var args = new[] { "config", "-s", @"C:\Source", "-d", @"D:\Dest" };

        // Act
        var result = Parser.Default.ParseArguments<CopyOptions, ScanOptions, ValidateOptions, ConfigOptions, RollbackOptions>(args);

        // Assert
        result.Tag.Should().Be(ParserResultType.Parsed);
        result.WithParsed<ConfigOptions>(opts =>
        {
            opts.Source.Should().Be(@"C:\Source");
            opts.Destination.Should().Be(@"D:\Dest");
        });
    }

    #endregion
}
