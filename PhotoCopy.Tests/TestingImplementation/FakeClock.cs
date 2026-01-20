using System;

namespace PhotoCopy.Tests.TestingImplementation;

/// <summary>
/// A controllable clock for testing time-dependent logic.
/// Replaces DateTime.UtcNow calls in production code that uses ISystemClock.
/// </summary>
public interface ISystemClock
{
    DateTime UtcNow { get; }
}

/// <summary>
/// Production implementation that returns actual system time.
/// </summary>
public class SystemClock : ISystemClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>
/// Fake clock for testing that allows controlling time.
/// </summary>
public class FakeClock : ISystemClock
{
    private DateTime _currentTime;

    public FakeClock() : this(DateTime.UtcNow) { }

    public FakeClock(DateTime initialTime)
    {
        _currentTime = initialTime;
    }

    public DateTime UtcNow => _currentTime;

    /// <summary>
    /// Advances the clock by the specified amount.
    /// </summary>
    public void Advance(TimeSpan amount)
    {
        _currentTime = _currentTime.Add(amount);
    }

    /// <summary>
    /// Sets the clock to a specific time.
    /// </summary>
    public void SetTime(DateTime time)
    {
        _currentTime = time;
    }
}
