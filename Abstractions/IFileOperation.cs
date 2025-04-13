using PhotoCopy.Files;

namespace PhotoCopy.Abstractions;

internal interface IFileOperation
{
    void MoveFile(IFile file, string destination, bool dryRun);
    void CopyFile(IFile file, string destination, bool dryRun);
}
