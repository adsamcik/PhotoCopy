using PhotoCopy.Files;

namespace PhotoCopy.Tests.TestingImplementation;

/// <summary>
/// An in-memory implementation of IFile for testing purposes.
/// The FileInfo points to a virtual path that doesn't need to exist on disk.
/// </summary>
public class InMemoryFile : IFile
{
    public FileInfo File { get; }
    public FileDateTime FileDateTime { get; private set; }
    public LocationData? Location { get; private set; }
    public string Checksum { get; private set; }
    public UnknownFileReason UnknownReason { get; set; } = UnknownFileReason.None;

    /// <summary>
    /// Creates an InMemoryFile with all metadata values specified.
    /// </summary>
    /// <param name="virtualPath">The virtual file path (doesn't need to exist on disk)</param>
    /// <param name="fileDateTime">The file date/time information</param>
    /// <param name="location">Optional location data</param>
    /// <param name="checksum">The file checksum (defaults to empty string)</param>
    public InMemoryFile(
        string virtualPath,
        FileDateTime fileDateTime,
        LocationData? location = null,
        string checksum = "")
    {
        File = new FileInfo(virtualPath);
        FileDateTime = fileDateTime;
        Location = location;
        Checksum = checksum;
        // Set UnknownReason based on location
        UnknownReason = location == null ? UnknownFileReason.NoGpsData : UnknownFileReason.None;
    }

    /// <summary>
    /// Creates an InMemoryFile with a simple date (used for all date fields).
    /// </summary>
    /// <param name="virtualPath">The virtual file path</param>
    /// <param name="date">The date to use for Created, Modified, and Taken</param>
    public InMemoryFile(string virtualPath, DateTime date)
        : this(virtualPath, new FileDateTime(date, date, date))
    {
    }

    /// <summary>
    /// Returns a new InMemoryFile with the specified checksum.
    /// </summary>
    public InMemoryFile WithChecksum(string checksum)
    {
        return new InMemoryFile(File.FullName, FileDateTime, Location, checksum);
    }

    /// <summary>
    /// Returns a new InMemoryFile with the specified location.
    /// </summary>
    public InMemoryFile WithLocation(LocationData location)
    {
        return new InMemoryFile(File.FullName, FileDateTime, location, Checksum);
    }

    /// <summary>
    /// Returns a new InMemoryFile with the specified location details.
    /// </summary>
    public InMemoryFile WithLocation(string district, string? state, string country)
    {
        return WithLocation(new LocationData(district, district, null, state, country));
    }
    
    /// <summary>
    /// Returns a new InMemoryFile with the specified location details including county.
    /// </summary>
    public InMemoryFile WithLocation(string district, string? county, string? state, string country)
    {
        return WithLocation(new LocationData(district, district, county, state, country));
    }

    /// <summary>
    /// Returns a new InMemoryFile with the specified FileDateTime.
    /// </summary>
    public InMemoryFile WithDateTime(FileDateTime fileDateTime)
    {
        return new InMemoryFile(File.FullName, fileDateTime, Location, Checksum);
    }

    /// <summary>
    /// Returns a new InMemoryFile with the specified taken date.
    /// </summary>
    public InMemoryFile WithTakenDate(DateTime taken)
    {
        return WithDateTime(new FileDateTime(FileDateTime.Created, FileDateTime.Modified, taken));
    }

    #region Static Factory Methods

    /// <summary>
    /// Creates a photo file with typical photo metadata.
    /// </summary>
    /// <param name="name">The file name (e.g., "photo.jpg"). If no directory is specified, uses a virtual temp path.</param>
    /// <param name="taken">The date the photo was taken</param>
    /// <param name="location">Optional location data</param>
    /// <returns>A new InMemoryFile configured as a photo</returns>
    public static InMemoryFile CreatePhoto(string name, DateTime taken, LocationData? location = null)
    {
        var path = EnsureFullPath(name);
        var fileDateTime = new FileDateTime(taken, taken, taken);
        var checksum = GenerateTestChecksum(path, taken);
        return new InMemoryFile(path, fileDateTime, location, checksum);
    }

