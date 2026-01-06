using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy.Files;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;

namespace PhotoCopy.Directories;

public class DirectoryScanner : IDirectoryScanner
{
    private readonly ILogger<DirectoryScanner> _logger;
    private readonly PhotoCopyConfig _config;
    private readonly IFileFactory _fileFactory;

    public DirectoryScanner(
        ILogger<DirectoryScanner> logger,
        IOptions<PhotoCopyConfig> config,
        IFileFactory fileFactory)
    {
        _logger = logger;
        _config = config.Value;
        _fileFactory = fileFactory;
    }

    public IEnumerable<IFile> EnumerateFiles(string path)
    {
        if (!Directory.Exists(path))
        {
            _logger.LogError("Directory {Path} does not exist", path);
            return new List<IFile>();
        }

        var files = new List<IFile>();
        foreach (var file in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
        {
            var fileInfo = new FileInfo(file);
            var fileObj = _fileFactory.Create(fileInfo);
            files.Add(fileObj);
        }

        // Group files by their directory
        var filesByDirectory = files.GroupBy(f => Path.GetDirectoryName(f.File.FullName));

        // For each directory
        foreach (var directoryGroup in filesByDirectory)
        {
            var allFilesInDir = directoryGroup.ToList();

            // Add related files to each photo
            foreach (var file in allFilesInDir)
            {
                if (file is FileWithMetadata metadata)
                {
                    metadata.AddRelatedFiles(allFilesInDir, _config.RelatedFileMode);
                }
            }
        }

        return files;
    }
}
