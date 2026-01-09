using System;
using System.Collections.Generic;
using System.Text;

namespace PhotoCopy.Tests.TestingImplementation;

/// <summary>
/// Generates minimal valid JPEG and PNG images with EXIF metadata for testing.
/// The generated images are parseable by MetadataExtractor library.
/// </summary>
public static class MockImageGenerator
{
    // TIFF header constants
    private const ushort TiffLittleEndian = 0x4949; // "II" - Intel byte order
    private const ushort TiffMagic = 0x002A;
    
    // IFD tag types
    private const ushort TypeRational = 5;     // Two LONGs: numerator and denominator
    private const ushort TypeAscii = 2;        // ASCII string
    private const ushort TypeLong = 4;         // 4-byte unsigned integer
    private const ushort TypeByte = 1;         // 1-byte unsigned integer
    
    // EXIF tags
    private const ushort TagExifOffset = 0x8769;           // Pointer to EXIF Sub-IFD
    private const ushort TagGpsOffset = 0x8825;            // Pointer to GPS IFD
    private const ushort TagDateTimeOriginal = 0x9003;     // DateTimeOriginal in EXIF Sub-IFD
    
    // GPS tags
    private const ushort TagGpsLatitudeRef = 0x0001;       // 'N' or 'S'
    private const ushort TagGpsLatitude = 0x0002;          // Latitude as 3 rationals
    private const ushort TagGpsLongitudeRef = 0x0003;      // 'E' or 'W'
    private const ushort TagGpsLongitude = 0x0004;         // Longitude as 3 rationals

