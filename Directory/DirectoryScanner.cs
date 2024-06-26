﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using PhotoCopy.Files;

namespace PhotoCopy.Directory;

internal static class DirectoryScanner
{
    /// <summary>
    /// Enumerates over all files in directory and its subdirectories.
    /// </summary>
    /// <param name="rootDir">Path to root directory</param>
    /// <returns>Enumerable of IFiles</returns>
    public static IEnumerable<IFile> EnumerateFiles(string rootDir, Options options)
    {
        var dirQueue = new Queue<string>();
        dirQueue.Enqueue(rootDir);
        var genericFileList = new List<IFile>();
        var photoFileList = new List<FileWithMetadata>();

        while (dirQueue.Count > 0)
        {
            var directory = dirQueue.Dequeue();
            genericFileList.Clear();
            photoFileList.Clear();
            Log.Print($"Building file list for {directory}", Options.LogLevel.verbose);
            try
            {
                foreach (var file in System.IO.Directory.EnumerateFiles(directory)
                    .Select(path => new FileInfo(path)))
                {
                    var dateTime = FileMetadataExtractor.GetDateTime(file);
                    if (dateTime.DateTimeSource == DateTimeSource.Exif)
                    {
                        photoFileList.Add(new FileWithMetadata(file, dateTime));
                    }
                    else
                    {
                        genericFileList.Add(new GenericFile(file, dateTime));
                    }
                }

                if (options.RelatedFileMode != Options.RelatedFileLookup.none)
                {
                    foreach (var photo in photoFileList)
                    {
                        photo.AddRelatedFiles(genericFileList, options.RelatedFileMode);
                    }
                }

                if (options.RequireExif)
                {
                    foreach (var genericFile in genericFileList)
                    {
                        Log.Print(
                        $"File {genericFile.File.FullName} has no date in exif, skipping.",
                        Options.LogLevel.important);
                    }
                }
                else
                {
                    foreach (var genericFile in genericFileList)
                    {
                        Log.Print(
                            $"File {genericFile.File.FullName} has no date in exif, defaulting to file {genericFile.FileDateTime.DateTimeSource} time.",
                            Options.LogLevel.important);
                    }
                }
            }
            catch (DirectoryNotFoundException e)
            {
                Log.Print($"Source {e.Message} does not exist", Options.LogLevel.errorsOnly);
            }
            Log.Print("Finished building file list", Options.LogLevel.verbose);

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
}