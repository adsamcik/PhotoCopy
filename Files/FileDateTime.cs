using System;

namespace PhotoCopy.Files
{
    public enum DateSource
    {
        EXIF,
        FILE_CREATION,
        FILE_MODIFICATION
    }

    struct FileDateTime
    {
        public DateTime DateTime;
        public DateSource Source;
    }
}
