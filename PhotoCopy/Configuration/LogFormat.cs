namespace PhotoCopy.Configuration;

/// <summary>
/// Specifies the output format for log messages.
/// </summary>
public enum LogFormat
{
    /// <summary>
    /// Human-readable text format (default).
    /// </summary>
    Text,

    /// <summary>
    /// Structured JSON format for machine parsing and log aggregation.
    /// </summary>
    Json
}
