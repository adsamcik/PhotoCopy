using System.IO;

namespace PhotoCopy.Files;

public interface IFile
{
    FileInfo File { get; }
    FileDateTime FileDateTime { get; }
    LocationData? Location { get; }
    string Checksum { get; }
    
    /// <summary>
    /// Gets the reason why this file has no location data.
    /// Returns <see cref="UnknownFileReason.None"/> if location data is available.
    /// </summary>
    UnknownFileReason UnknownReason { get; }
}