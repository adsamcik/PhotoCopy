﻿using System.Collections.Generic;
using System.IO;

namespace PhotoCopy.Files
{
    class PhotoFile : GenericFile
    {
        public IReadOnlyList<RelatedFile> RelatedFileList => _relatedFileList;

        private readonly List<RelatedFile> _relatedFileList = new List<RelatedFile>();

        public PhotoFile(FileInfo file, FileDateTime dateTime) : base(file, dateTime)
        {
        }

        public void AddRelatedFiles(List<IFile> fileList)
        {
            for (int i = 0; i < fileList.Count; i++)
            {
                var file = fileList[i].File;
                if (file.FullName.StartsWith(File.FullName))
                {
                    Log.Print($"Found related file {file.FullName} to file {File.FullName}", PhotoCopySort.LogLevel.Verbose);
                    _relatedFileList.Add(new RelatedFile(file, FileDateTime, file.FullName.Remove(0, File.FullName.Length)));
                    fileList.RemoveAt(i);
                    i--;
                }
            }
        }

        public override void CopyTo(string newPath, bool isDryRun)
        {
            base.CopyTo(newPath, isDryRun);
            foreach (var relatedFile in _relatedFileList)
            {
                relatedFile.CopyTo(newPath, isDryRun);
            }
        }

        public override void MoveTo(string newPath, bool isDryRun)
        {
            base.MoveTo(newPath, isDryRun);
            foreach (var relatedFile in _relatedFileList)
            {
                relatedFile.MoveTo(newPath, isDryRun);
            }
        }

        public class RelatedFile : GenericFile
        {
            public string Extension { get; }

            public RelatedFile(FileInfo file, FileDateTime dateTime, string extension) : base(file, dateTime)
            {
                Extension = extension;
            }

            public override void CopyTo(string newPath, bool isDryRun)
            {
                base.CopyTo(newPath + Extension, isDryRun);
            }

            public override void MoveTo(string newPath, bool isDryRun)
            {
                base.MoveTo(newPath + Extension, isDryRun);
            }
        }
    }
}
