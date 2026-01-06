using System.Collections.Generic;
using PhotoCopy.Files;

namespace PhotoCopy.Abstractions;

public interface IDirectoryScanner
{
    IEnumerable<IFile> EnumerateFiles(string path);
}