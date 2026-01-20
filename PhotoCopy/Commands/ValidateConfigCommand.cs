using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;

namespace PhotoCopy.Commands;

/// <summary>
/// Command for validating configuration settings before running operations.
/// </summary>
public class ValidateConfigCommand : ICommand
{
    private readonly ILogger<ValidateConfigCommand> _logger;
    private readonly PhotoCopyConfig _config;
    private readonly IFileSystem _fileSystem;
    private readonly List<ValidationIssue> _errors = new();
    private readonly List<ValidationIssue> _warnings = new();

    /// <summary>
    /// All known destination variables (without braces).
    /// </summary>
    private static readonly HashSet<string> KnownVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "year", "month", "day", "name", "namenoext", "ext", "directory", "number",
        "district", "city", "county", "state", "country", "camera"
    };

    /// <summary>
    /// Variables that support inline fallback syntax like {var:fallback} or conditional syntax.
    /// </summary>
    private static readonly HashSet<string> LocationVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "district", "city", "county", "state", "country", "camera"
    };

    /// <summary>
    /// Regex to match variables in destination pattern including inline fallbacks and conditionals.
    /// Matches: {var}, {var:fallback}, {var?then:else}, etc.
    /// </summary>
    private static readonly Regex VariablePattern = new(
        @"\{([a-zA-Z]+)(?:[?:].*?)?\}",
        RegexOptions.Compiled);

    public ValidateConfigCommand(
        ILogger<ValidateConfigCommand> logger,
        IOptions<PhotoCopyConfig> options,
        IFileSystem fileSystem)
    {
        _logger = logger;
        _config = options.Value;
        _fileSystem = fileSystem;
    }

    public Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Validating configuration...");

        ValidateSourcePath();
        ValidateDestinationPath();
        ValidateDestinationPattern();
        ValidateConflictingOptions();
        ValidateExcludePatterns();
        ValidateParallelism();
        ValidateMaxDepth();
        ValidateDateRange();
        ValidateDuplicatesFormat();

        // Print results
        PrintValidationResults();

        // Return appropriate exit code
        if (_errors.Count > 0)
        {
            return Task.FromResult((int)ExitCode.Error);
        }

        return Task.FromResult((int)ExitCode.Success);
    }

    private void ValidateSourcePath()
    {
        if (string.IsNullOrWhiteSpace(_config.Source))
        {
            // Source is optional for config validation - just a warning
            _warnings.Add(new ValidationIssue(
                ValidationCategory.Source,
                "Source path is not specified",
                "Configuration requires a source path when running copy/scan operations"));
            return;
        }

        try
        {
            var sourcePath = Path.GetFullPath(_config.Source);
            if (!_fileSystem.DirectoryExists(sourcePath))
            {
                _errors.Add(new ValidationIssue(
                    ValidationCategory.Source,
                    $"Source path does not exist: {_config.Source}",
                    "Ensure the source directory exists before running operations"));
            }
        }
        catch (Exception ex)
        {
            _errors.Add(new ValidationIssue(
                ValidationCategory.Source,
                $"Invalid source path: {_config.Source}",
                ex.Message));
        }
    }

    private void ValidateDestinationPath()
    {
        if (string.IsNullOrWhiteSpace(_config.Destination))
        {
            _warnings.Add(new ValidationIssue(
                ValidationCategory.Destination,
                "Destination path is not specified",
                "Configuration requires a destination path when running copy operations"));
            return;
        }

        try
        {
            // Extract the static part of the path (before any variables)
            var destination = _config.Destination;
            var firstVariableIndex = destination.IndexOf('{');
            var staticPart = firstVariableIndex > 0 
                ? destination.Substring(0, firstVariableIndex) 
                : destination;

            // Get the parent directory of the static part
            if (!string.IsNullOrWhiteSpace(staticPart))
            {
                var parentPath = Path.GetDirectoryName(Path.GetFullPath(staticPart.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                
                if (!string.IsNullOrEmpty(parentPath) && !_fileSystem.DirectoryExists(parentPath))
                {
                    // Check if it can be created (parent of parent exists)
                    var grandparent = Path.GetDirectoryName(parentPath);
                    if (!string.IsNullOrEmpty(grandparent) && !_fileSystem.DirectoryExists(grandparent))
                    {
                        _warnings.Add(new ValidationIssue(
                            ValidationCategory.Destination,
                            $"Destination parent directory does not exist: {parentPath}",
                            "The directory will be created when running operations"));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _errors.Add(new ValidationIssue(
                ValidationCategory.Destination,
                $"Invalid destination path: {_config.Destination}",
                ex.Message));
        }
    }

    private void ValidateDestinationPattern()
    {
        if (string.IsNullOrWhiteSpace(_config.Destination))
        {
            return;
        }

        var matches = VariablePattern.Matches(_config.Destination);
        foreach (Match match in matches)
        {
            var variableName = match.Groups[1].Value;
            
            if (!KnownVariables.Contains(variableName))
            {
                var suggestion = FindSimilarVariable(variableName);
                var message = suggestion != null
                    ? $"Unknown variable in destination: {{{variableName}}} (did you mean {{{suggestion}}}?)"
                    : $"Unknown variable in destination: {{{variableName}}}";

                _errors.Add(new ValidationIssue(
                    ValidationCategory.DestinationPattern,
                    message,
                    $"Valid variables are: {string.Join(", ", KnownVariables.OrderBy(v => v).Select(v => "{" + v + "}"))}"));
            }
        }

        // Check for unclosed braces
        var openBraces = _config.Destination.Count(c => c == '{');
        var closeBraces = _config.Destination.Count(c => c == '}');
        
        if (openBraces != closeBraces)
        {
            _errors.Add(new ValidationIssue(
                ValidationCategory.DestinationPattern,
                "Destination pattern has unbalanced braces",
                $"Found {openBraces} opening braces and {closeBraces} closing braces"));
        }
    }

    private void ValidateConflictingOptions()
    {
        // --skip-existing and --overwrite are mutually exclusive
        if (_config.SkipExisting && _config.Overwrite)
        {
            _errors.Add(new ValidationIssue(
                ValidationCategory.ConflictingOptions,
                "Conflicting options: --skip-existing and --overwrite cannot be used together",
                "Use --skip-existing to skip existing files, or --overwrite to replace them"));
        }
    }

    private void ValidateExcludePatterns()
    {
        if (_config.ExcludePatterns == null || _config.ExcludePatterns.Count == 0)
        {
            return;
        }

        foreach (var pattern in _config.ExcludePatterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                _warnings.Add(new ValidationIssue(
                    ValidationCategory.ExcludePatterns,
                    "Empty exclude pattern found",
                    "Remove empty patterns from the configuration"));
                continue;
            }

            // Validate glob pattern by trying to compile it
            try
            {
                var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
                matcher.AddInclude(pattern);
                
                // Try to match against a sample path to verify the pattern compiles correctly
                matcher.Match(".", "test.jpg");
            }
            catch (Exception ex)
            {
                _errors.Add(new ValidationIssue(
                    ValidationCategory.ExcludePatterns,
                    $"Invalid exclude pattern: {pattern}",
                    ex.Message));
            }
        }
    }

    private void ValidateParallelism()
    {
        if (_config.Parallelism <= 0)
        {
            _errors.Add(new ValidationIssue(
                ValidationCategory.Parallelism,
                $"Invalid parallelism value: {_config.Parallelism}",
                "Parallelism must be a positive integer"));
        }
        else if (_config.Parallelism > Environment.ProcessorCount * 4)
        {
            _warnings.Add(new ValidationIssue(
                ValidationCategory.Parallelism,
                $"High parallelism value: {_config.Parallelism}",
                $"Consider using a value closer to processor count ({Environment.ProcessorCount}) for optimal performance"));
        }
    }

    private void ValidateMaxDepth()
    {
        if (_config.MaxDepth.HasValue && _config.MaxDepth.Value < 0)
        {
            _warnings.Add(new ValidationIssue(
                ValidationCategory.MaxDepth,
                $"Negative MaxDepth value: {_config.MaxDepth}",
                "Negative values are treated as unlimited. Use null or 0 for unlimited depth."));
        }
    }

    private void ValidateDateRange()
    {
        if (_config.MinDate.HasValue && _config.MaxDate.HasValue)
        {
            if (_config.MinDate.Value > _config.MaxDate.Value)
            {
                _errors.Add(new ValidationIssue(
                    ValidationCategory.DateRange,
                    $"Invalid date range: MinDate ({_config.MinDate:yyyy-MM-dd}) is after MaxDate ({_config.MaxDate:yyyy-MM-dd})",
                    "Ensure MinDate is before or equal to MaxDate"));
            }
        }
    }

    private void ValidateDuplicatesFormat()
    {
        if (string.IsNullOrWhiteSpace(_config.DuplicatesFormat))
        {
            return;
        }

        if (!_config.DuplicatesFormat.Contains(DestinationVariables.Number))
        {
            _errors.Add(new ValidationIssue(
                ValidationCategory.DuplicatesFormat,
                "Duplicates format does not contain {number}",
                "The duplicates format must include {number} to differentiate duplicate files"));
        }
    }

    private string? FindSimilarVariable(string input)
    {
        input = input.ToLowerInvariant();
        
        foreach (var known in KnownVariables)
        {
            // Check if it's a typo (edit distance <= 2)
            if (LevenshteinDistance(input, known) <= 2)
            {
                return known;
            }
        }

        return null;
    }

    private static int LevenshteinDistance(string s1, string s2)
    {
        var n = s1.Length;
        var m = s2.Length;
        var d = new int[n + 1, m + 1];

        if (n == 0) return m;
        if (m == 0) return n;

        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = s2[j - 1] == s1[i - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }

    private void PrintValidationResults()
    {
        Console.WriteLine();

        if (_errors.Count == 0 && _warnings.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ Configuration is valid");
            Console.ResetColor();
            return;
        }

        // Print errors
        foreach (var error in _errors)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("✗ ");
            Console.ResetColor();
            Console.WriteLine(error.Message);
            
            if (!string.IsNullOrEmpty(error.Suggestion))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  Hint: {error.Suggestion}");
                Console.ResetColor();
            }
        }

        // Print warnings
        foreach (var warning in _warnings)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("⚠ ");
            Console.ResetColor();
            Console.WriteLine(warning.Message);
            
            if (!string.IsNullOrEmpty(warning.Suggestion))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  Hint: {warning.Suggestion}");
                Console.ResetColor();
            }
        }

        Console.WriteLine();

        // Print summary
        if (_errors.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Found {_errors.Count} error(s)" + (_warnings.Count > 0 ? $" and {_warnings.Count} warning(s)" : ""));
            Console.ResetColor();
        }
        else if (_warnings.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ Configuration is valid");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  with {_warnings.Count} warning(s)");
            Console.ResetColor();
        }
    }

    /// <summary>
    /// Gets the validation errors for testing purposes.
    /// </summary>
    internal IReadOnlyList<ValidationIssue> Errors => _errors.AsReadOnly();

    /// <summary>
    /// Gets the validation warnings for testing purposes.
    /// </summary>
    internal IReadOnlyList<ValidationIssue> Warnings => _warnings.AsReadOnly();
}

/// <summary>
/// Categories of validation issues.
/// </summary>
public enum ValidationCategory
{
    Source,
    Destination,
    DestinationPattern,
    ConflictingOptions,
    ExcludePatterns,
    Parallelism,
    MaxDepth,
    DateRange,
    DuplicatesFormat
}

/// <summary>
/// Represents a validation issue found during configuration validation.
/// </summary>
public class ValidationIssue
{
    public ValidationCategory Category { get; }
    public string Message { get; }
    public string? Suggestion { get; }

    public ValidationIssue(ValidationCategory category, string message, string? suggestion = null)
    {
        Category = category;
        Message = message;
        Suggestion = suggestion;
    }
}
