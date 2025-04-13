using System.IO;

namespace PhotoCopy.Files;

/// <summary>
/// Represents a file.
/// </summary>
internal interface IFile
{
    /// <summary>
    /// Gets the file information.
    /// </summary>
    public FileInfo File { get; }

    /// <summary>
    /// Gets the date and time of the file.
    /// </summary>
    public FileDateTime FileDateTime { get; }

    /// <summary>
    /// Gets the checksum of the file.
    /// </summary>
    public string Checksum { get; }

    /// <summary>
    /// Copies the file to a new location.
    /// </summary>
    /// <param name="newPath">The path to copy the file to.</param>
    /// <param name="isDryRun">Whether or not this is a dry run.</param>
    public void CopyTo(string newPath, bool isDryRun);

    /// <summary>
    /// Moves the file to a new location.
    /// </summary>
    /// <param name="newPath">The path to move the file to.</param>
    /// <param name="isDryRun">Whether or not this is a dry run.</param>
    public void MoveTo(string newPath, bool isDryRun);
}