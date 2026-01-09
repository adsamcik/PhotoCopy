using Microsoft.Extensions.Configuration;
using PhotoCopy.Commands;
using PhotoCopy.Duplicates;
using System;
using System.IO;

namespace PhotoCopy.Configuration;

/// <summary>
/// Handles loading and applying configuration from various sources.
/// </summary>
public static class ConfigurationLoader
{
    /// <summary>
    /// Loads configuration from files and environment variables, then applies command-line overrides.
    /// </summary>
    /// <typeparam name="TOptions">The command options type.</typeparam>
    /// <param name="options">The command-line options.</param>
    /// <returns>The loaded configuration.</returns>
    public static PhotoCopyConfig Load<TOptions>(TOptions options) where TOptions : CommonOptions
    {
        var config = LoadFromSources(options);
        ApplyOverrides(config, options);
        return config;
    }

    /// <summary>
    /// Loads configuration with full diagnostics tracking the source of each value.
    /// </summary>
    /// <typeparam name="TOptions">The command options type.</typeparam>
    /// <param name="options">The command-line options.</param>
    /// <returns>The configuration and diagnostics.</returns>
    public static (PhotoCopyConfig Config, ConfigurationDiagnostics Diagnostics) LoadWithDiagnostics<TOptions>(TOptions options)
        where TOptions : CommonOptions
    {
        var diagnostics = new ConfigurationDiagnostics();
        var config = new PhotoCopyConfig();

        // Record defaults first
        foreach (var prop in typeof(PhotoCopyConfig).GetProperties())
        {
            var defaultValue = prop.GetValue(config)?.ToString();
            diagnostics.RecordSource(prop.Name, defaultValue, ConfigSourceType.Default);
        }

        // Build configuration from files
        var (configuration, configFilePath) = BuildConfiguration(options);

        // Track which values came from config file
        foreach (var section in configuration.GetChildren())
        {
            if (section.Value is not null)
            {
                diagnostics.RecordSource(section.Key, section.Value, ConfigSourceType.ConfigFile, configFilePath);
            }
        }

        configuration.Bind(config);

        // Apply common overrides with diagnostics
        if (options.LogLevel.HasValue)
        {
            config.LogLevel = options.LogLevel.Value;
            diagnostics.RecordSource("LogLevel", options.LogLevel.Value.ToString(), ConfigSourceType.CommandLine, "--log-level");
        }

        return (config, diagnostics);
    }

    /// <summary>
    /// Applies command-line overrides to the configuration.
    /// </summary>
    public static void ApplyOverrides<TOptions>(PhotoCopyConfig config, TOptions options, ConfigurationDiagnostics? diagnostics = null)
        where TOptions : CommonOptions
    {
        // Apply common options (LogLevel already applied during load)
        
        // Use pattern matching to apply type-specific overrides
        switch (options)
        {
            case CopyOptions copyOptions:
                ApplyCopyOverrides(config, copyOptions, diagnostics);
                break;
            case ScanOptions scanOptions:
                ApplyScanOverrides(config, scanOptions, diagnostics);
                break;
            case ValidateOptions validateOptions:
                ApplyValidateOverrides(config, validateOptions, diagnostics);
                break;
            case ConfigOptions configOptions:
                ApplyConfigOverrides(config, configOptions, diagnostics);
                break;
        }
    }

    private static PhotoCopyConfig LoadFromSources<TOptions>(TOptions options) where TOptions : CommonOptions
    {
        var (configuration, _) = BuildConfiguration(options);
        var config = new PhotoCopyConfig();
        configuration.Bind(config);

        // Apply common overrides
        if (options.LogLevel.HasValue) config.LogLevel = options.LogLevel.Value;

        return config;
    }

    private static (IConfigurationRoot Configuration, string? ConfigFilePath) BuildConfiguration<TOptions>(TOptions options)
        where TOptions : CommonOptions
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        string? configFilePath = null;
        if (!string.IsNullOrEmpty(options.ConfigPath))
        {
            configFilePath = options.ConfigPath;
            var ext = Path.GetExtension(options.ConfigPath).ToLower();
            if (ext is ".yaml" or ".yml")
            {
                builder.AddYamlFile(options.ConfigPath, optional: false);
            }
            else
            {
                builder.AddJsonFile(options.ConfigPath, optional: false);
            }
        }

