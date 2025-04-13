using System;

namespace PhotoCopy.Files;

/// <summary>
/// Source of Date Time.
/// </summary>
internal enum DateTimeSource
{
    Exif,
    FileCreation,
    FileModification
}

/// <summary>
/// File date time
/// </summary>
internal record struct FileDateTime(DateTime DateTime, DateTimeSource DateTimeSource);