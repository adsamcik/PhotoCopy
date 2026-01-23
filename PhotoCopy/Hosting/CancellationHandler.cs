using Microsoft.Extensions.Logging;
using System;
using System.Threading;

namespace PhotoCopy.Hosting;

/// <summary>
/// Handles graceful cancellation via Ctrl+C.
/// </summary>
public sealed class CancellationHandler : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly ILogger? _logger;
    private readonly ConsoleCancelEventHandler _handler;
    private bool _disposed;

    /// <summary>
    /// Creates a new cancellation handler that listens for Ctrl+C.
    /// </summary>
    /// <param name="logger">Optional logger for cancellation message.</param>
    public CancellationHandler(ILogger? logger = null)
    {
        _cts = new CancellationTokenSource();
        _logger = logger;
        _handler = (_, e) =>
        {
            e.Cancel = true;
            _cts.Cancel();
            _logger?.LogWarning("Cancellation requested...");
        };
        Console.CancelKeyPress += _handler;
    }

    /// <summary>
    /// Gets the cancellation token.
    /// </summary>
    public CancellationToken Token => _cts.Token;

    /// <summary>
    /// Disposes the handler and cleans up event subscription.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Console.CancelKeyPress -= _handler;
        _cts.Dispose();
    }
}
