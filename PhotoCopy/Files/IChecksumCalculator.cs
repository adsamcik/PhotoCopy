using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoCopy.Files;

public interface IChecksumCalculator
{
    string Calculate(FileInfo fileInfo);
    
    /// <summary>
    /// Asynchronously calculates the checksum of a file.
    /// </summary>
    Task<string> CalculateAsync(FileInfo fileInfo, CancellationToken cancellationToken = default);
}