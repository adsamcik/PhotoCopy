using System.IO;

namespace PhotoCopy.Files;

public interface IFile
{
    FileInfo File { get; }
    FileDateTime FileDateTime { get; }
    LocationData? Location { get; }
    string Checksum { get; }
}