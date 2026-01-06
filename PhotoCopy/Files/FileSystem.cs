using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using PhotoCopy.Abstractions;
using PhotoCopy.Directories;

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

    public IEnumerable<IFile> EnumerateFiles(string path)
    {
        return _directoryScanner.EnumerateFiles(path);
    }

    public void CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    public bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    public void MoveFile(string sourcePath, string destinationPath)
    {
        File.Move(sourcePath, destinationPath);
    }

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false)
    {
        File.Copy(sourcePath, destinationPath, overwrite);
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
}