using System;
using System.IO;
using System.Security.Cryptography;

namespace PhotoCopy.Files;

public class Sha256ChecksumCalculator : IChecksumCalculator
{
    public string Calculate(FileInfo fileInfo)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(fileInfo.FullName);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}