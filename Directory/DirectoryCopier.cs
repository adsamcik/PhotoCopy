using System;
using System.IO;
using System.Security;
using System.Text;
using PhotoCopy.Files;

namespace PhotoCopySort
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
        public static void Copy(Options options)
        {
            foreach (var file in DirectoryScanner.EnumerateFiles(options.Source))
            {
                Log.Print($">> {file.File.FullName}", Options.LogLevel.verbose);

                var newPath = GeneratePath(options, file);
                var newFile = new FileInfo(newPath);

                if (newFile.Exists)
                {
                    if (!ResolveDuplicate(options, file, ref newFile))
                    {
                        continue;
                    }
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

        private static bool ResolveDuplicate(Options options, IFile file, ref FileInfo newFile)
        {
            if (options.SkipExisting)
            {
                return false;
            }

            var newGenericFile = new GenericFile(newFile,
                new FileDateTime {DateTime = newFile.CreationTime});
            if (!options.NoDuplicateSkip && EqualChecksum(file, newGenericFile))
            {
                Log.Print($"Duplicate of {newFile.FullName}", Options.LogLevel.verbose);
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
                .Replace(Options.DestinationEnum.Year, source.FileDateTime.DateTime.Year.ToString())
                .Replace(Options.DestinationEnum.Month, source.FileDateTime.DateTime.Month.ToString())
                .Replace(Options.DestinationEnum.Day, source.FileDateTime.DateTime.Day.ToString())
                .Replace(Options.DestinationEnum.DayOfYear, source.FileDateTime.DateTime.DayOfYear.ToString())
                .Replace(Options.DestinationEnum.Directory,
                    Path.GetRelativePath(options.Source, source.File.DirectoryName ?? ""))
                .Replace(Options.DestinationEnum.Name, source.File.Name)
                .Replace(Options.DestinationEnum.NameNoExtension, Path.GetFileNameWithoutExtension(source.File.Name))
                .Replace(Options.DestinationEnum.Extension, source.File.Extension.TrimStart('.'));


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