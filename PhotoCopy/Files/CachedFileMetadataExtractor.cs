using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCopy.Configuration;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;

namespace PhotoCopy.Files;

/// <summary>
/// A file metadata extractor that caches parsed metadata directories to avoid re-reading files.
/// Uses a small LRU cache since files are typically processed sequentially.
/// </summary>
public sealed class CachedFileMetadataExtractor : IFileMetadataExtractor
{
    private readonly ILogger<CachedFileMetadataExtractor> _logger;
    private readonly PhotoCopyConfig _config;
    
    /// <summary>
    /// LRU cache for metadata directories. Small size since files are processed sequentially.
    /// </summary>
    private readonly LruCache<string, CachedMetadata> _cache;
    
    /// <summary>
    /// Default cache size. Small because files are typically processed sequentially.
    /// </summary>
    private const int DefaultCacheSize = 8;
    
    public CachedFileMetadataExtractor(ILogger<CachedFileMetadataExtractor> logger, IOptions<PhotoCopyConfig> config)
        : this(logger, config, DefaultCacheSize)
    {
    }
    
    /// <summary>
    /// Constructor with configurable cache size for testing.
    /// </summary>
    internal CachedFileMetadataExtractor(ILogger<CachedFileMetadataExtractor> logger, IOptions<PhotoCopyConfig> config, int cacheSize)
    {
        _logger = logger;
        _config = config.Value;
        _cache = new LruCache<string, CachedMetadata>(cacheSize);
    }

    public FileDateTime GetDateTime(FileInfo file)
    {
        DateTime created = file.CreationTime;
        DateTime modified = file.LastWriteTime;
        DateTime taken = default;
        
        try
        {
            var cached = GetOrReadMetadata(file);
            taken = ExtractDateTakenFromDirectories(cached.Directories, file.Name);
        }
        catch (Exception ex)
        {
            LogWarning("Error extracting metadata from {FileName}: {Message}", file.Name, ex.Message);
        }
        
        return new FileDateTime(created, modified, taken);
    }
    
    public (double Latitude, double Longitude)? GetCoordinates(FileInfo file)
    {
        try
        {
            var cached = GetOrReadMetadata(file);
            return ExtractCoordinatesFromDirectories(cached.Directories, file.Name);
        }
        catch (Exception ex)
        {
            LogWarning("Error extracting coordinates from {FileName}: {Message}", file.Name, ex.Message);
        }

        return null;
    }
    
    public string? GetCamera(FileInfo file)
    {
        if (!IsImage(file.Extension))
        {
            return null;
        }
        
        try
        {
            var cached = GetOrReadMetadata(file);
            return ExtractCameraFromDirectories(cached.Directories, file.Name);
        }
        catch (Exception ex)
        {
            LogWarning("Error extracting camera from {FileName}: {Message}", file.Name, ex.Message);
            return null;
        }
    }
    
    public string? GetAlbum(FileInfo file)
    {
        if (!IsImage(file.Extension))
        {
            return null;
        }
        
        try
        {
            var cached = GetOrReadMetadata(file);
            return ExtractAlbumFromDirectories(cached.Directories, file.Name);
        }
        catch (Exception ex)
        {
            LogWarning("Error extracting album from {FileName}: {Message}", file.Name, ex.Message);
            return null;
        }
    }
    
    /// <summary>
    /// Gets cached metadata directories or reads them from the file.
    /// </summary>
    private CachedMetadata GetOrReadMetadata(FileInfo file)
    {
        var key = file.FullName;
        
        if (_cache.TryGet(key, out var cached))
        {
            return cached;
        }
        
        // Read all metadata directories once
        IReadOnlyList<MetadataExtractor.Directory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(file.FullName);
        }
        catch (Exception ex)
        {
            LogWarning("Failed to read metadata from {FileName}: {Message}", file.Name, ex.Message);
            // Return empty directories to allow processing to continue
            directories = Array.Empty<MetadataExtractor.Directory>();
        }
        
        cached = new CachedMetadata(directories, file.LastWriteTimeUtc);
        _cache.Add(key, cached);
        
