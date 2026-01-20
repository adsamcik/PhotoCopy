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
    
    /// <summary>
    /// Gets the camera make and model extracted from EXIF metadata.
    /// For example, "Apple iPhone 15 Pro" or "Canon EOS R5".
    /// Returns null if camera information is not available.
    /// </summary>
    string? Camera { get; }
}