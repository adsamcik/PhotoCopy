namespace PhotoCopy.Configuration;

public enum OperationMode
{
    None,
    Copy,
    Move
}

public enum OutputLevel
{
    Verbose,
    Important,
    ErrorsOnly
}

public enum RelatedFileLookup
{
    None,
    Strict,
    Loose
}

/// <summary>
/// Specifies the casing style for destination path variable values.
/// </summary>
public enum PathCasing
{
    /// <summary>
    /// Keep the original casing from the data source.
    /// Example: "New York" stays as "New York"
    /// </summary>
    Original,
    
    /// <summary>
    /// Convert to all lowercase.
    /// Example: "New York" → "new york"
    /// </summary>
    Lowercase,
    
    /// <summary>
    /// Convert to all uppercase.
    /// Example: "New York" → "NEW YORK"
    /// </summary>
    Uppercase,
    
    /// <summary>
    /// Capitalize the first letter of each word.
    /// Example: "new york" → "New York"
    /// </summary>
    TitleCase,
    
    /// <summary>
    /// Capitalize first letter of each word, no spaces.
    /// Example: "New York" → "NewYork"
    /// </summary>
    PascalCase,
    
    /// <summary>
    /// First word lowercase, subsequent words capitalized, no spaces.
    /// Example: "New York" → "newYork"
    /// </summary>
    CamelCase,
    
    /// <summary>
    /// Lowercase words separated by underscores.
    /// Example: "New York" → "new_york"
    /// </summary>
    SnakeCase,
    
    /// <summary>
    /// Lowercase words separated by hyphens.
    /// Example: "New York" → "new-york"
    /// </summary>
    KebabCase,
    
    /// <summary>
    /// Uppercase words separated by underscores.
    /// Example: "New York" → "NEW_YORK"
    /// </summary>
    ScreamingSnakeCase
}
