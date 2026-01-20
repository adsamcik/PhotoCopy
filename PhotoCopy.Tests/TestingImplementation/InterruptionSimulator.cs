using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using NSubstitute;
using PhotoCopy.Abstractions;
using PhotoCopy.Files;

namespace PhotoCopy.Tests.TestingImplementation;

/// <summary>
/// Simulates various crash and interruption scenarios for testing resume functionality.
/// </summary>
public static class InterruptionSimulator
{
    /// <summary>
    /// Creates a CancellationTokenSource that cancels after N files are processed.
    /// </summary>
    /// <param name="cancelAfterNFiles">Number of files to process before cancellation.</param>
    /// <param name="onFileProcessed">Callback invoked when a file is processed (for tracking).</param>
    /// <returns>A CancellationTokenSource and an action to call after each file.</returns>
    public static (CancellationTokenSource Cts, Action NotifyFileProcessed) CreateCancelAfterNFiles(int cancelAfterNFiles)
    {
        var cts = new CancellationTokenSource();
        var filesProcessed = 0;

        void NotifyFileProcessed()
        {
            if (Interlocked.Increment(ref filesProcessed) >= cancelAfterNFiles)
            {
                cts.Cancel();
            }
        }

        return (cts, NotifyFileProcessed);
    }

    /// <summary>
    /// Creates an IFileSystem that throws after N successful copy operations.
    /// Useful for simulating mid-operation crashes.
    /// </summary>
    public static IFileSystem CreateCrashingFileSystem(
        IFileSystem inner,
        int crashAfterNCopies,
        Exception? exceptionToThrow = null)
    {
        return new CrashingFileSystemDecorator(inner, crashAfterNCopies, exceptionToThrow);
    }

    /// <summary>
    /// Creates an IFileSystem that writes partial files for specific filenames.
    /// Simulates disk-full or power-loss during write scenarios.
    /// </summary>
    public static IFileSystem CreatePartialWriteFileSystem(
        IFileSystem inner,
        string crashOnFileName,
        double percentageWritten = 0.5)
    {
        return new PartialWriteFileSystemDecorator(inner, crashOnFileName, percentageWritten);
    }

    /// <summary>
    /// Creates an IFileSystem with controllable latency per operation.
    /// Useful for testing checkpoint timing and concurrent access.
    /// </summary>
    public static IFileSystem CreateSlowFileSystem(
        IFileSystem inner,
        TimeSpan copyLatency)
    {
        return new SlowFileSystemDecorator(inner, copyLatency);
    }

    private class CrashingFileSystemDecorator : IFileSystem
    {
        private readonly IFileSystem _inner;
        private readonly int _crashAfterNCopies;
        private readonly Exception _exception;
        private int _copyCount;

        public CrashingFileSystemDecorator(IFileSystem inner, int crashAfterNCopies, Exception? exception)
        {
            _inner = inner;
            _crashAfterNCopies = crashAfterNCopies;
            _exception = exception ?? new SimulatedCrashException("Process terminated unexpectedly");
        }

        public void CopyFile(string source, string destination, bool overwrite = false)
        {
            if (Interlocked.Increment(ref _copyCount) > _crashAfterNCopies)
            {
                throw _exception;
            }
            _inner.CopyFile(source, destination, overwrite);
        }

        public void MoveFile(string source, string destination)
        {
            if (Interlocked.Increment(ref _copyCount) > _crashAfterNCopies)
            {
                throw _exception;
            }
            _inner.MoveFile(source, destination);
        }

        // Delegate all other operations
        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
        public bool FileExists(string path) => _inner.FileExists(path);
        public void CreateDirectory(string path) => _inner.CreateDirectory(path);
        public FileInfo GetFileInfo(string path) => _inner.GetFileInfo(path);
        public DirectoryInfo GetDirectoryInfo(string path) => _inner.GetDirectoryInfo(path);
        public IEnumerable<IFile> EnumerateFiles(string directory, CancellationToken cancellationToken = default)
            => _inner.EnumerateFiles(directory, cancellationToken);
        public string GetCurrentDirectory() => _inner.GetCurrentDirectory();
    }

    private class PartialWriteFileSystemDecorator : IFileSystem
    {
        private readonly IFileSystem _inner;
        private readonly string _crashOnFileName;
        private readonly double _percentageWritten;

        public PartialWriteFileSystemDecorator(IFileSystem inner, string crashOnFileName, double percentageWritten)
        {
            _inner = inner;
            _crashOnFileName = crashOnFileName;
            _percentageWritten = percentageWritten;
        }

        public void CopyFile(string source, string destination, bool overwrite = false)
        {
            if (Path.GetFileName(source).Equals(_crashOnFileName, StringComparison.OrdinalIgnoreCase))
            {
                // Write partial file
                var content = File.ReadAllBytes(source);
                var partialLength = (int)(content.Length * _percentageWritten);
                var partialContent = content.Take(partialLength).ToArray();
                
                var destDir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }
                
                File.WriteAllBytes(destination, partialContent);
                throw new IOException($"Simulated disk full during write of {_crashOnFileName}");
            }
            _inner.CopyFile(source, destination, overwrite);
        }

        // Delegate all other operations
        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
        public bool FileExists(string path) => _inner.FileExists(path);
        public void CreateDirectory(string path) => _inner.CreateDirectory(path);
        public void MoveFile(string source, string destination) => _inner.MoveFile(source, destination);
        public FileInfo GetFileInfo(string path) => _inner.GetFileInfo(path);
        public DirectoryInfo GetDirectoryInfo(string path) => _inner.GetDirectoryInfo(path);
        public IEnumerable<IFile> EnumerateFiles(string directory, CancellationToken cancellationToken = default)
            => _inner.EnumerateFiles(directory, cancellationToken);
        public string GetCurrentDirectory() => _inner.GetCurrentDirectory();
    }

    private class SlowFileSystemDecorator : IFileSystem
    {
        private readonly IFileSystem _inner;
        private readonly TimeSpan _copyLatency;

        public SlowFileSystemDecorator(IFileSystem inner, TimeSpan copyLatency)
        {
            _inner = inner;
            _copyLatency = copyLatency;
        }

        public void CopyFile(string source, string destination, bool overwrite = false)
        {
            Thread.Sleep(_copyLatency);
            _inner.CopyFile(source, destination, overwrite);
        }

        public void MoveFile(string source, string destination)
        {
            Thread.Sleep(_copyLatency);
            _inner.MoveFile(source, destination);
        }

        // Delegate all other operations
        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
        public bool FileExists(string path) => _inner.FileExists(path);
        public void CreateDirectory(string path) => _inner.CreateDirectory(path);
        public FileInfo GetFileInfo(string path) => _inner.GetFileInfo(path);
        public DirectoryInfo GetDirectoryInfo(string path) => _inner.GetDirectoryInfo(path);
        public IEnumerable<IFile> EnumerateFiles(string directory, CancellationToken cancellationToken = default)
            => _inner.EnumerateFiles(directory, cancellationToken);
        public string GetCurrentDirectory() => _inner.GetCurrentDirectory();
    }
}

/// <summary>
/// Exception type for simulated crashes to distinguish from real exceptions.
/// </summary>
public class SimulatedCrashException : Exception
{
    public SimulatedCrashException(string message) : base(message) { }
    public SimulatedCrashException(string message, Exception inner) : base(message, inner) { }
}
