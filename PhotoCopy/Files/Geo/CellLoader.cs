using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Text;

namespace PhotoCopy.Files.Geo;

/// <summary>
/// Loads geo-index files and provides O(1) cell lookup.
/// The index is always kept in memory (~3MB for 155K cells).
/// </summary>
public sealed class SpatialIndex : IDisposable
{
    private readonly MemoryMappedFile? _indexMmap;
    private readonly MemoryMappedViewAccessor? _indexView;
    private readonly GeoIndexHeader _header;
    private readonly CellIndexEntry[] _cellIndex;
    private readonly Dictionary<uint, int> _geohashToIndex;
    private readonly string[] _countryNames;
    private bool _disposed;

    /// <summary>
    /// Number of cells in the index.
    /// </summary>
    public int CellCount => (int)_header.CellCount;

    /// <summary>
    /// Total number of locations across all cells.
    /// </summary>
    public int TotalLocationCount => (int)_header.TotalLocationCount;

    /// <summary>
    /// Geohash precision level.
    /// </summary>
    public int Precision => _header.Precision;

    /// <summary>
    /// When the index was built.
    /// </summary>
    public DateTimeOffset BuildTime => DateTimeOffset.FromUnixTimeSeconds(_header.BuildTimestamp);

    /// <summary>
    /// Expected size of the data file in bytes.
    /// </summary>
    public long DataFileSize => _header.DataFileSize;

    /// <summary>
    /// Country names table (indexed by CountryIndex from entries).
    /// </summary>
    public IReadOnlyList<string> CountryNames => _countryNames;

    private SpatialIndex(GeoIndexHeader header, CellIndexEntry[] cellIndex,
        Dictionary<uint, int> geohashToIndex, string[] countryNames,
        MemoryMappedFile? mmap = null, MemoryMappedViewAccessor? view = null)
    {
        _header = header;
        _cellIndex = cellIndex;
        _geohashToIndex = geohashToIndex;
        _countryNames = countryNames;
        _indexMmap = mmap;
        _indexView = view;
    }

