using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace PhotoCopy.Tests.TestingImplementation;

// Shared log collection to ensure all loggers access the same log entries
public static class SharedLogs
{
    private static readonly ConcurrentBag<LogEntry> _entries = new();
    private static readonly object _lock = new();
    
    public static List<LogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.Where(e => e != null).ToList();
            }
        }
    }
    
    public static void Add(LogEntry entry)
    {
        if (entry != null)
        {
            _entries.Add(entry);
        }
    }
    
    public static void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }
}

/// <summary>
/// A fake logger for testing that captures log entries
/// </summary>
public class FakeLogger<T> : ILogger<T>
{
    public IReadOnlyList<LogEntry> Logs => SharedLogs.Entries;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        SharedLogs.Add(new LogEntry
        {
            LogLevel = logLevel,
            EventId = eventId,
            Message = formatter(state, exception),
            Exception = exception,
            Category = typeof(T).Name,
            Timestamp = DateTime.UtcNow
        });
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoopDisposable();

    public void Clear() => SharedLogs.Clear();

    private class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

/// <summary>
/// A non-generic fake logger that delegates to the generic version
/// </summary>
public class FakeLogger : ILogger
{
    public IReadOnlyList<LogEntry> Logs => SharedLogs.Entries;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        SharedLogs.Add(new LogEntry
        {
            LogLevel = logLevel,
            EventId = eventId,
            Message = formatter(state, exception),
            Exception = exception,
            Category = "Generic",
            Timestamp = DateTime.UtcNow
        });
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoopDisposable();

    public void Clear() => SharedLogs.Clear();

    private class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

/// <summary>
/// Represents a captured log entry
/// </summary>
public class LogEntry
{
    public LogLevel LogLevel { get; set; }
    public EventId EventId { get; set; }
    public string Message { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
    public string Category { get; set; } = string.Empty;
    public string CategoryName => Category; // Add alias for backward compatibility
    public DateTime Timestamp { get; set; }
}