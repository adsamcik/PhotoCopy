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
internal struct FileDateTime
{
    public DateTime DateTime;
    public DateTimeSource TimeSource;
}