using System.IO;

namespace PhotoCopy.Files;

public interface IFileMetadataExtractor
{
    FileDateTime GetDateTime(FileInfo fileInfo);
    (double Latitude, double Longitude)? GetCoordinates(FileInfo fileInfo);
    
    /// <summary>
    /// Extracts the camera make and model from EXIF metadata.
    /// </summary>
    /// <param name="fileInfo">The file to extract camera info from.</param>
    /// <returns>Camera make and model (e.g., "Apple iPhone 15 Pro"), or null if not available.</returns>
    string? GetCamera(FileInfo fileInfo);
}