using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoCopy.Files;

public class GenericFile : IFile, IDisposable
{
    private string _checksum;
    private readonly object _checksumLock = new();
    private readonly SemaphoreSlim _asyncLock = new(1, 1);
    private readonly IChecksumCalculator _checksumCalculator;
    private bool _disposed;

    public FileInfo File { get; }
    public FileDateTime FileDateTime { get; }
    public LocationData? Location => null;
    
    /// <summary>
    /// Gets the reason why this file has no location data.
    /// GenericFile always returns NoGpsData as it doesn't support location metadata.
    /// </summary>
    public UnknownFileReason UnknownReason { get; init; } = UnknownFileReason.NoGpsData;
    
    /// <summary>
    /// Gets the camera make and model. GenericFile always returns null as it doesn't support EXIF metadata.
    /// </summary>
    public string? Camera => null;
    
    /// <summary>
    /// Gets the album name. GenericFile always returns null as it doesn't support EXIF metadata.
    /// </summary>
    public string? Album => null;
    
    /// <summary>
    /// Gets the file checksum. Returns an empty string if not calculated.
    /// Use <see cref="EnsureChecksum"/> or <see cref="CalculateChecksum"/> to compute the checksum.
    /// </summary>
    public string Checksum => _checksum;

    public GenericFile(FileInfo file, FileDateTime fileDateTime, string? checksum = null, IChecksumCalculator? checksumCalculator = null)
    {
        File = file;
        FileDateTime = fileDateTime;
        _checksum = checksum ?? string.Empty;
        _checksumCalculator = checksumCalculator ?? new Sha256ChecksumCalculator();
    }

    /// <summary>
    /// Ensures the checksum is calculated. If already calculated, returns the cached value.
    /// This method explicitly performs file I/O if the checksum hasn't been computed yet.
    /// </summary>
    /// <returns>The file's SHA256 checksum.</returns>
    public string EnsureChecksum()
    {
        // Fast path: already calculated
        if (!string.IsNullOrEmpty(_checksum))
        {
            return _checksum;
        }

        // Thread-safe lazy initialization
        lock (_checksumLock)
        {
            // Double-check after acquiring lock
            if (!string.IsNullOrEmpty(_checksum))
            {
                return _checksum;
            }

            _checksum = _checksumCalculator.Calculate(File);
            return _checksum;
        }
    }

    /// <summary>
    /// Asynchronously ensures the checksum is calculated. If already calculated, returns the cached value.
    /// This method performs file I/O asynchronously if the checksum hasn't been computed yet.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The file's SHA256 checksum.</returns>
    public async Task<string> EnsureChecksumAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: already calculated
        if (!string.IsNullOrEmpty(_checksum))
        {
            return _checksum;
        }

        await _asyncLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (!string.IsNullOrEmpty(_checksum))
            {
                return _checksum;
            }

            _checksum = await _checksumCalculator.CalculateAsync(File, cancellationToken);
            return _checksum;
        }
        finally
        {
            _asyncLock.Release();
        }
    }

    /// <summary>
    /// Calculates the checksum for this file. This method always performs file I/O.
    /// The result is cached for subsequent calls to <see cref="Checksum"/> and <see cref="EnsureChecksum"/>.
    /// </summary>
    /// <returns>The file's SHA256 checksum.</returns>
    public string CalculateChecksum()
    {
        lock (_checksumLock)
        {
            _checksum = _checksumCalculator.Calculate(File);
            return _checksum;
        }
    }

    /// <summary>
    /// Asynchronously calculates the checksum for this file. This method always performs file I/O.
    /// The result is cached for subsequent calls.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The file's SHA256 checksum.</returns>
    public async Task<string> CalculateChecksumAsync(CancellationToken cancellationToken = default)
    {
        await _asyncLock.WaitAsync(cancellationToken);
        try
        {
            _checksum = await _checksumCalculator.CalculateAsync(File, cancellationToken);
            return _checksum;
        }
        finally
        {
            _asyncLock.Release();
        }
    }

    /// <summary>
    /// Disposes the resources used by this file, including the async lock.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes managed resources.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _asyncLock.Dispose();
        }

        _disposed = true;
    }
}