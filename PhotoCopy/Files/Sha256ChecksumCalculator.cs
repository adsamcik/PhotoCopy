using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

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

    public async Task<string> CalculateAsync(FileInfo fileInfo, CancellationToken cancellationToken = default)
    {
        using var sha256 = SHA256.Create();
        await using var stream = new FileStream(
            fileInfo.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}