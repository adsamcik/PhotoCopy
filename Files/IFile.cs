using System.IO;

namespace PhotoCopy.Files
{
    internal interface IFile
    {
        public FileInfo File { get; }

        public FileDateTime FileDateTime { get; }

        public string Checksum { get; }

        public void CopyTo(string newPath, bool isDryRun);

        public void MoveTo(string newPath, bool isDryRun);
    }
}