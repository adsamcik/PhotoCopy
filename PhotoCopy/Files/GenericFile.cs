using System;
using System.IO;
using System.Security.Cryptography;

namespace PhotoCopy.Files;

public class GenericFile : IFile
{
    private string _checksum;

    public FileInfo File { get; }
    public FileDateTime FileDateTime { get; }
    public LocationData? Location => null;
    public string Checksum => CalculateChecksum();

    public GenericFile(FileInfo file, FileDateTime fileDateTime, string? checksum = null)
    {
        File = file;
        FileDateTime = fileDateTime;
        _checksum = checksum ?? string.Empty;
    }

    public string CalculateChecksum()
    {
        if (!string.IsNullOrEmpty(_checksum))
        {
            return _checksum;
        }

        using var sha256 = SHA256.Create();
        using var stream = System.IO.File.OpenRead(File.FullName);
        var hash = sha256.ComputeHash(stream);

        _checksum = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        return _checksum;
    }
}
