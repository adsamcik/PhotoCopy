using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PhotoCopy.Configuration;

namespace PhotoCopy.Validators;

/// <summary>
/// Validates PhotoCopy configuration for common errors before processing begins.
/// </summary>
public interface IConfigurationValidator
{
    /// <summary>
    /// Validates the configuration and returns a list of validation errors.
    /// </summary>
    /// <param name="config">The configuration to validate.</param>
    /// <returns>A list of validation errors. Empty if validation passes.</returns>
    IReadOnlyList<ConfigurationValidationError> Validate(PhotoCopyConfig config);
}

/// <summary>
/// Represents a configuration validation error.
/// </summary>
public record ConfigurationValidationError(string PropertyName, string ErrorMessage);

/// <summary>
/// Default implementation of configuration validation.
/// </summary>
public class ConfigurationValidator : IConfigurationValidator
{
    // Known destination pattern variables
    private static readonly HashSet<string> ValidVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "{year}",
        "{month}",
        "{day}",
        "{name}",
        "{namenoext}",
        "{ext}",
        "{directory}",
        "{number}",
        "{city}",
        "{state}",
        "{country}"
    };

    // Regex to find all {variable} patterns
    private static readonly Regex VariablePattern = new(@"\{[^}]+\}", RegexOptions.Compiled);

    public IReadOnlyList<ConfigurationValidationError> Validate(PhotoCopyConfig config)
    {
        var errors = new List<ConfigurationValidationError>();

        ValidateSourcePath(config, errors);
        ValidateDestinationPath(config, errors);
        ValidateSourceNotEqualsDestination(config, errors);
        ValidateDateRange(config, errors);
        ValidateParallelism(config, errors);
        ValidateDestinationPattern(config, errors);
        ValidateDuplicatesFormat(config, errors);

        return errors;
    }

    private static void ValidateSourcePath(PhotoCopyConfig config, List<ConfigurationValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(config.Source))
        {
            errors.Add(new ConfigurationValidationError(
                nameof(PhotoCopyConfig.Source),
                "Source path is required."));
            return;
        }

        if (!Directory.Exists(config.Source))
        {
            errors.Add(new ConfigurationValidationError(
                nameof(PhotoCopyConfig.Source),
                $"Source path '{config.Source}' does not exist."));
        }
    }

    private static void ValidateDestinationPath(PhotoCopyConfig config, List<ConfigurationValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(config.Destination))
        {
            errors.Add(new ConfigurationValidationError(
                nameof(PhotoCopyConfig.Destination),
                "Destination path is required."));
        }
    }

    private static void ValidateSourceNotEqualsDestination(PhotoCopyConfig config, List<ConfigurationValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(config.Source) || string.IsNullOrWhiteSpace(config.Destination))
        {
            return; // Skip if either is empty - will be caught by other validators
        }

        // Normalize paths for comparison
        var normalizedSource = NormalizePath(config.Source);
        var normalizedDest = GetDestinationBasePath(config.Destination);

        if (string.Equals(normalizedSource, normalizedDest, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new ConfigurationValidationError(
                nameof(PhotoCopyConfig.Destination),
                "Source and destination paths cannot be the same. This would cause an infinite loop."));
        }

        // Also check if destination is inside source (would also cause issues)
        if (normalizedDest.StartsWith(normalizedSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new ConfigurationValidationError(
                nameof(PhotoCopyConfig.Destination),
                "Destination path cannot be inside the source path. This would cause an infinite loop."));
        }
    }

    private static void ValidateDateRange(PhotoCopyConfig config, List<ConfigurationValidationError> errors)
    {
        if (config.MinDate.HasValue && config.MaxDate.HasValue)
        {
            if (config.MinDate.Value > config.MaxDate.Value)
            {
                errors.Add(new ConfigurationValidationError(
                    nameof(PhotoCopyConfig.MinDate),
                    $"MinDate ({config.MinDate.Value:yyyy-MM-dd}) cannot be after MaxDate ({config.MaxDate.Value:yyyy-MM-dd})."));
            }
        }
    }

    private static void ValidateParallelism(PhotoCopyConfig config, List<ConfigurationValidationError> errors)
    {
        if (config.Parallelism <= 0)
        {
            errors.Add(new ConfigurationValidationError(
                nameof(PhotoCopyConfig.Parallelism),
                $"Parallelism must be a positive number. Got: {config.Parallelism}."));
        }
    }

    private static void ValidateDestinationPattern(PhotoCopyConfig config, List<ConfigurationValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(config.Destination))
        {
            return; // Will be caught by ValidateDestinationPath
        }

        var matches = VariablePattern.Matches(config.Destination);
        foreach (Match match in matches)
        {
            var variable = match.Value;
            if (!ValidVariables.Contains(variable))
            {
                errors.Add(new ConfigurationValidationError(
                    nameof(PhotoCopyConfig.Destination),
                    $"Unknown destination pattern variable: '{variable}'. " +
                    $"Valid variables are: {string.Join(", ", ValidVariables)}."));
            }
        }
    }

    private static void ValidateDuplicatesFormat(PhotoCopyConfig config, List<ConfigurationValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(config.DuplicatesFormat))
        {
            errors.Add(new ConfigurationValidationError(
                nameof(PhotoCopyConfig.DuplicatesFormat),
                "Duplicates format is required."));
            return;
        }

        if (!config.DuplicatesFormat.Contains(DestinationVariables.Number))
        {
            errors.Add(new ConfigurationValidationError(
                nameof(PhotoCopyConfig.DuplicatesFormat),
                $"Duplicates format must contain the {DestinationVariables.Number} variable."));
        }
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string GetDestinationBasePath(string destination)
    {
        // Find the first variable in the pattern and get the base path before it
        var variableIndex = destination.IndexOf('{');
        if (variableIndex > 0)
        {
            var basePath = destination[..variableIndex].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return NormalizePath(basePath);
        }
        return NormalizePath(destination);
    }
}
