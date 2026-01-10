using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PhotoCopy.Abstractions;
using PhotoCopy.Files;

namespace PhotoCopy.Tests.TestingImplementation;

/// <summary>
/// An in-memory implementation of IFileSystem for testing purposes.
/// </summary>
public class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IFile> _iFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _fileCreationTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _fileLastWriteTimes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Normalizes a path for consistent storage and lookup.
    /// </summary>
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        // Normalize separators to backslash (Windows style) and remove trailing separators
        var normalized = path.Replace('/', '\\').TrimEnd('\\');
        
        // Handle drive letter casing
        if (normalized.Length >= 2 && normalized[1] == ':')
        {
            normalized = char.ToUpperInvariant(normalized[0]) + normalized[1..];
        }

        return normalized;
    }

    /// <summary>
    /// Gets the parent directory path from a given path.
    /// </summary>
    private static string? GetParentPath(string path)
    {
        var normalized = NormalizePath(path);
        var lastSeparator = normalized.LastIndexOf('\\');
        
        if (lastSeparator <= 0)
            return null;
        
        // Handle root paths like "C:\"
        if (lastSeparator == 2 && normalized[1] == ':')
            return normalized[..3];
            
        return normalized[..lastSeparator];
    }

    /// <summary>
    /// Ensures all parent directories exist for a given path.
    /// </summary>
    private void EnsureParentDirectoriesExist(string path)
    {
        var parent = GetParentPath(path);
        while (parent != null && !_directories.Contains(parent))
        {
            _directories.Add(parent);
            parent = GetParentPath(parent);
        }
    }

    /// <summary>
    /// Adds a file with the specified content to the in-memory file system.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="content">The content of the file as a byte array.</param>
    /// <param name="creationTime">Optional creation time for the file.</param>
    /// <param name="lastWriteTime">Optional last write time for the file.</param>
    public void AddFile(string path, byte[] content, DateTime? creationTime = null, DateTime? lastWriteTime = null)
    {
        var normalizedPath = NormalizePath(path);
        _files[normalizedPath] = content;
        _fileCreationTimes[normalizedPath] = creationTime ?? DateTime.Now;
        _fileLastWriteTimes[normalizedPath] = lastWriteTime ?? DateTime.Now;
        EnsureParentDirectoriesExist(normalizedPath);
    }

    /// <summary>
    /// Adds a file with string content to the in-memory file system.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="content">The content of the file as a string.</param>
    /// <param name="creationTime">Optional creation time for the file.</param>
    /// <param name="lastWriteTime">Optional last write time for the file.</param>
    public void AddFile(string path, string content, DateTime? creationTime = null, DateTime? lastWriteTime = null)
    {
        AddFile(path, System.Text.Encoding.UTF8.GetBytes(content), creationTime, lastWriteTime);
    }

    /// <summary>
    /// Adds an IFile instance for enumeration.
    /// </summary>
    /// <param name="file">The IFile instance to add.</param>
    public void AddIFile(IFile file)
    {
        var normalizedPath = NormalizePath(file.File.FullName);
        _iFiles[normalizedPath] = file;
        
        // Also ensure the file exists in the files dictionary if not already present
        if (!_files.ContainsKey(normalizedPath))
        {
            _files[normalizedPath] = Array.Empty<byte>();
            _fileCreationTimes[normalizedPath] = DateTime.Now;
            _fileLastWriteTimes[normalizedPath] = DateTime.Now;
        }
        
        EnsureParentDirectoriesExist(normalizedPath);
    }

    /// <summary>
    /// Gets the content of a file.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The file content as a byte array, or null if the file doesn't exist.</returns>
    public byte[]? GetFileContent(string path)
    {
        var normalizedPath = NormalizePath(path);
        return _files.TryGetValue(normalizedPath, out var content) ? content : null;
    }

    /// <summary>
    /// Gets all file paths in the in-memory file system.
    /// </summary>
    public IEnumerable<string> GetAllFilePaths() => _files.Keys;

    /// <summary>
    /// Gets all directory paths in the in-memory file system.
    /// </summary>
    public IEnumerable<string> GetAllDirectoryPaths() => _directories;

    /// <inheritdoc />
    public bool DirectoryExists(string path)
    {
        var normalizedPath = NormalizePath(path);
        return _directories.Contains(normalizedPath);
    }

    /// <inheritdoc />
    public bool FileExists(string path)
    {
        var normalizedPath = NormalizePath(path);
        return _files.ContainsKey(normalizedPath);
    }

    /// <inheritdoc />
    public void CreateDirectory(string path)
    {
        var normalizedPath = NormalizePath(path);
        _directories.Add(normalizedPath);
        EnsureParentDirectoriesExist(normalizedPath);
    }

    /// <inheritdoc />
    public void CopyFile(string source, string destination, bool overwrite = false)
    {
        var normalizedSource = NormalizePath(source);
        var normalizedDestination = NormalizePath(destination);

        if (!_files.TryGetValue(normalizedSource, out var content))
        {
            throw new FileNotFoundException($"Source file not found: {source}", source);
        }

        if (_files.ContainsKey(normalizedDestination) && !overwrite)
        {
            throw new IOException($"Destination file already exists: {destination}");
        }

        // Copy the content (create a new array to avoid sharing references)
        _files[normalizedDestination] = content.ToArray();
        _fileCreationTimes[normalizedDestination] = DateTime.Now;
        _fileLastWriteTimes[normalizedDestination] = DateTime.Now;
        
        // Copy IFile if it exists
        if (_iFiles.TryGetValue(normalizedSource, out var iFile))
        {
            _iFiles[normalizedDestination] = iFile;
        }
        
        EnsureParentDirectoriesExist(normalizedDestination);
    }

    /// <inheritdoc />
    public void MoveFile(string source, string destination)
    {
        var normalizedSource = NormalizePath(source);
        var normalizedDestination = NormalizePath(destination);

        if (!_files.TryGetValue(normalizedSource, out var content))
        {
            throw new FileNotFoundException($"Source file not found: {source}", source);
        }

        if (_files.ContainsKey(normalizedDestination))
        {
            throw new IOException($"Destination file already exists: {destination}");
        }

        // Move file content
        _files[normalizedDestination] = content;
        _files.Remove(normalizedSource);

        // Move timestamps
        if (_fileCreationTimes.TryGetValue(normalizedSource, out var creationTime))
        {
            _fileCreationTimes[normalizedDestination] = creationTime;
            _fileCreationTimes.Remove(normalizedSource);
        }

        if (_fileLastWriteTimes.TryGetValue(normalizedSource, out var lastWriteTime))
        {
            _fileLastWriteTimes[normalizedDestination] = lastWriteTime;
            _fileLastWriteTimes.Remove(normalizedSource);
        }

        // Move IFile if it exists
        if (_iFiles.TryGetValue(normalizedSource, out var iFile))
        {
            _iFiles[normalizedDestination] = iFile;
            _iFiles.Remove(normalizedSource);
        }

        EnsureParentDirectoriesExist(normalizedDestination);
    }

    /// <inheritdoc />
    public FileInfo GetFileInfo(string path)
    {
        var normalizedPath = NormalizePath(path);
        
        if (_files.TryGetValue(normalizedPath, out var content))
        {
            return new InMemoryFileInfoWrapper(
                normalizedPath,
                content.Length,
                _fileCreationTimes.GetValueOrDefault(normalizedPath, DateTime.Now),
                _fileLastWriteTimes.GetValueOrDefault(normalizedPath, DateTime.Now),
                exists: true);
        }

        return new InMemoryFileInfoWrapper(normalizedPath, 0, DateTime.MinValue, DateTime.MinValue, exists: false);
    }

    /// <inheritdoc />
    public DirectoryInfo GetDirectoryInfo(string path)
    {
        var normalizedPath = NormalizePath(path);
        var exists = _directories.Contains(normalizedPath);
        return new InMemoryDirectoryInfoWrapper(normalizedPath, exists);
    }

    /// <inheritdoc />
    public IEnumerable<IFile> EnumerateFiles(string directory, CancellationToken cancellationToken = default)
    {
        var normalizedDirectory = NormalizePath(directory);
        
        foreach (var kvp in _iFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var filePath = kvp.Key;
            var parentPath = GetParentPath(filePath);
            
            if (string.Equals(parentPath, normalizedDirectory, StringComparison.OrdinalIgnoreCase))
            {
                yield return kvp.Value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the current directory for testing purposes.
    /// </summary>
    public string CurrentDirectory { get; set; } = TestPaths.Combine(TestPaths.Root, "TestWorkingDir");

    /// <inheritdoc />
    public string GetCurrentDirectory()
    {
        return CurrentDirectory;
    }

    /// <summary>
    /// Clears all files and directories from the in-memory file system.
    /// </summary>
    public void Clear()
    {
        _files.Clear();
        _directories.Clear();
        _iFiles.Clear();
        _fileCreationTimes.Clear();
        _fileLastWriteTimes.Clear();
    }

    /// <summary>
    /// Removes a file from the in-memory file system.
    /// </summary>
    /// <param name="path">The path of the file to remove.</param>
    /// <returns>True if the file was removed, false if it didn't exist.</returns>
    public bool RemoveFile(string path)
    {
        var normalizedPath = NormalizePath(path);
        var removed = _files.Remove(normalizedPath);
        _iFiles.Remove(normalizedPath);
        _fileCreationTimes.Remove(normalizedPath);
        _fileLastWriteTimes.Remove(normalizedPath);
        return removed;
    }

    /// <summary>
    /// Removes a directory from the in-memory file system.
    /// </summary>
    /// <param name="path">The path of the directory to remove.</param>
    /// <returns>True if the directory was removed, false if it didn't exist.</returns>
    public bool RemoveDirectory(string path)
    {
        var normalizedPath = NormalizePath(path);
        return _directories.Remove(normalizedPath);
    }
}

/// <summary>
/// A wrapper around FileInfo that can represent a virtual file path.
/// Uses the real FileInfo class but provides virtual file metadata.
/// </summary>
public class InMemoryFileInfoWrapper
{
    private readonly FileInfo _fileInfo;
    private readonly long _length;
    private readonly DateTime _creationTime;
    private readonly DateTime _lastWriteTime;
    private readonly bool _exists;

    public InMemoryFileInfoWrapper(string path, long length, DateTime creationTime, DateTime lastWriteTime, bool exists)
    {
        _fileInfo = new FileInfo(path);
        _length = length;
        _creationTime = creationTime;
        _lastWriteTime = lastWriteTime;
        _exists = exists;
    }

    public string FullName => _fileInfo.FullName;
    public string Name => _fileInfo.Name;
    public long Length => _exists ? _length : throw new FileNotFoundException("File not found", _fileInfo.FullName);
    public bool Exists => _exists;
    public DateTime CreationTime => _creationTime;
    public DateTime CreationTimeUtc => _creationTime.ToUniversalTime();
    public DateTime LastWriteTime => _lastWriteTime;
    public DateTime LastWriteTimeUtc => _lastWriteTime.ToUniversalTime();
    public DateTime LastAccessTime => _lastWriteTime;
    public DateTime LastAccessTimeUtc => _lastWriteTime.ToUniversalTime();
    public string? DirectoryName => _fileInfo.DirectoryName;
    public string Extension => _fileInfo.Extension;
    public FileAttributes Attributes => _exists ? FileAttributes.Normal : FileAttributes.Normal;
    
    /// <summary>
    /// Gets the underlying FileInfo object.
    /// </summary>
    public FileInfo ToFileInfo() => _fileInfo;
    
    /// <summary>
    /// Implicit conversion to FileInfo for compatibility.
    /// </summary>
    public static implicit operator FileInfo(InMemoryFileInfoWrapper wrapper) => wrapper._fileInfo;
}

/// <summary>
/// A wrapper around DirectoryInfo that can represent a virtual directory path.
/// </summary>
public class InMemoryDirectoryInfoWrapper
{
    private readonly DirectoryInfo _directoryInfo;
    private readonly bool _exists;

    public InMemoryDirectoryInfoWrapper(string path, bool exists)
    {
        _directoryInfo = new DirectoryInfo(path);
        _exists = exists;
    }

    public string FullName => _directoryInfo.FullName;
    public string Name => _directoryInfo.Name;
    public bool Exists => _exists;
    public DirectoryInfo? Parent => _directoryInfo.Parent;
    public DirectoryInfo Root => _directoryInfo.Root;
    
    /// <summary>
    /// Gets the underlying DirectoryInfo object.
    /// </summary>
    public DirectoryInfo ToDirectoryInfo() => _directoryInfo;
    
    /// <summary>
    /// Implicit conversion to DirectoryInfo for compatibility.
    /// </summary>
    public static implicit operator DirectoryInfo(InMemoryDirectoryInfoWrapper wrapper) => wrapper._directoryInfo;
}
