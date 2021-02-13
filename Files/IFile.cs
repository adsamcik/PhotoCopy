﻿using System.IO;

namespace PhotoCopy.Files
{
    interface IFile
    {
        public FileInfo File { get; }

        public FileDateTime FileDateTime { get; }

        public void CopyTo(string newPath, bool isDryRun);

        public void MoveTo(string newPath, bool isDryRun);
    }
}