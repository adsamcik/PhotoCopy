using System.IO;

namespace PhotoCopy.Files;

public interface IFileFactory
{
    IFile Create(FileInfo fileInfo);
}
