# PhotoCopy
 
Allows batch moving/copying photos to a new location organized by time.

## How to use

The following command will copy all files in /input/path to /output/path/with/variables
where each file will retain its relative directory but will be placed in addition year/month/extension directory inside the relative structure. Duplicate files will be appended with -number (eg. -1). And all files will be moved instead of copied.

`
./PhotoCopy -i "/input/path" -o "/output/path/with/variables/{directory}/{year}/{month}/{extension}/{name}" --duplicate-format "-{number}" --mode "Move"
`

### Options

-i, --input            Required. Set source folder

-o, --output           Required. Set output folder. Supported variables (case-sensitive): {year}, {month}, {day}, {name}, {directory}, {extension}

Variables:

* {year} -  year extracted from file creation date or exif
* {month} - month extracted from file creation date or exif
* {day} - day extracted from file creation date or exif
* {dayOfYear} - day of year (~1-365) from file creation date or exif
* {directory} - relative directory structure from input path
* {extension} - file extension
* {name} - file name with extension
* {nameNoExtension} - file name without extension

-d, --dry              True if no files should be moved and only printed to the command line.

-m, --mode             (Default: Copy) Operation mode. Available modes: Move, Copy

--skip-existing        Skips file if it already exists in the output.

-l, --logLevel         (Default: Important) Determines what is printed on screen. Options: Verbose, Important, ErrorsOnly

--no-skip-duplicate    Disables duplicate skipping.

--duplicate-format     (Default: _{number}) Format used for differentiating duplicates. Use {number} for number placeholder.