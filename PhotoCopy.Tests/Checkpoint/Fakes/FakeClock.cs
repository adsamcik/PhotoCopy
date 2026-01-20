using System;
using PhotoCopy.Checkpoint;

namespace PhotoCopy.Tests.Checkpoint.Fakes;

/// <summary>
/// Fake clock for testing time-dependent checkpoint functionality.
/// </summary>
public class FakeClock : ISystemClock
{
    private DateTime _currentTime;

    public FakeClock(DateTime? initialTime = null)
    {
        _currentTime = initialTime ?? new DateTime(2026, 1, 20, 12, 0, 0, DateTimeKind.Utc);
    }

    public DateTime UtcNow => _currentTime;

    /// <summary>
    /// Advance the clock by the specified duration.
    /// </summary>
    public void Advance(TimeSpan duration) => _currentTime = _currentTime.Add(duration);

    /// <summary>
    /// Set the clock to a specific time.
    /// </summary>
    public void SetTime(DateTime time) => _currentTime = time;
}
