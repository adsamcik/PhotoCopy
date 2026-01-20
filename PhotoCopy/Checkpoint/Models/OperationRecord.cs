using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace PhotoCopy.Checkpoint.Models;

/// <summary>
/// Append-only log record for completed operations.
/// Minimal size for I/O efficiency.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct OperationRecord
{
    /// <summary>Index into the original plan (4 bytes)</summary>
    public readonly int FileIndex;

    /// <summary>Operation result (1 byte)</summary>
    public readonly OperationResult Result;

    /// <summary>Padding for alignment (3 bytes)</summary>
    private readonly byte _pad1, _pad2, _pad3;

    /// <summary>File size for progress tracking (8 bytes)</summary>
    public readonly long FileSize;

    /// <summary>Timestamp as UTC ticks (8 bytes)</summary>
    public readonly long TimestampUtcTicks;

    // Total: 24 bytes per record
    // 1M files = 24 MB log file (vs 800 MB JSON)

    public const int RecordSize = 24;

    public OperationRecord(int fileIndex, OperationResult result, long fileSize, DateTime timestampUtc)
    {
        FileIndex = fileIndex;
        Result = result;
        FileSize = fileSize;
        TimestampUtcTicks = timestampUtc.Ticks;
        _pad1 = _pad2 = _pad3 = 0;
    }

    public DateTime TimestampUtc => new(TimestampUtcTicks, DateTimeKind.Utc);

    public void WriteTo(Span<byte> dest)
    {
        BinaryPrimitives.WriteInt32LittleEndian(dest, FileIndex);
        dest[4] = (byte)Result;
        dest[5] = dest[6] = dest[7] = 0; // padding
        BinaryPrimitives.WriteInt64LittleEndian(dest.Slice(8), FileSize);
        BinaryPrimitives.WriteInt64LittleEndian(dest.Slice(16), TimestampUtcTicks);
    }

    public static OperationRecord ReadFrom(ReadOnlySpan<byte> source)
    {
        var fileIndex = BinaryPrimitives.ReadInt32LittleEndian(source);
        var result = (OperationResult)source[4];
        var fileSize = BinaryPrimitives.ReadInt64LittleEndian(source.Slice(8));
        var ticks = BinaryPrimitives.ReadInt64LittleEndian(source.Slice(16));
        return new OperationRecord(fileIndex, result, fileSize, new DateTime(ticks, DateTimeKind.Utc));
    }
}

/// <summary>
/// Result of a file operation for checkpoint tracking.
/// </summary>
public enum OperationResult : byte
{
    /// <summary>Copy/move completed successfully.</summary>
    Completed = 1,

    /// <summary>Copy done, source deletion pending (Move mode crash recovery).</summary>
    CopyDonePendingDelete = 2,

    /// <summary>Skipped (validation, duplicate, etc.).</summary>
    Skipped = 3,

    /// <summary>Failed with error.</summary>
    Failed = 4
}
