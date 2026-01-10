using System;
using System.Collections.Generic;
using System.Text;

namespace PhotoCopy.Tests.TestingImplementation;

/// <summary>
/// Extended image generator with builder pattern for creating JPEG and PNG images with comprehensive EXIF metadata.
/// Supports additional EXIF fields beyond the basic MockImageGenerator including camera information, orientation, and dimensions.
/// 
/// The generated images follow the TIFF/EXIF specification with:
/// - Little-endian byte order ("II")
/// - IFD0 for basic tags (Make, Model, Orientation, ImageWidth, ImageHeight)
/// - EXIF Sub-IFD for date/camera settings (DateTimeOriginal, DateTimeDigitized)
/// - GPS IFD for coordinates
/// - Tags sorted by tag number within each IFD
/// </summary>
public class MockImageGeneratorExtended
{
    #region TIFF/EXIF Constants
    
    // TIFF header constants
    private const ushort TiffLittleEndian = 0x4949; // "II" - Intel byte order
    private const ushort TiffMagic = 0x002A;
    
    // IFD tag types
    private const ushort TypeByte = 1;         // 1-byte unsigned integer
    private const ushort TypeAscii = 2;        // ASCII string (null-terminated)
    private const ushort TypeShort = 3;        // 2-byte unsigned integer
    private const ushort TypeLong = 4;         // 4-byte unsigned integer
    private const ushort TypeRational = 5;     // Two LONGs: numerator and denominator
    
    // IFD0 tags (sorted by tag number)
    private const ushort TagImageWidth = 0x0100;           // Image width in pixels
    private const ushort TagImageHeight = 0x0101;          // Image height in pixels
    private const ushort TagMake = 0x010F;                 // Camera manufacturer
    private const ushort TagModel = 0x0110;                // Camera model
    private const ushort TagOrientation = 0x0112;          // Image orientation (1-8)
    private const ushort TagExifOffset = 0x8769;           // Pointer to EXIF Sub-IFD
    private const ushort TagGpsOffset = 0x8825;            // Pointer to GPS IFD
    
    // EXIF Sub-IFD tags (sorted by tag number)
    private const ushort TagDateTimeOriginal = 0x9003;     // Date/time photo was taken
    private const ushort TagDateTimeDigitized = 0x9004;    // Date/time photo was digitized
    
    // GPS IFD tags (sorted by tag number)
    private const ushort TagGpsLatitudeRef = 0x0001;       // 'N' or 'S'
    private const ushort TagGpsLatitude = 0x0002;          // Latitude as 3 rationals (degrees, minutes, seconds)
    private const ushort TagGpsLongitudeRef = 0x0003;      // 'E' or 'W'
    private const ushort TagGpsLongitude = 0x0004;         // Longitude as 3 rationals (degrees, minutes, seconds)
    
    #endregion
    
    #region Builder State
    
    private readonly ImageFormat _format;
    private DateTime? _dateTimeOriginal;
    private DateTime? _dateTimeDigitized;
    private (double Lat, double Lon)? _gps;
    private string? _cameraMake;
    private string? _cameraModel;
    private ushort? _orientation;
    private int? _imageWidth;
    private int? _imageHeight;
    
    #endregion
    
    /// <summary>
    /// Image format to generate.
    /// </summary>
    public enum ImageFormat
    {
        Jpeg,
        Png
    }
    
    /// <summary>
    /// Private constructor - use static factory methods.
    /// </summary>
    private MockImageGeneratorExtended(ImageFormat format)
    {
        _format = format;
    }
    
    #region Static Factory Methods
    
    /// <summary>
    /// Creates a new JPEG image builder.
    /// </summary>
    /// <returns>A new builder instance configured for JPEG output.</returns>
    /// <example>
    /// <code>
    /// var imageBytes = MockImageGeneratorExtended.Jpeg()
    ///     .WithDate(new DateTime(2023, 6, 15))
    ///     .WithCameraMake("Canon")
    ///     .Build();
    /// </code>
    /// </example>
    public static MockImageGeneratorExtended Jpeg() => new(ImageFormat.Jpeg);
    
    /// <summary>
    /// Creates a new PNG image builder.
    /// </summary>
    /// <returns>A new builder instance configured for PNG output.</returns>
    /// <example>
    /// <code>
    /// var imageBytes = MockImageGeneratorExtended.Png()
    ///     .WithGps(48.8566, 2.3522)
    ///     .WithOrientation(1)
    ///     .Build();
    /// </code>
    /// </example>
    public static MockImageGeneratorExtended Png() => new(ImageFormat.Png);
    
    #endregion
    
