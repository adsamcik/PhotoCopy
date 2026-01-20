using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace PhotoCopy.Checkpoint.Models;

/// <summary>
/// Fixed-size binary header for checkpoint file.
/// Written atomically at start, updated on completion.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CheckpointHeader
{
    /// <summary>Magic bytes: "PCOPY01\0" (8 bytes)</summary>
    public long Magic;

    /// <summary>Schema version (4 bytes)</summary>
    public int Version;

    /// <summary>Session state: 0=InProgress, 1=Completed, 2=Failed (4 bytes)</summary>
    public int Status;

    /// <summary>Start time as UTC ticks (8 bytes)</summary>
    public long StartTimeUtcTicks;

    /// <summary>Last update time as UTC ticks (8 bytes)</summary>
    public long LastUpdateUtcTicks;

    /// <summary>Total files in plan (4 bytes)</summary>
    public int TotalFiles;

    /// <summary>Reserved for alignment (4 bytes)</summary>
    private int _reserved1;

    /// <summary>Total bytes in plan (8 bytes)</summary>
    public long TotalBytes;

    /// <summary>Files completed so far (4 bytes)</summary>
    public int CompletedCount;

    /// <summary>Reserved for alignment (4 bytes)</summary>
    private int _reserved2;

    /// <summary>Bytes completed so far (8 bytes)</summary>
    public long CompletedBytes;

    /// <summary>Config hash - first 16 bytes of SHA256 (16 bytes)</summary>
    public Guid ConfigHashPrefix;

    /// <summary>Plan hash - first 16 bytes of SHA256 (16 bytes)</summary>
    public Guid PlanHashPrefix;

    /// <summary>Length of source directory string (4 bytes)</summary>
    public int SourceDirectoryLength;

    /// <summary>Length of destination pattern string (4 bytes)</summary>
    public int DestinationPatternLength;

    /// <summary>Offset where operation records begin (4 bytes)</summary>
    public int RecordsOffset;

    /// <summary>Reserved for future use (4 bytes)</summary>
    private int _reserved3;

    /// <summary>Reserved for future use (16 bytes)</summary>
    private long _reserved4, _reserved5;

    // Total: 128 bytes fixed header

    public const long MagicValue = 0x00_31_30_59_50_4F_43_50; // "PCOPY01\0" in little-endian
    public const int CurrentVersion = 1;
    public const int HeaderSize = 128;

    public static CheckpointHeader Create(
        int totalFiles,
        long totalBytes,
        byte[] configHash,
        byte[] planHash,
        DateTime startTimeUtc)
    {
        return new CheckpointHeader
        {
            Magic = MagicValue,
            Version = CurrentVersion,
            Status = (int)CheckpointStatus.InProgress,
            StartTimeUtcTicks = startTimeUtc.Ticks,
            LastUpdateUtcTicks = startTimeUtc.Ticks,
            TotalFiles = totalFiles,
            TotalBytes = totalBytes,
            CompletedCount = 0,
            CompletedBytes = 0,
            ConfigHashPrefix = new Guid(configHash.AsSpan(0, 16)),
            PlanHashPrefix = new Guid(planHash.AsSpan(0, 16))
        };
    }

    public readonly bool IsValid => Magic == MagicValue && Version <= CurrentVersion;

    public readonly DateTime StartTimeUtc => new(StartTimeUtcTicks, DateTimeKind.Utc);
    public readonly DateTime LastUpdateUtc => new(LastUpdateUtcTicks, DateTimeKind.Utc);

    public void WriteTo(Span<byte> dest)
    {
        BinaryPrimitives.WriteInt64LittleEndian(dest, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(dest.Slice(8), Version);
        BinaryPrimitives.WriteInt32LittleEndian(dest.Slice(12), Status);
        BinaryPrimitives.WriteInt64LittleEndian(dest.Slice(16), StartTimeUtcTicks);
        BinaryPrimitives.WriteInt64LittleEndian(dest.Slice(24), LastUpdateUtcTicks);
        BinaryPrimitives.WriteInt32LittleEndian(dest.Slice(32), TotalFiles);
        BinaryPrimitives.WriteInt32LittleEndian(dest.Slice(36), 0); // reserved
        BinaryPrimitives.WriteInt64LittleEndian(dest.Slice(40), TotalBytes);
        BinaryPrimitives.WriteInt32LittleEndian(dest.Slice(48), CompletedCount);
        BinaryPrimitives.WriteInt32LittleEndian(dest.Slice(52), 0); // reserved
        BinaryPrimitives.WriteInt64LittleEndian(dest.Slice(56), CompletedBytes);
        ConfigHashPrefix.TryWriteBytes(dest.Slice(64));
        PlanHashPrefix.TryWriteBytes(dest.Slice(80));
        BinaryPrimitives.WriteInt32LittleEndian(dest.Slice(96), SourceDirectoryLength);
        BinaryPrimitives.WriteInt32LittleEndian(dest.Slice(100), DestinationPatternLength);
        BinaryPrimitives.WriteInt32LittleEndian(dest.Slice(104), RecordsOffset);
        // Rest is reserved, zero-filled
    }

    public static CheckpointHeader ReadFrom(ReadOnlySpan<byte> source)
    {
        return new CheckpointHeader
        {
            Magic = BinaryPrimitives.ReadInt64LittleEndian(source),
            Version = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(8)),
            Status = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(12)),
            StartTimeUtcTicks = BinaryPrimitives.ReadInt64LittleEndian(source.Slice(16)),
            LastUpdateUtcTicks = BinaryPrimitives.ReadInt64LittleEndian(source.Slice(24)),
            TotalFiles = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(32)),
            TotalBytes = BinaryPrimitives.ReadInt64LittleEndian(source.Slice(40)),
            CompletedCount = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(48)),
            CompletedBytes = BinaryPrimitives.ReadInt64LittleEndian(source.Slice(56)),
            ConfigHashPrefix = new Guid(source.Slice(64, 16)),
            PlanHashPrefix = new Guid(source.Slice(80, 16)),
            SourceDirectoryLength = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(96)),
            DestinationPatternLength = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(100)),
            RecordsOffset = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(104))
        };
    }
}

/// <summary>
/// Status of a checkpoint session.
/// </summary>
public enum CheckpointStatus
{
    /// <summary>Operation is in progress.</summary>
    InProgress = 0,

    /// <summary>Operation completed successfully.</summary>
    Completed = 1,

    /// <summary>Operation failed.</summary>
    Failed = 2
}
