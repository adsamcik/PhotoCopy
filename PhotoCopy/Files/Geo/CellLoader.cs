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

    private SpatialIndex(GeoIndexHeader header, CellIndexEntry[] cellIndex,
        Dictionary<uint, int> geohashToIndex, MemoryMappedFile? mmap = null, MemoryMappedViewAccessor? view = null)
    {
        _header = header;
        _cellIndex = cellIndex;
        _geohashToIndex = geohashToIndex;
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

            // Validate file size for entries
            long expectedSize = GeoIndexHeader.Size + (header.CellCount * CellIndexEntry.Size);
            if (fileInfo.Length < expectedSize)
                throw new InvalidDataException($"Index file truncated: expected {expectedSize} bytes, got {fileInfo.Length}");

            // Read cell index entries
            var cellIndex = new CellIndexEntry[header.CellCount];
            var geohashToIndex = new Dictionary<uint, int>((int)header.CellCount);

            long offset = GeoIndexHeader.Size;
            for (int i = 0; i < header.CellCount; i++)
            {
                cellIndex[i] = ReadCellIndexEntry(view, offset);
                geohashToIndex[cellIndex[i].GeohashCode] = i;
                offset += CellIndexEntry.Size;
            }

            return new SpatialIndex(header, cellIndex, geohashToIndex, mmap, view);
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
            Flags = bytes[7],
            CellCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4)),
            TotalLocationCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12, 4)),
            BuildTimestamp = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(16, 8)),
            DataFileSize = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(24, 8)),
            Reserved1 = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(32, 8)),
            Reserved2 = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(40, 8)),
        };
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
            UncompressedSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16, 4)),
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
    private bool _disposed;

    private CellLoader(MemoryMappedFile mmap, MemoryMappedViewAccessor view, long fileSize)
    {
        _dataMmap = mmap;
        _dataView = view;
        _fileSize = fileSize;
    }

    /// <summary>
    /// Opens a data file for reading.
    /// </summary>
    /// <param name="dataPath">Path to the .geodata file.</param>
    public static CellLoader Open(string dataPath)
    {
        if (!File.Exists(dataPath))
            throw new FileNotFoundException("Geo-data file not found", dataPath);

        var fileInfo = new FileInfo(dataPath);
        var mmap = MemoryMappedFile.CreateFromFile(dataPath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        var view = mmap.CreateViewAccessor(0, fileInfo.Length, MemoryMappedFileAccess.Read);

        return new CellLoader(mmap, view, fileInfo.Length);
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
        var decompressedData = new byte[entry.UncompressedSize];
        using var compressedStream = new MemoryStream(compressedData);
        using var brotli = new BrotliStream(compressedStream, CompressionMode.Decompress);
        int totalRead = 0;
        int bytesRead;
        while ((bytesRead = brotli.Read(decompressedData, totalRead, entry.UncompressedSize - totalRead)) > 0)
        {
            totalRead += bytesRead;
        }
        if (totalRead != entry.UncompressedSize)
            throw new InvalidDataException($"Brotli decompression failed: expected {entry.UncompressedSize}, got {totalRead}");

        // Parse cell block
        return ParseCellBlock(decompressedData, geohash);
    }

    /// <summary>
    /// Parses a decompressed cell block into a GeoCell.
    /// </summary>
    internal static GeoCell ParseCellBlock(byte[] data, string geohash)
    {
        if (data.Length < CellBlockHeader.Size)
            throw new InvalidDataException("Cell block too small for header");

        // Parse header
        var header = new CellBlockHeader
        {
            EntryCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0, 2)),
            StringPoolOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2, 2)),
            StringPoolSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4, 2)),
        };

        // Validate sizes
        int entriesEnd = CellBlockHeader.Size + (header.EntryCount * LocationEntryDisk.Size);
        if (entriesEnd > data.Length)
            throw new InvalidDataException($"Entry data extends beyond block: {entriesEnd} > {data.Length}");
        if (header.StringPoolOffset + header.StringPoolSize > data.Length)
            throw new InvalidDataException("String pool extends beyond block");

        // Parse entries
        var entries = new LocationEntryMemory[header.EntryCount];
        var stringPool = data.AsSpan(header.StringPoolOffset, header.StringPoolSize);

        int offset = CellBlockHeader.Size;
        for (int i = 0; i < header.EntryCount; i++)
        {
            var diskEntry = ParseLocationEntryDisk(data.AsSpan(offset, LocationEntryDisk.Size));
            entries[i] = new LocationEntryMemory
            {
                Latitude = diskEntry.Latitude,
                Longitude = diskEntry.Longitude,
                GeoNameId = diskEntry.GeoNameId,
                City = ReadStringFromPool(stringPool, diskEntry.CityOffset),
                State = ReadStringFromPool(stringPool, diskEntry.StateOffset),
                Country = ReadStringFromPool(stringPool, diskEntry.CountryOffset),
                PopulationK = diskEntry.PopulationK,
                FeatureClass = diskEntry.FeatureClass == 0 ? ' ' : (char)diskEntry.FeatureClass,
            };
            offset += LocationEntryDisk.Size;
        }

        // Estimate memory usage
        int memoryEstimate = entries.Length * 80; // Rough per-entry overhead
        foreach (var entry in entries)
        {
            memoryEstimate += (entry.City.Length + entry.State.Length + entry.Country.Length) * 2;
        }

        return new GeoCell
        {
            Geohash = geohash,
            Entries = entries,
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
            GeoNameId = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(8, 4)),
            CityOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(12, 2)),
            StateOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(14, 2)),
            CountryOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(16, 2)),
            PopulationK = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(18, 2)),
            FeatureClass = data[20],
            Reserved = data[21],
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
