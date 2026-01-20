namespace PhotoCopy.Files.Sidecar;

/// <summary>
/// Interface for sidecar file parsers.
/// </summary>
public interface ISidecarParser
{
    /// <summary>
    /// Checks if this parser can handle the given file extension.
    /// </summary>
    /// <param name="extension">The file extension including the leading dot (e.g., ".json").</param>
    /// <returns>True if this parser can handle the extension.</returns>
    bool CanParse(string extension);

    /// <summary>
    /// Parses the sidecar file and extracts metadata.
    /// </summary>
    /// <param name="filePath">The path to the sidecar file.</param>
    /// <returns>Extracted metadata, or null if parsing fails.</returns>
    SidecarMetadata? Parse(string filePath);
}
