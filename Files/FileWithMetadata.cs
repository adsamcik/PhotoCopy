using System.Collections.Generic;
using System.IO;

namespace PhotoCopy.Files;

internal record class FileWithMetadata(FileInfo File, FileDateTime DateTime) : GenericFile(File, DateTime)
{
    public IReadOnlyList<RelatedFile> RelatedFileList => _relatedFileList;

    private readonly List<RelatedFile> _relatedFileList = [];

    public void AddRelatedFiles(List<IFile> fileList, Options.RelatedFileLookup mode)
    {
        if(mode == Options.RelatedFileLookup.none)
        {
            return;
        }

        for (var i = 0; i < fileList.Count; i++)
        {
            var file = fileList[i].File;
            var found = false;

            switch (mode)
            {
                case Options.RelatedFileLookup.strict:
                    found = file.FullName.StartsWith(File.FullName);
                    break;
                case Options.RelatedFileLookup.loose:
                    found = file.FullName.StartsWith(File.Name);
                    break;
            }

            if (found)
            {
                Log.Print($"Found related file {file.FullName} to file {File.FullName}", Options.LogLevel.verbose);
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

    public record class RelatedFile(FileInfo File, FileDateTime DateTime, string Extension) : GenericFile(File, DateTime)
    {
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