        return cached;
    }
    
    /// <summary>
    /// Extracts date taken from cached metadata directories.
    /// </summary>
    private DateTime ExtractDateTakenFromDirectories(IReadOnlyList<MetadataExtractor.Directory> directories, string fileName)
    {
        var subIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        
        if (subIfdDirectory != null)
        {
            // Try DateTimeOriginal first (when photo was taken)
            if (subIfdDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTaken))
            {
                return dateTaken;
            }
            
            // Fallback to DateTimeDigitized (when photo was digitized/captured by sensor)
            if (subIfdDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeDigitized, out var dateDigitized))
            {
                return dateDigitized;
            }
        }
        
        return default;
    }
    
    /// <summary>
    /// Extracts GPS coordinates from cached metadata directories.
    /// </summary>
    private (double Latitude, double Longitude)? ExtractCoordinatesFromDirectories(IReadOnlyList<MetadataExtractor.Directory> directories, string fileName)
    {
        // Try GPS directory first (for images)
        var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();
        
        if (gpsDirectory != null && gpsDirectory.TryGetGeoLocation(out var location) && !location.IsZero)
        {
            return (location.Latitude, location.Longitude);
        }
        
        // Fallback to QuickTime metadata (for videos like MOV/MP4)
        var quickTimeDirectory = directories.OfType<QuickTimeMetadataHeaderDirectory>().FirstOrDefault();
        if (quickTimeDirectory != null)
        {
            var gpsString = quickTimeDirectory.GetString(QuickTimeMetadataHeaderDirectory.TagGpsLocation);
            if (!string.IsNullOrEmpty(gpsString))
            {
                var coords = ParseIso6709(gpsString);
                if (coords.HasValue)
                {
                    return coords;
                }
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Extracts camera make and model from cached metadata directories.
    /// </summary>
    private string? ExtractCameraFromDirectories(IReadOnlyList<MetadataExtractor.Directory> directories, string fileName)
    {
        var ifd0Directory = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        
        if (ifd0Directory == null)
        {
            return null;
        }
        
        var make = ifd0Directory.GetString(ExifDirectoryBase.TagMake)?.Trim();
        var model = ifd0Directory.GetString(ExifDirectoryBase.TagModel)?.Trim();
        
        if (string.IsNullOrWhiteSpace(make) && string.IsNullOrWhiteSpace(model))
        {
            return null;
        }
        
        // Combine make and model, avoiding duplication if model already contains make
        string camera;
        if (!string.IsNullOrWhiteSpace(make) && !string.IsNullOrWhiteSpace(model))
        {
            // Check if model already starts with make (some cameras do this)
            if (model.StartsWith(make, StringComparison.OrdinalIgnoreCase))
            {
                camera = model;
            }
            else
            {
                camera = $"{make} {model}";
            }
        }
        else if (!string.IsNullOrWhiteSpace(model))
        {
            camera = model;
        }
        else
        {
            camera = make!;
        }
        
        // Sanitize for filesystem (remove invalid chars)
        return SanitizeName(camera);
    }
    
    /// <summary>
    /// Extracts album name from cached metadata directories.
    /// </summary>
    private string? ExtractAlbumFromDirectories(IReadOnlyList<MetadataExtractor.Directory> directories, string fileName)
    {
        // Try XMP Album first (most specific)
        var xmpDirectory = directories.OfType<MetadataExtractor.Formats.Xmp.XmpDirectory>().FirstOrDefault();
        if (xmpDirectory != null)
        {
            var xmpMeta = xmpDirectory.XmpMeta;
            if (xmpMeta != null)
            {
                // Try common XMP album properties
                var albumValue = TryGetXmpProperty(xmpMeta, "http://ns.adobe.com/xap/1.0/", "xap:Album")
                    ?? TryGetXmpProperty(xmpMeta, "http://ns.adobe.com/photoshop/1.0/", "photoshop:Album")
                    ?? TryGetXmpProperty(xmpMeta, "http://purl.org/dc/elements/1.1/", "dc:album");
                
                if (!string.IsNullOrWhiteSpace(albumValue))
                {
                    return SanitizeName(albumValue);
                }
            }
        }
        
        // Try IPTC SupplementalCategories
        var iptcDirectory = directories.OfType<MetadataExtractor.Formats.Iptc.IptcDirectory>().FirstOrDefault();
        if (iptcDirectory != null)
        {
            var supplementalCategories = iptcDirectory.GetString(MetadataExtractor.Formats.Iptc.IptcDirectory.TagSupplementalCategories);
            if (!string.IsNullOrWhiteSpace(supplementalCategories))
            {
                return SanitizeName(supplementalCategories);
            }
        }
        
        // Try Windows XP Subject (commonly used for albums in Windows)
        var ifd0Directory = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        if (ifd0Directory != null)
        {
            var xpSubject = ifd0Directory.GetString(ExifDirectoryBase.TagWinSubject);
            if (!string.IsNullOrWhiteSpace(xpSubject))
            {
                return SanitizeName(xpSubject);
            }
            
            // Try ImageDescription containing "Album:" prefix
            var imageDescription = ifd0Directory.GetString(ExifDirectoryBase.TagImageDescription);
            if (!string.IsNullOrWhiteSpace(imageDescription) && 
                imageDescription.StartsWith("Album:", StringComparison.OrdinalIgnoreCase))
            {
                var albumPart = imageDescription.Substring(6).Trim();
                if (!string.IsNullOrWhiteSpace(albumPart))
                {
                    return SanitizeName(albumPart);
                }
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Parses ISO 6709 formatted GPS string (e.g., "+48.8584+002.2945/" or "+48.8584+002.2945+100.00/").
    /// Used for QuickTime/MP4 video GPS metadata.
    /// </summary>
    internal static (double Latitude, double Longitude)? ParseIso6709(string iso6709)
    {
        if (string.IsNullOrWhiteSpace(iso6709))
        {
            return null;
        }
        
        // Remove trailing slash if present
        var value = iso6709.TrimEnd('/');
        
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        
        // Find all sign positions (+ or -) which indicate the start of each component
        var signPositions = new List<int>();
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '+' || value[i] == '-')
            {
                signPositions.Add(i);
            }
        }
        
        // We need at least 2 components (latitude and longitude)
        if (signPositions.Count < 2)
        {
            return null;
        }
        
        try
        {
            // Extract latitude (first component)
            var latEnd = signPositions[1];
            var latString = value.Substring(0, latEnd);
            
            // Extract longitude (second component)
            var lonEnd = signPositions.Count > 2 ? signPositions[2] : value.Length;
            var lonString = value.Substring(signPositions[1], lonEnd - signPositions[1]);
            
            if (!double.TryParse(latString, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
                !double.TryParse(lonString, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
            {
                return null;
            }
            
            // Validate coordinate ranges
            if (latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180)
            {
                return null;
            }
            
            // Skip zero coordinates (same as GpsDirectory behavior)
            if (latitude == 0 && longitude == 0)
            {
                return null;
            }
            
            return (latitude, longitude);
        }
        catch
        {
            return null;
        }
    }
    
    private bool IsImage(string extension)
    {
        return _config.AllowedExtensions.Contains(extension);
    }
    
    /// <summary>
    /// Sanitizes a name for use in file paths by removing or replacing invalid characters.
    /// </summary>
    private static string SanitizeName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }
        
        // Remove invalid path characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var result = new System.Text.StringBuilder(name.Length);
        
        foreach (var c in name)
        {
            if (Array.IndexOf(invalidChars, c) < 0)
            {
                result.Append(c);
            }
            else
            {
                // Replace with space (will be normalized later)
                result.Append(' ');
            }
        }
        
        // Normalize multiple spaces to single space and trim
        var sanitized = result.ToString();
        while (sanitized.Contains("  "))
        {
            sanitized = sanitized.Replace("  ", " ");
        }
        
        return sanitized.Trim();
    }
    
    /// <summary>
    /// Attempts to get a property value from XMP metadata.
    /// </summary>
    private static string? TryGetXmpProperty(XmpCore.IXmpMeta xmpMeta, string namespaceUri, string propertyName)
    {
        try
        {
            var property = xmpMeta.GetProperty(namespaceUri, propertyName);
            return property?.Value;
        }
        catch
        {
            return null;
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LogWarning(string message, params object[] args)
    {
        if (_config.LogLevel == OutputLevel.Verbose)
        {
            _logger.LogWarning(message, args);
        }
    }
    
    /// <summary>
    /// Clears the metadata cache. Useful for testing or when memory is constrained.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
    }
    
    /// <summary>
    /// Gets the current cache size for diagnostics.
    /// </summary>
    public int CacheCount => _cache.Count;
    
    /// <summary>
    /// Holds cached metadata directories with a timestamp for potential staleness checking.
    /// </summary>
    private sealed class CachedMetadata
    {
        public IReadOnlyList<MetadataExtractor.Directory> Directories { get; }
        public DateTime FileLastWriteTimeUtc { get; }
        
        public CachedMetadata(IReadOnlyList<MetadataExtractor.Directory> directories, DateTime fileLastWriteTimeUtc)
        {
            Directories = directories;
            FileLastWriteTimeUtc = fileLastWriteTimeUtc;
        }
    }
}

/// <summary>
/// A simple LRU (Least Recently Used) cache implementation.
/// Thread-safe for concurrent access.
/// </summary>
internal sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<CacheEntry>> _cache;
    private readonly LinkedList<CacheEntry> _lruList;
    private readonly object _lock = new();
    
    public LruCache(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive");
        }
        
        _capacity = capacity;
        _cache = new Dictionary<TKey, LinkedListNode<CacheEntry>>(capacity);
        _lruList = new LinkedList<CacheEntry>();
    }
    
    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var node))
            {
                // Move to front (most recently used)
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            
            value = default!;
            return false;
        }
    }
    
    public void Add(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var existingNode))
            {
                // Update existing entry and move to front
                existingNode.Value = new CacheEntry(key, value);
                _lruList.Remove(existingNode);
                _lruList.AddFirst(existingNode);
                return;
            }
            
            // Evict least recently used if at capacity
            if (_cache.Count >= _capacity)
            {
                var lruNode = _lruList.Last;
                if (lruNode != null)
                {
                    _cache.Remove(lruNode.Value.Key);
                    _lruList.RemoveLast();
                }
            }
            
            // Add new entry at front
            var entry = new CacheEntry(key, value);
            var newNode = new LinkedListNode<CacheEntry>(entry);
            _lruList.AddFirst(newNode);
            _cache[key] = newNode;
        }
    }
    
    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
            _lruList.Clear();
        }
    }
    
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _cache.Count;
            }
        }
    }
    
    private readonly struct CacheEntry
    {
        public TKey Key { get; }
        public TValue Value { get; }
        
        public CacheEntry(TKey key, TValue value)
        {
            Key = key;
            Value = value;
        }
    }
}
