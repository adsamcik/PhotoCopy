using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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

    public IEnumerable<IFile> EnumerateFiles(string path, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(path))
        {
            _logger.LogError("Directory {Path} does not exist", path);
            return new List<IFile>();
        }

        _logger.LogInformation("Starting file enumeration from {Path}...", path);
        
        var files = new List<IFile>();
        var filesFound = 0;
        var lastLogTime = DateTime.UtcNow;
        
        // Use optimized enumeration when MaxDepth is unlimited (null, 0, or negative)
        var maxDepth = _config.MaxDepth;
        var isUnlimited = !maxDepth.HasValue || maxDepth.Value <= 0;
        
        IEnumerable<string> filePaths;
        if (isUnlimited)
        {
            filePaths = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories);
        }
        else
        {
            // maxDepth is guaranteed to have a value here since isUnlimited is false
            filePaths = EnumerateFilesWithDepthLimit(path, maxDepth!.Value, cancellationToken);
        }
        
        foreach (var file in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            filesFound++;
            
            // Log progress every 5 seconds
            var now = DateTime.UtcNow;
            if ((now - lastLogTime).TotalSeconds >= 5)
            {
                _logger.LogInformation("File scanning progress: {FilesFound} files found, extracting metadata...", filesFound);
                lastLogTime = now;
            }
            
            var fileInfo = new FileInfo(file);
            _logger.LogTrace("Processing file: {FileName}", fileInfo.Name);
            var startTime = DateTime.UtcNow;
            var fileObj = _fileFactory.Create(fileInfo);
            var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
            if (elapsed > 2)
            {
                _logger.LogWarning("Slow metadata extraction ({Elapsed:F1}s) for: {FileName}", elapsed, fileInfo.Name);
            }
            files.Add(fileObj);
        }

        _logger.LogInformation("File enumeration complete: {FilesFound} files found.", filesFound);

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

    /// <summary>
    /// Enumerates files with a maximum depth limit.
    /// </summary>
    /// <param name="rootPath">The root directory to start enumeration from.</param>
    /// <param name="maxDepth">Maximum depth (1 = root only, 2 = root + 1 level, etc.).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Enumerable of file paths within the depth limit.</returns>
    private IEnumerable<string> EnumerateFilesWithDepthLimit(string rootPath, int maxDepth, CancellationToken cancellationToken)
    {
        var directoriesToProcess = new Queue<(string Path, int Depth)>();
        directoriesToProcess.Enqueue((rootPath, 1));

        while (directoriesToProcess.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var (currentPath, currentDepth) = directoriesToProcess.Dequeue();

            // Enumerate files in current directory
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(currentPath, "*.*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning("Access denied to directory {Path}: {Message}", currentPath, ex.Message);
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            // Only recurse into subdirectories if we haven't reached max depth
            if (currentDepth < maxDepth)
            {
                IEnumerable<string> subdirectories;
                try
                {
                    subdirectories = Directory.EnumerateDirectories(currentPath);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning("Access denied to enumerate subdirectories of {Path}: {Message}", currentPath, ex.Message);
                    continue;
                }

                foreach (var subdir in subdirectories)
                {
                    directoriesToProcess.Enqueue((subdir, currentDepth + 1));
                }
            }
        }
    }
}