    #region Builder Methods
    
    /// <summary>
    /// Sets the DateTimeOriginal EXIF field - the date/time when the photo was originally taken.
    /// </summary>
    /// <param name="date">The date and time the photo was taken.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public MockImageGeneratorExtended WithDate(DateTime date)
    {
        _dateTimeOriginal = date;
        return this;
    }
    
    /// <summary>
    /// Sets both DateTimeOriginal and DateTimeDigitized to the same value.
    /// </summary>
    /// <param name="date">The date and time to set for both fields.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public MockImageGeneratorExtended WithDates(DateTime date)
    {
        _dateTimeOriginal = date;
        _dateTimeDigitized = date;
        return this;
    }
    
    /// <summary>
    /// Sets the DateTimeDigitized EXIF field - the date/time when the image was digitized.
    /// This is typically the same as DateTimeOriginal for digital cameras.
    /// </summary>
    /// <param name="date">The date and time the photo was digitized.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public MockImageGeneratorExtended WithDateDigitized(DateTime date)
    {
        _dateTimeDigitized = date;
        return this;
    }
    
    /// <summary>
    /// Sets the GPS coordinates in the EXIF GPS IFD.
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees. Positive values are North, negative are South.</param>
    /// <param name="longitude">Longitude in decimal degrees. Positive values are East, negative are West.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <example>
    /// <code>
    /// // Paris, France coordinates
    /// .WithGps(48.8566, 2.3522)
    /// 
    /// // Sydney, Australia coordinates (Southern hemisphere)
    /// .WithGps(-33.8688, 151.2093)
    /// </code>
    /// </example>
    public MockImageGeneratorExtended WithGps(double latitude, double longitude)
    {
        if (latitude < -90 || latitude > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be between -90 and 90 degrees.");
        if (longitude < -180 || longitude > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be between -180 and 180 degrees.");
        
        _gps = (latitude, longitude);
        return this;
    }
    
    /// <summary>
    /// Sets the camera manufacturer (Make) in IFD0.
    /// </summary>
    /// <param name="make">The camera manufacturer name (e.g., "Canon", "Nikon", "Sony", "Apple").</param>
    /// <returns>The builder instance for method chaining.</returns>
    public MockImageGeneratorExtended WithCameraMake(string make)
    {
        _cameraMake = make ?? throw new ArgumentNullException(nameof(make));
        return this;
    }
    
    /// <summary>
    /// Sets the camera model in IFD0.
    /// </summary>
    /// <param name="model">The camera model name (e.g., "EOS R5", "D850", "A7R IV", "iPhone 14 Pro").</param>
    /// <returns>The builder instance for method chaining.</returns>
    public MockImageGeneratorExtended WithCameraModel(string model)
    {
        _cameraModel = model ?? throw new ArgumentNullException(nameof(model));
        return this;
    }
    
    /// <summary>
    /// Sets both camera make and model in a single call.
    /// </summary>
    /// <param name="make">The camera manufacturer name.</param>
    /// <param name="model">The camera model name.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public MockImageGeneratorExtended WithCamera(string make, string model)
    {
        return WithCameraMake(make).WithCameraModel(model);
    }
    
    /// <summary>
    /// Sets the image orientation in IFD0.
    /// </summary>
    /// <param name="orientation">
    /// The orientation value (1-8) as defined by EXIF:
    /// <list type="bullet">
    /// <item>1 = Normal (0° rotation)</item>
    /// <item>2 = Mirrored horizontal</item>
    /// <item>3 = Rotated 180°</item>
    /// <item>4 = Mirrored vertical</item>
    /// <item>5 = Mirrored horizontal then rotated 270° CW</item>
    /// <item>6 = Rotated 90° CW</item>
    /// <item>7 = Mirrored horizontal then rotated 90° CW</item>
    /// <item>8 = Rotated 270° CW</item>
    /// </list>
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    public MockImageGeneratorExtended WithOrientation(ushort orientation)
    {
        if (orientation < 1 || orientation > 8)
            throw new ArgumentOutOfRangeException(nameof(orientation), "Orientation must be between 1 and 8.");
        
        _orientation = orientation;
        return this;
    }
    
    /// <summary>
    /// Sets the image dimensions (width and height) in IFD0.
    /// Note: These are metadata values and do not affect the actual pixel dimensions of the minimal test image.
    /// </summary>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public MockImageGeneratorExtended WithDimensions(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than 0.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than 0.");
        
        _imageWidth = width;
        _imageHeight = height;
        return this;
    }
    
    #endregion
    
    #region Build Methods
    
    /// <summary>
    /// Builds the image with all configured metadata and returns the raw bytes.
    /// </summary>
    /// <returns>A byte array containing the complete image file with EXIF metadata.</returns>
    public byte[] Build()
    {
        return _format switch
        {
            ImageFormat.Jpeg => CreateJpeg(),
            ImageFormat.Png => CreatePng(),
            _ => throw new InvalidOperationException($"Unsupported image format: {_format}")
        };
    }
    
    #endregion
    
    #region JPEG Generation
    
    /// <summary>
    /// Creates a minimal valid JPEG image with the configured EXIF metadata.
    /// </summary>
    private byte[] CreateJpeg()
    {
        var result = new List<byte>();
        
        // SOI (Start of Image)
        result.Add(0xFF);
        result.Add(0xD8);
        
        // APP1 segment with EXIF data (if we have any metadata)
        if (HasMetadata())
        {
            var exifData = CreateExifData();
            
            // APP1 marker
            result.Add(0xFF);
            result.Add(0xE1);
            
            // Length (includes length field itself but not marker)
            ushort length = (ushort)(2 + 6 + exifData.Length); // 2 for length, 6 for "Exif\0\0"
            result.Add((byte)(length >> 8));   // Big endian for JPEG segments
            result.Add((byte)(length & 0xFF));
            
            // EXIF header: "Exif\0\0"
            result.AddRange(Encoding.ASCII.GetBytes("Exif"));
            result.Add(0x00);
            result.Add(0x00);
            
            // TIFF data (little-endian within EXIF)
            result.AddRange(exifData);
        }
        
        // APP0 (JFIF marker) - minimal
        result.Add(0xFF);
        result.Add(0xE0);
        result.Add(0x00);
        result.Add(0x10); // Length = 16
        result.AddRange(Encoding.ASCII.GetBytes("JFIF"));
        result.Add(0x00); // Null terminator
        result.Add(0x01); // Version major
        result.Add(0x01); // Version minor
        result.Add(0x00); // Aspect ratio units (0 = no units)
        result.Add(0x00);
        result.Add(0x01); // X density
        result.Add(0x00);
        result.Add(0x01); // Y density
        result.Add(0x00); // Thumbnail width
        result.Add(0x00); // Thumbnail height
        
        // DQT (Define Quantization Table) - minimal
        result.Add(0xFF);
        result.Add(0xDB);
        result.Add(0x00);
        result.Add(0x43); // Length = 67
        result.Add(0x00); // Table ID 0, 8-bit precision
        // 64 bytes of quantization values (all 16 for simplicity)
        for (int i = 0; i < 64; i++)
            result.Add(0x10);
        
        // SOF0 (Start of Frame - Baseline DCT)
        result.Add(0xFF);
        result.Add(0xC0);
        result.Add(0x00);
        result.Add(0x0B); // Length = 11
        result.Add(0x08); // Precision = 8 bits
        result.Add(0x00);
        result.Add(0x01); // Height = 1
        result.Add(0x00);
        result.Add(0x01); // Width = 1
        result.Add(0x01); // Number of components = 1 (grayscale)
        result.Add(0x01); // Component ID
        result.Add(0x11); // Sampling factors (1x1)
        result.Add(0x00); // Quantization table ID
        
        // DHT (Define Huffman Table) - minimal DC table
        result.Add(0xFF);
        result.Add(0xC4);
        result.Add(0x00);
        result.Add(0x1F); // Length = 31
        result.Add(0x00); // Table class 0 (DC), ID 0
        // Huffman table specification (16 bytes for code lengths + symbols)
        result.Add(0x00); result.Add(0x01); result.Add(0x05); result.Add(0x01);
        result.Add(0x01); result.Add(0x01); result.Add(0x01); result.Add(0x01);
        result.Add(0x01); result.Add(0x00); result.Add(0x00); result.Add(0x00);
        result.Add(0x00); result.Add(0x00); result.Add(0x00); result.Add(0x00);
        // Symbols
        result.Add(0x00); result.Add(0x01); result.Add(0x02); result.Add(0x03);
        result.Add(0x04); result.Add(0x05); result.Add(0x06); result.Add(0x07);
        result.Add(0x08); result.Add(0x09); result.Add(0x0A); result.Add(0x0B);
        
        // DHT (Define Huffman Table) - minimal AC table
        result.Add(0xFF);
        result.Add(0xC4);
        result.Add(0x00);
        result.Add(0xB5); // Length = 181
        result.Add(0x10); // Table class 1 (AC), ID 0
        // Standard JPEG AC Huffman table
        result.Add(0x00); result.Add(0x02); result.Add(0x01); result.Add(0x03);
        result.Add(0x03); result.Add(0x02); result.Add(0x04); result.Add(0x03);
        result.Add(0x05); result.Add(0x05); result.Add(0x04); result.Add(0x04);
        result.Add(0x00); result.Add(0x00); result.Add(0x01); result.Add(0x7D);
        // AC Huffman symbols (162 symbols)
        byte[] acSymbols = {
            0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12,
            0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
            0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xA1, 0x08,
            0x23, 0x42, 0xB1, 0xC1, 0x15, 0x52, 0xD1, 0xF0,
            0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0A, 0x16,
            0x17, 0x18, 0x19, 0x1A, 0x25, 0x26, 0x27, 0x28,
            0x29, 0x2A, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39,
            0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
            0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
            0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
            0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79,
            0x7A, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
            0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98,
            0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7,
            0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6,
            0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4, 0xC5,
            0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4,
            0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE1, 0xE2,
            0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA,
            0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
            0xF9, 0xFA
        };
        result.AddRange(acSymbols);
        
        // SOS (Start of Scan)
        result.Add(0xFF);
        result.Add(0xDA);
        result.Add(0x00);
        result.Add(0x08); // Length = 8
        result.Add(0x01); // Number of components
        result.Add(0x01); // Component ID
        result.Add(0x00); // DC/AC Huffman table IDs
        result.Add(0x00); // Spectral selection start
        result.Add(0x3F); // Spectral selection end
        result.Add(0x00); // Successive approximation
        
        // Minimal scan data (1x1 gray pixel)
        result.Add(0xFB);
        result.Add(0xD3);
        result.Add(0x28);
        result.Add(0xA0);
        
        // EOI (End of Image)
        result.Add(0xFF);
        result.Add(0xD9);
        
        return result.ToArray();
    }
    
    #endregion
    
    #region PNG Generation
    
    /// <summary>
    /// Creates a minimal valid PNG image with the configured EXIF metadata.
    /// </summary>
    private byte[] CreatePng()
    {
        var result = new List<byte>();
        
        // PNG signature
        result.AddRange(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        
        // IHDR chunk (image header) - 1x1 pixel, 8-bit grayscale
        var ihdrData = new byte[]
        {
            0x00, 0x00, 0x00, 0x01, // Width = 1
            0x00, 0x00, 0x00, 0x01, // Height = 1
            0x08,                   // Bit depth = 8
            0x00,                   // Color type = 0 (grayscale)
            0x00,                   // Compression method = 0 (deflate)
            0x00,                   // Filter method = 0
            0x00                    // Interlace method = 0 (no interlace)
        };
        result.AddRange(CreatePngChunk("IHDR", ihdrData));
        
        // eXIf chunk (EXIF data) - if we have metadata
        if (HasMetadata())
        {
            var exifData = CreateExifData();
            result.AddRange(CreatePngChunk("eXIf", exifData));
        }
        
        // IDAT chunk (image data) - minimal deflate-compressed 1x1 grayscale image
        var idatData = new byte[]
        {
            0x78, 0x9C,             // zlib header (deflate, default compression)
            0x62, 0x60, 0x00,       // Compressed data (filter byte 0 + gray pixel)
            0x00, 0x00, 0x02, 0x00, 0x01 // Adler-32 checksum
        };
        result.AddRange(CreatePngChunk("IDAT", idatData));
        
        // IEND chunk (image end)
        result.AddRange(CreatePngChunk("IEND", Array.Empty<byte>()));
        
        return result.ToArray();
    }
    
    /// <summary>
    /// Creates a PNG chunk with proper length, type, data, and CRC.
    /// </summary>
    private static byte[] CreatePngChunk(string type, byte[] data)
    {
        var result = new List<byte>();
        
        // Length (4 bytes, big endian)
        int length = data.Length;
        result.Add((byte)((length >> 24) & 0xFF));
        result.Add((byte)((length >> 16) & 0xFF));
        result.Add((byte)((length >> 8) & 0xFF));
        result.Add((byte)(length & 0xFF));
        
        // Type (4 bytes ASCII)
        var typeBytes = Encoding.ASCII.GetBytes(type);
        result.AddRange(typeBytes);
        
        // Data
        result.AddRange(data);
        
        // CRC (calculated over type + data)
        var crcData = new byte[4 + data.Length];
        Array.Copy(typeBytes, 0, crcData, 0, 4);
        Array.Copy(data, 0, crcData, 4, data.Length);
        uint crc = CalculateCrc32(crcData);
        result.Add((byte)((crc >> 24) & 0xFF));
        result.Add((byte)((crc >> 16) & 0xFF));
        result.Add((byte)((crc >> 8) & 0xFF));
        result.Add((byte)(crc & 0xFF));
        
        return result.ToArray();
    }
    
    #endregion
    
    #region EXIF Data Generation
    
    /// <summary>
    /// Checks if any metadata has been configured.
    /// </summary>
    private bool HasMetadata()
    {
        return _dateTimeOriginal.HasValue ||
               _dateTimeDigitized.HasValue ||
               _gps.HasValue ||
               _cameraMake != null ||
               _cameraModel != null ||
               _orientation.HasValue ||
               _imageWidth.HasValue ||
               _imageHeight.HasValue;
    }
    
    /// <summary>
    /// Checks if we need an EXIF Sub-IFD (for date fields).
    /// </summary>
    private bool NeedsExifSubIfd()
    {
        return _dateTimeOriginal.HasValue || _dateTimeDigitized.HasValue;
    }
    
    /// <summary>
    /// Checks if we need a GPS IFD.
    /// </summary>
    private bool NeedsGpsIfd()
    {
        return _gps.HasValue;
    }
    
    /// <summary>
    /// Creates the complete EXIF/TIFF data structure with all configured metadata.
    /// 
    /// TIFF Structure:
    /// - TIFF Header (8 bytes) at offset 0
    /// - IFD0 starting at offset 8
    ///   - Contains: ImageWidth, ImageHeight, Make, Model, Orientation, ExifOffset, GpsOffset
    ///   - Tags are sorted by tag number
    /// - EXIF Sub-IFD (if needed)
    ///   - Contains: DateTimeOriginal, DateTimeDigitized
    /// - GPS IFD (if needed)
    ///   - Contains: GPSLatitudeRef, GPSLatitude, GPSLongitudeRef, GPSLongitude
    /// - Data values that don't fit in 4 bytes follow the IFDs
    /// </summary>
    private byte[] CreateExifData()
    {
        var result = new List<byte>();
        
        // Collect all IFD0 entries we need (will be sorted by tag)
        var ifd0Entries = new List<Ifd0Entry>();
        
        if (_imageWidth.HasValue)
            ifd0Entries.Add(new Ifd0Entry { Tag = TagImageWidth, Type = Ifd0EntryType.Long, Value = _imageWidth.Value });
        if (_imageHeight.HasValue)
            ifd0Entries.Add(new Ifd0Entry { Tag = TagImageHeight, Type = Ifd0EntryType.Long, Value = _imageHeight.Value });
        if (_cameraMake != null)
            ifd0Entries.Add(new Ifd0Entry { Tag = TagMake, Type = Ifd0EntryType.Ascii, StringValue = _cameraMake });
        if (_cameraModel != null)
            ifd0Entries.Add(new Ifd0Entry { Tag = TagModel, Type = Ifd0EntryType.Ascii, StringValue = _cameraModel });
        if (_orientation.HasValue)
            ifd0Entries.Add(new Ifd0Entry { Tag = TagOrientation, Type = Ifd0EntryType.Short, Value = _orientation.Value });
        
        // Add ExifOffset and GpsOffset placeholders - actual offsets calculated later
        if (NeedsExifSubIfd())
            ifd0Entries.Add(new Ifd0Entry { Tag = TagExifOffset, Type = Ifd0EntryType.SubIfdPointer });
        if (NeedsGpsIfd())
            ifd0Entries.Add(new Ifd0Entry { Tag = TagGpsOffset, Type = Ifd0EntryType.SubIfdPointer });
        
        // Sort entries by tag number (TIFF requirement)
        ifd0Entries.Sort((a, b) => a.Tag.CompareTo(b.Tag));
        
        // TIFF header (8 bytes)
        // Byte order: Little endian ("II")
        result.Add(0x49); // 'I'
        result.Add(0x49); // 'I'
        // TIFF magic number (42)
        WriteUInt16(result, TiffMagic);
        // Offset to first IFD (8 = immediately after header)
        WriteUInt32(result, 8);
        
        // Now at offset 8: IFD0
        int ifd0Offset = 8;
        int ifd0EntryCount = ifd0Entries.Count;
        
        // IFD size: 2 (count) + entries*12 + 4 (next IFD pointer)
        int ifd0Size = 2 + (ifd0EntryCount * 12) + 4;
        int afterIfd0 = ifd0Offset + ifd0Size;
        
        // Calculate string data offsets for IFD0 strings
        int currentDataOffset = afterIfd0;
        
        // First pass: assign offsets to strings that don't fit inline
        foreach (var entry in ifd0Entries)
        {
            if (entry.Type == Ifd0EntryType.Ascii && entry.StringValue != null)
            {
                int stringLen = entry.StringValue.Length + 1; // +1 for null terminator
                if (stringLen > 4)
                {
                    entry.DataOffset = currentDataOffset;
                    currentDataOffset += stringLen;
                }
            }
        }
        
        // Calculate EXIF Sub-IFD offset and size
        int exifSubIfdOffset = 0;
        int exifDateCount = 0;
        int afterExifSubIfd = currentDataOffset;
        
        if (NeedsExifSubIfd())
        {
            exifSubIfdOffset = currentDataOffset;
            if (_dateTimeOriginal.HasValue) exifDateCount++;
            if (_dateTimeDigitized.HasValue) exifDateCount++;
            
            // EXIF Sub-IFD: 2 (count) + N entries * 12 + 4 (next pointer)
            int exifSubIfdSize = 2 + (exifDateCount * 12) + 4;
            afterExifSubIfd = exifSubIfdOffset + exifSubIfdSize;
            
            // Update the ExifOffset entry with actual offset
            foreach (var entry in ifd0Entries)
            {
                if (entry.Tag == TagExifOffset)
                {
                    entry.Value = exifSubIfdOffset;
                    break;
                }
            }
            
            currentDataOffset = afterExifSubIfd;
        }
        
        // Calculate date string offsets (each date string is 20 bytes: "YYYY:MM:DD HH:MM:SS\0")
        int dateTimeOriginalOffset = 0;
        int dateTimeDigitizedOffset = 0;
        
        if (_dateTimeOriginal.HasValue)
        {
            dateTimeOriginalOffset = currentDataOffset;
            currentDataOffset += 20;
        }
        if (_dateTimeDigitized.HasValue)
        {
            dateTimeDigitizedOffset = currentDataOffset;
            currentDataOffset += 20;
        }
        
        // Calculate GPS IFD offset
        int gpsIfdOffset = 0;
        int afterGpsIfd = currentDataOffset;
        int gpsLatValueOffset = 0;
        int gpsLonValueOffset = 0;
        
        if (NeedsGpsIfd())
        {
            gpsIfdOffset = currentDataOffset;
            // GPS IFD: 2 (count) + 4 entries * 12 + 4 (next pointer) = 54 bytes
            int gpsIfdSize = 2 + (4 * 12) + 4;
            afterGpsIfd = gpsIfdOffset + gpsIfdSize;
            
            // GPS rational values come after GPS IFD
            // Latitude: 3 rationals = 24 bytes
            // Longitude: 3 rationals = 24 bytes
            gpsLatValueOffset = afterGpsIfd;
            gpsLonValueOffset = gpsLatValueOffset + 24;
            
            // Update the GpsOffset entry with actual offset
            foreach (var entry in ifd0Entries)
            {
                if (entry.Tag == TagGpsOffset)
                {
                    entry.Value = gpsIfdOffset;
                    break;
                }
            }
        }
        
        // Write IFD0 entry count
        WriteUInt16(result, (ushort)ifd0EntryCount);
        
        // Write IFD0 entries
        foreach (var entry in ifd0Entries)
        {
            WriteIfd0Entry(result, entry);
        }
        
        // Next IFD offset (0 = no more IFDs in main chain)
        WriteUInt32(result, 0);
        
        // Write IFD0 string data (strings that didn't fit inline)
        foreach (var entry in ifd0Entries)
        {
            if (entry.Type == Ifd0EntryType.Ascii && entry.StringValue != null && entry.DataOffset > 0)
            {
                result.AddRange(Encoding.ASCII.GetBytes(entry.StringValue));
                result.Add(0x00); // Null terminator
            }
        }
        
        // Write EXIF Sub-IFD if needed
        if (NeedsExifSubIfd())
        {
            // Entry count
            WriteUInt16(result, (ushort)exifDateCount);
            
            // Write entries in tag order (0x9003 before 0x9004)
            if (_dateTimeOriginal.HasValue)
            {
                WriteIfdEntry(result, TagDateTimeOriginal, TypeAscii, 20, dateTimeOriginalOffset);
            }
            if (_dateTimeDigitized.HasValue)
            {
                WriteIfdEntry(result, TagDateTimeDigitized, TypeAscii, 20, dateTimeDigitizedOffset);
            }
            
            // Next IFD offset (0 = no more)
            WriteUInt32(result, 0);
            
            // Write date string values
            if (_dateTimeOriginal.HasValue)
            {
                string dateStr = _dateTimeOriginal.Value.ToString("yyyy:MM:dd HH:mm:ss") + "\0";
                result.AddRange(Encoding.ASCII.GetBytes(dateStr));
            }
            if (_dateTimeDigitized.HasValue)
            {
                string dateStr = _dateTimeDigitized.Value.ToString("yyyy:MM:dd HH:mm:ss") + "\0";
                result.AddRange(Encoding.ASCII.GetBytes(dateStr));
            }
        }
        
        // Write GPS IFD if needed
        if (NeedsGpsIfd())
        {
            double lat = _gps!.Value.Lat;
            double lon = _gps!.Value.Lon;
            
            // GPS IFD entry count (4 entries: LatRef, Lat, LonRef, Lon)
            WriteUInt16(result, 4);
            
            // GPSLatitudeRef (tag 0x0001) - ASCII, count 2 ("N\0" or "S\0")
            string latRef = lat >= 0 ? "N" : "S";
            WriteIfdEntryInlineAscii(result, TagGpsLatitudeRef, latRef);
            
            // GPSLatitude (tag 0x0002) - 3 RATIONAL values
            WriteIfdEntry(result, TagGpsLatitude, TypeRational, 3, gpsLatValueOffset);
            
            // GPSLongitudeRef (tag 0x0003) - ASCII, count 2 ("E\0" or "W\0")
            string lonRef = lon >= 0 ? "E" : "W";
            WriteIfdEntryInlineAscii(result, TagGpsLongitudeRef, lonRef);
            
            // GPSLongitude (tag 0x0004) - 3 RATIONAL values
            WriteIfdEntry(result, TagGpsLongitude, TypeRational, 3, gpsLonValueOffset);
            
            // Next IFD offset (0 = no more)
            WriteUInt32(result, 0);
            
            // Write GPS coordinate rational values
            // Latitude: 3 rationals (degrees, minutes, seconds)
            var (latDeg, latMin, latSec) = DecimalToDms(Math.Abs(lat));
            WriteRational(result, latDeg, 1);
            WriteRational(result, latMin, 1);
            // Use higher precision for seconds (multiply by 1000000 for 6 decimal places)
            WriteRational(result, (int)Math.Round(latSec * 1000000), 1000000);
            
            // Longitude: 3 rationals (degrees, minutes, seconds)
            var (lonDeg, lonMin, lonSec) = DecimalToDms(Math.Abs(lon));
            WriteRational(result, lonDeg, 1);
            WriteRational(result, lonMin, 1);
            WriteRational(result, (int)Math.Round(lonSec * 1000000), 1000000);
        }
        
        return result.ToArray();
    }
    
    #endregion
    
    #region IFD Entry Helpers
    
    /// <summary>
    /// Represents an entry in IFD0 with its type and value.
    /// </summary>
    private class Ifd0Entry
    {
        public ushort Tag { get; set; }
        public Ifd0EntryType Type { get; set; }
        public int Value { get; set; }
        public string? StringValue { get; set; }
        public int DataOffset { get; set; } // For strings that don't fit inline
    }
    
    /// <summary>
    /// Types of IFD0 entries we support.
    /// </summary>
    private enum Ifd0EntryType
    {
        Short,
        Long,
        Ascii,
        SubIfdPointer
    }
    
    /// <summary>
    /// Writes an IFD0 entry based on its type.
    /// </summary>
    private void WriteIfd0Entry(List<byte> data, Ifd0Entry entry)
    {
        switch (entry.Type)
        {
            case Ifd0EntryType.Short:
                WriteIfdEntryInlineShort(data, entry.Tag, (ushort)entry.Value);
                break;
            case Ifd0EntryType.Long:
                WriteIfdEntry(data, entry.Tag, TypeLong, 1, entry.Value);
                break;
            case Ifd0EntryType.Ascii:
                int stringLen = (entry.StringValue?.Length ?? 0) + 1;
                if (stringLen <= 4)
                {
                    // Inline - value fits in 4-byte field
                    WriteIfdEntryInlineAscii(data, entry.Tag, entry.StringValue ?? "");
                }
                else
                {
                    // External - value stored at offset
                    WriteIfdEntry(data, entry.Tag, TypeAscii, stringLen, entry.DataOffset);
                }
                break;
            case Ifd0EntryType.SubIfdPointer:
                WriteIfdEntry(data, entry.Tag, TypeLong, 1, entry.Value);
                break;
        }
    }
    
    /// <summary>
    /// Writes a 12-byte IFD entry with value stored at an offset.
    /// </summary>
    private static void WriteIfdEntry(List<byte> data, ushort tag, ushort type, int count, int valueOrOffset)
    {
        // Tag (2 bytes)
        WriteUInt16(data, tag);
        
        // Type (2 bytes)
        WriteUInt16(data, type);
        
        // Count (4 bytes)
        WriteUInt32(data, (uint)count);
        
        // Value or offset (4 bytes)
        WriteUInt32(data, (uint)valueOrOffset);
    }
    
    /// <summary>
    /// Writes an IFD entry with an inline ASCII value (for short strings like "N", "S", "E", "W").
    /// The ASCII string is null-terminated and stored directly in the 4-byte value field.
    /// </summary>
    private static void WriteIfdEntryInlineAscii(List<byte> data, ushort tag, string value)
    {
        // Tag (2 bytes)
        WriteUInt16(data, tag);
        
        // Type: ASCII (2 bytes)
        WriteUInt16(data, TypeAscii);
        
        // Count: string length + null terminator (4 bytes)
        WriteUInt32(data, (uint)(value.Length + 1));
        
        // Value: the ASCII string with null terminator, padded to 4 bytes
        var bytes = new byte[4];
        for (int i = 0; i < value.Length && i < 4; i++)
        {
            bytes[i] = (byte)value[i];
        }
        // Null terminator (if room)
        if (value.Length < 4)
        {
            bytes[value.Length] = 0;
        }
        // Remaining bytes are already 0 (padding)
        data.AddRange(bytes);
    }
    
    /// <summary>
    /// Writes an IFD entry with an inline SHORT value.
    /// </summary>
    private static void WriteIfdEntryInlineShort(List<byte> data, ushort tag, ushort value)
    {
        // Tag (2 bytes)
        WriteUInt16(data, tag);
        
        // Type: SHORT (2 bytes)
        WriteUInt16(data, TypeShort);
        
        // Count (4 bytes) - 1 value
        WriteUInt32(data, 1);
        
        // Value: SHORT value (2 bytes) + 2 bytes padding
        WriteUInt16(data, value);
        WriteUInt16(data, 0); // Padding
    }
    
    #endregion
    
    #region Low-Level Write Helpers
    
    /// <summary>
    /// Writes a 16-bit unsigned integer in little-endian format.
    /// </summary>
    private static void WriteUInt16(List<byte> data, ushort value)
    {
        data.Add((byte)(value & 0xFF));
        data.Add((byte)((value >> 8) & 0xFF));
    }
    
    /// <summary>
    /// Writes a 32-bit unsigned integer in little-endian format.
    /// </summary>
    private static void WriteUInt32(List<byte> data, uint value)
    {
        data.Add((byte)(value & 0xFF));
        data.Add((byte)((value >> 8) & 0xFF));
        data.Add((byte)((value >> 16) & 0xFF));
        data.Add((byte)((value >> 24) & 0xFF));
    }
    
    /// <summary>
    /// Writes a rational value (numerator/denominator) in little-endian format.
    /// </summary>
    private static void WriteRational(List<byte> data, int numerator, int denominator)
    {
        // Numerator (4 bytes, unsigned)
        WriteUInt32(data, (uint)numerator);
        
        // Denominator (4 bytes, unsigned)
        WriteUInt32(data, (uint)denominator);
    }
    
    #endregion
    
    #region Coordinate Conversion
    
    /// <summary>
    /// Converts decimal degrees to degrees, minutes, seconds format.
    /// </summary>
    private static (int Degrees, int Minutes, double Seconds) DecimalToDms(double decimalDegrees)
    {
        int degrees = (int)decimalDegrees;
        double minutesDecimal = (decimalDegrees - degrees) * 60;
        int minutes = (int)minutesDecimal;
        double seconds = (minutesDecimal - minutes) * 60;
        
        return (degrees, minutes, seconds);
    }
    
    #endregion
    
    #region CRC-32 Calculation (for PNG)
    
    /// <summary>
    /// Calculates CRC-32 checksum for PNG chunks.
    /// Uses the standard PNG/zlib polynomial (0xEDB88320).
    /// </summary>
    private static uint CalculateCrc32(byte[] data)
    {
        uint[] crcTable = GenerateCrc32Table();
        uint crc = 0xFFFFFFFF;
        
        foreach (byte b in data)
        {
            crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        
        return crc ^ 0xFFFFFFFF;
    }
    
    /// <summary>
    /// Generates the CRC-32 lookup table.
    /// </summary>
    private static uint[] GenerateCrc32Table()
    {
        uint[] table = new uint[256];
        const uint polynomial = 0xEDB88320;
        
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 1) == 1)
                    crc = (crc >> 1) ^ polynomial;
                else
                    crc >>= 1;
            }
            table[i] = crc;
        }
        
        return table;
    }
    
    #endregion
}
