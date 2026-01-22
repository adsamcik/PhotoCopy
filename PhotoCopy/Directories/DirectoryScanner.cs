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
    private readonly ILivePhotoEnricher? _livePhotoEnricher;
    private readonly ICompanionGpsEnricher? _companionGpsEnricher;

    public DirectoryScanner(
        ILogger<DirectoryScanner> logger,
        IOptions<PhotoCopyConfig> config,
        IFileFactory fileFactory,
        ILivePhotoEnricher? livePhotoEnricher = null,
        ICompanionGpsEnricher? companionGpsEnricher = null)
    {
        _logger = logger;
        _config = config.Value;
        _fileFactory = fileFactory;
        _livePhotoEnricher = livePhotoEnricher;
        _companionGpsEnricher = companionGpsEnricher;
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
            // Use safe enumeration that handles UnauthorizedAccessException for inaccessible directories
            filePaths = EnumerateFilesRecursiveSafe(path, cancellationToken);
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

        // First pass: Live Photo enrichment - pair .heic photos with companion .mov videos
        // This should run before companion GPS enrichment as it's more specific/accurate
        if (_livePhotoEnricher != null && _livePhotoEnricher.IsEnabled)
        {
            _logger.LogInformation("Running Live Photo enrichment pass...");
            _livePhotoEnricher.EnrichFiles(files);
        }

        // Second pass: Companion GPS enrichment for files without GPS data
        // This runs after all files have been scanned so the GPS index is fully populated
        if (_companionGpsEnricher != null && _companionGpsEnricher.IsEnabled)
        {
            _logger.LogInformation("Running companion GPS enrichment pass...");
            _companionGpsEnricher.EnrichFiles(files);
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

    /// <summary>
    /// Enumerates files recursively with no depth limit, handling UnauthorizedAccessException gracefully.
    /// </summary>
    /// <param name="rootPath">The root directory to start enumeration from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Enumerable of file paths.</returns>
    private IEnumerable<string> EnumerateFilesRecursiveSafe(string rootPath, CancellationToken cancellationToken)
    {
        var directoriesToProcess = new Queue<string>();
        directoriesToProcess.Enqueue(rootPath);

        while (directoriesToProcess.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentPath = directoriesToProcess.Dequeue();

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

            // Enumerate subdirectories for recursive processing
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
                directoriesToProcess.Enqueue(subdir);
            }
        }
    }
}