    /// <summary>
    /// Loads the spatial index from an index file.
    /// </summary>
    /// <param name="indexPath">Path to the .geoindex file.</param>
    /// <returns>Loaded spatial index.</returns>
    public static SpatialIndex Load(string indexPath)
    {
        if (!File.Exists(indexPath))
            throw new FileNotFoundException("Geo-index file not found", indexPath);

        var fileInfo = new FileInfo(indexPath);
        if (fileInfo.Length < GeoIndexHeader.Size)
            throw new InvalidDataException("Index file too small");

        // Memory-map the index file for efficient reading
        var mmap = MemoryMappedFile.CreateFromFile(indexPath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        var view = mmap.CreateViewAccessor(0, fileInfo.Length, MemoryMappedFileAccess.Read);

        try
        {
            // Read header
            var header = ReadHeader(view);
            if (!header.IsValid)
            {
                view.Dispose();
                mmap.Dispose();
                throw new InvalidDataException($"Invalid index header: Magic={header.Magic:X8}, Version={header.Version}");
            }

            // Read country table
            long offset = GeoIndexHeader.Size;
            var (countryNames, countryPoolEnd) = ReadCountryTable(view, offset, header.CountryCount);
            offset = countryPoolEnd;

            // Validate file size for cell entries
            long expectedSize = offset + (header.CellCount * CellIndexEntry.Size);
            if (fileInfo.Length < expectedSize)
                throw new InvalidDataException($"Index file truncated: expected {expectedSize} bytes, got {fileInfo.Length}");

            // Read cell index entries
            var cellIndex = new CellIndexEntry[header.CellCount];
            var geohashToIndex = new Dictionary<uint, int>((int)header.CellCount);

            for (int i = 0; i < header.CellCount; i++)
            {
                cellIndex[i] = ReadCellIndexEntry(view, offset);
                geohashToIndex[cellIndex[i].GeohashCode] = i;
                offset += CellIndexEntry.Size;
            }

            return new SpatialIndex(header, cellIndex, geohashToIndex, countryNames, mmap, view);
        }
        catch
        {
            view.Dispose();
            mmap.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Tries to get the index entry for a specific geohash.
    /// </summary>
    /// <param name="geohash">Geohash string.</param>
    /// <param name="entry">The cell index entry if found.</param>
    /// <returns>True if the cell exists in the index.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetCell(string geohash, out CellIndexEntry entry)
    {
        uint code = Geohash.EncodeToUInt32(geohash);
        return TryGetCell(code, out entry);
    }

    /// <summary>
    /// Tries to get the index entry for a specific geohash code.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetCell(uint geohashCode, out CellIndexEntry entry)
    {
        if (_geohashToIndex.TryGetValue(geohashCode, out int index))
        {
            entry = _cellIndex[index];
            return true;
        }
        entry = default;
        return false;
    }

    /// <summary>
    /// Checks if a cell exists in the index.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsCell(string geohash)
    {
        uint code = Geohash.EncodeToUInt32(geohash);
        return _geohashToIndex.ContainsKey(code);
    }

    /// <summary>
    /// Gets all cell index entries (for iteration).
    /// </summary>
    public IReadOnlyList<CellIndexEntry> GetAllCells() => _cellIndex;

    /// <summary>
    /// Gets index entries for a geohash and all its neighbors.
    /// </summary>
    public IEnumerable<(string Geohash, CellIndexEntry Entry)> GetCellAndNeighbors(string geohash)
    {
        foreach (var hash in Geohash.GetCellAndNeighbors(geohash))
        {
            if (TryGetCell(hash, out var entry))
            {
                yield return (hash, entry);
            }
        }
    }

    /// <summary>
    /// Gets approximate memory usage of the index in bytes.
    /// </summary>
    public long EstimatedMemoryBytes =>
        GeoIndexHeader.Size +
        (_cellIndex.Length * CellIndexEntry.Size) +
        (_geohashToIndex.Count * 12); // Approximate Dictionary overhead

    private static GeoIndexHeader ReadHeader(MemoryMappedViewAccessor view)
    {
        var bytes = new byte[GeoIndexHeader.Size];
        view.ReadArray(0, bytes, 0, GeoIndexHeader.Size);

        return new GeoIndexHeader
        {
            Magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)),
            Version = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2)),
            Precision = bytes[6],
            CountryCount = bytes[7],
            CellCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4)),
            TotalLocationCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12, 4)),
            BuildTimestamp = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(16, 8)),
            DataFileSize = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(24, 8)),
        };
    }

    private static (string[] Names, long PoolEnd) ReadCountryTable(MemoryMappedViewAccessor view, long offset, int countryCount)
    {
        if (countryCount == 0)
            return (Array.Empty<string>(), offset);

        // Read country entries (4 bytes each)
        var entries = new CountryEntry[countryCount];
        var entryBytes = new byte[CountryEntry.Size];
        ushort maxNameOffset = 0;

        for (int i = 0; i < countryCount; i++)
        {
            view.ReadArray(offset, entryBytes, 0, CountryEntry.Size);
            entries[i] = new CountryEntry
            {
                CountryCode = BinaryPrimitives.ReadUInt16LittleEndian(entryBytes.AsSpan(0, 2)),
                NameOffset = BinaryPrimitives.ReadUInt16LittleEndian(entryBytes.AsSpan(2, 2)),
            };
            if (entries[i].NameOffset > maxNameOffset)
                maxNameOffset = entries[i].NameOffset;
            offset += CountryEntry.Size;
        }

        // Read country name string pool
        // We need to read enough to cover the last string - estimate max 64 chars per country name
        long stringPoolStart = offset;
        int estimatedPoolSize = maxNameOffset + 128;
        var poolBytes = new byte[estimatedPoolSize];
        view.ReadArray(stringPoolStart, poolBytes, 0, estimatedPoolSize);

        // Parse country names
        var names = new string[countryCount];
        int poolEnd = 0;
        for (int i = 0; i < countryCount; i++)
        {
            int nameStart = entries[i].NameOffset;
            int nameEnd = nameStart;
            while (nameEnd < poolBytes.Length && poolBytes[nameEnd] != 0)
                nameEnd++;
            names[i] = Encoding.UTF8.GetString(poolBytes, nameStart, nameEnd - nameStart);
            if (nameEnd + 1 > poolEnd)
                poolEnd = nameEnd + 1;
        }

        return (names, stringPoolStart + poolEnd);
    }

    private static CellIndexEntry ReadCellIndexEntry(MemoryMappedViewAccessor view, long offset)
    {
        var bytes = new byte[CellIndexEntry.Size];
        view.ReadArray(offset, bytes, 0, CellIndexEntry.Size);

        return new CellIndexEntry
        {
            GeohashCode = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)),
            DataOffset = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(4, 8)),
            CompressedSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12, 4)),
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _indexView?.Dispose();
        _indexMmap?.Dispose();
    }
}

/// <summary>
/// Loads and decompresses cell data from the geodata file.
/// Uses memory-mapped I/O for efficient random access.
/// </summary>
public sealed class CellLoader : IDisposable
{
    private readonly MemoryMappedFile _dataMmap;
    private readonly MemoryMappedViewAccessor _dataView;
    private readonly long _fileSize;
    private readonly string[] _countryNames;
    private bool _disposed;

    private CellLoader(MemoryMappedFile mmap, MemoryMappedViewAccessor view, long fileSize, string[] countryNames)
    {
        _dataMmap = mmap;
        _dataView = view;
        _fileSize = fileSize;
        _countryNames = countryNames;
    }

