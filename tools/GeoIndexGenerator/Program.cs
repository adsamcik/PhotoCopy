using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace GeoIndexGenerator;

/// <summary>
/// Generates optimized geo-index files from GeoNames data.
/// 
/// Produces v1 format with:
/// - PlaceType classification (District, Village, Town, City, AdminSeat, Capital)
/// - Full state/admin names (not codes)
/// - Country table with full country names
/// - Entries sorted: districts first, cities last for efficient filtering
/// 
/// Usage:
///   GeoIndexGenerator --download --output ./data
///   GeoIndexGenerator --input allCountries.txt --output ./data
///   GeoIndexGenerator --test-only --output ./data
/// </summary>
class Program
{
    const string GeoNamesUrl = "https://download.geonames.org/export/dump/allCountries.zip";
    const string CitiesUrl = "https://download.geonames.org/export/dump/cities15000.zip";
    const string Admin1Url = "https://download.geonames.org/export/dump/admin1CodesASCII.txt";
    const string CountryInfoUrl = "https://download.geonames.org/export/dump/countryInfo.txt";
    const int DefaultPrecision = 4;

    // Magic numbers for v1 format
    const uint IndexMagic = 0x31494750; // "PGI1"
    const uint DataMagic = 0x31444750; // "PGD1"
    const ushort FormatVersion = 1;

    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("GeoNames index generator for PhotoCopy (v1 format)");

        var downloadOption = new Option<bool>(
            "--download",
            "Download GeoNames data from the internet");

        var inputOption = new Option<FileInfo?>(
            "--input",
            "Path to existing GeoNames TSV file (e.g., allCountries.txt)");

        var outputOption = new Option<DirectoryInfo>(
            "--output",
            () => new DirectoryInfo("."),
            "Output directory for generated index files");

        var testOnlyOption = new Option<bool>(
            "--test-only",
            "Generate small test dataset (~100 cities) for unit tests");

        var citiesOnlyOption = new Option<bool>(
            "--cities-only",
            "Use cities15000.zip instead of allCountries.zip (smaller, faster)");

        var precisionOption = new Option<int>(
            "--precision",
            () => DefaultPrecision,
            "Geohash precision level (1-6, default 4)");

        var pruneDistanceOption = new Option<double>(
            "--prune-distance",
            () => 5.0,
            "Prune locations within this distance (km). Set to 0 to disable.");

        var boundariesOption = new Option<bool>(
            "--boundaries",
            "Generate country boundary data (.geobounds) from Natural Earth");

        var simplifyToleranceOption = new Option<double>(
            "--simplify-tolerance",
            () => 0.01,
            "Polygon simplification tolerance in degrees (default 0.01 = ~1km)");

