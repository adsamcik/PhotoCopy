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

---

## Location-Based Organization

PhotoCopy supports organizing photos by geographic location using GPS metadata from your photos. Location variables include:

* `{district}` - Neighborhood or district name
* `{city}` - City name
* `{county}` - County/region name
* `{state}` - State/province name
* `{country}` - Country name or code

### Customizing Unknown Location Handling

When a photo lacks GPS metadata or the location can't be determined, PhotoCopy provides flexible options for handling these cases.

#### Global Fallback Setting

Set a default fallback in `appsettings.yaml`:

```yaml
photoCopy:
  unknownLocationFallback: "Unknown"  # Default folder for photos without location
```

#### Inline Fallbacks

Override the global fallback for specific variables using the pipe (`|`) syntax:

```bash
# Use "NoLocation" instead of the global fallback
./PhotoCopy -i "/photos" -o "/sorted/{year}/{city|NoLocation}/{name}"
```

#### Variable Chains

Try multiple location levels in order, falling back through the chain:

```bash
# Try city first, then country if city is unavailable
./PhotoCopy -i "/photos" -o "/sorted/{year}/{city|country}/{name}"

# Three-level chain: try district, then city, then country
./PhotoCopy -i "/photos" -o "/sorted/{year}/{district|city|country}/{name}"

# Chain ending with a literal fallback
./PhotoCopy -i "/photos" -o "/sorted/{year}/{city|country|Unknown}/{name}"
```

#### Empty Fallbacks

Omit a folder entirely when the variable is unavailable:

```bash
# If city is unknown, the folder level is simply omitted
./PhotoCopy -i "/photos" -o "/sorted/{year}/{city|}/{month}/{name}"
# Photos with city: /sorted/2024/Prague/01/photo.jpg
# Photos without:   /sorted/2024/01/photo.jpg
```

#### Conditional Variables (Threshold-Based)

Use location values only when you have enough photos to justify a separate folder:

```bash
# Only create city folders if there are at least 10 photos from that city
./PhotoCopy -i "/photos" -o "/sorted/{year}/{city?min=10|country}/{name}"
```

__How it works:__ PhotoCopy performs a two-pass operation:

1. First pass: Scans all files and counts photos per location
2. Second pass: Applies thresholds and organizes files

__Example results with `{city?min=10|country}`:__

```text
Source: 15 photos from Prague, 3 from Brno, 8 from Vienna

Output:
  - Prague photos → /sorted/2024/Prague/photo.jpg     (15 ≥ 10, uses city)
  - Brno photos   → /sorted/2024/CZ/photo.jpg         (3 < 10, falls back to country)
  - Vienna photos → /sorted/2024/AT/photo.jpg         (8 < 10, falls back to country)
```

__Advanced threshold examples:__

```bash
# Maximum threshold: use city only if ≤100 photos (avoid huge folders)
./PhotoCopy -i "/photos" -o "/sorted/{city?max=100|country}/{name}"

# Range: use city if between 10-100 photos
./PhotoCopy -i "/photos" -o "/sorted/{city?min=10,max=100|country}/{name}"

# Chained conditions: city if ≥10, else country if ≥5, else literal
./PhotoCopy -i "/photos" -o "/sorted/{city?min=10|country?min=5|Misc}/{name}"
```


