using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace PhotoCopy.Logging;

/// <summary>
/// A console formatter that outputs log messages in structured JSON format.
/// Each log entry is written as a single-line JSON object for easy parsing.
/// </summary>
public class JsonConsoleFormatter : ConsoleFormatter
{
    /// <summary>
    /// The name used to identify this formatter.
    /// </summary>
    public const string FormatterName = "json";

    private readonly IDisposable? _optionsReloadToken;
    private PhotoCopyJsonFormatterOptions _formatterOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonConsoleFormatter"/> class.
    /// </summary>
    /// <param name="options">The formatter options.</param>
    public JsonConsoleFormatter(IOptionsMonitor<PhotoCopyJsonFormatterOptions> options)
        : base(FormatterName)
    {
        _formatterOptions = options.CurrentValue;
        _optionsReloadToken = options.OnChange(ReloadOptions);
    }

    private void ReloadOptions(PhotoCopyJsonFormatterOptions options)
    {
        _formatterOptions = options;
    }

    /// <inheritdoc />
    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (message is null && logEntry.Exception is null)
        {
            return;
        }

        var logLevel = GetLogLevelString(logEntry.LogLevel);
        var timestamp = GetTimestamp();
        var category = logEntry.Category;

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });

        writer.WriteStartObject();
        writer.WriteString("timestamp", timestamp);
        writer.WriteString("level", logLevel);
        
        if (!string.IsNullOrEmpty(message))
        {
            writer.WriteString("message", message);
        }
        
        writer.WriteString("category", category);

        // Write structured properties from state if available
        WriteStateProperties(writer, logEntry.State);

        // Write exception details if present
        if (logEntry.Exception is not null)
        {
            writer.WritePropertyName("exception");
            writer.WriteStartObject();
            writer.WriteString("type", logEntry.Exception.GetType().FullName);
            writer.WriteString("message", logEntry.Exception.Message);
            if (!string.IsNullOrEmpty(logEntry.Exception.StackTrace))
            {
                writer.WriteString("stackTrace", logEntry.Exception.StackTrace);
            }
            if (logEntry.Exception.InnerException is not null)
            {
                writer.WriteString("innerException", logEntry.Exception.InnerException.Message);
            }
            writer.WriteEndObject();
        }

        // Write scopes if enabled and available
        if (_formatterOptions.IncludeScopes && scopeProvider is not null)
        {
            WriteScopeInformation(writer, scopeProvider);
        }

        writer.WriteEndObject();
        writer.Flush();

        var json = Encoding.UTF8.GetString(stream.ToArray());
        textWriter.WriteLine(json);
    }

    private static void WriteStateProperties<TState>(Utf8JsonWriter writer, TState state)
    {
        if (state is IReadOnlyList<KeyValuePair<string, object?>> stateProperties)
        {
            var hasProperties = false;

            foreach (var property in stateProperties)
            {
                // Skip the {OriginalFormat} property and message
                if (property.Key == "{OriginalFormat}")
                {
                    continue;
                }

                if (!hasProperties)
                {
                    writer.WritePropertyName("properties");
                    writer.WriteStartObject();
                    hasProperties = true;
                }

                WriteProperty(writer, property.Key, property.Value);
            }

            if (hasProperties)
            {
                writer.WriteEndObject();
            }
        }
    }

    private static void WriteProperty(Utf8JsonWriter writer, string key, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNull(key);
                break;
            case string s:
                writer.WriteString(key, s);
                break;
            case int i:
                writer.WriteNumber(key, i);
                break;
            case long l:
                writer.WriteNumber(key, l);
                break;
            case double d:
                writer.WriteNumber(key, d);
                break;
            case float f:
                writer.WriteNumber(key, f);
                break;
            case decimal dec:
                writer.WriteNumber(key, dec);
                break;
            case bool b:
                writer.WriteBoolean(key, b);
                break;
            case DateTime dt:
                writer.WriteString(key, dt.ToString("o", CultureInfo.InvariantCulture));
                break;
            case DateTimeOffset dto:
                writer.WriteString(key, dto.ToString("o", CultureInfo.InvariantCulture));
                break;
            case TimeSpan ts:
                writer.WriteString(key, ts.ToString());
                break;
            default:
                writer.WriteString(key, value.ToString());
                break;
        }
    }

    private void WriteScopeInformation(Utf8JsonWriter writer, IExternalScopeProvider scopeProvider)
    {
        var scopes = new List<string>();
        
        scopeProvider.ForEachScope((scope, state) =>
        {
            if (scope is not null)
            {
                state.Add(scope.ToString() ?? string.Empty);
            }
        }, scopes);

        if (scopes.Count > 0)
        {
            writer.WritePropertyName("scopes");
            writer.WriteStartArray();
            foreach (var scope in scopes)
            {
                writer.WriteStringValue(scope);
            }
            writer.WriteEndArray();
        }
    }

    private string GetTimestamp()
    {
        var timestamp = _formatterOptions.UseUtcTimestamp
            ? DateTimeOffset.UtcNow
            : DateTimeOffset.Now;

        return timestamp.ToString("o", CultureInfo.InvariantCulture);
    }

    private static string GetLogLevelString(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "error",
        LogLevel.Critical => "critical",
        LogLevel.None => "none",
        _ => "unknown"
    };

    /// <inheritdoc />
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _optionsReloadToken?.Dispose();
        }
    }
}

/// <summary>
/// Options for <see cref="JsonConsoleFormatter"/>.
/// </summary>
public class PhotoCopyJsonFormatterOptions : ConsoleFormatterOptions
{
    // Inherits TimestampFormat, UseUtcTimestamp, and IncludeScopes from base class
}
