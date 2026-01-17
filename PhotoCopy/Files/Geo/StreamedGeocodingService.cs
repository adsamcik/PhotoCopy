using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PhotoCopy.Abstractions;
using PhotoCopy.Configuration;

namespace PhotoCopy.Files.Geo;

/// <summary>
/// High-performance reverse geocoding service that streams data from allCountries.txt on demand.
/// 
/// Architecture:
/// 1. Small in-memory index (~10MB) mapping geohash cells to lists of line byte offsets
/// 2. LRU cache of loaded cells (configurable memory limit)
/// 3. Direct file streaming for on-demand cell loading
/// 
/// On first run, scans the entire file to build the index (takes ~60-90 seconds for 13M lines).
/// Index is saved to disk and reused on subsequent runs (loads in ~1 second).
/// 
/// Performance characteristics:
/// - First run: ~60-90s to build index
/// - Subsequent starts: ~1s to load index
/// - First lookup in a region: ~5-20ms (file seeks + parse)
/// - Subsequent lookups in same region: ~0.01ms (cache hit)
/// - Memory usage: ~10MB index + cached cells (configurable)
/// - Supports dual tree lookup (district vs city) with population threshold
/// </summary>
public sealed class StreamedGeocodingService : IReverseGeocodingService, IDisposable
{
    private readonly ILogger<StreamedGeocodingService> _logger;
    private readonly PhotoCopyConfig _config;
    private readonly object _lock = new();

    private GeoStreamIndex? _index;
    private string? _dataFilePath;
    private StreamedCellCache? _cache;
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Default memory limit for cell cache (100MB).
    /// </summary>
    public const long DefaultCacheMemoryBytes = 100 * 1024 * 1024;

    /// <summary>
    /// Default maximum distance in km for a match to be considered valid.
    /// </summary>
    public const double DefaultMaxDistanceKm = 50.0;

    /// <summary>
    /// Default priority threshold in km - within this distance, prefer populated places over parks.
    /// </summary>
    public const double DefaultPriorityThresholdKm = 15.0;

    /// <summary>
    /// Minimum population for a place to be considered a "city" (not a district/village/neighborhood).
    /// </summary>
    public const long MinimumCityPopulation = 100_000;

    /// <summary>
    /// Default geohash precision (4 = ~20-40km cells).
    /// </summary>
    public const int DefaultPrecision = 4;

    /// <summary>
    /// Index file extension.
    /// </summary>
    public const string IndexExtension = ".geostreamindex";

    /// <summary>
    /// Gets whether the service has been initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// Gets cache statistics (null if not initialized).
    /// </summary>
    public string? CacheStatistics => _cache?.GetStatistics();

    public StreamedGeocodingService(ILogger<StreamedGeocodingService> logger, PhotoCopyConfig config)
    {
        _logger = logger;
        _config = config;
    }

