using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PhotoCopy.Tests.TestingImplementation;

namespace PhotoCopy.Tests.E2E;

/// <summary>
/// Abstract base class for end-to-end tests that run the PhotoCopy executable.
/// Provides common setup/teardown and helper methods for creating test files and running commands.
/// </summary>
public abstract class E2ETestBase
{
    private static readonly object ExecutablePathLock = new();
    private static string? _cachedExecutablePath;

    /// <summary>
    /// Base directory for all test artifacts for this test run.
    /// </summary>
    protected string TestBaseDirectory { get; private set; } = null!;

    /// <summary>
    /// Source directory containing files to be copied/scanned.
    /// </summary>
    protected string SourceDir { get; private set; } = null!;

    /// <summary>
    /// Destination directory where files are copied to.
    /// </summary>
    protected string DestDir { get; private set; } = null!;

    /// <summary>
    /// Directory for configuration files.
    /// </summary>
    protected string ConfigDir { get; private set; } = null!;

    /// <summary>
    /// Directory for log files and transaction logs.
    /// </summary>
    protected string LogsDir { get; private set; } = null!;

    /// <summary>
    /// Path to the PhotoCopy executable.
    /// Searches in bin/Debug/net10.0 first, then bin/Release/net10.0.
    /// </summary>
    public static string ExecutablePath
    {
        get
        {
            if (_cachedExecutablePath is not null)
                return _cachedExecutablePath;

            lock (ExecutablePathLock)
            {
                if (_cachedExecutablePath is not null)
                    return _cachedExecutablePath;

                _cachedExecutablePath = FindExecutablePath();
                return _cachedExecutablePath;
            }
        }
    }

    private static string FindExecutablePath()
    {
        // Get the test project directory - navigate up from the test output directory
        var testAssemblyLocation = typeof(E2ETestBase).Assembly.Location;
        var testBinDir = Path.GetDirectoryName(testAssemblyLocation)!;

        // Navigate from PhotoCopy.Tests/bin/Debug/net10.0 to the solution root
        // Expected structure: SolutionRoot/PhotoCopy.Tests/bin/Debug/net10.0
        var solutionRoot = Path.GetFullPath(Path.Combine(testBinDir, "..", "..", "..", ".."));

        // Check Debug first
        var debugPath = Path.Combine(solutionRoot, "PhotoCopy", "bin", "Debug", "net10.0", "PhotoCopy.exe");
        if (File.Exists(debugPath))
            return debugPath;

        // Check for .dll (cross-platform)
        var debugDllPath = Path.Combine(solutionRoot, "PhotoCopy", "bin", "Debug", "net10.0", "PhotoCopy.dll");
        if (File.Exists(debugDllPath))
            return debugDllPath;

        // Check Release
        var releasePath = Path.Combine(solutionRoot, "PhotoCopy", "bin", "Release", "net10.0", "PhotoCopy.exe");
        if (File.Exists(releasePath))
            return releasePath;

        // Check Release .dll
        var releaseDllPath = Path.Combine(solutionRoot, "PhotoCopy", "bin", "Release", "net10.0", "PhotoCopy.dll");
        if (File.Exists(releaseDllPath))
            return releaseDllPath;

        throw new FileNotFoundException(
            $"PhotoCopy executable not found. Searched in:\n" +
            $"  - {debugPath}\n" +
            $"  - {debugDllPath}\n" +
            $"  - {releasePath}\n" +
            $"  - {releaseDllPath}\n" +
            $"Solution root detected as: {solutionRoot}");
    }

    /// <summary>
    /// Sets up the test directories before each test.
    /// Creates a unique directory structure under the temp folder.
    /// </summary>
    [Before(Test)]
    public virtual void BaseSetup()
    {
        var testName = GetType().Name;
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        TestBaseDirectory = Path.Combine(Path.GetTempPath(), "PhotoCopy.E2E", $"{testName}_{uniqueId}");

        SourceDir = Path.Combine(TestBaseDirectory, "source");
        DestDir = Path.Combine(TestBaseDirectory, "dest");
        ConfigDir = Path.Combine(TestBaseDirectory, "config");
        LogsDir = Path.Combine(TestBaseDirectory, "logs");

        Directory.CreateDirectory(SourceDir);
        Directory.CreateDirectory(DestDir);
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(LogsDir);
    }

    /// <summary>
    /// Cleans up the test directories after each test.
    /// </summary>
    [After(Test)]
    public virtual void BaseCleanup()
    {
        SafeDeleteDirectory(TestBaseDirectory);
    }

    /// <summary>
    /// Safely deletes a directory, handling common exceptions.
    /// </summary>
    protected static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                // Force garbage collection to release any file handles
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(50);

