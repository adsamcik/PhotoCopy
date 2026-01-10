using System;

namespace PhotoCopy.Tests.TestingImplementation;

/// <summary>
/// Provides pre-defined test image configurations for comprehensive EXIF testing.
/// All JPEG and PNG images are generated using <see cref="MockImageGenerator"/> to ensure
/// they are valid, parseable files with the appropriate metadata.
/// </summary>
public static class TestSampleImages
{
    #region JPEG with Full EXIF Data

    /// <summary>
    /// JPEG with full EXIF metadata: DateTimeOriginal (2023-06-15 14:30:00) and GPS coordinates (Paris: 48.8566, 2.3522).
    /// This represents a typical photo taken with a modern smartphone or camera.
    /// </summary>
    public static byte[] JpegWithFullExif => MockImageGenerator.CreateJpeg(
        dateTaken: new DateTime(2023, 6, 15, 14, 30, 0),
        gps: (Lat: 48.8566, Lon: 2.3522));

    #endregion

    #region JPEG with Partial EXIF Data

    /// <summary>
    /// JPEG with only DateTimeOriginal (2022-12-25 10:00:00), no GPS coordinates.
    /// Simulates a photo from a camera without GPS capability.
    /// </summary>
    public static byte[] JpegWithDateOnly => MockImageGenerator.CreateJpeg(
        dateTaken: new DateTime(2022, 12, 25, 10, 0, 0),
        gps: null);

    /// <summary>
    /// JPEG with only GPS coordinates (New York: 40.7128, -74.0060), no date metadata.
    /// Simulates a scenario where date EXIF was stripped but GPS remained.
    /// </summary>
    public static byte[] JpegWithGpsOnly => MockImageGenerator.CreateJpeg(
        dateTaken: null,
        gps: (Lat: 40.7128, Lon: -74.0060));

    /// <summary>
    /// Valid minimal JPEG with no EXIF metadata at all.
    /// Tests fallback behavior when no metadata is available.
    /// </summary>
    public static byte[] JpegWithNoExif => MockImageGenerator.CreateJpeg(
        dateTaken: null,
        gps: null);

    #endregion

    #region JPEG with Various GPS Coordinates

    /// <summary>
    /// JPEG with GPS coordinates in the southern hemisphere (Sydney: -33.8688, 151.2093).
    /// Tests proper handling of negative latitude values.
    /// </summary>
    public static byte[] JpegSouthernHemisphere => MockImageGenerator.CreateJpeg(
        dateTaken: new DateTime(2023, 1, 15, 9, 0, 0),
        gps: (Lat: -33.8688, Lon: 151.2093));

    /// <summary>
    /// JPEG with GPS coordinates in the western hemisphere (Los Angeles: 34.0522, -118.2437).
    /// Tests proper handling of negative longitude values.
    /// </summary>
    public static byte[] JpegWesternHemisphere => MockImageGenerator.CreateJpeg(
        dateTaken: new DateTime(2023, 7, 4, 12, 0, 0),
        gps: (Lat: 34.0522, Lon: -118.2437));

    /// <summary>
    /// JPEG with GPS at coordinates (0, 0) - "Null Island" in the Gulf of Guinea.
    /// Tests edge case where both latitude and longitude are exactly zero.
    /// </summary>
    public static byte[] JpegZeroGps => MockImageGenerator.CreateJpeg(
        dateTaken: new DateTime(2023, 3, 20, 15, 45, 0),
        gps: (Lat: 0.0, Lon: 0.0));

    /// <summary>
    /// JPEG with GPS at the North Pole (90, 0).
    /// Tests extreme latitude value at the geographic pole.
    /// </summary>
    public static byte[] JpegNorthPole => MockImageGenerator.CreateJpeg(
        dateTaken: new DateTime(2023, 12, 21, 0, 0, 0),
        gps: (Lat: 90.0, Lon: 0.0));

    #endregion

    #region JPEG with Edge Case Dates

    /// <summary>
    /// JPEG with date at the Y2K boundary (2000-01-01 00:00:00).
    /// Tests handling of the millennium transition date.
    /// </summary>
    public static byte[] JpegDateEdgeY2K => MockImageGenerator.CreateJpeg(
        dateTaken: new DateTime(2000, 1, 1, 0, 0, 0),
        gps: null);

    /// <summary>
    /// JPEG with date on a leap year day (2024-02-29 12:00:00).
    /// Tests proper handling of February 29th in a leap year.
    /// </summary>
    public static byte[] JpegDateEdgeLeapYear => MockImageGenerator.CreateJpeg(
        dateTaken: new DateTime(2024, 2, 29, 12, 0, 0),
        gps: null);

    /// <summary>
    /// JPEG with a very old date (1990-05-15 08:30:00).
    /// Tests handling of dates from early digital photography era.
    /// </summary>
    public static byte[] JpegVeryOldDate => MockImageGenerator.CreateJpeg(
        dateTaken: new DateTime(1990, 5, 15, 8, 30, 0),
        gps: null);

    #endregion

    #region PNG Images

    /// <summary>
    /// PNG with full EXIF metadata: DateTimeOriginal (2023-08-20 16:45:00) and GPS coordinates (Tokyo: 35.6762, 139.6503).
    /// Tests EXIF extraction from PNG format.
    /// </summary>
    public static byte[] PngWithExif => MockImageGenerator.CreatePng(
        dateTaken: new DateTime(2023, 8, 20, 16, 45, 0),
        gps: (Lat: 35.6762, Lon: 139.6503));