    /// <summary>
    /// Initializes the geocoding service by loading or building the index.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        await Task.Run(() => Initialize(), cancellationToken);
    }

    private void Initialize()
    {
        if (_initialized)
            return;

        lock (_lock)
        {
            if (_initialized)
                return;

            try
            {
                var dataPath = FindDataFile();
                if (dataPath == null)
                {
                    _logger.LogWarning("GeoNames data file (allCountries.txt) not found. Reverse geocoding will be disabled.");
                    return;
                }

                _dataFilePath = dataPath;
                var indexPath = dataPath + IndexExtension;

                // Check if we need to build/rebuild the index
                bool needsRebuild = !File.Exists(indexPath) || 
                                    File.GetLastWriteTimeUtc(indexPath) < File.GetLastWriteTimeUtc(dataPath);

                if (needsRebuild)
                {
                    _logger.LogInformation("Building geo-stream index for {DataPath}... (this may take 1-2 minutes on first run)", dataPath);
                    var startTime = DateTime.UtcNow;
                    _index = GeoStreamIndex.Build(dataPath, indexPath, DefaultPrecision, _logger);
                    var buildTime = DateTime.UtcNow - startTime;
                    _logger.LogInformation("Geo-stream index built in {BuildTime:F1}s: {CellCount} cells, {EntryCount:N0} entries", 
                        buildTime.TotalSeconds, _index.CellCount, _index.TotalEntries);
                }
                else
                {
                    _logger.LogInformation("Loading geo-stream index from {IndexPath}", indexPath);
                    var startTime = DateTime.UtcNow;
                    _index = GeoStreamIndex.Load(indexPath);
                    var loadTime = DateTime.UtcNow - startTime;
                    _logger.LogInformation("Geo-stream index loaded in {LoadTime:F2}s: {CellCount} cells, {EntryCount:N0} entries",
                        loadTime.TotalSeconds, _index.CellCount, _index.TotalEntries);
                }

                _cache = new StreamedCellCache(DefaultCacheMemoryBytes);
                _initialized = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize geo-stream service. Reverse geocoding will be disabled.");
            }
        }
    }

    /// <summary>
    /// Performs reverse geocoding for the given coordinates.
    /// </summary>
    public LocationData? ReverseGeocode(double latitude, double longitude)
    {
        if (!_initialized || _index == null || _dataFilePath == null || _cache == null)
            return null;

        try
        {
            // Find nearest district (any populated place)
            var districtResult = FindNearest(latitude, longitude, DefaultMaxDistanceKm, includeAllPlaces: true);
            
            // Find nearest city (population >= MinimumCityPopulation)
            var cityResult = FindNearest(latitude, longitude, DefaultMaxDistanceKm, includeAllPlaces: false);

            if (districtResult == null && cityResult == null)
                return null;

            return new LocationData(
                District: districtResult?.Entry.Name ?? cityResult?.Entry.Name ?? string.Empty,
                City: cityResult?.Entry.Name,
                County: null, // Not stored in GeoNames format we use
                State: districtResult?.Entry.State ?? cityResult?.Entry.State,
                Country: districtResult?.Entry.Country ?? cityResult?.Entry.Country ?? string.Empty,
                Population: (cityResult?.Entry.Population ?? districtResult?.Entry.Population)
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during reverse geocoding for ({Lat}, {Lon})", latitude, longitude);
            return null;
        }
    }

    /// <summary>
    /// Finds the nearest location to the specified coordinates.
    /// </summary>
    private GeoStreamLookupResult? FindNearest(
        double latitude, 
        double longitude, 
        double maxDistanceKm,
        bool includeAllPlaces)
    {
        if (_index == null || _dataFilePath == null || _cache == null)
            return null;

        // Get the geohash for this location
        string geohash = Geohash.Encode(latitude, longitude, DefaultPrecision);

        // Collect candidates from this cell and neighbors
        List<(GeoStreamEntry Entry, string CellHash, double Distance)> candidates = [];

        foreach (var cellHash in Geohash.GetCellAndNeighbors(geohash))
        {
            var cell = GetOrLoadCell(cellHash);
            if (cell == null)
                continue;

            // Get the entries to search
            var entries = includeAllPlaces ? cell.AllEntries : cell.CityEntries;
            
            foreach (var entry in entries)
            {
                double distance = HaversineDistance(latitude, longitude, entry.Latitude, entry.Longitude);
                if (distance <= maxDistanceKm)
                {
                    candidates.Add((entry, cellHash, distance));
                }
            }
        }

        if (candidates.Count == 0)
            return null;

        // Apply priority logic: prefer populated places (P) over admin (A) over landmarks (L)
        var best = SelectBestCandidate(candidates, DefaultPriorityThresholdKm);

        return new GeoStreamLookupResult
        {
            Entry = best.Entry,
            DistanceKm = best.Distance,
            CellGeohash = best.CellHash,
            IsFromNeighborCell = best.CellHash != geohash
        };
    }

    /// <summary>
    /// Selects the best candidate using priority logic.
    /// </summary>
    private static (GeoStreamEntry Entry, string CellHash, double Distance) SelectBestCandidate(
        List<(GeoStreamEntry Entry, string CellHash, double Distance)> candidates,
        double priorityThresholdKm)
    {
        var best = candidates[0];
        int bestPriority = GeoFeatureClass.GetPriority(best.Entry.FeatureCode ?? "PPL");

        for (int i = 1; i < candidates.Count; i++)
        {
            var current = candidates[i];
            int currentPriority = GeoFeatureClass.GetPriority(current.Entry.FeatureCode ?? "PPL");

            bool isBetter;
            if (current.Distance <= priorityThresholdKm && best.Distance <= priorityThresholdKm)
            {
                // Both within threshold: prefer better priority, then closer
                isBetter = currentPriority < bestPriority ||
                           (currentPriority == bestPriority && current.Distance < best.Distance);
            }
            else if (current.Distance <= priorityThresholdKm)
            {
                // Current is within threshold, best isn't: prefer current
                isBetter = true;
            }
            else if (best.Distance <= priorityThresholdKm)
            {
                // Best is within threshold, current isn't: keep best
                isBetter = false;
            }
            else
            {
                // Both outside threshold: prefer closer
                isBetter = current.Distance < best.Distance;
            }

            if (isBetter)
            {
                best = current;
                bestPriority = currentPriority;
            }
        }

        return best;
    }

    /// <summary>
    /// Gets or loads a cell from cache or file.
    /// </summary>
    private GeoStreamCell? GetOrLoadCell(string geohash)
    {
        if (_cache!.TryGet(geohash, out var cachedCell))
            return cachedCell;

        if (!_index!.TryGetOffsets(geohash, out var offsets))
            return null;

        // Load from disk
        try
        {
            var cell = LoadCellFromFile(geohash, offsets);
            _cache.Put(geohash, cell);
            return cell;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load cell {Geohash}", geohash);
            return null;
        }
    }

    /// <summary>
    /// Loads a cell from the data file by reading specific line offsets.
    /// </summary>
    private GeoStreamCell LoadCellFromFile(string geohash, long[] lineOffsets)
    {
        var allEntries = new List<GeoStreamEntry>();
        var cityEntries = new List<GeoStreamEntry>();
        int estimatedMemory = 0;

        using var stream = new FileStream(_dataFilePath!, FileMode.Open, FileAccess.Read, FileShare.Read, 
            bufferSize: 4096, FileOptions.RandomAccess);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096, leaveOpen: true);

        var lineBuffer = new char[8192];

        foreach (var offset in lineOffsets)
        {
            stream.Seek(offset, SeekOrigin.Begin);
            reader.DiscardBufferedData();
            
            var line = reader.ReadLine();
            if (line == null) continue;

            var entry = ParseGeoNamesLine(line);
            if (entry != null)
            {
                allEntries.Add(entry);
                estimatedMemory += EstimateEntryMemory(entry);

                // Add to city tree if population is high enough
                if (entry.Population >= MinimumCityPopulation)
                {
                    cityEntries.Add(entry);
                }
            }
        }

        return new GeoStreamCell
        {
            Geohash = geohash,
            AllEntries = allEntries.ToArray(),
            CityEntries = cityEntries.ToArray(),
            EstimatedMemoryBytes = estimatedMemory
        };
    }

    /// <summary>
    /// Parses a single line from allCountries.txt.
    /// Format: geonameid, name, asciiname, alternatenames, latitude, longitude, feature class, feature code,
    ///         country code, cc2, admin1 code, admin2 code, admin3 code, admin4 code, population, elevation,
    ///         dem, timezone, modification date
    /// </summary>
    private static GeoStreamEntry? ParseGeoNamesLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var parts = line.Split('\t');
        if (parts.Length < 15)
            return null;

        // Only include populated places (P) and administrative areas (A)
        var featureClass = parts[6];
        if (featureClass != "P" && featureClass != "A")
            return null;

        if (!double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
            !double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            return null;

        long.TryParse(parts[14], out var population);

        return new GeoStreamEntry
        {
            Name = parts[1],
            Latitude = lat,
            Longitude = lon,
            FeatureClass = featureClass.Length > 0 ? featureClass[0] : 'P',
            FeatureCode = parts[7],
            Country = parts[8],
            State = parts[10],
            Population = population
        };
    }

    private static int EstimateEntryMemory(GeoStreamEntry entry)
    {
        // Base object overhead + doubles + references
        return 48 + (entry.Name?.Length ?? 0) * 2 + 
                    (entry.Country?.Length ?? 0) * 2 + 
                    (entry.State?.Length ?? 0) * 2 +
                    (entry.FeatureCode?.Length ?? 0) * 2;
    }

    /// <summary>
    /// Calculates the Haversine distance between two points.
    /// </summary>
    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0; // Earth radius in km
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

    private string? FindDataFile()
    {
        // Check locations in priority order
        var searchPaths = new[]
        {
            _config.GeonamesPath, // Configured path
            Path.Combine(AppContext.BaseDirectory, "data", "allCountries.txt"),
            Path.Combine(AppContext.BaseDirectory, "allCountries.txt"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".photocopy", "allCountries.txt"),
        };

        foreach (var path in searchPaths)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return path;
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_cache != null)
        {
            _logger.LogDebug("StreamedGeocodingService disposed. Final stats: {Stats}", _cache.GetStatistics());
        }

        _cache?.Dispose();
        _index = null;
    }
}

