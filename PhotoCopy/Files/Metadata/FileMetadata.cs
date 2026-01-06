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
}