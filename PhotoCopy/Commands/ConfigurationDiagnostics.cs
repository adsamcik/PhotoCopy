using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PhotoCopy.Configuration;

namespace PhotoCopy.Commands;

/// <summary>
/// Tracks where each configuration value came from.
/// </summary>
public sealed class ConfigurationDiagnostics
{
    private readonly Dictionary<string, ConfigValueSource> _sources = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the source information for all tracked properties.
    /// </summary>
    public IReadOnlyDictionary<string, ConfigValueSource> Sources => _sources;

    /// <summary>
    /// Records the source of a configuration value.
    /// </summary>
    public void RecordSource(string propertyName, string? value, ConfigSourceType source, string? sourceDetail = null)
    {
        _sources[propertyName] = new ConfigValueSource(propertyName, value, source, sourceDetail);
    }

    /// <summary>
    /// Generates a diagnostic report.
    /// </summary>
    public ConfigDiagnosticReport GenerateReport(PhotoCopyConfig config)
    {
        var resolvedValues = new List<ConfigValueSource>();
        var properties = typeof(PhotoCopyConfig).GetProperties();

        foreach (var prop in properties)
        {
            var value = prop.GetValue(config);
            var valueStr = FormatValue(value);

            if (_sources.TryGetValue(prop.Name, out var source))
            {
                resolvedValues.Add(source with { ResolvedValue = valueStr });
            }
            else
            {
                resolvedValues.Add(new ConfigValueSource(prop.Name, valueStr, ConfigSourceType.Default, null));
            }
        }

        return new ConfigDiagnosticReport(resolvedValues);
    }

    private static string? FormatValue(object? value)
    {
        return value switch
        {
            null => null,
            string s => s,
            DateTime dt => dt.ToString("O"),
            IEnumerable<string> list => string.Join(", ", list),
            _ => value.ToString()
        };
    }
}

/// <summary>
/// Information about where a config value came from.
/// </summary>
public sealed record ConfigValueSource(
    string PropertyName,
    string? ResolvedValue,
    ConfigSourceType Source,
    string? SourceDetail);

/// <summary>
/// Type of configuration source.
/// </summary>
public enum ConfigSourceType
{
    Default,
    ConfigFile,
    EnvironmentVariable,
    CommandLine
}

/// <summary>
/// Complete diagnostic report for configuration.
/// </summary>
public sealed record ConfigDiagnosticReport(IReadOnlyList<ConfigValueSource> Values)
{
    /// <summary>
    /// Outputs the report to the console.
    /// </summary>
    public void PrintToConsole()
    {
        Console.WriteLine();
        Console.WriteLine("=== PhotoCopy Configuration Diagnostics ===");
        Console.WriteLine();
        Console.WriteLine($"{"Property",-25} {"Value",-40} {"Source",-15} {"Detail"}");
        Console.WriteLine(new string('-', 100));

        foreach (var value in Values)
        {
            var displayValue = value.ResolvedValue ?? "(null)";
            if (displayValue.Length > 38)
            {
                displayValue = displayValue[..35] + "...";
            }

            var sourceStr = value.Source switch
            {
                ConfigSourceType.Default => "Default",
                ConfigSourceType.ConfigFile => "Config File",
                ConfigSourceType.EnvironmentVariable => "Environment",
                ConfigSourceType.CommandLine => "CLI",
                _ => "Unknown"
            };

            var detail = value.SourceDetail ?? "";
            if (detail.Length > 30)
            {
                detail = "..." + detail[^27..];
            }

            Console.WriteLine($"{value.PropertyName,-25} {displayValue,-40} {sourceStr,-15} {detail}");
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Outputs the report as JSON.
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(Values, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