/// <summary>
/// A single entry from the GeoNames data.
/// </summary>
public sealed class GeoStreamEntry
{
    public required string Name { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public char FeatureClass { get; init; }
    public string? FeatureCode { get; init; }
    public string? Country { get; init; }
    public string? State { get; init; }
    public long Population { get; init; }
}

/// <summary>
/// A loaded cell containing all entries for a geohash region.
/// </summary>
public sealed class GeoStreamCell
{
    public required string Geohash { get; init; }
    
    /// <summary>All populated places in this cell.</summary>
    public required GeoStreamEntry[] AllEntries { get; init; }
    
    /// <summary>Only places with population >= MinimumCityPopulation.</summary>
    public required GeoStreamEntry[] CityEntries { get; init; }
    
    public int EstimatedMemoryBytes { get; init; }
}

/// <summary>
/// Result of a geo-stream lookup.
/// </summary>
public sealed class GeoStreamLookupResult
{
    public required GeoStreamEntry Entry { get; init; }
    public double DistanceKm { get; init; }
    public required string CellGeohash { get; init; }
    public bool IsFromNeighborCell { get; init; }
}

/// <summary>
/// Index that maps geohash cells to byte offsets in allCountries.txt.
/// Stores a list of line start offsets for each cell.
/// </summary>
public sealed class GeoStreamIndex
{
    private readonly Dictionary<string, long[]> _cells;
    private readonly int _precision;
    private readonly long _totalEntries;