    /// <summary>
    /// PNG with only DateTimeOriginal (2023-04-10 11:20:00), no GPS coordinates.
    /// Tests PNG date extraction without location data.
    /// </summary>
    public static byte[] PngWithDateOnly => MockImageGenerator.CreatePng(
        dateTaken: new DateTime(2023, 4, 10, 11, 20, 0),
        gps: null);

    /// <summary>
    /// Valid minimal PNG with no EXIF metadata at all.
    /// Tests fallback behavior for PNG files without metadata.
    /// </summary>
    public static byte[] PngWithNoExif => MockImageGenerator.CreatePng(
        dateTaken: null,
        gps: null);

    #endregion

    #region Invalid/Corrupt Images

    /// <summary>
    /// Invalid/corrupted JPEG bytes.
    /// Contains a valid JPEG SOI marker followed by garbage data.
    /// Tests error handling for corrupted image files.
    /// </summary>
    public static byte[] CorruptJpeg => new byte[]
    {
        0xFF, 0xD8,             // SOI marker (valid JPEG start)
        0xFF, 0xE0,             // APP0 marker
        0x00, 0x10,             // Invalid/truncated length
        0x4A, 0x46, 0x49, 0x46, // "JFIF"
        0x00,                   // Null terminator
        // Truncated/missing data - file ends abruptly
        0xDE, 0xAD, 0xBE, 0xEF, // Garbage bytes
        0xCA, 0xFE, 0xBA, 0xBE
    };

    /// <summary>
    /// Empty byte array (0 bytes).
    /// Tests handling of completely empty files.
    /// </summary>
    public static byte[] EmptyJpeg => Array.Empty<byte>();

    /// <summary>
    /// Random binary data that is not a valid image of any format.
    /// Tests handling of non-image binary files.
    /// </summary>
    public static byte[] RandomBinaryJpeg => new byte[]
    {
        0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0,
        0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88,
        0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00, 0x11,
        0x99, 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22,
        0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10,
        0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF
    };

    #endregion

    #region Helper Methods

    /// <summary>
    /// Gets the expected DateTime for JpegWithFullExif.
    /// </summary>
    public static DateTime JpegWithFullExifDate => new DateTime(2023, 6, 15, 14, 30, 0);

    /// <summary>
    /// Gets the expected GPS coordinates for JpegWithFullExif (Paris).
    /// </summary>
    public static (double Lat, double Lon) JpegWithFullExifGps => (48.8566, 2.3522);

    /// <summary>
    /// Gets the expected DateTime for JpegWithDateOnly.
    /// </summary>
    public static DateTime JpegWithDateOnlyDate => new DateTime(2022, 12, 25, 10, 0, 0);

    /// <summary>
    /// Gets the expected GPS coordinates for JpegWithGpsOnly (New York).
    /// </summary>
    public static (double Lat, double Lon) JpegWithGpsOnlyGps => (40.7128, -74.0060);

    /// <summary>
    /// Gets the expected GPS coordinates for JpegSouthernHemisphere (Sydney).
    /// </summary>
    public static (double Lat, double Lon) JpegSouthernHemisphereGps => (-33.8688, 151.2093);

    /// <summary>
    /// Gets the expected GPS coordinates for JpegWesternHemisphere (Los Angeles).
    /// </summary>
    public static (double Lat, double Lon) JpegWesternHemisphereGps => (34.0522, -118.2437);

    /// <summary>
    /// Gets the expected GPS coordinates for JpegZeroGps (Null Island).
    /// </summary>
    public static (double Lat, double Lon) JpegZeroGpsCoordinates => (0.0, 0.0);

    /// <summary>
    /// Gets the expected GPS coordinates for JpegNorthPole.
    /// </summary>
    public static (double Lat, double Lon) JpegNorthPoleGps => (90.0, 0.0);

    /// <summary>
    /// Gets the expected DateTime for JpegDateEdgeY2K.
    /// </summary>
    public static DateTime JpegDateEdgeY2KDate => new DateTime(2000, 1, 1, 0, 0, 0);

    /// <summary>
    /// Gets the expected DateTime for JpegDateEdgeLeapYear.
    /// </summary>
    public static DateTime JpegDateEdgeLeapYearDate => new DateTime(2024, 2, 29, 12, 0, 0);

    /// <summary>
    /// Gets the expected DateTime for JpegVeryOldDate.
    /// </summary>
    public static DateTime JpegVeryOldDateValue => new DateTime(1990, 5, 15, 8, 30, 0);

    /// <summary>
    /// Gets the expected DateTime for PngWithExif.
    /// </summary>
    public static DateTime PngWithExifDate => new DateTime(2023, 8, 20, 16, 45, 0);

    /// <summary>
    /// Gets the expected GPS coordinates for PngWithExif (Tokyo).
    /// </summary>
    public static (double Lat, double Lon) PngWithExifGps => (35.6762, 139.6503);

    /// <summary>
    /// Gets the expected DateTime for PngWithDateOnly.
    /// </summary>
    public static DateTime PngWithDateOnlyDate => new DateTime(2023, 4, 10, 11, 20, 0);

    #endregion
}
