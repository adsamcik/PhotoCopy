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
using K4os.Compression.LZ4;

namespace GeoIndexGenerator;

/// <summary>
/// Generates optimized geo-index files from GeoNames data.
/// 
/// Usage:
///   GeoIndexGenerator --download --output ./data
///   GeoIndexGenerator --input allCountries.txt --output ./data
///   GeoIndexGenerator --test-only --output ./data
/// </summary>
class Program
{
    const string GeoNamesUrl = "https://download.geonames.org/export/dump/allCountries.zip";
    const string CitiesUrl = "https://download.geonames.org/export/dump/cities15000.zip"; // Cities with pop > 15000
    const int DefaultPrecision = 4;

    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("GeoNames index generator for PhotoCopy");

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
            "Generate small test dataset (~1000 cities) for unit tests");

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
            "Prune locations within this distance (km). Within this radius, only the highest priority location is kept (P > A > L, then by population). Set to 0 to disable.");

        rootCommand.AddOption(downloadOption);
        rootCommand.AddOption(inputOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(testOnlyOption);
        rootCommand.AddOption(citiesOnlyOption);
        rootCommand.AddOption(precisionOption);
        rootCommand.AddOption(pruneDistanceOption);

        rootCommand.SetHandler(async (bool download, FileInfo? input, DirectoryInfo output, bool testOnly, bool citiesOnly, int precision, double pruneDistance) =>
        {
            try
            {
                await RunAsync(download, input, output, testOnly, citiesOnly, precision, pruneDistance);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, downloadOption, inputOption, outputOption, testOnlyOption, citiesOnlyOption, precisionOption, pruneDistanceOption);

        return await rootCommand.InvokeAsync(args);
    }

    /// <summary>
    /// Main entry point for programmatic access.
    /// </summary>
    public static async Task RunAsync(bool download, FileInfo? input, DirectoryInfo output, bool testOnly, bool citiesOnly, int precision, double pruneDistanceKm = 5.0)
    {
        if (precision < 1 || precision > 6)
            throw new ArgumentException("Precision must be between 1 and 6");

        output.Create();

        string? dataPath = input?.FullName;

        if (testOnly)
        {
            Console.WriteLine("Generating test dataset...");
            var testData = GenerateTestData();
            await BuildIndexAsync(testData, output.FullName, precision, pruneDistanceKm);
            return;
        }

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

        await BuildIndexAsync(locations, output.FullName, precision, pruneDistanceKm);
    }

    static async Task DownloadAndExtractAsync(string url, string outputDir)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(30);

        var zipPath = Path.Combine(outputDir, Path.GetFileName(new Uri(url).LocalPath));

        // Download with progress
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

        // Extract
        Console.WriteLine("Extracting...");
        ZipFile.ExtractToDirectory(zipPath, outputDir, overwriteFiles: true);
        Console.WriteLine($"Extracted to {outputDir}");
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
        // 12: admin3_code, 13: admin4_code, 14: population, 15: elevation,
        // 16: dem, 17: timezone, 18: modification_date

        var parts = line.Split('\t');
        if (parts.Length < 15)
            return null;

        // Filter: only keep populated places and administrative divisions
        char featureClass = parts[6].Length > 0 ? parts[6][0] : ' ';
        // P = populated places, A = administrative divisions, L = areas (parks, reserves, regions)
        if (featureClass != 'P' && featureClass != 'A' && featureClass != 'L')
            return null;

        if (!double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) ||
            !double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
            return null;

        if (!int.TryParse(parts[0], out int geoNameId))
            return null;

        long.TryParse(parts[14], out long population);

        return new GeoLocation
        {
            GeoNameId = geoNameId,
            Name = parts[1],
            Latitude = lat,
            Longitude = lon,
            FeatureClass = featureClass,
            CountryCode = parts[8],
            Admin1Code = parts[10],
            Population = population
        };
    }

    static List<GeoLocation> GenerateTestData()
    {
        // Generate ~1000 test cities across the world
        var cities = new List<GeoLocation>
        {
            // Major world cities
            new() { GeoNameId = 1, Name = "New York", Latitude = 40.7128, Longitude = -74.0060, CountryCode = "US", Admin1Code = "NY", FeatureClass = 'P', Population = 8336817 },
            new() { GeoNameId = 2, Name = "Los Angeles", Latitude = 34.0522, Longitude = -118.2437, CountryCode = "US", Admin1Code = "CA", FeatureClass = 'P', Population = 3979576 },
            new() { GeoNameId = 3, Name = "Chicago", Latitude = 41.8781, Longitude = -87.6298, CountryCode = "US", Admin1Code = "IL", FeatureClass = 'P', Population = 2693976 },
            new() { GeoNameId = 4, Name = "Houston", Latitude = 29.7604, Longitude = -95.3698, CountryCode = "US", Admin1Code = "TX", FeatureClass = 'P', Population = 2320268 },
            new() { GeoNameId = 5, Name = "Phoenix", Latitude = 33.4484, Longitude = -112.0740, CountryCode = "US", Admin1Code = "AZ", FeatureClass = 'P', Population = 1680992 },
            new() { GeoNameId = 6, Name = "London", Latitude = 51.5074, Longitude = -0.1278, CountryCode = "GB", Admin1Code = "ENG", FeatureClass = 'P', Population = 8982000 },
            new() { GeoNameId = 7, Name = "Paris", Latitude = 48.8566, Longitude = 2.3522, CountryCode = "FR", Admin1Code = "IDF", FeatureClass = 'P', Population = 2161000 },
            new() { GeoNameId = 8, Name = "Berlin", Latitude = 52.5200, Longitude = 13.4050, CountryCode = "DE", Admin1Code = "BE", FeatureClass = 'P', Population = 3748148 },
            new() { GeoNameId = 9, Name = "Tokyo", Latitude = 35.6762, Longitude = 139.6503, CountryCode = "JP", Admin1Code = "13", FeatureClass = 'P', Population = 13960000 },
            new() { GeoNameId = 10, Name = "Sydney", Latitude = -33.8688, Longitude = 151.2093, CountryCode = "AU", Admin1Code = "NSW", FeatureClass = 'P', Population = 5312000 },
            new() { GeoNameId = 11, Name = "Moscow", Latitude = 55.7558, Longitude = 37.6173, CountryCode = "RU", Admin1Code = "MOW", FeatureClass = 'P', Population = 12537954 },
            new() { GeoNameId = 12, Name = "Beijing", Latitude = 39.9042, Longitude = 116.4074, CountryCode = "CN", Admin1Code = "11", FeatureClass = 'P', Population = 21540000 },
            new() { GeoNameId = 13, Name = "Mumbai", Latitude = 19.0760, Longitude = 72.8777, CountryCode = "IN", Admin1Code = "MH", FeatureClass = 'P', Population = 12478447 },
            new() { GeoNameId = 14, Name = "São Paulo", Latitude = -23.5505, Longitude = -46.6333, CountryCode = "BR", Admin1Code = "SP", FeatureClass = 'P', Population = 12325232 },
            new() { GeoNameId = 15, Name = "Cairo", Latitude = 30.0444, Longitude = 31.2357, CountryCode = "EG", Admin1Code = "C", FeatureClass = 'P', Population = 20076000 },
            new() { GeoNameId = 16, Name = "Mexico City", Latitude = 19.4326, Longitude = -99.1332, CountryCode = "MX", Admin1Code = "CMX", FeatureClass = 'P', Population = 8918653 },
            new() { GeoNameId = 17, Name = "Toronto", Latitude = 43.6532, Longitude = -79.3832, CountryCode = "CA", Admin1Code = "ON", FeatureClass = 'P', Population = 2731571 },
            new() { GeoNameId = 18, Name = "Seoul", Latitude = 37.5665, Longitude = 126.9780, CountryCode = "KR", Admin1Code = "11", FeatureClass = 'P', Population = 9776000 },
            new() { GeoNameId = 19, Name = "Singapore", Latitude = 1.3521, Longitude = 103.8198, CountryCode = "SG", Admin1Code = "", FeatureClass = 'P', Population = 5685807 },
            new() { GeoNameId = 20, Name = "Cape Town", Latitude = -33.9249, Longitude = 18.4241, CountryCode = "ZA", Admin1Code = "WC", FeatureClass = 'P', Population = 433688 },
        };

        // Add more US cities
        var usCities = new[]
        {
            ("San Francisco", 37.7749, -122.4194, "CA", 873965),
            ("Seattle", 47.6062, -122.3321, "WA", 753675),
            ("Denver", 39.7392, -104.9903, "CO", 727211),
            ("Boston", 42.3601, -71.0589, "MA", 692600),
            ("Miami", 25.7617, -80.1918, "FL", 467963),
            ("Atlanta", 33.7490, -84.3880, "GA", 498044),
            ("Dallas", 32.7767, -96.7970, "TX", 1304379),
            ("Philadelphia", 39.9526, -75.1652, "PA", 1584064),
            ("San Diego", 32.7157, -117.1611, "CA", 1423851),
            ("Portland", 45.5152, -122.6784, "OR", 652503),
            ("Las Vegas", 36.1699, -115.1398, "NV", 651319),
            ("Austin", 30.2672, -97.7431, "TX", 978908),
            ("Nashville", 36.1627, -86.7816, "TN", 689447),
            ("Orlando", 28.5383, -81.3792, "FL", 307573),
            ("Minneapolis", 44.9778, -93.2650, "MN", 429954),
        };

        int id = 100;
        foreach (var (name, lat, lon, state, pop) in usCities)
        {
            cities.Add(new GeoLocation
            {
                GeoNameId = id++,
                Name = name,
                Latitude = lat,
                Longitude = lon,
                CountryCode = "US",
                Admin1Code = state,
                FeatureClass = 'P',
                Population = pop
            });
        }

        // Add European cities
        var euCities = new[]
        {
            ("Amsterdam", 52.3676, 4.9041, "NL", "NH", 872680),
            ("Rome", 41.9028, 12.4964, "IT", "RM", 2872800),
            ("Madrid", 40.4168, -3.7038, "ES", "M", 3223334),
            ("Barcelona", 41.3851, 2.1734, "ES", "CT", 1620343),
            ("Vienna", 48.2082, 16.3738, "AT", "9", 1897491),
            ("Prague", 50.0755, 14.4378, "CZ", "10", 1309000),
            ("Munich", 48.1351, 11.5820, "DE", "BY", 1471508),
            ("Dublin", 53.3498, -6.2603, "IE", "L", 544107),
            ("Brussels", 50.8503, 4.3517, "BE", "BRU", 1208542),
            ("Stockholm", 59.3293, 18.0686, "SE", "AB", 975904),
            ("Oslo", 59.9139, 10.7522, "NO", "03", 693494),
            ("Copenhagen", 55.6761, 12.5683, "DK", "84", 602481),
            ("Helsinki", 60.1699, 24.9384, "FI", "18", 653835),
            ("Warsaw", 52.2297, 21.0122, "PL", "MZ", 1790658),
            ("Budapest", 47.4979, 19.0402, "HU", "BU", 1752286),
        };

        foreach (var (name, lat, lon, country, admin1, pop) in euCities)
        {
            cities.Add(new GeoLocation
            {
                GeoNameId = id++,
                Name = name,
                Latitude = lat,
                Longitude = lon,
                CountryCode = country,
                Admin1Code = admin1,
                FeatureClass = 'P',
                Population = pop
            });
        }

        Console.WriteLine($"Generated {cities.Count} test cities");
        return cities;
    }

    static async Task BuildIndexAsync(List<GeoLocation> locations, string outputDir, int precision, double pruneDistanceKm = 5.0)
    {
        Console.WriteLine($"Building index at precision {precision}...");
        var sw = Stopwatch.StartNew();

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

        int originalLocationCount = locations.Count;
        Console.WriteLine($"Grouped into {cells.Count} cells");

        // Prune nearby locations - keep only the best representative per area
        if (pruneDistanceKm > 0)
        {
            int prunedCount = 0;
            foreach (var (geohash, cellLocations) in cells)
            {
                int before = cellLocations.Count;
                PruneNearbyLocations(cellLocations, pruneDistanceKm);
                prunedCount += before - cellLocations.Count;
            }
            Console.WriteLine($"Pruned {prunedCount:N0} locations within {pruneDistanceKm}km of higher-priority locations");
        }

        // Sort cells by geohash code for binary search
        var sortedCells = cells.OrderBy(kv => EncodeGeohashToUInt32(kv.Key)).ToList();

        // Write single data file and collect index entries
        string dataPath = Path.Combine(outputDir, "geo.geodata");
        var cellEntries = new List<(uint GeohashCode, long Offset, int CompressedSize, int UncompressedSize)>();
        int totalLocations = 0;

        await using (var dataStream = File.Create(dataPath))
        {
            foreach (var (geohash, cellLocations) in sortedCells)
            {
                long offset = dataStream.Position;
                var (compressedSize, uncompressedSize) = WriteCellBlock(dataStream, cellLocations);
                uint code = EncodeGeohashToUInt32(geohash);
                cellEntries.Add((code, offset, compressedSize, uncompressedSize));
                totalLocations += cellLocations.Count;
            }
        }

        var dataInfo = new FileInfo(dataPath);

        // Write index file
        string indexPath = Path.Combine(outputDir, "geo.geoindex");
        await using var indexStream = File.Create(indexPath);

        // Write index header (48 bytes)
        var headerBytes = new byte[48];
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(0, 4), 0x58444947); // "GIDX"
        BinaryPrimitives.WriteUInt16LittleEndian(headerBytes.AsSpan(4, 2), 1); // Version 1
        headerBytes[6] = (byte)precision;
        headerBytes[7] = 0; // Flags: reserved
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(8, 4), (uint)cellEntries.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(12, 4), (uint)totalLocations);
        BinaryPrimitives.WriteInt64LittleEndian(headerBytes.AsSpan(16, 8), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        BinaryPrimitives.WriteInt64LittleEndian(headerBytes.AsSpan(24, 8), dataInfo.Length); // Data file size
        // Rest is reserved/zero
        await indexStream.WriteAsync(headerBytes);

        // Write cell index entries (20 bytes each: 4 code + 8 offset + 4 compressed + 4 uncompressed)
        var entryBytes = new byte[20];
        foreach (var (code, offset, compressedSize, uncompressedSize) in cellEntries)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(entryBytes.AsSpan(0, 4), code);
            BinaryPrimitives.WriteInt64LittleEndian(entryBytes.AsSpan(4, 8), offset);
            BinaryPrimitives.WriteInt32LittleEndian(entryBytes.AsSpan(12, 4), compressedSize);
            BinaryPrimitives.WriteInt32LittleEndian(entryBytes.AsSpan(16, 4), uncompressedSize);
            await indexStream.WriteAsync(entryBytes);
        }

        sw.Stop();
        var indexInfo = new FileInfo(indexPath);

        Console.WriteLine($"Build complete in {sw.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"  Index file: {indexPath} ({indexInfo.Length / 1024.0:F2} KB)");
        Console.WriteLine($"  Data file: {dataPath} ({dataInfo.Length / 1024.0 / 1024.0:F2} MB)");
        Console.WriteLine($"  Cells: {cellEntries.Count:N0}, Locations: {totalLocations:N0}");
    }

    static (int CompressedSize, int UncompressedSize) WriteCellBlock(Stream stream, List<GeoLocation> locations)
    {
        // Build uncompressed block
        using var blockStream = new MemoryStream();
        using var writer = new BinaryWriter(blockStream);

        // Build string pool
        var stringPool = new MemoryStream();
        var stringOffsets = new Dictionary<string, ushort>();

        ushort GetOrAddString(string s)
        {
            if (string.IsNullOrEmpty(s))
                return 0;
            if (stringOffsets.TryGetValue(s, out var offset))
                return offset;
            offset = (ushort)stringPool.Position;
            var bytes = Encoding.UTF8.GetBytes(s);
            stringPool.Write(bytes);
            stringPool.WriteByte(0); // null terminator
            stringOffsets[s] = offset;
            return offset;
        }

        // Pre-calculate string offsets
        var entries = new List<(GeoLocation Loc, ushort CityOffset, ushort StateOffset, ushort CountryOffset)>();
        foreach (var loc in locations)
        {
            var cityOffset = GetOrAddString(loc.Name);
            var stateOffset = GetOrAddString(loc.Admin1Code);
            var countryOffset = GetOrAddString(loc.CountryCode);
            entries.Add((loc, cityOffset, stateOffset, countryOffset));
        }

        // Write header
        ushort entriesSize = (ushort)(12 + entries.Count * 22); // 12-byte header + 22 bytes per entry
        writer.Write((ushort)entries.Count);
        writer.Write(entriesSize); // StringPoolOffset
        writer.Write((ushort)stringPool.Length);
        writer.Write((ushort)0); // Reserved
        writer.Write((uint)0); // Reserved

        // Write entries
        foreach (var (loc, cityOffset, stateOffset, countryOffset) in entries)
        {
            writer.Write((int)(loc.Latitude * 1_000_000));
            writer.Write((int)(loc.Longitude * 1_000_000));
            writer.Write(loc.GeoNameId);
            writer.Write(cityOffset);
            writer.Write(stateOffset);
            writer.Write(countryOffset);
            writer.Write((ushort)Math.Min(loc.Population / 1000, ushort.MaxValue));
            writer.Write((byte)loc.FeatureClass);
            writer.Write((byte)0); // Reserved
        }

        // Write string pool
        stringPool.Position = 0;
        stringPool.CopyTo(blockStream);

        // Compress with Brotli (Optimal = level 4, good balance of speed and compression)
        var uncompressedData = blockStream.ToArray();
        using var compressedStream = new MemoryStream();
        using (var brotli = new BrotliStream(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            brotli.Write(uncompressedData, 0, uncompressedData.Length);
        }
        var compressedData = compressedStream.ToArray();

        stream.Write(compressedData, 0, compressedData.Length);
        return (compressedData.Length, uncompressedData.Length);
    }

    // Simple geohash implementation for the generator
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
        
        // Left-align the geohash bits (shift to fill 30 bits for 6 chars max)
        int bitsUsed = geohash.Length * 5;
        result <<= (30 - bitsUsed);
        
        // Store: [3-bit length in upper bits][29-bit geohash data]
        return ((uint)geohash.Length << 29) | (result >> 1);
    }

    /// <summary>
    /// Prunes nearby locations, keeping only the best representative per area.
    /// Priority: P (populated) > A (admin) > L (area), then by population.
    /// </summary>
    static void PruneNearbyLocations(List<GeoLocation> locations, double thresholdKm)
    {
        if (locations.Count <= 1)
            return;

        // Sort by priority (P > A > L) then by population descending
        // This ensures we process the best locations first
        locations.Sort((a, b) =>
        {
            int priorityA = GetFeatureClassPriority(a.FeatureClass);
            int priorityB = GetFeatureClassPriority(b.FeatureClass);
            if (priorityA != priorityB)
                return priorityB.CompareTo(priorityA); // Higher priority first
            return b.Population.CompareTo(a.Population); // Higher population first
        });

        // Mark locations to keep using a simple greedy algorithm
        var keep = new bool[locations.Count];
        keep[0] = true; // Always keep the best one

        for (int i = 1; i < locations.Count; i++)
        {
            bool shouldKeep = true;
            
            // Check if this location is too close to any already-kept location
            for (int j = 0; j < i; j++)
            {
                if (!keep[j])
                    continue;

                double distance = HaversineDistance(
                    locations[i].Latitude, locations[i].Longitude,
                    locations[j].Latitude, locations[j].Longitude);

                if (distance <= thresholdKm)
                {
                    // Too close to a better location - prune this one
                    shouldKeep = false;
                    break;
                }
            }

            keep[i] = shouldKeep;
        }

        // Remove pruned locations (iterate backwards to preserve indices)
        for (int i = locations.Count - 1; i >= 0; i--)
        {
            if (!keep[i])
                locations.RemoveAt(i);
        }
    }

    /// <summary>
    /// Gets the priority of a feature class. Higher = better.
    /// P (populated places) > A (administrative) > L (areas/landmarks)
    /// </summary>
    static int GetFeatureClassPriority(char featureClass)
    {
        return featureClass switch
        {
            'P' => 3, // Populated places (cities, towns, villages)
            'A' => 2, // Administrative divisions
            'L' => 1, // Areas, parks, reserves
            _ => 0,
        };
    }

    /// <summary>
    /// Calculates the distance between two coordinates using the Haversine formula.
    /// </summary>
    static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0; // Earth's radius in km

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

class GeoLocation
{
    public int GeoNameId { get; set; }
    public string Name { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public char FeatureClass { get; set; }
    public string CountryCode { get; set; } = "";
    public string Admin1Code { get; set; } = "";
    public long Population { get; set; }
}