    public int CellCount => _cells.Count;
    public int Precision => _precision;
    public long TotalEntries => _totalEntries;

    private GeoStreamIndex(Dictionary<string, long[]> cells, int precision, long totalEntries)
    {
        _cells = cells;
        _precision = precision;
        _totalEntries = totalEntries;
    }

    public bool TryGetOffsets(string geohash, out long[] offsets)
    {
        return _cells.TryGetValue(geohash, out offsets!);
    }

    /// <summary>
    /// Builds an index by scanning the data file and recording line offsets grouped by geohash.
    /// </summary>
    public static GeoStreamIndex Build(string dataPath, string indexPath, int precision, ILogger logger)
    {
        // Collect all line offsets grouped by geohash
        var cellOffsets = new Dictionary<string, List<long>>();
        long totalEntries = 0;
        
        // Read the file with a buffered approach while accurately tracking byte offsets
        // We cannot rely on StreamReader.Position due to buffering
        using (var stream = new FileStream(dataPath, FileMode.Open, FileAccess.Read, FileShare.Read, 
            bufferSize: 65536, FileOptions.SequentialScan))
        {
            int linesProcessed = 0;
            var lastLogTime = DateTime.UtcNow;
            
            // Read entire file into memory for fast processing
            // For 1.7GB file, this is acceptable on modern systems
            // and MUCH faster than byte-by-byte reading
            logger.LogInformation("Loading file into memory for indexing...");
            var fileBytes = new byte[stream.Length];
            stream.ReadExactly(fileBytes);
            
            logger.LogInformation("Scanning {Size:N0} bytes for line offsets...", fileBytes.Length);
            
            long lineStart = 0;
            for (long i = 0; i < fileBytes.Length; i++)
            {
                if (fileBytes[i] == '\n')
                {
                    // Found end of line - process it
                    int lineLength = (int)(i - lineStart);
                    if (lineLength > 0 && fileBytes[lineStart + lineLength - 1] == '\r')
                        lineLength--; // Remove trailing \r

                    var lineString = Encoding.UTF8.GetString(fileBytes, (int)lineStart, lineLength);
                    
                    linesProcessed++;

                    // Log progress every 30 seconds
                    if ((DateTime.UtcNow - lastLogTime).TotalSeconds >= 30)
                    {
                        var percentComplete = (double)i / fileBytes.Length * 100;
                        logger.LogInformation("Indexing progress: {Lines:N0} lines ({Percent:F1}%), {Cells:N0} cells, {Entries:N0} entries...", 
                            linesProcessed, percentComplete, cellOffsets.Count, totalEntries);
                        lastLogTime = DateTime.UtcNow;
                    }

                    // Parse minimal data to get coordinates and feature class
                    var parts = lineString.Split('\t');
                    if (parts.Length >= 15)
                    {
                        var featureClass = parts[6];
                        if (featureClass == "P" || featureClass == "A")
                        {
                            if (double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                                double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                            {
                                var geohash = Geohash.Encode(lat, lon, precision);
                                
                                if (!cellOffsets.TryGetValue(geohash, out var offsets))
                                {
                                    offsets = new List<long>();
                                    cellOffsets[geohash] = offsets;
                                }
                                
                                offsets.Add(lineStart);
                                totalEntries++;
                            }
                        }
                    }

                    // Next line starts after the newline
                    lineStart = i + 1;
                }
            }
        }

        // Convert to arrays for more compact storage
        var cells = new Dictionary<string, long[]>(cellOffsets.Count);
        foreach (var (geohash, offsets) in cellOffsets)
        {
            cells[geohash] = offsets.ToArray();
        }

        var index = new GeoStreamIndex(cells, precision, totalEntries);
        
        // Save to disk
        Save(index, indexPath, logger);
        
        return index;
    }

    /// <summary>
    /// Saves the index to a compressed file.
    /// </summary>
    private static void Save(GeoStreamIndex index, string path, ILogger logger)
    {
        using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal);
        using var writer = new BinaryWriter(gzipStream);

        // Magic + version
        writer.Write((uint)0x47534958); // "GSIX"
        writer.Write((ushort)2); // version 2 - stores offset arrays
        writer.Write((byte)index._precision);
        writer.Write((byte)0); // reserved
        writer.Write(index._cells.Count);
        writer.Write(index._totalEntries);

        foreach (var (geohash, offsets) in index._cells)
        {
            // Write geohash as length-prefixed string
            writer.Write((byte)geohash.Length);
            writer.Write(Encoding.ASCII.GetBytes(geohash));
            
            // Write offset count and offsets (delta-encoded for better compression)
            writer.Write(offsets.Length);
            long prevOffset = 0;
            foreach (var offset in offsets)
            {
                // Delta encoding: store difference from previous offset
                writer.Write(offset - prevOffset);
                prevOffset = offset;
            }
        }

        logger.LogDebug("Index saved: {Size:F2}MB compressed", fileStream.Length / 1024.0 / 1024.0);
    }

