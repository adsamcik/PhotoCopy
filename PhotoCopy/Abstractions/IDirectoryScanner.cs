using System.Collections.Generic;
using System.Threading;
using PhotoCopy.Files;

namespace PhotoCopy.Abstractions;

public interface IDirectoryScanner
{
    IEnumerable<IFile> EnumerateFiles(string path, CancellationToken cancellationToken = default);
}