        rootCommand.AddOption(downloadOption);
        rootCommand.AddOption(inputOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(testOnlyOption);
        rootCommand.AddOption(citiesOnlyOption);
        rootCommand.AddOption(precisionOption);
        rootCommand.AddOption(pruneDistanceOption);
        rootCommand.AddOption(boundariesOption);
        rootCommand.AddOption(simplifyToleranceOption);

        rootCommand.SetHandler(async (bool download, FileInfo? input, DirectoryInfo output, bool testOnly, bool citiesOnly, int precision, double pruneDistance, bool boundaries, double simplifyTolerance) =>
        {
            try
            {
                if (boundaries)
                {
                    await BoundaryGenerator.GenerateAsync(output, simplifyTolerance);
                }
                else
                {
                    await RunAsync(download, input, output, testOnly, citiesOnly, precision, pruneDistance);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, downloadOption, inputOption, outputOption, testOnlyOption, citiesOnlyOption, precisionOption, pruneDistanceOption, boundariesOption, simplifyToleranceOption);

        return await rootCommand.InvokeAsync(args);
    }

    public static async Task RunAsync(bool download, FileInfo? input, DirectoryInfo output, bool testOnly, bool citiesOnly, int precision, double pruneDistanceKm = 5.0)
    {
        if (precision < 1 || precision > 6)
            throw new ArgumentException("Precision must be between 1 and 6");

        output.Create();

        // Load lookup tables
        var admin1Lookup = new Dictionary<string, string>();
        var countryLookup = new Dictionary<string, string>();

        if (testOnly)
        {
            Console.WriteLine("Generating test dataset...");
            // Use hardcoded test data with pre-resolved names
            var testData = GenerateTestData(out admin1Lookup, out countryLookup);
            await BuildIndexAsync(testData, admin1Lookup, countryLookup, output.FullName, precision, pruneDistanceKm);
            return;
        }

        // Download or locate lookup files
        string admin1Path = Path.Combine(output.FullName, "admin1CodesASCII.txt");
        string countryPath = Path.Combine(output.FullName, "countryInfo.txt");

        if (download || !File.Exists(admin1Path))
        {
            Console.WriteLine("Downloading admin1 codes...");
            await DownloadFileAsync(Admin1Url, admin1Path);
        }

        if (download || !File.Exists(countryPath))
        {
            Console.WriteLine("Downloading country info...");
            await DownloadFileAsync(CountryInfoUrl, countryPath);
        }

        // Parse lookup tables
        Console.WriteLine("Loading lookup tables...");
        admin1Lookup = await ParseAdmin1CodesAsync(admin1Path);
        countryLookup = await ParseCountryInfoAsync(countryPath);
        Console.WriteLine($"  Loaded {admin1Lookup.Count} admin1 codes, {countryLookup.Count} countries");

        string? dataPath = input?.FullName;

        if (download)
        {
            string url = citiesOnly ? CitiesUrl : GeoNamesUrl;
            string fileName = citiesOnly ? "cities15000.zip" : "allCountries.zip";
            dataPath = Path.Combine(output.FullName, fileName.Replace(".zip", ".txt"));

            if (!File.Exists(dataPath))
            {
                Console.WriteLine($"Downloading {url}...");
                await DownloadAndExtractAsync(url, output.FullName);
            }
            else
            {
                Console.WriteLine($"Using cached file: {dataPath}");
            }
        }

        if (string.IsNullOrEmpty(dataPath) || !File.Exists(dataPath))
        {
            throw new FileNotFoundException("No input file specified. Use --download or --input");
        }

        Console.WriteLine($"Parsing {dataPath}...");
        var locations = await ParseGeoNamesAsync(dataPath);

        await BuildIndexAsync(locations, admin1Lookup, countryLookup, output.FullName, precision, pruneDistanceKm);
    }

    static async Task DownloadFileAsync(string url, string destPath)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(10);
        var content = await httpClient.GetStringAsync(url);
        await File.WriteAllTextAsync(destPath, content);
    }

    static async Task DownloadAndExtractAsync(string url, string outputDir)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(30);

        var zipPath = Path.Combine(outputDir, Path.GetFileName(new Uri(url).LocalPath));

        Console.Write("Downloading: ");
        using (var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? -1;

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = File.Create(zipPath);

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;
            int lastProgress = -1;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    int progress = (int)(totalRead * 100 / totalBytes);
                    if (progress != lastProgress && progress % 10 == 0)
                    {
                        Console.Write($"{progress}% ");
                        lastProgress = progress;
                    }
                }
            }
        }
        Console.WriteLine("Done!");