                // Reset attributes and delete
                foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                    catch
                    {
                        // Best effort
                    }
                }

                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Directory may be locked by another process
        }
        catch (UnauthorizedAccessException)
        {
            // May not have permission to delete
        }
    }

    #region File Creation Helpers

    /// <summary>
    /// Creates a JPEG file with EXIF metadata in the source directory.
    /// </summary>
    /// <param name="filename">Name of the file to create.</param>
    /// <param name="dateTaken">Date/time to embed as DateTimeOriginal.</param>
    /// <param name="gps">Optional GPS coordinates to embed.</param>
    /// <param name="subfolder">Optional subfolder within the source directory.</param>
    /// <returns>Full path to the created file.</returns>
    protected async Task<string> CreateSourceJpegAsync(
        string filename,
        DateTime dateTaken,
        (double Lat, double Lon)? gps = null,
        string? subfolder = null)
    {
        var directory = subfolder is not null
            ? Path.Combine(SourceDir, subfolder)
            : SourceDir;

        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, filename);
        var jpegBytes = MockImageGenerator.CreateJpeg(dateTaken: dateTaken, gps: gps);
        await File.WriteAllBytesAsync(filePath, jpegBytes);

        return filePath;
    }

    /// <summary>
    /// Creates a PNG file with EXIF metadata in the source directory.
    /// </summary>
    /// <param name="filename">Name of the file to create.</param>
    /// <param name="dateTaken">Date/time to embed as DateTimeOriginal.</param>
    /// <param name="gps">Optional GPS coordinates to embed.</param>
    /// <param name="subfolder">Optional subfolder within the source directory.</param>
    /// <returns>Full path to the created file.</returns>
    protected async Task<string> CreateSourcePngAsync(
        string filename,
        DateTime dateTaken,
        (double Lat, double Lon)? gps = null,
        string? subfolder = null)
    {
        var directory = subfolder is not null
            ? Path.Combine(SourceDir, subfolder)
            : SourceDir;

        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, filename);
        var pngBytes = MockImageGenerator.CreatePng(dateTaken: dateTaken, gps: gps);
        await File.WriteAllBytesAsync(filePath, pngBytes);

        return filePath;
    }

    /// <summary>
    /// Creates a configuration file in the config directory.
    /// </summary>
    /// <param name="filename">Name of the config file (e.g., "appsettings.yaml").</param>
    /// <param name="content">Content of the configuration file.</param>
    /// <returns>Full path to the created config file.</returns>
    protected async Task<string> CreateConfigFileAsync(string filename, string content)
    {
        var filePath = Path.Combine(ConfigDir, filename);
        await File.WriteAllTextAsync(filePath, content);
        return filePath;
    }

    #endregion

    #region Process Execution Helpers

    /// <summary>
    /// Runs PhotoCopy with the specified verb and arguments.
    /// </summary>
    /// <param name="verb">The command verb (e.g., "copy", "scan", "validate").</param>
    /// <param name="args">Additional arguments.</param>
    /// <returns>The process result.</returns>
    protected Task<ProcessResult> RunPhotoCopyAsync(string verb, params string[] args)
    {
        return ProcessRunner.RunAsync(
            ExecutablePath,
            verb,
            args,
            workingDirectory: TestBaseDirectory);
    }

    /// <summary>
    /// Runs the PhotoCopy copy command with common options.
    /// </summary>
    /// <param name="source">Source directory. If null, uses SourceDir.</param>
    /// <param name="destination">Destination pattern. If null, uses DestDir.</param>
    /// <param name="dryRun">Whether to run in dry-run mode.</param>
    /// <param name="verbose">Whether to enable verbose output.</param>
    /// <param name="additionalArgs">Any additional arguments.</param>
    /// <returns>The process result.</returns>
    protected Task<ProcessResult> RunCopyAsync(
        string? source = null,
        string? destination = null,
        bool dryRun = false,
        bool verbose = false,
        params string[] additionalArgs)
    {
        var args = new System.Collections.Generic.List<string>
        {
            "--source", source ?? SourceDir,
            "--destination", destination ?? DestDir
        };

        if (dryRun)
        {
            args.Add("--dry-run");
            args.Add("true");
        }

        if (verbose)
        {
            args.Add("--log-level");
            args.Add("verbose");
        }

        args.AddRange(additionalArgs);

        return RunPhotoCopyAsync("copy", args.ToArray());
    }

    /// <summary>
    /// Runs the PhotoCopy scan command.
    /// </summary>
    /// <param name="source">Source directory to scan. If null, uses SourceDir.</param>
    /// <param name="verbose">Whether to enable verbose output.</param>
    /// <param name="additionalArgs">Any additional arguments.</param>
    /// <returns>The process result.</returns>
    protected Task<ProcessResult> RunScanAsync(
        string? source = null,
        bool verbose = false,
        params string[] additionalArgs)
    {
        var args = new System.Collections.Generic.List<string>
        {
            "--source", source ?? SourceDir
        };

        if (verbose)
        {
            args.Add("--log-level");
            args.Add("verbose");
        }

        args.AddRange(additionalArgs);

        return RunPhotoCopyAsync("scan", args.ToArray());
    }

    /// <summary>
    /// Runs the PhotoCopy validate command.
    /// </summary>
    /// <param name="configPath">Path to the config file to validate. If null, uses default config location.</param>
    /// <param name="additionalArgs">Any additional arguments.</param>
    /// <returns>The process result.</returns>
    protected Task<ProcessResult> RunValidateAsync(
        string? configPath = null,
        params string[] additionalArgs)
    {
        var args = new System.Collections.Generic.List<string>();

        if (configPath is not null)
        {
            args.Add("--config");
            args.Add(configPath);
        }

        args.AddRange(additionalArgs);

        return RunPhotoCopyAsync("validate", args.ToArray());
    }

    #endregion

    #region Assertion Helpers

    /// <summary>
    /// Gets all files in the destination directory.
    /// </summary>
    protected string[] GetDestinationFiles(string searchPattern = "*.*")
    {
        return Directory.Exists(DestDir)
            ? Directory.GetFiles(DestDir, searchPattern, SearchOption.AllDirectories)
            : Array.Empty<string>();
    }

    /// <summary>
    /// Gets all files in the source directory.
    /// </summary>
    protected string[] GetSourceFiles(string searchPattern = "*.*")
    {
        return Directory.Exists(SourceDir)
            ? Directory.GetFiles(SourceDir, searchPattern, SearchOption.AllDirectories)
            : Array.Empty<string>();
    }

    /// <summary>
    /// Checks if a file exists at the expected destination path.
    /// </summary>
    protected bool DestinationFileExists(string relativePath)
    {
        var fullPath = Path.Combine(DestDir, relativePath);
        return File.Exists(fullPath);
    }

    #endregion
}
