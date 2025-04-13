using PhotoCopy.Files;

namespace PhotoCopy.Abstractions;

internal class FileOperation : IFileOperation
{
    public void MoveFile(IFile file, string destination, bool dryRun)
    {
        file.MoveTo(destination, dryRun);
    }

    public void CopyFile(IFile file, string destination, bool dryRun)
    {
        file.CopyTo(destination, dryRun);
    }
}