        Console.WriteLine("Extracting...");
        ZipFile.ExtractToDirectory(zipPath, outputDir, overwriteFiles: true);
        Console.WriteLine($"Extracted to {outputDir}");
    }

    static async Task<Dictionary<string, string>> ParseAdmin1CodesAsync(string path)
    {
        var result = new Dictionary<string, string>();
        
        foreach (var line in await File.ReadAllLinesAsync(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            // Format: CountryCode.Admin1Code<TAB>Name<TAB>ASCIIName<TAB>GeonameId
            var parts = line.Split('\t');
            if (parts.Length >= 2)
            {
                result[parts[0]] = parts[1]; // Key = "US.CA", Value = "California"
            }
        }

        return result;
    }

    static async Task<Dictionary<string, string>> ParseCountryInfoAsync(string path)
    {
        var result = new Dictionary<string, string>();

        foreach (var line in await File.ReadAllLinesAsync(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            // Format: ISO<TAB>ISO3<TAB>ISO-Numeric<TAB>fips<TAB>Country<TAB>...
            var parts = line.Split('\t');
            if (parts.Length >= 5)
            {
                result[parts[0]] = parts[4]; // Key = "US", Value = "United States"
            }
        }

        return result;
    }

    static async Task<List<GeoLocation>> ParseGeoNamesAsync(string path)
    {
        var locations = new List<GeoLocation>();
        var sw = Stopwatch.StartNew();
        int lineCount = 0;

        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            lineCount++;

            if (lineCount % 1_000_000 == 0)
            {
                Console.WriteLine($"  Parsed {lineCount:N0} lines, {locations.Count:N0} locations kept...");
            }

            var loc = ParseGeoNamesLine(line);
            if (loc != null)
            {
                locations.Add(loc);
            }
        }

        sw.Stop();
        Console.WriteLine($"Parsed {lineCount:N0} lines in {sw.Elapsed.TotalSeconds:F1}s, kept {locations.Count:N0} locations");
        return locations;
    }

    static GeoLocation? ParseGeoNamesLine(string line)
    {
        // GeoNames format: tab-separated
        // 0: geonameId, 1: name, 2: asciiname, 3: alternatenames,
        // 4: latitude, 5: longitude, 6: feature_class, 7: feature_code,
        // 8: country_code, 9: cc2, 10: admin1_code, 11: admin2_code,
        // 12: admin3_code, 13: admin4_code, 14: population, ...

        var parts = line.Split('\t');
        if (parts.Length < 15)
            return null;

        // Only keep populated places (feature class P)
        char featureClass = parts[6].Length > 0 ? parts[6][0] : ' ';
        if (featureClass != 'P')
            return null;

        if (!double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) ||
            !double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
            return null;

        long.TryParse(parts[14], out long population);

        string featureCode = parts[7];

        return new GeoLocation
        {
            Name = parts[1],
            Latitude = lat,
            Longitude = lon,
            FeatureCode = featureCode,
            CountryCode = parts[8],
            Admin1Code = parts[10],
            Population = population
        };
    }

    static List<GeoLocation> GenerateTestData(out Dictionary<string, string> admin1Lookup, out Dictionary<string, string> countryLookup)
    {
        admin1Lookup = new Dictionary<string, string>
        {
            ["US.NY"] = "New York",
            ["US.CA"] = "California",
            ["US.TX"] = "Texas",
            ["US.IL"] = "Illinois",
            ["GB.ENG"] = "England",
            ["FR.IDF"] = "Île-de-France",
            ["DE.BE"] = "Berlin",
            ["JP.13"] = "Tokyo",
            ["AU.NSW"] = "New South Wales",
            ["CZ.10"] = "Prague",
        };

        countryLookup = new Dictionary<string, string>
        {
            ["US"] = "United States",
            ["GB"] = "United Kingdom",
            ["FR"] = "France",
            ["DE"] = "Germany",
            ["JP"] = "Japan",
            ["AU"] = "Australia",
            ["CZ"] = "Czech Republic",
        };

        return new List<GeoLocation>
        {
            // Major cities (PPLC or large PPL)
            new() { Name = "New York", Latitude = 40.7128, Longitude = -74.0060, CountryCode = "US", Admin1Code = "NY", FeatureCode = "PPL", Population = 8336817 },
            new() { Name = "Los Angeles", Latitude = 34.0522, Longitude = -118.2437, CountryCode = "US", Admin1Code = "CA", FeatureCode = "PPL", Population = 3979576 },
            new() { Name = "Chicago", Latitude = 41.8781, Longitude = -87.6298, CountryCode = "US", Admin1Code = "IL", FeatureCode = "PPL", Population = 2693976 },
            new() { Name = "Houston", Latitude = 29.7604, Longitude = -95.3698, CountryCode = "US", Admin1Code = "TX", FeatureCode = "PPL", Population = 2320268 },
            new() { Name = "London", Latitude = 51.5074, Longitude = -0.1278, CountryCode = "GB", Admin1Code = "ENG", FeatureCode = "PPLC", Population = 8982000 },
            new() { Name = "Paris", Latitude = 48.8566, Longitude = 2.3522, CountryCode = "FR", Admin1Code = "IDF", FeatureCode = "PPLC", Population = 2161000 },
            new() { Name = "Berlin", Latitude = 52.5200, Longitude = 13.4050, CountryCode = "DE", Admin1Code = "BE", FeatureCode = "PPLC", Population = 3748148 },
            new() { Name = "Tokyo", Latitude = 35.6762, Longitude = 139.6503, CountryCode = "JP", Admin1Code = "13", FeatureCode = "PPLC", Population = 13960000 },
            new() { Name = "Sydney", Latitude = -33.8688, Longitude = 151.2093, CountryCode = "AU", Admin1Code = "NSW", FeatureCode = "PPL", Population = 5312000 },
            new() { Name = "Prague", Latitude = 50.0755, Longitude = 14.4378, CountryCode = "CZ", Admin1Code = "10", FeatureCode = "PPLC", Population = 1309000 },
            
            // Districts (PPLX) - neighborhoods within cities
            new() { Name = "Manhattan", Latitude = 40.7831, Longitude = -73.9712, CountryCode = "US", Admin1Code = "NY", FeatureCode = "PPLX", Population = 1628706 },
            new() { Name = "Brooklyn", Latitude = 40.6782, Longitude = -73.9442, CountryCode = "US", Admin1Code = "NY", FeatureCode = "PPLX", Population = 2559903 },
            new() { Name = "Westminster", Latitude = 51.4975, Longitude = -0.1357, CountryCode = "GB", Admin1Code = "ENG", FeatureCode = "PPLX", Population = 255324 },
            new() { Name = "Stodůlky", Latitude = 50.0453, Longitude = 14.3086, CountryCode = "CZ", Admin1Code = "10", FeatureCode = "PPLX", Population = 15000 },
            
            // Small towns (PPL with small population)
            new() { Name = "Smallville", Latitude = 40.5, Longitude = -74.5, CountryCode = "US", Admin1Code = "NY", FeatureCode = "PPL", Population = 5000 },
        };
    }

    static async Task BuildIndexAsync(List<GeoLocation> locations, 
        Dictionary<string, string> admin1Lookup, 
        Dictionary<string, string> countryLookup,
        string outputDir, int precision, double pruneDistanceKm = 5.0)
    {
        Console.WriteLine($"Building v1 index at precision {precision}...");
        var sw = Stopwatch.StartNew();

        // Build country table (byte index → country name)
        var countryIndex = new Dictionary<string, byte>(); // country code → index
        var countryNames = new List<string>(); // index → country name
        
        foreach (var loc in locations)
        {
            if (!countryIndex.ContainsKey(loc.CountryCode))
            {
                if (countryNames.Count >= 255)
                {
                    Console.WriteLine($"Warning: More than 255 countries, truncating.");
                    break;
                }
                byte idx = (byte)countryNames.Count;
                countryIndex[loc.CountryCode] = idx;
                string name = countryLookup.TryGetValue(loc.CountryCode, out var n) ? n : loc.CountryCode;
                countryNames.Add(name);
            }
        }
        Console.WriteLine($"  {countryNames.Count} unique countries");

        // Group locations by geohash cell
        var cells = new Dictionary<string, List<GeoLocation>>();
        foreach (var loc in locations)
        {
            string geohash = EncodeGeohash(loc.Latitude, loc.Longitude, precision);
            if (!cells.TryGetValue(geohash, out var list))
            {
                list = new List<GeoLocation>();
                cells[geohash] = list;
            }
            list.Add(loc);
        }

        Console.WriteLine($"  Grouped into {cells.Count} cells");

        // Prune nearby locations
        if (pruneDistanceKm > 0)
        {
            int prunedCount = 0;
            foreach (var (_, cellLocations) in cells)
            {
                int before = cellLocations.Count;
                PruneNearbyLocations(cellLocations, pruneDistanceKm);
                prunedCount += before - cellLocations.Count;
            }
            Console.WriteLine($"  Pruned {prunedCount:N0} locations within {pruneDistanceKm}km of higher-priority locations");
        }

        // Sort cells by geohash code for binary search
        var sortedCells = cells.OrderBy(kv => EncodeGeohashToUInt32(kv.Key)).ToList();

        // Write data file
        string dataPath = Path.Combine(outputDir, "geo.geodata");
        var cellEntries = new List<(uint GeohashCode, long Offset, int CompressedSize)>();
        int totalLocations = 0;

        await using (var dataStream = File.Create(dataPath))
        {
            foreach (var (geohash, cellLocations) in sortedCells)
            {
                long offset = dataStream.Position;
                int compressedSize = WriteCellBlock(dataStream, cellLocations, admin1Lookup, countryIndex);
                uint code = EncodeGeohashToUInt32(geohash);
                cellEntries.Add((code, offset, compressedSize));
                totalLocations += cellLocations.Count;
            }
        }

        var dataInfo = new FileInfo(dataPath);

        // Write index file
        string indexPath = Path.Combine(outputDir, "geo.geoindex");
        await using var indexStream = File.Create(indexPath);

        // 1. Write header (32 bytes)
        var headerBytes = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(0, 4), IndexMagic);
        BinaryPrimitives.WriteUInt16LittleEndian(headerBytes.AsSpan(4, 2), FormatVersion);
        headerBytes[6] = (byte)precision;
        headerBytes[7] = (byte)countryNames.Count;
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(8, 4), (uint)cellEntries.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(12, 4), (uint)totalLocations);
        BinaryPrimitives.WriteInt64LittleEndian(headerBytes.AsSpan(16, 8), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        BinaryPrimitives.WriteInt64LittleEndian(headerBytes.AsSpan(24, 8), dataInfo.Length);
        await indexStream.WriteAsync(headerBytes);

        // 2. Write country table (4 bytes per entry)
        var countryPool = new MemoryStream();
        var countryPoolOffsets = new ushort[countryNames.Count];
        for (int i = 0; i < countryNames.Count; i++)
        {
            countryPoolOffsets[i] = (ushort)countryPool.Position;
            var nameBytes = Encoding.UTF8.GetBytes(countryNames[i]);
            countryPool.Write(nameBytes);
            countryPool.WriteByte(0); // null terminator
        }

        // Get the country codes in order
        var countryCodesByIndex = countryIndex.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToArray();
        var countryEntryBytes = new byte[4];
        for (int i = 0; i < countryNames.Count; i++)
        {
            string code = countryCodesByIndex[i];
            // 2 bytes: country code as ASCII
            countryEntryBytes[0] = (byte)code[0];
            countryEntryBytes[1] = (byte)(code.Length > 1 ? code[1] : ' ');
            // 2 bytes: offset into string pool
            BinaryPrimitives.WriteUInt16LittleEndian(countryEntryBytes.AsSpan(2, 2), countryPoolOffsets[i]);
            await indexStream.WriteAsync(countryEntryBytes);
        }

        // 3. Write country name string pool
        countryPool.Position = 0;
        await countryPool.CopyToAsync(indexStream);

        // 4. Write cell index entries (16 bytes each)
        var entryBytes = new byte[16];
        foreach (var (code, offset, compressedSize) in cellEntries)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(entryBytes.AsSpan(0, 4), code);
            BinaryPrimitives.WriteInt64LittleEndian(entryBytes.AsSpan(4, 8), offset);
            BinaryPrimitives.WriteInt32LittleEndian(entryBytes.AsSpan(12, 4), compressedSize);
            await indexStream.WriteAsync(entryBytes);
        }

        sw.Stop();
        var indexInfo = new FileInfo(indexPath);

        Console.WriteLine($"Build complete in {sw.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"  Index file: {indexPath} ({indexInfo.Length / 1024.0:F2} KB)");
        Console.WriteLine($"  Data file: {dataPath} ({dataInfo.Length / 1024.0 / 1024.0:F2} MB)");
        Console.WriteLine($"  Cells: {cellEntries.Count:N0}, Locations: {totalLocations:N0}");
    }

    static int WriteCellBlock(Stream stream, List<GeoLocation> locations, 
        Dictionary<string, string> admin1Lookup, Dictionary<string, byte> countryIndex)
    {
        // Sort entries: districts first (PlaceType < Town), cities last (PlaceType >= Town)
        var sortedLocations = locations.OrderBy(loc => GetPlaceType(loc.FeatureCode, loc.Population) >= PlaceType.Town ? 1 : 0).ToList();
        
        // Find city start index
        int cityStartIndex = sortedLocations.FindIndex(loc => GetPlaceType(loc.FeatureCode, loc.Population) >= PlaceType.Town);
        if (cityStartIndex < 0) cityStartIndex = sortedLocations.Count;

        // Build uncompressed block
        using var blockStream = new MemoryStream();
        using var writer = new BinaryWriter(blockStream);

        // Build string pool
        var stringPool = new MemoryStream();
        var stringOffsets = new Dictionary<string, ushort>();

        ushort GetOrAddString(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                // Add empty string at offset 0 if not present
                if (!stringOffsets.ContainsKey(""))
                {
                    stringOffsets[""] = 0;
                    stringPool.WriteByte(0); // null terminator for empty string
                }
                return 0;
            }
            if (stringOffsets.TryGetValue(s, out var offset))
                return offset;
            offset = (ushort)stringPool.Position;
            var bytes = Encoding.UTF8.GetBytes(s);
            stringPool.Write(bytes);
            stringPool.WriteByte(0);
            stringOffsets[s] = offset;
            return offset;
        }

        // Pre-calculate string offsets and resolve admin names
        var entries = new List<(GeoLocation Loc, ushort NameOffset, ushort StateOffset, byte CountryIdx, PlaceType PlaceType)>();
        foreach (var loc in sortedLocations)
        {
            var nameOffset = GetOrAddString(loc.Name);
            
            // Resolve admin1 code to full name
            string admin1Key = $"{loc.CountryCode}.{loc.Admin1Code}";
            string stateName = admin1Lookup.TryGetValue(admin1Key, out var resolved) ? resolved : loc.Admin1Code;
            var stateOffset = GetOrAddString(stateName);
            
            byte countryIdx = countryIndex.TryGetValue(loc.CountryCode, out var idx) ? idx : (byte)0;
            var placeType = GetPlaceType(loc.FeatureCode, loc.Population);
            
            entries.Add((loc, nameOffset, stateOffset, countryIdx, placeType));
        }

        // Write header (6 bytes)
        ushort entriesDataSize = (ushort)(6 + entries.Count * 14); // 6-byte header + 14 bytes per entry
        writer.Write((ushort)entries.Count);
        writer.Write((ushort)cityStartIndex);
        writer.Write(entriesDataSize); // StringPoolOffset

        // Write entries (14 bytes each)
        foreach (var (loc, nameOffset, stateOffset, countryIdx, placeType) in entries)
        {
            writer.Write((int)(loc.Latitude * 1_000_000));
            writer.Write((int)(loc.Longitude * 1_000_000));
            writer.Write(nameOffset);
            writer.Write(stateOffset);
            writer.Write(countryIdx);
            writer.Write((byte)placeType);
        }

        // Write string pool
        stringPool.Position = 0;
        stringPool.CopyTo(blockStream);

        // Compress with Brotli
        var uncompressedData = blockStream.ToArray();
        using var compressedStream = new MemoryStream();
        using (var brotli = new BrotliStream(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            brotli.Write(uncompressedData, 0, uncompressedData.Length);
        }
        var compressedData = compressedStream.ToArray();

        stream.Write(compressedData, 0, compressedData.Length);
        return compressedData.Length;
    }

    static PlaceType GetPlaceType(string featureCode, long population)
    {
        // Country capitals
        if (featureCode == "PPLC")
            return PlaceType.Capital;

        // Administrative seats (state/region capitals)
        if (featureCode.StartsWith("PPLA"))
            return PlaceType.AdminSeat;

        // District/neighborhood within a city
        if (featureCode == "PPLX")
            return PlaceType.District;

        // Locality (very small place)
        if (featureCode == "PPLL")
            return PlaceType.Village;

        // For generic PPL, classify by population
        return population switch
        {
            >= 100_000 => PlaceType.City,
            >= 10_000 => PlaceType.Town,
            _ => PlaceType.Village
        };
    }

    // Simple geohash implementation
    const string Base32Chars = "0123456789bcdefghjkmnpqrstuvwxyz";

    static string EncodeGeohash(double latitude, double longitude, int precision)
    {
        double minLat = -90.0, maxLat = 90.0;
        double minLon = -180.0, maxLon = 180.0;

        var result = new StringBuilder(precision);
        bool isLon = true;
        int bits = 0;
        int charValue = 0;

        while (result.Length < precision)
        {
            if (isLon)
            {
                double mid = (minLon + maxLon) / 2;
                if (longitude >= mid)
                {
                    charValue = (charValue << 1) | 1;
                    minLon = mid;
                }
                else
                {
                    charValue <<= 1;
                    maxLon = mid;
                }
            }
            else
            {
                double mid = (minLat + maxLat) / 2;
                if (latitude >= mid)
                {
                    charValue = (charValue << 1) | 1;
                    minLat = mid;
                }
                else
                {
                    charValue <<= 1;
                    maxLat = mid;
                }
            }

            isLon = !isLon;
            bits++;

            if (bits == 5)
            {
                result.Append(Base32Chars[charValue]);
                bits = 0;
                charValue = 0;
            }
        }

        return result.ToString();
    }

    static uint EncodeGeohashToUInt32(string geohash)
    {
        var decode = new int[128];
        for (int i = 0; i < Base32Chars.Length; i++)
        {
            decode[Base32Chars[i]] = i;
            if (char.IsLetter(Base32Chars[i]))
                decode[char.ToUpperInvariant(Base32Chars[i])] = i;
        }

        uint result = 0;
        foreach (char c in geohash)
        {
            result = (result << 5) | (uint)decode[c];
        }
        
        int bitsUsed = geohash.Length * 5;
        result <<= (30 - bitsUsed);
        
        return ((uint)geohash.Length << 29) | (result >> 1);
    }

    static void PruneNearbyLocations(List<GeoLocation> locations, double thresholdKm)
    {
        if (locations.Count <= 1)
            return;

        // Sort by importance: cities first (higher PlaceType), then by population
        locations.Sort((a, b) =>
        {
            var typeA = GetPlaceType(a.FeatureCode, a.Population);
            var typeB = GetPlaceType(b.FeatureCode, b.Population);
            if (typeA != typeB)
                return typeB.CompareTo(typeA); // Higher type first
            return b.Population.CompareTo(a.Population); // Higher population first
        });

        var keep = new bool[locations.Count];
        keep[0] = true;

        for (int i = 1; i < locations.Count; i++)
        {
            bool shouldKeep = true;
            
            for (int j = 0; j < i; j++)
            {
                if (!keep[j])
                    continue;

                double distance = HaversineDistance(
                    locations[i].Latitude, locations[i].Longitude,
                    locations[j].Latitude, locations[j].Longitude);

                if (distance <= thresholdKm)
                {
                    shouldKeep = false;
                    break;
                }
            }

            keep[i] = shouldKeep;
        }

        for (int i = locations.Count - 1; i >= 0; i--)
        {
            if (!keep[i])
                locations.RemoveAt(i);
        }
    }

    static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;

        double dLat = DegreesToRadians(lat2 - lat1);
        double dLon = DegreesToRadians(lon2 - lon1);

        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return R * c;
    }

    static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}

enum PlaceType : byte
{
    District = 0,
    Village = 1,
    Town = 2,
    City = 3,
    AdminSeat = 4,
    Capital = 5,
}

class GeoLocation
{
    public string Name { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string FeatureCode { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string Admin1Code { get; set; } = "";
    public long Population { get; set; }
}
