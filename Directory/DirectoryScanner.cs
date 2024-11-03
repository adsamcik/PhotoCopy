using PhotoCopy;
using PhotoCopy.Files;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class DirectoryScanner
{
    public static IEnumerable<IFile> EnumerateFiles(string rootDir, Options options)
    {
        Stack<string> dirStack = [];
        dirStack.Push(rootDir);

        while (dirStack.Count > 0)
        {
            string directory = dirStack.Pop();
            Log.Print($"Processing directory {directory}", Options.LogLevel.verbose);

            foreach (IFile file in ProcessDirectory(directory, options))
            {
                yield return file;
            }

            // Push subdirectories onto the stack for depth-first traversal
            foreach (string subdirectory in System.IO.Directory.GetDirectories(directory))
            {
                dirStack.Push(subdirectory);
            }
        }
    }

    private static IEnumerable<IFile> ProcessDirectory(string directory, Options options)
    {
        IEnumerable<FileInfo> files;
        try
        {
            files = System.IO.Directory.EnumerateFiles(directory).Select(path => new FileInfo(path));
        }
        catch (DirectoryNotFoundException e)
        {
            Log.Print($"Source {e.Message} does not exist", Options.LogLevel.errorsOnly);
            yield break; // Exit if directory is not found
        }

        // Load generic files once per directory, if needed
        List<GenericFile> genericFiles = options.RelatedFileMode != Options.RelatedFileLookup.none
            ? LoadGenericFiles(directory)
            : null;

        foreach (FileInfo file in files)
        {
            IFile processedFile = ProcessFile(file, genericFiles, options);
            if (processedFile != null)
            {
                yield return processedFile;
            }
        }

        Log.Print("Finished processing directory", Options.LogLevel.verbose);
    }

    private static IFile ProcessFile(FileInfo file, List<GenericFile> genericFiles, Options options)
    {
        try
        {
            FileDateTime dateTime = FileMetadataExtractor.GetDateTime(file);

            if (dateTime.DateTimeSource == DateTimeSource.Exif)
            {
                return ProcessPhotoFile(file, dateTime, genericFiles, options);
            }
            else
            {
                return ProcessGenericFile(file, dateTime, genericFiles, options);
            }
        }
        catch (Exception e)
        {
            Log.Print($"Error processing file {file.FullName}: {e.Message}", Options.LogLevel.errorsOnly);
            return null; // Skip the file if there's an error
        }
    }


    private static FileWithMetadata ProcessPhotoFile(FileInfo file, FileDateTime dateTime, List<GenericFile> genericFiles, Options options)
    {
        FileWithMetadata photoFile = new FileWithMetadata(file, dateTime);

        if (options.RelatedFileMode != Options.RelatedFileLookup.none && genericFiles != null)
        {
            photoFile.AddRelatedFiles(genericFiles, options.RelatedFileMode);
        }

        return photoFile;
    }

    private static GenericFile ProcessGenericFile(FileInfo file, FileDateTime dateTime, List<GenericFile> genericFiles, Options options)
    {
        if (options.RequireExif)
        {
            Log.Print($"File {file.FullName} has no date in EXIF, skipping.", Options.LogLevel.important);
            return null;
        }

        GenericFile genericFile = new(file, dateTime);
        Log.Print($"File {file.FullName} has no date in EXIF, defaulting to file {dateTime.DateTimeSource} time.", Options.LogLevel.important);

        genericFiles?.Add(genericFile);

        return genericFile;
    }

    private static List<GenericFile> LoadGenericFiles(string directory)
    {
        IEnumerable<FileInfo> files = System.IO.Directory.EnumerateFiles(directory)
            .Select(f => new FileInfo(f))
            .Where(f => FileMetadataExtractor.GetDateTime(f).DateTimeSource != DateTimeSource.Exif);

        return files.Select(f => new GenericFile(f, FileMetadataExtractor.GetDateTime(f))).ToList();
    }
}
