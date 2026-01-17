using System;
using System.Text;
using System.Text.RegularExpressions;
using PhotoCopy.Configuration;

namespace PhotoCopy.Extensions;

/// <summary>
/// Provides text casing transformation utilities for destination path variables.
/// </summary>
public static partial class CasingFormatter
{
    /// <summary>
    /// Applies the specified casing transformation to the input text.
    /// </summary>
    /// <param name="text">The text to transform.</param>
    /// <param name="casing">The casing style to apply.</param>
    /// <returns>The transformed text.</returns>
    public static string ApplyCasing(string? text, PathCasing casing)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        return casing switch
        {
            PathCasing.Original => text,
            PathCasing.Lowercase => text.ToLowerInvariant(),
            PathCasing.Uppercase => text.ToUpperInvariant(),
            PathCasing.TitleCase => ToTitleCase(text),
            PathCasing.PascalCase => ToPascalCase(text),
            PathCasing.CamelCase => ToCamelCase(text),
            PathCasing.SnakeCase => ToSnakeCase(text),
            PathCasing.KebabCase => ToKebabCase(text),
            PathCasing.ScreamingSnakeCase => ToScreamingSnakeCase(text),
            _ => text
        };
    }

    /// <summary>
    /// Converts text to Title Case (each word capitalized).
    /// Example: "new york city" → "New York City"
    /// </summary>
    public static string ToTitleCase(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var words = SplitIntoWords(text);
        var sb = new StringBuilder();
        
        foreach (var word in words)
        {
            if (sb.Length > 0)
                sb.Append(' ');
            
            if (word.Length > 0)
            {
                sb.Append(char.ToUpperInvariant(word[0]));
                if (word.Length > 1)
                    sb.Append(word[1..].ToLowerInvariant());
            }
        }
        
        return sb.ToString();
    }

    /// <summary>
    /// Converts text to PascalCase (each word capitalized, no separators).
    /// Example: "new york city" → "NewYorkCity"
    /// </summary>
    public static string ToPascalCase(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var words = SplitIntoWords(text);
        var sb = new StringBuilder();
        
        foreach (var word in words)
        {
            if (word.Length > 0)
            {
                sb.Append(char.ToUpperInvariant(word[0]));
                if (word.Length > 1)
                    sb.Append(word[1..].ToLowerInvariant());
            }
        }
        
        return sb.ToString();
    }

    /// <summary>
    /// Converts text to camelCase (first word lowercase, subsequent words capitalized).
    /// Example: "new york city" → "newYorkCity"
    /// </summary>
    public static string ToCamelCase(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var words = SplitIntoWords(text);
        var sb = new StringBuilder();
        var isFirst = true;
        
        foreach (var word in words)
        {
            if (word.Length > 0)
            {
                if (isFirst)
                {
                    sb.Append(word.ToLowerInvariant());
                    isFirst = false;
                }
                else
                {
                    sb.Append(char.ToUpperInvariant(word[0]));
                    if (word.Length > 1)
                        sb.Append(word[1..].ToLowerInvariant());
                }
            }
        }
        
        return sb.ToString();
    }

    /// <summary>
    /// Converts text to snake_case (lowercase words separated by underscores).
    /// Example: "New York City" → "new_york_city"
    /// </summary>
    public static string ToSnakeCase(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var words = SplitIntoWords(text);
        return string.Join("_", words).ToLowerInvariant();
    }

    /// <summary>
    /// Converts text to kebab-case (lowercase words separated by hyphens).
    /// Example: "New York City" → "new-york-city"
    /// </summary>
    public static string ToKebabCase(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var words = SplitIntoWords(text);
        return string.Join("-", words).ToLowerInvariant();
    }

    /// <summary>
    /// Converts text to SCREAMING_SNAKE_CASE (uppercase words separated by underscores).
    /// Example: "new york city" → "NEW_YORK_CITY"
    /// </summary>
    public static string ToScreamingSnakeCase(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var words = SplitIntoWords(text);
        return string.Join("_", words).ToUpperInvariant();
    }

    /// <summary>
    /// Splits text into words, handling various separators and casing patterns.
    /// </summary>
    private static string[] SplitIntoWords(string text)
    {
        // First, normalize common separators to spaces
        var normalized = text
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Replace('.', ' ');

        // Insert spaces before uppercase letters in camelCase/PascalCase
        normalized = CamelCaseSplitRegex().Replace(normalized, " $1");

        // Split on whitespace and filter empty entries
        return normalized.Split([' '], StringSplitOptions.RemoveEmptyEntries);
    }

    [GeneratedRegex(@"(?<!^)(?<![\s_\-])([A-Z][a-z])")]
    private static partial Regex CamelCaseSplitRegex();
}
