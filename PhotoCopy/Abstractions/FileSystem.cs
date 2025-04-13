using PhotoCopy.Files;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PhotoCopy.Abstractions;

internal class FileSystem : IFileSystem
{
    public IEnumerable<IFile> EnumerateFiles(string source, Options options)
    {
        return DirectoryScanner.EnumerateFiles(source, options);
    }

    public void CreateDirectory(string directoryPath)
    {
        System.IO.Directory.CreateDirectory(directoryPath);
    }

    public bool DirectoryExists(string directoryPath)
    {
        return System.IO.Directory.Exists(directoryPath);
    }
}