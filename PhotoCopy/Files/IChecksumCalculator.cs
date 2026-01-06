using System.IO;

namespace PhotoCopy.Files;

public interface IChecksumCalculator
{
    string Calculate(FileInfo fileInfo);
}