        return (builder.Build(), configFilePath);
    }

    private static void ApplyCopyOverrides(PhotoCopyConfig config, CopyOptions options, ConfigurationDiagnostics? diagnostics)
    {
        ApplyIfNotEmpty(options.Source, v => config.Source = v, "Source", "--source", diagnostics);
        ApplyIfNotEmpty(options.Destination, v => config.Destination = v, "Destination", "--destination", diagnostics);
        ApplyIfHasValue(options.DryRun, v => config.DryRun = v, "DryRun", "--dry-run", diagnostics);
        ApplyIfHasValue(options.SkipExisting, v => config.SkipExisting = v, "SkipExisting", "--skip-existing", diagnostics);
        ApplyIfHasValue(options.Overwrite, v => config.Overwrite = v, "Overwrite", "--overwrite", diagnostics);
        ApplyIfHasValue(options.NoDuplicateSkip, v => config.NoDuplicateSkip = v, "NoDuplicateSkip", "--no-duplicate-skip", diagnostics);
        ApplyIfHasValue(options.Mode, v => config.Mode = v, "Mode", "--mode", diagnostics);
        ApplyIfHasValue(options.RelatedFileMode, v => config.RelatedFileMode = v, "RelatedFileMode", "--related-file-mode", diagnostics);
        ApplyIfNotEmpty(options.DuplicatesFormat, v => config.DuplicatesFormat = v, "DuplicatesFormat", "--duplicates-format", diagnostics);
        ApplyIfHasValue(options.MinDate, v => config.MinDate = v, "MinDate", "--min-date", diagnostics);
        ApplyIfHasValue(options.MaxDate, v => config.MaxDate = v, "MaxDate", "--max-date", diagnostics);
        ApplyIfNotEmpty(options.GeonamesPath, v => config.GeonamesPath = v, "GeonamesPath", "--geonames-path", diagnostics);
        ApplyIfHasValue(options.CalculateChecksums, v => config.CalculateChecksums = v, "CalculateChecksums", "--calculate-checksums", diagnostics);
        ApplyIfHasValue(options.Parallelism, v => config.Parallelism = v, "Parallelism", "--parallelism", diagnostics);
        ApplyIfHasValue(options.EnableRollback, v => config.EnableRollback = v, "EnableRollback", "--enable-rollback", diagnostics);
        ApplyIfHasValue(options.MaxDepth, v => config.MaxDepth = v, "MaxDepth", "--max-depth", diagnostics);

        if (options.DuplicateHandling.HasValue)
        {
            config.DuplicateHandling = options.DuplicateHandling.Value switch
            {
                DuplicateHandlingOption.Skip => DuplicateHandling.SkipDuplicates,
                DuplicateHandlingOption.Prompt => DuplicateHandling.Prompt,
                DuplicateHandlingOption.Report => DuplicateHandling.ReportOnly,
                _ => DuplicateHandling.None
            };
            diagnostics?.RecordSource("DuplicateHandling", config.DuplicateHandling.ToString(), ConfigSourceType.CommandLine, "--duplicate-handling");
        }
    }

    private static void ApplyScanOverrides(PhotoCopyConfig config, ScanOptions options, ConfigurationDiagnostics? diagnostics)
    {
        ApplyIfNotEmpty(options.Source, v => config.Source = v, "Source", "--source", diagnostics);
        ApplyIfHasValue(options.MinDate, v => config.MinDate = v, "MinDate", "--min-date", diagnostics);
        ApplyIfHasValue(options.MaxDate, v => config.MaxDate = v, "MaxDate", "--max-date", diagnostics);
        ApplyIfNotEmpty(options.GeonamesPath, v => config.GeonamesPath = v, "GeonamesPath", "--geonames-path", diagnostics);
        ApplyIfHasValue(options.CalculateChecksums, v => config.CalculateChecksums = v, "CalculateChecksums", "--calculate-checksums", diagnostics);
        ApplyIfHasValue(options.MaxDepth, v => config.MaxDepth = v, "MaxDepth", "--max-depth", diagnostics);
    }

    private static void ApplyValidateOverrides(PhotoCopyConfig config, ValidateOptions options, ConfigurationDiagnostics? diagnostics)
    {
        ApplyIfNotEmpty(options.Source, v => config.Source = v, "Source", "--source", diagnostics);
        ApplyIfHasValue(options.MinDate, v => config.MinDate = v, "MinDate", "--min-date", diagnostics);
        ApplyIfHasValue(options.MaxDate, v => config.MaxDate = v, "MaxDate", "--max-date", diagnostics);
        ApplyIfNotEmpty(options.GeonamesPath, v => config.GeonamesPath = v, "GeonamesPath", "--geonames-path", diagnostics);
        ApplyIfHasValue(options.MaxDepth, v => config.MaxDepth = v, "MaxDepth", "--max-depth", diagnostics);
    }

    private static void ApplyConfigOverrides(PhotoCopyConfig config, ConfigOptions options, ConfigurationDiagnostics? diagnostics)
    {
        ApplyIfNotEmpty(options.Source, v => config.Source = v, "Source", "--source", diagnostics);
        ApplyIfNotEmpty(options.Destination, v => config.Destination = v, "Destination", "--destination", diagnostics);
    }

    private static void ApplyIfNotEmpty(string? value, Action<string> setter, string propertyName, string cliFlag, ConfigurationDiagnostics? diagnostics)
    {
        if (!string.IsNullOrEmpty(value))
        {
            setter(value);
            diagnostics?.RecordSource(propertyName, value, ConfigSourceType.CommandLine, cliFlag);
        }
    }

    private static void ApplyIfHasValue<T>(T? value, Action<T> setter, string propertyName, string cliFlag, ConfigurationDiagnostics? diagnostics)
        where T : struct
    {
        if (value.HasValue)
        {
            setter(value.Value);
            diagnostics?.RecordSource(propertyName, value.Value.ToString()!, ConfigSourceType.CommandLine, cliFlag);
        }
    }
}
