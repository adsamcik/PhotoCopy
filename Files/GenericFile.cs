using PhotoCopySort;
using System;
using System.IO;

namespace PhotoCopy.Files
{
    class GenericFile : IFile
    {
        public FileInfo File { get; }

        public FileDateTime FileDateTime { get; }

        public GenericFile(FileInfo file, FileDateTime dateTime)
        {
            File = file ?? throw new ArgumentNullException(nameof(file));
            FileDateTime = dateTime;
        }


        public virtual void CopyTo(string newPath, bool isDryRun)
        {
            Log.Print($"{File.FullName} >> cp >> {newPath}", LogLevel.Verbose);
            if (!isDryRun)
            {
                File.CopyTo(newPath);
            }
        }

        public virtual void MoveTo(string newPath, bool isDryRun)
        {
            Log.Print($"{File.FullName} >> mv >> {newPath}", LogLevel.Verbose);
            if (!isDryRun)
            {
                File.MoveTo(newPath);
            }
        }
    }
}
