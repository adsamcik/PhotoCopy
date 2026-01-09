# PhotoCopy
 
Allows batch moving/copying photos to a new location organized by time.

## How to use

The following command will copy all files in /input/path to /output/path/with/variables
where each file will retain its relative directory but will be placed in addition year/month/extension directory inside the relative structure. Duplicate files will be appended with -number (eg. -1). And all files will be moved instead of copied.

`
./PhotoCopy -i "/input/path" -o "/output/path/with/variables/{directory}/{year}/{month}/{extension}/{name}" --duplicate-format "-{number}" --mode "Move"
`

### Options

__-i, --input__            Required. Path to a source directory, which will be scanned for files.

__-o, --output__          Required. Destination path for the operation. Determines the final path files have. Supported
                         variables (case-sensitive): {year}, {month}, {day}, {dayOfYear}, {directory}, {name},
                         {nameNoExtension}, {extension}

* {year} -  year extracted from file creation date or exif
* {month} - month extracted from file creation date or exif
* {day} - day extracted from file creation date or exif
* {dayOfYear} - day of year (~1-365) from file creation date or exif
* {directory} - relative directory structure from input path
* {name} - file name with extension
* {nameNoExtension} - file name without extension
* {extension} - file extension

__-d, --dry__              Only prints what will happen without actually doing it. It is recommended to combine it with
                         log level verbose.

__-m, --mode__             (Default: copy) Operation mode. Available modes: copy, move

__-l, --logLevel__         (Default: important) Determines how much information is printed on the screen. Options:
                         verbose, important, errorsOnly

__--no-skip-duplicate__    Disables duplicate skipping.

__--duplicate-format     (Default: _{number}) Format used for differentiating files with the same name. Use {number} for
                         number placeholder.

__--skip-existing__        Skips file if it already exists in the output.

__--max-depth__            Maximum directory recursion depth when scanning for files. 0 or omit for unlimited
                         (default), 1 = root directory only, 2 = root + one level of subdirectories, etc.


