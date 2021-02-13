﻿using CommandLine;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using PhotoCopy.Files;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PhotoCopySort
{
    public enum LogLevel
    {
        Verbose,
        Important,
        ErrorsOnly
    }

    public static class ApplicationState
    {
        public static Options Options { get; set; }
    }

    /// <summary>
    /// Command line options
    /// </summary>
    public class Options
    {
        public enum OperationMode
        {
            Move,
            Copy,
        }

        // todo: Unfortunately Default true causes problems so variables have default false for now

        [Option('i', "input", Required = true, HelpText = "Set source folder")]
        public string Source { get; set; }

        [Option('o', "output", Required = true, HelpText = "Set output folder. Supported variables (case-sensitive): {year}, {month}, {day}, {name}, {directory}, {extension}")]
        public string Destination { get; set; }

        [Option('d', "dry", Required = false,
            HelpText = "True if no files should be moved and only printed to the command line.")]
        public bool DryRun { get; set; }

        [Option('m', "mode", Required = false, Default = OperationMode.Copy,
            HelpText = "Operation mode. Available modes: Move, Copy")]
        public OperationMode Mode { get; set; }

        [Option("skip-existing", Required = false, HelpText = "Skips file if it already exists in the output.")]
        public bool SkipExisting { get; set; }

        [Option('l', "logLevel", Required = false, Default = LogLevel.Important,
            HelpText = "Determines what is printed on screen. Options: Verbose, Important, ErrorsOnly")]
        public LogLevel LogLevel { get; set; }

        [Option("no-skip-duplicate", Required = false,
            HelpText = "Disables duplicate skipping.")]
        public bool NoDuplicateSkip { get; set; }

        [Option("duplicate-format", Required = false, Default = "_{number}",
            HelpText = "Format used for differentiating duplicates. Use {number} for number placeholder.")]
        public string DuplicatesFormat { get; set; }
    }

    class Program
    {

        private static IEnumerable<IFile> GetAllFilesInSubdir(string rootDir)
        {
            var dirQueue = new Queue<string>();
            dirQueue.Enqueue(rootDir);
            var genericFileList = new List<IFile>();
            var photoFileList = new List<PhotoFile>();

            while (dirQueue.Count > 0)
            {
                var directory = dirQueue.Dequeue();
                genericFileList.Clear();
                photoFileList.Clear();
                try
                {
                    foreach (var file in System.IO.Directory.EnumerateFiles(directory).Select(path => new FileInfo(path)))
                    {
                        var dateTime = GetDateTime(file);
                        if (dateTime.IsFromExif)
                        {
                            photoFileList.Add(new PhotoFile(file, dateTime));
                        }
                        else
                        {
                            genericFileList.Add(new GenericFile(file, dateTime));
                        }
                    }

                    foreach (var photo in photoFileList)
                    {
                        photo.AddRelatedFiles(genericFileList);
                    }


                    foreach (var genericFile in genericFileList)
                    {
                        Log.Print($"File {genericFile.File.FullName} has no date in exif, defaulting to file creation time.", LogLevel.Important);
                    }
                }
                catch (DirectoryNotFoundException e)
                {
                    Log.Print($"Source {e.Message} does not exist", LogLevel.ErrorsOnly);
                }

                foreach (var directoryPath in System.IO.Directory.GetDirectories(directory))
                {
                    dirQueue.Enqueue(directoryPath);
                }

                foreach (var photoFile in photoFileList)
                {
                    yield return photoFile;
                }

                foreach (var genericFile in genericFileList)
                {
                    yield return genericFile;
                }
            }
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

        private static bool ValidateInput(Options options)
        {
            var sourceFile = new FileInfo(options.Source);
            var isValid = true;
            if (sourceFile.Exists)
            {
                Log.Print($"Source {sourceFile.FullName} does not exist", LogLevel.ErrorsOnly);
                isValid = false;
            }

            if (!sourceFile.Attributes.HasFlag(FileAttributes.Directory))
            {
                Log.Print("Source is not a directory", LogLevel.ErrorsOnly);
                isValid = false;
            }

            if (!options.DuplicatesFormat.Contains("{number}"))
            {
                Log.Print("Duplicates format does not contain {number}", LogLevel.ErrorsOnly);
                isValid = false;
            }

            return isValid;
        }

        private static readonly int[] DateTags =
        {
            ExifDirectoryBase.TagDateTime,
            ExifDirectoryBase.TagDateTimeOriginal,
            ExifDirectoryBase.TagDateTimeDigitized
        };

        private static FileDateTime GetDateTime(FileInfo file)
        {
            try
            {
                var directories = ImageMetadataReader.ReadMetadata(file.FullName);
                var directory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                if (directory != null)
                {
                    foreach (var tag in DateTags)
                    {
                        directory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var exifTime);
                        if (exifTime != default)
                        {
                            return new FileDateTime
                            {
                                DateTime = exifTime,
                                IsFromExif = true
                            };
                        }
                    }
                }
            }
            catch (ImageProcessingException e)
            {
                if (e.Message != "File format could not be determined")
                {
                    Log.Print($"{file.FullName} --- {e.Message}", LogLevel.ErrorsOnly);
                }
                // do nothing
            }


            return new FileDateTime
            {
                DateTime = file.CreationTime,
                IsFromExif = false
            };
        }

        private static string GeneratePath(Options options, IFile source)
        {
            var builder = new StringBuilder(options.Destination)
                .Replace("{year}", source.FileDateTime.DateTime.Year.ToString())
                .Replace("{month}", source.FileDateTime.DateTime.Month.ToString())
                .Replace("{day}", source.FileDateTime.DateTime.Day.ToString())
                .Replace("{directory}", Path.GetRelativePath(options.Source, source.File.DirectoryName))
                .Replace("{name}", source.File.Name)
                .Replace("{extension}", source.File.Extension);


            return builder.ToString();
        }

        static void Main(string[] args)
        {
            Parser
                .Default
                .ParseArguments<Options>(args)
                .WithParsed(options =>
                {
                    if (!ValidateInput(options)) return;
                    ApplicationState.Options = options;


                    foreach (var file in GetAllFilesInSubdir(options.Source))
                    {
                        Log.Print($">> {file.File.FullName}", LogLevel.Verbose);

                        var newPath = GeneratePath(options, file);
                        var newFile = new FileInfo(newPath);

                        if (newFile.Exists)
                        {
                            if (options.SkipExisting)
                            {
                                continue;
                            }

                            var newGenericFile = new GenericFile(newFile, new FileDateTime() { DateTime = newFile.CreationTime });
                            if (!options.NoDuplicateSkip && EqualChecksum(file, newGenericFile))
                            {
                                Log.Print($"Duplicate of {newFile.FullName}", LogLevel.Verbose);
                                if (!options.DryRun && options.Mode == Options.OperationMode.Move)
                                {
                                    file.File.Delete();
                                }

                                continue;
                            }
                            else
                            {
                                var number = 0;
                                do
                                {
                                    number++;
                                    newFile = new FileInfo(Path.Combine(newFile.DirectoryName,
                                        $"{Path.GetFileNameWithoutExtension(newPath)}" +
                                        $"{options.DuplicatesFormat.Replace("{number}", number.ToString())}" +
                                        $"{Path.GetExtension(newPath)}"));
                                } while (newFile.Exists);
                            }
                        }

                        if (options.Mode == Options.OperationMode.Move)
                        {
                            file.MoveTo(newFile.FullName, options.DryRun);
                        }
                        else
                        {
                            file.CopyTo(newFile.FullName, options.DryRun);
                        }

                        Log.Print("", LogLevel.Verbose);
                    }
                });
        }
    }
}