    /// <summary>
    /// Creates a minimal valid JPEG image with optional EXIF metadata.
    /// </summary>
    /// <param name="dateTaken">Optional date when the photo was taken (stored as DateTimeOriginal).</param>
    /// <param name="gps">Optional GPS coordinates (Latitude, Longitude).</param>
    /// <returns>A byte array containing a valid JPEG file.</returns>
    public static byte[] CreateJpeg(DateTime? dateTaken = null, (double Lat, double Lon)? gps = null)
    {
        var result = new List<byte>();
        
        // SOI (Start of Image)
        result.Add(0xFF);
        result.Add(0xD8);
        
        // APP1 segment with EXIF data (if we have metadata)
        if (dateTaken.HasValue || gps.HasValue)
        {
            var exifData = CreateExifData(dateTaken, gps);
            
            // APP1 marker
            result.Add(0xFF);
            result.Add(0xE1);
            
            // Length (includes length field itself but not marker)
            ushort length = (ushort)(2 + 6 + exifData.Length); // 2 for length, 6 for "Exif\0\0"
            result.Add((byte)(length >> 8));   // Big endian
            result.Add((byte)(length & 0xFF));
            
            // EXIF header: "Exif\0\0"
            result.AddRange(Encoding.ASCII.GetBytes("Exif"));
            result.Add(0x00);
            result.Add(0x00);
            
            // TIFF data
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
        result.Add(0xFB); // Encoded pixel data
        result.Add(0xD3);
        result.Add(0x28);
        result.Add(0xA0);
        
        // EOI (End of Image)
        result.Add(0xFF);
        result.Add(0xD9);
        
        return result.ToArray();
    }

    /// <summary>
    /// Creates a minimal valid PNG image with optional EXIF metadata.
    /// </summary>
    /// <param name="dateTaken">Optional date when the photo was taken (stored as DateTimeOriginal).</param>
    /// <param name="gps">Optional GPS coordinates (Latitude, Longitude).</param>
    /// <returns>A byte array containing a valid PNG file.</returns>
    public static byte[] CreatePng(DateTime? dateTaken = null, (double Lat, double Lon)? gps = null)
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
        if (dateTaken.HasValue || gps.HasValue)
        {
            var exifData = CreateExifData(dateTaken, gps);
            result.AddRange(CreatePngChunk("eXIf", exifData));
        }
        
        // IDAT chunk (image data) - minimal deflate-compressed 1x1 grayscale image
        // This is a valid deflate stream for a 1x1 grayscale image (filter byte 0x00 + gray value 0x80)
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
    /// Creates a PNG chunk with proper CRC.
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

    /// <summary>
    /// Creates EXIF/TIFF data structure containing the specified metadata.
    /// This follows the TIFF/EXIF spec with proper IFD structure for MetadataExtractor compatibility.
    /// </summary>
    private static byte[] CreateExifData(DateTime? dateTaken, (double Lat, double Lon)? gps)
    {
        // We'll build the TIFF data in sections and calculate offsets properly
        // Structure:
        // - TIFF Header (8 bytes) starting at offset 0
        // - IFD0 at offset 8
        // - EXIF Sub-IFD (if dateTaken) follows IFD0
        // - GPS IFD (if gps) follows EXIF Sub-IFD
        // - Data values follow the IFDs
        
        var result = new List<byte>();
        
        // TIFF header (8 bytes)
        // Byte order: Little endian ("II")
        result.Add(0x49); // 'I'
        result.Add(0x49); // 'I'
        // TIFF magic number (42)
        result.Add(0x2A);
        result.Add(0x00);
        // Offset to first IFD (8 = immediately after header)
        result.Add(0x08);
        result.Add(0x00);
        result.Add(0x00);
        result.Add(0x00);
        
        // Now at offset 8: IFD0
        
        // Calculate IFD0 structure
        int ifd0EntryCount = 0;
        if (dateTaken.HasValue) ifd0EntryCount++; // ExifOffset pointer
        if (gps.HasValue) ifd0EntryCount++;       // GPSOffset pointer
        
        // IFD0 size: 2 (count) + entries*12 + 4 (next IFD pointer)
        int ifd0Size = 2 + (ifd0EntryCount * 12) + 4;
        int ifd0Offset = 8;
        
        // Calculate where each sub-IFD and data will be located
        int currentOffset = ifd0Offset + ifd0Size;
        
        int exifSubIfdOffset = 0;
        int exifSubIfdSize = 0;
        int dateStringOffset = 0;
        
        int gpsIfdOffset = 0;
        int gpsIfdSize = 0;
        int gpsLatValueOffset = 0;
        int gpsLonValueOffset = 0;
        
        if (dateTaken.HasValue)
        {
            exifSubIfdOffset = currentOffset;
            // EXIF Sub-IFD: 2 (count) + 1 entry * 12 + 4 (next pointer) = 18 bytes
            exifSubIfdSize = 2 + 12 + 4;
            // Date string comes right after the EXIF Sub-IFD
            dateStringOffset = exifSubIfdOffset + exifSubIfdSize;
            // "YYYY:MM:DD HH:MM:SS\0" = 20 bytes
            currentOffset = dateStringOffset + 20;
        }
        
        if (gps.HasValue)
        {
            gpsIfdOffset = currentOffset;
            // GPS IFD: 2 (count) + 4 entries * 12 + 4 (next pointer) = 54 bytes
            gpsIfdSize = 2 + (4 * 12) + 4;
            // GPS rational values come right after GPS IFD
            // Latitude: 3 rationals = 24 bytes
            // Longitude: 3 rationals = 24 bytes
            gpsLatValueOffset = gpsIfdOffset + gpsIfdSize;
            gpsLonValueOffset = gpsLatValueOffset + 24;
            currentOffset = gpsLonValueOffset + 24;
        }
        
        // Write IFD0 entry count
        WriteUInt16(result, (ushort)ifd0EntryCount);
        
        // IFD0 entries (must be sorted by tag number)
        // Tag 0x8769 (ExifOffset) comes before 0x8825 (GPSOffset)
        if (dateTaken.HasValue)
        {
            // ExifOffset tag (0x8769) - points to EXIF Sub-IFD
            WriteIfdEntry(result, TagExifOffset, TypeLong, 1, exifSubIfdOffset);
        }
        
        if (gps.HasValue)
        {
            // GPSInfo tag (0x8825) - points to GPS IFD
            WriteIfdEntry(result, TagGpsOffset, TypeLong, 1, gpsIfdOffset);
        }
        
        // Next IFD offset (0 = no more IFDs)
        WriteUInt32(result, 0);
        
        // Write EXIF Sub-IFD if needed
        if (dateTaken.HasValue)
        {
            // EXIF Sub-IFD entry count (1 entry)
            WriteUInt16(result, 1);
            
            // DateTimeOriginal entry (format: "YYYY:MM:DD HH:MM:SS\0" = 20 bytes)
            // Since 20 > 4, the value is stored at an offset
            WriteIfdEntry(result, TagDateTimeOriginal, TypeAscii, 20, dateStringOffset);
            
            // Next IFD offset (0 = no more)
            WriteUInt32(result, 0);
            
            // Date string value (20 bytes)
            string dateStr = dateTaken.Value.ToString("yyyy:MM:dd HH:mm:ss") + "\0";
            result.AddRange(Encoding.ASCII.GetBytes(dateStr));
        }
        
        // Write GPS IFD if needed
        if (gps.HasValue)
        {
            double lat = gps.Value.Lat;
            double lon = gps.Value.Lon;
            
            // GPS IFD entry count (4 entries: LatRef, Lat, LonRef, Lon)
            WriteUInt16(result, 4);
            
            // GPSLatitudeRef (tag 0x0001) - ASCII type, count 2
            // "N\0" or "S\0" - 2 bytes fits in the 4-byte value field (stored inline)
            string latRef = lat >= 0 ? "N" : "S";
            WriteIfdEntryInlineAscii(result, TagGpsLatitudeRef, latRef);
            
            // GPSLatitude (tag 0x0002) - 3 RATIONAL values (24 bytes, must be at offset)
            WriteIfdEntry(result, TagGpsLatitude, TypeRational, 3, gpsLatValueOffset);
            
            // GPSLongitudeRef (tag 0x0003) - ASCII type, count 2
            // "E\0" or "W\0" - 2 bytes fits in the 4-byte value field (stored inline)
            string lonRef = lon >= 0 ? "E" : "W";
            WriteIfdEntryInlineAscii(result, TagGpsLongitudeRef, lonRef);
            
            // GPSLongitude (tag 0x0004) - 3 RATIONAL values (24 bytes, must be at offset)
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
        // Null terminator
        if (value.Length < 4)
        {
            bytes[value.Length] = 0;
        }
        // Remaining bytes are already 0 (padding)
        data.AddRange(bytes);
    }
    
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
    /// Writes a 12-byte IFD entry in little-endian format.
    /// </summary>
    private static void WriteIfdEntry(List<byte> data, ushort tag, ushort type, int count, int valueOrOffset)
    {
        // Tag (2 bytes)
        data.Add((byte)(tag & 0xFF));
        data.Add((byte)((tag >> 8) & 0xFF));
        
        // Type (2 bytes)
        data.Add((byte)(type & 0xFF));
        data.Add((byte)((type >> 8) & 0xFF));
        
        // Count (4 bytes)
        data.Add((byte)(count & 0xFF));
        data.Add((byte)((count >> 8) & 0xFF));
        data.Add((byte)((count >> 16) & 0xFF));
        data.Add((byte)((count >> 24) & 0xFF));
        
        // Value or offset (4 bytes)
        data.Add((byte)(valueOrOffset & 0xFF));
        data.Add((byte)((valueOrOffset >> 8) & 0xFF));
        data.Add((byte)((valueOrOffset >> 16) & 0xFF));
        data.Add((byte)((valueOrOffset >> 24) & 0xFF));
    }

    /// <summary>
    /// Writes a rational value (numerator/denominator) in little-endian format.
    /// </summary>
    private static void WriteRational(List<byte> data, int numerator, int denominator)
    {
        // Numerator (4 bytes, unsigned)
        data.Add((byte)(numerator & 0xFF));
        data.Add((byte)((numerator >> 8) & 0xFF));
        data.Add((byte)((numerator >> 16) & 0xFF));
        data.Add((byte)((numerator >> 24) & 0xFF));
        
        // Denominator (4 bytes, unsigned)
        data.Add((byte)(denominator & 0xFF));
        data.Add((byte)((denominator >> 8) & 0xFF));
        data.Add((byte)((denominator >> 16) & 0xFF));
        data.Add((byte)((denominator >> 24) & 0xFF));
    }

    /// <summary>
    /// Converts decimal degrees to degrees, minutes, seconds.
    /// </summary>
    private static (int Degrees, int Minutes, double Seconds) DecimalToDms(double decimalDegrees)
    {
        int degrees = (int)decimalDegrees;
        double minutesDecimal = (decimalDegrees - degrees) * 60;
        int minutes = (int)minutesDecimal;
        double seconds = (minutesDecimal - minutes) * 60;
        
        return (degrees, minutes, seconds);
    }

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
}
