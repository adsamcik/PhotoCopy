using System;

namespace PhotoCopy.Files
{
    public enum DateSource
    {
        Exif,
        FileCreation,
        FileModification
    }

    internal struct FileDateTime
    {
        public DateTime DateTime;
        public DateSource Source;
    }
}
