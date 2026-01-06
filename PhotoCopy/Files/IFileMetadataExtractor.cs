using System.IO;

namespace PhotoCopy.Files;

public interface IFileMetadataExtractor
{
    FileDateTime GetDateTime(FileInfo fileInfo);
    (double Latitude, double Longitude)? GetCoordinates(FileInfo fileInfo);
}