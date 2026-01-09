using System.Collections.Generic;
using System.IO;
using System.Threading;
using PhotoCopy.Files;

namespace PhotoCopy.Abstractions;

public interface IFileSystem
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    void CreateDirectory(string path);
    void CopyFile(string source, string destination, bool overwrite = false);
    void MoveFile(string source, string destination);
    FileInfo GetFileInfo(string path);
    DirectoryInfo GetDirectoryInfo(string path);
    IEnumerable<IFile> EnumerateFiles(string directory, CancellationToken cancellationToken = default);
    string GetCurrentDirectory();
}