    /// <summary>
    /// Opens a data file for reading.
    /// </summary>
    /// <param name="dataPath">Path to the .geodata file.</param>
    /// <param name="countryNames">Country name lookup table from the index.</param>
    public static CellLoader Open(string dataPath, IReadOnlyList<string> countryNames)
    {
        if (!File.Exists(dataPath))
            throw new FileNotFoundException("Geo-data file not found", dataPath);

        var fileInfo = new FileInfo(dataPath);
        var mmap = MemoryMappedFile.CreateFromFile(dataPath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        var view = mmap.CreateViewAccessor(0, fileInfo.Length, MemoryMappedFileAccess.Read);

        // Copy country names to internal array for fast lookup
        var names = new string[countryNames.Count];
        for (int i = 0; i < countryNames.Count; i++)
            names[i] = countryNames[i];

        return new CellLoader(mmap, view, fileInfo.Length, names);
    }

    /// <summary>
    /// Loads and decompresses a cell from the data file.
    /// </summary>
    /// <param name="entry">Cell index entry with offset/size information.</param>
    /// <param name="geohash">Geohash string for metadata.</param>
    /// <returns>Fully loaded GeoCell with all location entries.</returns>
    public GeoCell LoadCell(CellIndexEntry entry, string geohash)
    {
        if (entry.DataOffset + entry.CompressedSize > _fileSize)
            throw new InvalidDataException($"Cell data extends beyond file: offset={entry.DataOffset}, size={entry.CompressedSize}, fileSize={_fileSize}");

        // Read compressed data
        var compressedData = new byte[entry.CompressedSize];
        _dataView.ReadArray(entry.DataOffset, compressedData, 0, entry.CompressedSize);

        // Decompress using Brotli
        using var compressedStream = new MemoryStream(compressedData);
        using var brotli = new BrotliStream(compressedStream, CompressionMode.Decompress);
        using var decompressedStream = new MemoryStream();
        brotli.CopyTo(decompressedStream);
        var decompressedData = decompressedStream.ToArray();

        // Parse cell block
        return ParseCellBlock(decompressedData, geohash, _countryNames);
    }

    /// <summary>
    /// Parses a decompressed cell block into a GeoCell.
    /// </summary>
    internal static GeoCell ParseCellBlock(byte[] data, string geohash, string[] countryNames)
    {
        if (data.Length < CellBlockHeader.Size)
            throw new InvalidDataException("Cell block too small for header");

        // Parse header
        var header = new CellBlockHeader
        {
            EntryCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0, 2)),
            CityStartIndex = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2, 2)),
            StringPoolOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4, 2)),
        };

        // Validate sizes
        int entriesEnd = CellBlockHeader.Size + (header.EntryCount * LocationEntryDisk.Size);
        if (entriesEnd > data.Length)
            throw new InvalidDataException($"Entry data extends beyond block: {entriesEnd} > {data.Length}");
        if (header.StringPoolOffset > data.Length)
            throw new InvalidDataException("String pool extends beyond block");

        // Parse entries
        var entries = new LocationEntry[header.EntryCount];
        var stringPool = data.AsSpan(header.StringPoolOffset);

        int offset = CellBlockHeader.Size;
        for (int i = 0; i < header.EntryCount; i++)
        {
            var diskEntry = ParseLocationEntryDisk(data.AsSpan(offset, LocationEntryDisk.Size));
            
            // Look up country name from index
            string countryName = diskEntry.CountryIndex < countryNames.Length 
                ? countryNames[diskEntry.CountryIndex] 
                : string.Empty;

            entries[i] = new LocationEntry
            {
                Latitude = diskEntry.Latitude,
                Longitude = diskEntry.Longitude,
                Name = ReadStringFromPool(stringPool, diskEntry.NameOffset),
                State = ReadStringFromPool(stringPool, diskEntry.StateOffset),
                Country = countryName,
                PlaceType = diskEntry.PlaceType,
            };
            offset += LocationEntryDisk.Size;
        }

        // Estimate memory usage
        int memoryEstimate = entries.Length * 80; // Rough per-entry overhead
        foreach (var entry in entries)
        {
            memoryEstimate += (entry.Name.Length + entry.State.Length + entry.Country.Length) * 2;
        }

        return new GeoCell
        {
            Geohash = geohash,
            Entries = entries,
            CityStartIndex = header.CityStartIndex,
            Bounds = Geohash.DecodeBounds(geohash),
            EstimatedMemoryBytes = memoryEstimate,
        };
    }

    private static LocationEntryDisk ParseLocationEntryDisk(ReadOnlySpan<byte> data)
    {
        return new LocationEntryDisk
        {
            LatitudeMicro = BinaryPrimitives.ReadInt32LittleEndian(data[..4]),
            LongitudeMicro = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4, 4)),
            NameOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2)),
            StateOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(10, 2)),
            CountryIndex = data[12],
            PlaceType = (PlaceType)data[13],
        };
    }

    private static string ReadStringFromPool(ReadOnlySpan<byte> pool, ushort offset)
    {
        if (offset >= pool.Length)
            return string.Empty;

        // Strings are null-terminated UTF-8
        var slice = pool[offset..];
        int length = slice.IndexOf((byte)0);
        if (length < 0) length = slice.Length;

        return Encoding.UTF8.GetString(slice[..length]);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dataView.Dispose();
        _dataMmap.Dispose();
    }
}
