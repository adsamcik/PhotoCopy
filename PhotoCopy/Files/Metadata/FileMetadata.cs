using System.IO;

namespace PhotoCopy.Files.Metadata;

public sealed class FileMetadata
{
    public FileMetadata(FileDateTime dateTime)
    {
        DateTime = dateTime;
    }

    public FileDateTime DateTime { get; set; }

    public LocationData? Location { get; set; }

    public string? Checksum { get; set; }

    /// <summary>
    /// Gets or sets the reason why this file has no location data.
    /// Set to <see cref="UnknownFileReason.None"/> if location data is available.
    /// </summary>
    public UnknownFileReason UnknownReason { get; set; } = UnknownFileReason.None;
    
    /// <summary>
    /// Gets or sets the camera make and model extracted from EXIF metadata.
    /// For example, "Apple iPhone 15 Pro" or "Canon EOS R5".
    /// Null if camera information is not available.
    /// </summary>
    public string? Camera { get; set; }
}

public sealed class FileMetadataContext
{
    public FileMetadataContext(FileInfo fileInfo)
    {
        FileInfo = fileInfo;
        Metadata = new FileMetadata(new FileDateTime(fileInfo.CreationTime, DateTimeSource.FileCreation));
    }

    public FileInfo FileInfo { get; }

    public FileMetadata Metadata { get; }
    
    /// <summary>
    /// Gets or sets the raw GPS coordinates extracted from the file.
    /// Used by enrichment steps to share coordinate data and populate the GPS index.
    /// </summary>
    public (double Latitude, double Longitude)? Coordinates { get; set; }
}