    /// <summary>
    /// Creates a video file with typical video metadata.
    /// </summary>
    /// <param name="name">The file name (e.g., "video.mp4"). If no directory is specified, uses a virtual temp path.</param>
    /// <param name="taken">The date the video was taken</param>
    /// <returns>A new InMemoryFile configured as a video</returns>
    public static InMemoryFile CreateVideo(string name, DateTime taken)
    {
        var path = EnsureFullPath(name);
        var fileDateTime = new FileDateTime(taken, taken, taken);
        var checksum = GenerateTestChecksum(path, taken);
        return new InMemoryFile(path, fileDateTime, null, checksum);
    }

    /// <summary>
    /// Creates a file with separate created, modified, and taken dates.
    /// </summary>
    /// <param name="name">The file name</param>
    /// <param name="created">The file creation date</param>
    /// <param name="modified">The file modification date</param>
    /// <param name="taken">The date the media was captured</param>
    /// <returns>A new InMemoryFile with the specified dates</returns>
    public static InMemoryFile CreateWithDates(string name, DateTime created, DateTime modified, DateTime taken)
    {
        var path = EnsureFullPath(name);
        var fileDateTime = new FileDateTime(created, modified, taken);
        var checksum = GenerateTestChecksum(path, taken);
        return new InMemoryFile(path, fileDateTime, null, checksum);
    }

    /// <summary>
    /// Creates a minimal file for simple tests.
    /// </summary>
    /// <param name="name">The file name</param>
    /// <returns>A new InMemoryFile with default metadata</returns>
    public static InMemoryFile Create(string name)
    {
        return CreatePhoto(name, DateTime.Now);
    }

    #endregion

    #region Builder Pattern

    /// <summary>
    /// Starts building an InMemoryFile with the specified path.
    /// </summary>
    public static InMemoryFileBuilder Builder(string virtualPath) => new(virtualPath);

    public class InMemoryFileBuilder
    {
        private readonly string _virtualPath;
        private DateTime _created = DateTime.Now;
        private DateTime _modified = DateTime.Now;
        private DateTime _taken = DateTime.Now;
        private LocationData? _location;
        private string _checksum = "";

        public InMemoryFileBuilder(string virtualPath)
        {
            _virtualPath = EnsureFullPath(virtualPath);
        }

        public InMemoryFileBuilder WithCreated(DateTime created)
        {
            _created = created;
            return this;
        }

        public InMemoryFileBuilder WithModified(DateTime modified)
        {
            _modified = modified;
            return this;
        }

        public InMemoryFileBuilder WithTaken(DateTime taken)
        {
            _taken = taken;
            return this;
        }

        public InMemoryFileBuilder WithAllDates(DateTime date)
        {
            _created = date;
            _modified = date;
            _taken = date;
            return this;
        }

        public InMemoryFileBuilder WithLocation(LocationData location)
        {
            _location = location;
            return this;
        }

        public InMemoryFileBuilder WithLocation(string district, string? state, string country)
        {
            _location = new LocationData(district, district, null, state, country);
            return this;
        }
        
        public InMemoryFileBuilder WithLocation(string district, string? county, string? state, string country)
        {
            _location = new LocationData(district, district, county, state, country);
            return this;
        }

        public InMemoryFileBuilder WithChecksum(string checksum)
        {
            _checksum = checksum;
            return this;
        }

        public InMemoryFile Build()
        {
            var fileDateTime = new FileDateTime(_created, _modified, _taken);
            var checksum = string.IsNullOrEmpty(_checksum) 
                ? GenerateTestChecksum(_virtualPath, _taken) 
                : _checksum;
            return new InMemoryFile(_virtualPath, fileDateTime, _location, checksum);
        }
    }

    #endregion

    #region Private Helpers

    private static string EnsureFullPath(string name)
    {
        if (Path.IsPathRooted(name))
        {
            return name;
        }
        
        // Use a virtual temp directory for testing
        return Path.Combine(Path.GetTempPath(), "PhotoCopyTests", name);
    }

    private static string GenerateTestChecksum(string path, DateTime date)
    {
        // Generate a deterministic checksum based on path and date for testing
        var input = $"{path}_{date:yyyyMMddHHmmss}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    #endregion

    public override string ToString()
    {
        return $"InMemoryFile: {File.Name} (Taken: {FileDateTime.Taken:yyyy-MM-dd HH:mm:ss})";
    }
}
