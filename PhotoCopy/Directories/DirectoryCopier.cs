using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Security;
using System.Text;
using Humanizer;
using Microsoft.Extensions.Logging;
using PhotoCopy.Abstractions;
using PhotoCopy.Extensions;
using PhotoCopy.Files;
using PhotoCopy.Validators;

namespace PhotoCopy.Directories;

/// <summary>
/// Handles copying of files in a directory.
/// </summary>
internal class DirectoryCopier : IDirectoryCopier
{
    private readonly IFileSystem _fileSystem;
    private readonly IFileOperation _fileOperation;
    private readonly ILogger _logger;

    public DirectoryCopier(IFileSystem fileSystem, IFileOperation fileOperation, ILogger logger)
    {
        _fileSystem = fileSystem;
        _fileOperation = fileOperation;
        _logger = logger;
    }

    /// <summary>
    /// Copies files from the input folder to the output folder based on options.
    /// </summary>
    public void Copy(Options options, IReadOnlyCollection<IValidator> validators)
    {
        foreach (var file in _fileSystem.EnumerateFiles(options.Source, options))
        {
            _logger.Log($">> {file.File.FullName}", Options.LogLevel.verbose);

            if (!ShouldCopy(validators, file, options))
            {
                continue;
            }

            var relativePath = GeneratePath(options, file);
            var fullPath = Path.GetFullPath(relativePath);
            var newFile = new FileInfo(fullPath);

            if (!ResolveDuplicate(options, file, ref newFile))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(relativePath);
            if (!options.DryRun && !string.IsNullOrEmpty(directory))
            {
                _fileSystem.CreateDirectory(directory);
            }

            if (options.Mode == Options.OperationMode.move)
            {
                _fileOperation.MoveFile(file, newFile.FullName, options.DryRun);
            }
            else
            {
                _fileOperation.CopyFile(file, newFile.FullName, options.DryRun);
            }

            _logger.Log(string.Empty, Options.LogLevel.verbose);
        }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal bool ShouldCopy(IReadOnlyCollection<IValidator> validators, IFile file, Options options)
    {
        foreach (var validator in validators)
        {
            if (!validator.Validate(file))
            {
                _logger.Log($"\tFiltered by {validator.GetType().Name.Humanize().ToLowerInvariant()}", Options.LogLevel.verbose);
                return false;
            }
        }
        return true;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal bool ResolveDuplicate(Options options, IFile file, ref FileInfo newFile)
    {
        if (!newFile.Exists)
        {
            return true;
        }

        if (options.SkipExisting)
        {
            return false;
        }

        // Create a GenericFile using newFile info.
        var newGenericFile = new GenericFile(newFile, new FileDateTime(newFile.CreationTime, DateTimeSource.FileCreation));

        if (!options.NoDuplicateSkip && EqualChecksum(file, newGenericFile))
        {
            _logger.Log($"\tDuplicate of {newFile.FullName}", Options.LogLevel.verbose);
            if (!options.DryRun && options.Mode == Options.OperationMode.move)
            {
                file.File.Delete();
            }
            return false;
        }

        var number = 0;
        var originalNewPath = newFile.Name;
        try
        {
            var directoryPath = newFile.DirectoryName ?? string.Empty;
            do
            {
                number++;
                newFile = new FileInfo(Path.Combine(directoryPath,
                    $"{Path.GetFileNameWithoutExtension(originalNewPath)}" +
                    $"{options.DuplicatesFormat.Replace("{number}", number.ToString())}" +
                    $"{Path.GetExtension(originalNewPath)}"));
            } while (newFile.Exists);
        }
        catch (SecurityException e)
        {
            _logger.Log(e.Message, Options.LogLevel.errorsOnly);
        }
        catch (PathTooLongException e)
        {
            _logger.Log(e.Message, Options.LogLevel.errorsOnly);
        }
        catch (Exception e)
        {
            _logger.Log(e.Message, Options.LogLevel.errorsOnly);
        }

        return true;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal string GeneratePath(Options options, IFile source)
    {
        // Get the directory part relative to source, or empty if in root
        var relativePath = Path.GetRelativePath(options.Source, source.File.DirectoryName ?? string.Empty);
        relativePath = relativePath == "." ? string.Empty : relativePath;

        var builder = new StringBuilder(options.Destination)
            .Replace(Options.DestinationVariables.Year, source.FileDateTime.DateTime.Year.ToString())
            .Replace(Options.DestinationVariables.Month, source.FileDateTime.DateTime.Month.ToString())
            .Replace(Options.DestinationVariables.Day, source.FileDateTime.DateTime.Day.ToString())
            .Replace(Options.DestinationVariables.DayOfYear, source.FileDateTime.DateTime.DayOfYear.ToString())
            .Replace(Options.DestinationVariables.Directory, relativePath)
            .Replace(Options.DestinationVariables.Name, source.File.Name)
            .Replace(Options.DestinationVariables.NameNoExtension, Path.GetFileNameWithoutExtension(source.File.Name))
            .Replace(Options.DestinationVariables.Extension, source.File.Extension.TrimStart('.'));

        return builder.ToString();
    }

    /// <summary>
    /// Checks if the checksums for two files are identical.
    /// </summary>
    private bool EqualChecksum(IFile fileA, IFile fileB)
    {
        return fileA.File.Length == fileB.File.Length && fileA.Checksum == fileB.Checksum;
    }
}