using PhotoCopy.Files;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PhotoCopy.Abstractions;

internal interface IFileSystem
{
    IEnumerable<IFile> EnumerateFiles(string source, Options options);
    void CreateDirectory(string directoryPath);
    bool DirectoryExists(string directoryPath);
}
