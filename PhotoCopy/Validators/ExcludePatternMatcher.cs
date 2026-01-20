using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.FileSystemGlobbing;
using PhotoCopy.Files;

namespace PhotoCopy.Validators;

/// <summary>
/// Validator that excludes files matching specified glob patterns.
/// Patterns are matched against relative file paths from the source root.
/// </summary>
public class ExcludePatternMatcher : IValidator
{
    private readonly Matcher _matcher;
    private readonly string _sourceRoot;
    private readonly IReadOnlyList<string> _patterns;

    public string Name => nameof(ExcludePatternMatcher);

    /// <summary>
    /// Creates a new ExcludePatternMatcher with the specified patterns.
    /// </summary>
    /// <param name="patterns">Glob patterns to match against files (e.g., "*.aae", "*_thumb*", ".trashed-*").</param>
    /// <param name="sourceRoot">The root directory to calculate relative paths from.</param>
    public ExcludePatternMatcher(IEnumerable<string> patterns, string sourceRoot)
    {
        _patterns = patterns.ToList();
        _sourceRoot = NormalizePath(sourceRoot);
        _matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        
        foreach (var pattern in _patterns)
        {
            // Add pattern - the Matcher will handle both simple patterns like "*.aae"
            // and path patterns like "**/*.aae"
            _matcher.AddInclude(pattern);
        }
    }

    /// <summary>
    /// Validates if the file should be included (not matched by any exclude pattern).
    /// </summary>
    /// <param name="file">The file to validate.</param>
    /// <returns>Success if file should be processed, Fail if it matches an exclude pattern.</returns>
    public ValidationResult Validate(IFile file)
    {
        if (_patterns.Count == 0)
        {
            return ValidationResult.Success(Name);
        }

        var filePath = file.File.FullName;
        var relativePath = GetRelativePath(filePath);
        
        // Check if the file matches any exclude pattern
        var result = _matcher.Match(_sourceRoot, relativePath);
        
        if (result.HasMatches)
        {
            var matchedPattern = FindMatchingPattern(relativePath);
            var reason = $"File matches exclude pattern '{matchedPattern}'";
            return ValidationResult.Fail(Name, reason);
        }

        return ValidationResult.Success(Name);
    }

    /// <summary>
    /// Gets the relative path from the source root to the file.
    /// </summary>
    private string GetRelativePath(string fullPath)
    {
        var normalizedFullPath = NormalizePath(fullPath);
        
        if (normalizedFullPath.StartsWith(_sourceRoot, StringComparison.OrdinalIgnoreCase))
        {
            var relativePath = normalizedFullPath.Substring(_sourceRoot.Length);
            return relativePath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        // If the path is not under source root, just use the filename
        return Path.GetFileName(fullPath);
    }

    /// <summary>
    /// Finds which pattern matched the file (for error message).
    /// </summary>
    private string FindMatchingPattern(string relativePath)
    {
        foreach (var pattern in _patterns)
        {
            var singleMatcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            singleMatcher.AddInclude(pattern);
            
            if (singleMatcher.Match(_sourceRoot, relativePath).HasMatches)
            {
                return pattern;
            }
        }

        return _patterns.FirstOrDefault() ?? "unknown";
    }

    /// <summary>
    /// Normalizes a path for consistent comparison.
    /// </summary>
    private static string NormalizePath(string path)
    {
        // Ensure consistent path separators
        return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                   .TrimEnd(Path.DirectorySeparatorChar);
    }
}
