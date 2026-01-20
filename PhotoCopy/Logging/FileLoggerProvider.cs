using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using PhotoCopy.Configuration;

namespace PhotoCopy.Logging;

/// <summary>
/// A logger provider that writes log messages to a file.
/// Supports both text and JSON formats.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider, IAsyncDisposable
{
    private readonly string _filePath;
    private readonly LogFormat _logFormat;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<string> _messageChannel;
    private readonly Task _outputTask;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileLoggerProvider"/> class.
    /// </summary>
    /// <param name="filePath">The path to the log file.</param>
    /// <param name="logFormat">The format for log messages.</param>
    public FileLoggerProvider(string filePath, LogFormat logFormat)
    {
        _filePath = filePath;
        _logFormat = logFormat;
        
        // Ensure directory exists
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _messageChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _outputTask = ProcessLogQueueAsync(_cts.Token);
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this, _logFormat));
    }

    internal void WriteMessage(string message)
    {
        if (!_disposed)
        {
            _messageChannel.Writer.TryWrite(message);
        }
    }

    private async Task ProcessLogQueueAsync(CancellationToken cancellationToken)
    {
        await using var streamWriter = new StreamWriter(_filePath, append: true, Encoding.UTF8)
        {
            AutoFlush = true
        };

        try
        {
            await foreach (var message in _messageChannel.Reader.ReadAllAsync(cancellationToken))
            {
                await streamWriter.WriteLineAsync(message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected on shutdown, drain remaining messages
            while (_messageChannel.Reader.TryRead(out var message))
            {
                await streamWriter.WriteLineAsync(message);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _messageChannel.Writer.Complete();
        
        await _cts.CancelAsync();
        
        try
        {
            await _outputTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        _cts.Dispose();
        _loggers.Clear();
    }
}

/// <summary>
/// A logger that writes to a file via <see cref="FileLoggerProvider"/>.
/// </summary>
internal sealed class FileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly FileLoggerProvider _provider;
    private readonly LogFormat _logFormat;

    public FileLogger(string categoryName, FileLoggerProvider provider, LogFormat logFormat)
    {
        _categoryName = categoryName;
        _provider = provider;
        _logFormat = logFormat;
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception is null)
        {
            return;
        }

        var formattedMessage = _logFormat == LogFormat.Json
            ? FormatAsJson(logLevel, message, exception, state)
            : FormatAsText(logLevel, message, exception);

        _provider.WriteMessage(formattedMessage);
    }

    private string FormatAsText(LogLevel logLevel, string message, Exception? exception)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var level = GetLogLevelShortName(logLevel);
        var result = $"[{timestamp}] [{level}] [{_categoryName}] {message}";

        if (exception is not null)
        {
            result += Environment.NewLine + exception;
        }

        return result;
    }

    private string FormatAsJson<TState>(LogLevel logLevel, string message, Exception? exception, TState state)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append($"\"timestamp\":\"{DateTimeOffset.Now:o}\",");
        sb.Append($"\"level\":\"{GetLogLevelString(logLevel)}\",");
        sb.Append($"\"message\":{System.Text.Json.JsonSerializer.Serialize(message)},");
        sb.Append($"\"category\":\"{EscapeJson(_categoryName)}\"");

        // Add properties from structured state
        if (state is IReadOnlyList<KeyValuePair<string, object?>> stateProperties)
        {
            var hasProperties = false;
            foreach (var prop in stateProperties)
            {
                if (prop.Key == "{OriginalFormat}")
                {
                    continue;
                }

                if (!hasProperties)
                {
                    sb.Append(",\"properties\":{");
                    hasProperties = true;
                }
                else
                {
                    sb.Append(',');
                }

                sb.Append($"\"{EscapeJson(prop.Key)}\":");
                AppendJsonValue(sb, prop.Value);
            }

            if (hasProperties)
            {
                sb.Append('}');
            }
        }

        if (exception is not null)
        {
            sb.Append(",\"exception\":{");
            sb.Append($"\"type\":\"{EscapeJson(exception.GetType().FullName ?? "Unknown")}\",");
            sb.Append($"\"message\":{System.Text.Json.JsonSerializer.Serialize(exception.Message)}");
            if (!string.IsNullOrEmpty(exception.StackTrace))
            {
                sb.Append($",\"stackTrace\":{System.Text.Json.JsonSerializer.Serialize(exception.StackTrace)}");
            }
            sb.Append('}');
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static void AppendJsonValue(StringBuilder sb, object? value)
    {
        switch (value)
        {
            case null:
                sb.Append("null");
                break;
            case string s:
                sb.Append(System.Text.Json.JsonSerializer.Serialize(s));
                break;
            case int or long or double or float or decimal:
                sb.Append(value);
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            default:
                sb.Append(System.Text.Json.JsonSerializer.Serialize(value.ToString()));
                break;
        }
    }

    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string GetLogLevelShortName(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???"
    };

    private static string GetLogLevelString(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "error",
        LogLevel.Critical => "critical",
        _ => "unknown"
    };
}
