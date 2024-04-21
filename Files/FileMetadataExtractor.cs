using System.Collections.Generic;
using System.IO;
using System.Linq;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;

namespace PhotoCopy.Files;

internal static class FileMetadataExtractor
{
    private static readonly (
        System.Func<IReadOnlyList<MetadataExtractor.Directory>,
            MetadataExtractor.Directory>, int[]
        )[]
        DirectoryArray =
        [
            (list => list.OfType<ExifDirectoryBase>().FirstOrDefault(),
                [
                    ExifDirectoryBase.TagDateTime,
                    ExifDirectoryBase.TagDateTimeOriginal,
                    ExifDirectoryBase.TagDateTimeDigitized,
                ]),
            (list => list.OfType<ExifSubIfdDirectory>().FirstOrDefault(), [
                    ExifDirectoryBase.TagDateTime,
                    ExifDirectoryBase.TagDateTimeOriginal,
                    ExifDirectoryBase.TagDateTimeDigitized,
            ]),
            (list => list.OfType<QuickTimeTrackHeaderDirectory>().FirstOrDefault(),
                [QuickTimeTrackHeaderDirectory.TagCreated]),
            (list => list.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault(),
                [QuickTimeMovieHeaderDirectory.TagCreated]),
        ];

    public static FileDateTime GetDateTime(FileSystemInfo file)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(file.FullName);
            foreach (var tagRequest in DirectoryArray)
            {
                var directory = tagRequest.Item1(directories);

                if (directory == default)
                {
                    continue;
                }

                foreach (var tag in tagRequest.Item2)
                {
                    if (directory.TryGetDateTime(tag, out var exifTime))
                    {
                        return new FileDateTime
                        {
                            DateTime = exifTime,
                            DateTimeSource = DateTimeSource.Exif
                        };
                    }
                }
            }
        }
        catch (ImageProcessingException e)
        {
            if (e.Message != "File format could not be determined")
            {
                Log.Print($"{file.FullName} --- {e.Message}", Options.LogLevel.errorsOnly);
            }

            // do nothing
        }

        // Assume creation was overwritten
        if (file.CreationTime > file.LastWriteTime)
        {
            return new FileDateTime
            {
                DateTime = file.LastWriteTime,
                DateTimeSource = DateTimeSource.FileModification
            };
        }

        return new FileDateTime
        {
            DateTime = file.CreationTime,
            DateTimeSource = DateTimeSource.FileCreation
        };
    }
}