    /// <summary>
    /// Loads the index from a compressed file.
    /// </summary>
    public static GeoStreamIndex Load(string path)
    {
        using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzipStream);

        var magic = reader.ReadUInt32();
        if (magic != 0x47534958)
            throw new InvalidDataException($"Invalid index magic: {magic:X8}");

        var version = reader.ReadUInt16();
        if (version != 2)
            throw new InvalidDataException($"Unsupported index version: {version}");

        var precision = reader.ReadByte();
        reader.ReadByte(); // reserved
        var cellCount = reader.ReadInt32();
        var totalEntries = reader.ReadInt64();

        var cells = new Dictionary<string, long[]>(cellCount);

        for (int i = 0; i < cellCount; i++)
        {
            var geohashLen = reader.ReadByte();
            var geohashBytes = reader.ReadBytes(geohashLen);
            var geohash = Encoding.ASCII.GetString(geohashBytes);

            var offsetCount = reader.ReadInt32();
            var offsets = new long[offsetCount];
            
            long prevOffset = 0;
            for (int j = 0; j < offsetCount; j++)
            {
                // Delta decoding
                prevOffset += reader.ReadInt64();
                offsets[j] = prevOffset;
            }

            cells[geohash] = offsets;
        }

        return new GeoStreamIndex(cells, precision, totalEntries);
    }
}

