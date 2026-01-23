using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;
using PhotoCopy.Abstractions;
using PhotoCopy.Directories;
using PhotoCopy.Extensions;

namespace PhotoCopy.Files;

public class FileSystem : IFileSystem
{
    private readonly ILogger<FileSystem> _logger;
    private readonly IDirectoryScanner _directoryScanner;

    public FileSystem(ILogger<FileSystem> logger, IDirectoryScanner directoryScanner)
    {
        _logger = logger;
        _directoryScanner = directoryScanner;
    }

    public IEnumerable<IFile> EnumerateFiles(string path, CancellationToken cancellationToken = default)
    {
        return _directoryScanner.EnumerateFiles(path, cancellationToken);
    }

    public void CreateDirectory(string path)
    {
        ValidatePathSecurity(path, nameof(path));
        PathSecurityHelper.ThrowIfReparsePoint(path);
        RetryHelper.ExecuteWithRetry(
            () => Directory.CreateDirectory(path),
            _logger,
            $"CreateDirectory {path}");
    }

    public bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    public void MoveFile(string sourcePath, string destinationPath)
    {
        ValidatePathSecurity(sourcePath, nameof(sourcePath));
        ValidatePathSecurity(destinationPath, nameof(destinationPath));
        PathSecurityHelper.ThrowIfReparsePoint(sourcePath);
        PathSecurityHelper.ThrowIfReparsePoint(destinationPath);
        RetryHelper.ExecuteWithRetry(
            () => File.Move(sourcePath, destinationPath),
            _logger,
            $"MoveFile {Path.GetFileName(sourcePath)}");
    }

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false)
    {
        ValidatePathSecurity(sourcePath, nameof(sourcePath));
        ValidatePathSecurity(destinationPath, nameof(destinationPath));
        PathSecurityHelper.ThrowIfReparsePoint(sourcePath);
        PathSecurityHelper.ThrowIfReparsePoint(destinationPath);
        RetryHelper.ExecuteWithRetry(
            () => File.Copy(sourcePath, destinationPath, overwrite),
            _logger,
            $"CopyFile {Path.GetFileName(sourcePath)}");
    }

    public bool FileExists(string path)
    {
        return File.Exists(path);
    }
    
    public FileInfo GetFileInfo(string path)
    {
        return new FileInfo(path);
    }
    
    public DirectoryInfo GetDirectoryInfo(string path)
    {
        return new DirectoryInfo(path);
    }

    public string GetCurrentDirectory()
    {
        return Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Validates that a path is safe for file operations.
    /// </summary>
    /// <param name="path">The path to validate.</param>
    /// <param name="paramName">The parameter name for error messages.</param>
    /// <exception cref="ArgumentException">Thrown when path is unsafe.</exception>
    private static void ValidatePathSecurity(string path, string paramName)
    {
        if (!PathSecurityHelper.IsPathSafe(path))
        {
            throw new ArgumentException(
                $"Security violation: Path '{path}' is not safe. " +
                "Paths must be absolute and cannot contain path traversal sequences.",
                paramName);
        }
    }
}