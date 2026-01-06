using System;

namespace PhotoCopy.Files;

public class FileDateTime
{
    public DateTime DateTime { get; }
    public DateTimeSource Source { get; }
    
    public DateTime Created { get; }
    public DateTime Modified { get; }
    public DateTime Taken { get; }

    public FileDateTime(DateTime dateTime, DateTimeSource source)
    {
        DateTime = dateTime;
        Source = source;
        Created = dateTime;
        Modified = dateTime;
        Taken = dateTime;
    }
    
    public FileDateTime(DateTime created, DateTime modified, DateTime taken)
    {
        Created = created;
        Modified = modified;
        Taken = taken;
        
        // Use the most appropriate date as the main DateTime
        if (Taken != default)
        {
            DateTime = Taken;
            Source = DateTimeSource.ExifDateTimeOriginal;
        }
        else if (Created != default)
        {
            DateTime = Created;
            Source = DateTimeSource.FileCreation;
        }
        else
        {
            DateTime = Modified;
            Source = DateTimeSource.FileModification;
        }
    }
}

public enum DateTimeSource
{
    FileCreation,
    FileModification,
    ExifDateTime,
    ExifDateTimeOriginal,
    ExifDateTimeDigitized
}