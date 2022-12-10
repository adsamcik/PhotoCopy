using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using Humanizer;
using PhotoCopy.Files;
using PhotoCopy.Validators;

namespace PhotoCopy.Directory
{
    /// <summary>
    /// Handles copying of files in directory.
    /// </summary>
    internal static class DirectoryCopier
    {
        /// <summary>
        /// Copies files from input folder to output folder based on options.
        /// </summary>
        /// <param name="options">Options</param>
        public static void Copy(Options options, IReadOnlyCollection<IValidator> validators)
        {
            foreach (var file in DirectoryScanner.EnumerateFiles(options.Source, options))
            {
                Log.Print($">> {file.File.FullName}", Options.LogLevel.verbose);

                if (ShouldCopy(validators, file))
                {
                    continue;
                }

                var newPath = GeneratePath(options, file);
                var newFile = new FileInfo(newPath);

                if (!ResolveDuplicate(options, file, ref newFile))
                {
                    continue;
                }

                var directory = newFile.Directory;
                if (!options.DryRun && directory?.Exists == false)
                {
                    directory.Create();
                }

                if (options.Mode == Options.OperationMode.move)
                {
                    file.MoveTo(newFile.FullName, options.DryRun);
                }
                else
                {
                    file.CopyTo(newFile.FullName, options.DryRun);
                }

                Log.Print("", Options.LogLevel.verbose);
            }
        }

        private static bool ShouldCopy(IReadOnlyCollection<IValidator> validators, IFile file)
        {
            foreach (var validator in validators)
            {
                if (!validator.Validate(file))
                {
                    Log.Print($"\tFiltered by {validator.GetType().Name.Humanize().ToLowerInvariant()}", Options.LogLevel.verbose);
                    return true;
                }
            }

            return false;
        }

        private static bool ResolveDuplicate(Options options, IFile file, ref FileInfo newFile)
        {
            if (!newFile.Exists)
            {
                return true;
            }

            if (options.SkipExisting)
            {
                return false;
            }

            var newGenericFile = new GenericFile(newFile,
                new FileDateTime { DateTime = newFile.CreationTime });
            if (!options.NoDuplicateSkip && EqualChecksum(file, newGenericFile))
            {
                Log.Print($"\tDuplicate of {newFile.FullName}", Options.LogLevel.verbose);
                if (!options.DryRun && options.Mode == Options.OperationMode.move)
                {
                    file.File.Delete();
                }

                return false;
            }

            var number = 0;
            var originalNewPath = newFile.FullName;
            try
            {
                var directoryPath = newFile.DirectoryName ?? "";

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
                Log.Print(e.Message, Options.LogLevel.errorsOnly);
            }
            catch (PathTooLongException e)
            {
                Log.Print(e.Message, Options.LogLevel.errorsOnly);
            }
            catch (Exception e)
            {
                Log.Print(e.Message, Options.LogLevel.errorsOnly);
            }

            return true;
        }

        private static string GeneratePath(Options options, IFile source)
        {
            var builder = new StringBuilder(options.Destination)
                .Replace(Options.DestinationVariables.Year, source.FileDateTime.DateTime.Year.ToString())
                .Replace(Options.DestinationVariables.Month, source.FileDateTime.DateTime.Month.ToString())
                .Replace(Options.DestinationVariables.Day, source.FileDateTime.DateTime.Day.ToString())
                .Replace(Options.DestinationVariables.DayOfYear, source.FileDateTime.DateTime.DayOfYear.ToString())
                .Replace(Options.DestinationVariables.Directory,
                    Path.GetRelativePath(options.Source, source.File.DirectoryName ?? ""))
                .Replace(Options.DestinationVariables.Name, source.File.Name)
                .Replace(Options.DestinationVariables.NameNoExtension, Path.GetFileNameWithoutExtension(source.File.Name))
                .Replace(Options.DestinationVariables.Extension, source.File.Extension.TrimStart('.'));


            return builder.ToString();
        }

        /// <summary>
        /// Checks if checksums for two files are identical
        /// </summary>
        /// <param name="fileA">File A</param>
        /// <param name="fileB">File B</param>
        /// <returns>True if checksum are identical</returns>
        private static bool EqualChecksum(IFile fileA, IFile fileB)
        {
            return fileA.File.Length == fileB.File.Length && fileA.Checksum == fileB.Checksum;
        }
    }
}