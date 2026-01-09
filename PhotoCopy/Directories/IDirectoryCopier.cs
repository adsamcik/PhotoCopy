using System.Collections.Generic;
using PhotoCopy.Abstractions;
using PhotoCopy.Files;
using PhotoCopy.Validators;

namespace PhotoCopy.Directories;

public interface IDirectoryCopier
{
    CopyResult Copy(IReadOnlyCollection<IValidator> validators);
    string GeneratePath(IFile file);
}