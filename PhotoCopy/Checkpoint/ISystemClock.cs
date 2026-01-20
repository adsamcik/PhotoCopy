using System;

namespace PhotoCopy.Checkpoint;

/// <summary>
/// Time abstraction for testability.
/// </summary>
public interface ISystemClock
{
    /// <summary>Gets the current UTC time.</summary>
    DateTime UtcNow { get; }
}

/// <summary>
/// Default system clock implementation.
/// </summary>
public sealed class SystemClock : ISystemClock
{
    /// <summary>Singleton instance.</summary>
    public static readonly SystemClock Instance = new();

    /// <inheritdoc/>
    public DateTime UtcNow => DateTime.UtcNow;
}
