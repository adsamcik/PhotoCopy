using CommandLine;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
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

        [Option('o', "output", Required = true, HelpText = "Set output folder")]
        public string Destination { get; set; }

        [Option('f', "format", Required = false, Default = "{directory}/{name}",
            HelpText =
                "Path format. Supported variables (case-sensitive): {year}, {month}, {day}, {name}, {directory}, {extension}")]
        public string Format { get; set; }

        [Option('d', "dry", Required = false,
            HelpText = "True if no files should be moved and only printed to the command line.")]
        public bool DryRun { get; set; }

        [Option('m', "mode", Required = false, Default = OperationMode.Copy,
            HelpText = "Operation mode. Available modes: Move, MoveAll, Copy, CopyAll")]
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
        private static List<string> DirSearch(string sDir, Options options)
        {
            var files = new List<string>();
            try
            {
                files.AddRange(System.IO.Directory.GetFiles(sDir));

                foreach (var d in System.IO.Directory.GetDirectories(sDir))
                {
                    files.AddRange(DirSearch(d, options));
                }
            }
            catch (Exception e)
            {
                Print($"Source {e.Message} does not exist", options, LogLevel.ErrorsOnly);
            }

            return files;
        }

        private static string GetChecksum(string file)
        {
            using var stream = new BufferedStream(File.OpenRead(file), 1200000);
            var sha = new SHA256Managed();
            var checksum = sha.ComputeHash(stream);
            return BitConverter.ToString(checksum).Replace("-", string.Empty);
        }

        private static bool EqualChecksum(FileInfo fileA, FileInfo fileB)
        {
            return fileA.Length == fileB.Length && GetChecksum(fileA.FullName) == GetChecksum(fileB.FullName);
        }

        private static bool ValidateInput(Options options)
        {
            var sourceFile = new FileInfo(options.Source);
            var isValid = true;
            if (sourceFile.Exists)
            {
                Print($"Source {sourceFile.FullName} does not exist", options, LogLevel.ErrorsOnly);
                isValid = false;
            }

            if (!sourceFile.Attributes.HasFlag(FileAttributes.Directory))
            {
                Print("Source is not a directory", options, LogLevel.ErrorsOnly);
                isValid = false;
            }

            if (!options.DuplicatesFormat.Contains("{number}"))
            {
                Print("Duplicates format does not contain {number}", options, LogLevel.ErrorsOnly);
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

        private static string GeneratePath(Options options, string rootSourcePath, FileInfo source)
        {
            DateTime takenTime = default;
            try
            {
                var directories = ImageMetadataReader.ReadMetadata(source.FullName);
                var directory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                if (directory != null)
                {
                    foreach (var tag in DateTags)
                    {
                        directory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out takenTime);
                        if (takenTime != default)
                        {
                            break;
                        }
                    }
                }
            }
            catch (ImageProcessingException e)
            {
                Print($"{source.FullName} --- {e.Message}", options, LogLevel.ErrorsOnly);
            }

            if (takenTime == default)
            {
                Print($"File {source.FullName} has no date in exif, defaulting to file creation time.", options, LogLevel.Important);
                takenTime = source.CreationTime;
            }

            var builder = new StringBuilder(options.Format)
                .Replace("{year}", takenTime.Year.ToString())
                .Replace("{month}", takenTime.Month.ToString())
                .Replace("{day}", takenTime.Day.ToString())
                .Replace("{directory}", Path.GetRelativePath(rootSourcePath, source.DirectoryName))
                .Replace("{name}", source.Name)
                .Replace("{extension}", source.Extension);


            return builder.ToString();
        }

        private static void Print(string message, Options options, LogLevel logLevel)
        {
            if ((int)logLevel >= (int)options.LogLevel)
            {
                Console.WriteLine(message);
            }
        }

        static void Main(string[] args)
        {
            Parser
                .Default
                .ParseArguments<Options>(args)
                .WithParsed(options =>
                {
                    if (!ValidateInput(options)) return;

                    var files = DirSearch(options.Source, options);

                    foreach (var path in files)
                    {
                        Print($">> {path}", options, LogLevel.Verbose);
                        var file = new FileInfo(path);

                        var newPath = GeneratePath(options, options.Source, file);
                        var newFile = new FileInfo(newPath);

                        if (newFile.Exists)
                        {
                            if (options.SkipExisting)
                            {
                                continue;
                            }

                            if (!options.NoDuplicateSkip && EqualChecksum(file, newFile))
                            {
                                Print($"Duplicate of {newFile.FullName}", options, LogLevel.Verbose);
                                if (!options.DryRun && options.Mode == Options.OperationMode.Move)
                                {
                                    file.Delete();
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

                        if (!options.DryRun)
                        {
                            if (options.Mode == Options.OperationMode.Move)
                            {
                                file.MoveTo(newFile.FullName);
                            }
                            else
                            {
                                file.CopyTo(newFile.FullName);
                            }
                        }
                        else
                        {
                            Print($">>> {file.FullName} => {newPath}", options, LogLevel.Verbose);
                        }

                        Print("", options, LogLevel.Verbose);
                    }
                });
        }
    }
}