/// <summary>
/// LRU cache for loaded cells.
/// </summary>
public sealed class StreamedCellCache : IDisposable
{
    private readonly long _maxMemoryBytes;
    private readonly object _lock = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, GeoStreamCell Cell)>> _cache = new();
    private readonly LinkedList<(string Key, GeoStreamCell Cell)> _lru = new();
    private long _currentMemoryBytes;
    private long _hits;
    private long _misses;

    public StreamedCellCache(long maxMemoryBytes)
    {
        _maxMemoryBytes = maxMemoryBytes;
    }

    public bool TryGet(string key, out GeoStreamCell? cell)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var node))
            {
                // Move to front (most recently used)
                _lru.Remove(node);
                _lru.AddFirst(node);
                cell = node.Value.Cell;
                _hits++;
                return true;
            }
        }

        _misses++;
        cell = null;
        return false;
    }

    public void Put(string key, GeoStreamCell cell)
    {
        lock (_lock)
        {
            // Remove existing if present
            if (_cache.TryGetValue(key, out var existingNode))
            {
                _currentMemoryBytes -= existingNode.Value.Cell.EstimatedMemoryBytes;
                _lru.Remove(existingNode);
                _cache.Remove(key);
            }

            // Evict if necessary
            while (_currentMemoryBytes + cell.EstimatedMemoryBytes > _maxMemoryBytes && _lru.Count > 0)
            {
                var lruNode = _lru.Last!;
                _currentMemoryBytes -= lruNode.Value.Cell.EstimatedMemoryBytes;
                _cache.Remove(lruNode.Value.Key);
                _lru.RemoveLast();
            }

            // Add new entry
            var node = _lru.AddFirst((key, cell));
            _cache[key] = node;
            _currentMemoryBytes += cell.EstimatedMemoryBytes;
        }
    }

    public string GetStatistics()
    {
        lock (_lock)
        {
            var hitRate = _hits + _misses > 0 ? (double)_hits / (_hits + _misses) * 100 : 0;
            return $"Cells: {_cache.Count}, Memory: {_currentMemoryBytes / 1024.0 / 1024.0:F2}MB, " +
                   $"Hits: {_hits:N0}, Misses: {_misses:N0}, Hit Rate: {hitRate:F1}%";
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _cache.Clear();
            _lru.Clear();
            _currentMemoryBytes = 0;
        }
    }
}
