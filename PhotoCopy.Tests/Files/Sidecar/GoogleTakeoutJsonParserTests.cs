using System;
using System.IO;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PhotoCopy.Files.Sidecar;

namespace PhotoCopy.Tests.Files.Sidecar;

public class GoogleTakeoutJsonParserTests
{
    private readonly ILogger<GoogleTakeoutJsonParser> _logger;
    private readonly GoogleTakeoutJsonParser _parser;
    private readonly string _tempDir;

    public GoogleTakeoutJsonParserTests()
    {
        _logger = Substitute.For<ILogger<GoogleTakeoutJsonParser>>();
        _parser = new GoogleTakeoutJsonParser(_logger);
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    [After(Test)]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    #region CanParse Tests

    [Test]
    public async Task CanParse_WithJsonExtension_ReturnsTrue()
    {
        // Act
        var result = _parser.CanParse(".json");

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task CanParse_WithUppercaseJsonExtension_ReturnsTrue()
    {
        // Act
        var result = _parser.CanParse(".JSON");

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task CanParse_WithMixedCaseJsonExtension_ReturnsTrue()
    {
        // Act
        var result = _parser.CanParse(".Json");

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task CanParse_WithXmpExtension_ReturnsFalse()
    {
        // Act
        var result = _parser.CanParse(".xmp");

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CanParse_WithEmptyExtension_ReturnsFalse()
    {
        // Act
        var result = _parser.CanParse("");

        // Assert
        await Assert.That(result).IsFalse();
    }

    #endregion

    #region Parse Valid JSON Tests

    [Test]
    public async Task Parse_ValidJsonWithAllFields_ReturnsCompleteMetadata()
    {
        // Arrange
        var json = """
        {
            "title": "photo.jpg",
            "description": "A beautiful sunset",
            "imageViews": "0",
            "creationTime": {
                "timestamp": "1609459200",
                "formatted": "Jan 1, 2021, 12:00:00 AM UTC"
            },
            "photoTakenTime": {
                "timestamp": "1609455600",
                "formatted": "Dec 31, 2020, 11:00:00 PM UTC"
            },
            "geoData": {
                "latitude": 40.7128,
                "longitude": -74.006,
                "altitude": 10.0,
                "latitudeSpan": 0.0,
                "longitudeSpan": 0.0
            },
            "geoDataExif": {
                "latitude": 40.7128,
                "longitude": -74.006,
                "altitude": 10.0,
                "latitudeSpan": 0.0,
                "longitudeSpan": 0.0
            }
        }
        """;
        var jsonPath = CreateTempJsonFile(json);

        // Act
        var result = _parser.Parse(jsonPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.DateTaken).IsNotNull();
        await Assert.That(result.DateTaken!.Value).IsEqualTo(new DateTime(2020, 12, 31, 23, 0, 0, DateTimeKind.Utc));
        await Assert.That(result.Latitude).IsEqualTo(40.7128);
        await Assert.That(result.Longitude).IsEqualTo(-74.006);
        await Assert.That(result.Altitude).IsEqualTo(10.0);
        await Assert.That(result.Title).IsEqualTo("photo.jpg");
        await Assert.That(result.Description).IsEqualTo("A beautiful sunset");
        await Assert.That(result.HasGpsData).IsTrue();
        await Assert.That(result.HasDateTaken).IsTrue();
    }

    [Test]
    public async Task Parse_ValidJsonWithOnlyTimestamp_ReturnsMetadataWithDateOnly()
    {
        // Arrange
        var json = """
        {
            "title": "photo.jpg",
            "photoTakenTime": {
                "timestamp": "1609455600"
            }
        }
        """;
        var jsonPath = CreateTempJsonFile(json);

        // Act
        var result = _parser.Parse(jsonPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.DateTaken).IsNotNull();
        await Assert.That(result.DateTaken!.Value).IsEqualTo(new DateTime(2020, 12, 31, 23, 0, 0, DateTimeKind.Utc));
        await Assert.That(result.HasGpsData).IsFalse();
        await Assert.That(result.Latitude).IsNull();
        await Assert.That(result.Longitude).IsNull();
    }

    [Test]
    public async Task Parse_ValidJsonWithOnlyGps_ReturnsMetadataWithGpsOnly()
    {
        // Arrange
        var json = """
        {
            "title": "photo.jpg",
            "geoData": {
                "latitude": 51.5074,
                "longitude": -0.1278,
                "altitude": 15.0
            }
        }
        """;
        var jsonPath = CreateTempJsonFile(json);

        // Act
        var result = _parser.Parse(jsonPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.HasGpsData).IsTrue();
        await Assert.That(result.Latitude).IsEqualTo(51.5074);
        await Assert.That(result.Longitude).IsEqualTo(-0.1278);
        await Assert.That(result.Altitude).IsEqualTo(15.0);
        await Assert.That(result.HasDateTaken).IsFalse();
        await Assert.That(result.DateTaken).IsNull();
    }

    [Test]
    public async Task Parse_ValidJsonWithGeoDataExifFallback_ReturnsMetadataFromExif()
    {
        // Arrange
        var json = """
        {
            "title": "photo.jpg",
            "geoDataExif": {
                "latitude": 35.6762,
                "longitude": 139.6503,
                "altitude": 40.0
            }
        }
        """;
        var jsonPath = CreateTempJsonFile(json);

        // Act
        var result = _parser.Parse(jsonPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.HasGpsData).IsTrue();
        await Assert.That(result.Latitude).IsEqualTo(35.6762);
        await Assert.That(result.Longitude).IsEqualTo(139.6503);
    }

    [Test]
    public async Task Parse_ValidJsonWithGeoDataPreferredOverGeoDataExif_ReturnsGeoData()
    {
        // Arrange - geoData has NYC coordinates, geoDataExif has London coordinates
        var json = """
        {
            "title": "photo.jpg",
            "geoData": {
                "latitude": 40.7128,
                "longitude": -74.006,
                "altitude": 10.0
            },
            "geoDataExif": {
                "latitude": 51.5074,
                "longitude": -0.1278,
                "altitude": 11.0
            }
        }
        """;
        var jsonPath = CreateTempJsonFile(json);

        // Act
        var result = _parser.Parse(jsonPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Latitude).IsEqualTo(40.7128); // Should use geoData (NYC)
        await Assert.That(result.Longitude).IsEqualTo(-74.006);
    }

    [Test]
    public async Task Parse_ValidJsonWithNumericTimestamp_ReturnsCorrectDate()
    {
        // Arrange - timestamp as number instead of string
        var json = """
        {
            "title": "photo.jpg",
            "photoTakenTime": {
                "timestamp": 1609455600
            }
        }
        """;
        var jsonPath = CreateTempJsonFile(json);

        // Act
        var result = _parser.Parse(jsonPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.DateTaken).IsNotNull();
        await Assert.That(result.DateTaken!.Value).IsEqualTo(new DateTime(2020, 12, 31, 23, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public async Task Parse_ValidJsonWithCreationTimeFallback_ReturnsCreationTime()
    {
        // Arrange - no photoTakenTime, should fall back to creationTime
        var json = """
        {
            "title": "photo.jpg",
            "creationTime": {
                "timestamp": "1609459200"
            }
        }
        """;
        var jsonPath = CreateTempJsonFile(json);

        // Act
        var result = _parser.Parse(jsonPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.DateTaken).IsNotNull();
        await Assert.That(result.DateTaken!.Value).IsEqualTo(new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    #endregion

    #region Zero GPS Handling Tests

    [Test]
    public async Task Parse_ZeroLatLong_TreatedAsNoGpsData()
    {
        // Arrange
        var json = """
        {
            "title": "photo.jpg",
            "geoData": {
                "latitude": 0.0,
                "longitude": 0.0,
                "altitude": 0.0
            }
        }
        """;
        var jsonPath = CreateTempJsonFile(json);

        // Act
        var result = _parser.Parse(jsonPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.HasGpsData).IsFalse();
        await Assert.That(result.Latitude).IsNull();
        await Assert.That(result.Longitude).IsNull();
    }

    [Test]
    public async Task Parse_VerySmallLatLong_TreatedAsNoGpsData()
    {
        // Arrange - coordinates very close to (0,0)
        var json = """
        {
            "title": "photo.jpg",
            "geoData": {
                "latitude": 0.00001,
                "longitude": -0.00005,
                "altitude": 0.0
            }
        }
        """;
        var jsonPath = CreateTempJsonFile(json);

        // Act
        var result = _parser.Parse(jsonPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.HasGpsData).IsFalse();
    }

    [Test]
    public async Task Parse_ValidSmallButNonZeroCoordinates_ReturnsGpsData()
    {
        // Arrange - coordinates small but clearly not (0,0)
        var json = """
        {
            "title": "photo.jpg",
            "geoData": {
                "latitude": 0.5,
                "longitude": -0.5,
                "altitude": 0.0
            }
        }
        """;
        var jsonPath = CreateTempJsonFile(json);

        // Act
        var result = _parser.Parse(jsonPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.HasGpsData).IsTrue();
        await Assert.That(result.Latitude).IsEqualTo(0.5);
        await Assert.That(result.Longitude).IsEqualTo(-0.5);
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public async Task Parse_MissingFile_ReturnsNull()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDir, "nonexistent.json");

        // Act
        var result = _parser.Parse(nonExistentPath);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Parse_EmptyFile_ReturnsNull()
    {
        // Arrange
        var jsonPath = CreateTempJsonFile("");

        // Act
        var result = _parser.Parse(jsonPath);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Parse_WhitespaceOnlyFile_ReturnsNull()
    {
        // Arrange
        var jsonPath = CreateTempJsonFile("   \n\t  ");

        // Act
        var result = _parser.Parse(jsonPath);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Parse_InvalidJson_ReturnsNull()
    {
        // Arrange
        var jsonPath = CreateTempJsonFile("{ this is not valid json }");

        // Act
        var result = _parser.Parse(jsonPath);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Parse_MalformedJson_ReturnsNull()
    {
        // Arrange
        var jsonPath = CreateTempJsonFile("{ \"title\": \"test\", }"); // trailing comma

        // Act
        var result = _parser.Parse(jsonPath);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Parse_NullPath_ReturnsNull()
    {
        // Act
        var result = _parser.Parse(null!);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Parse_EmptyPath_ReturnsNull()
    {
        // Act
        var result = _parser.Parse("");

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Parse_WhitespacePath_ReturnsNull()
    {
        // Act
        var result = _parser.Parse("   ");

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Parse_JsonWithNoMeaningfulData_ReturnsNull()
    {
        // Arrange - JSON that parses but has no useful metadata
        var json = """
        {
            "imageViews": "0",
            "creationTime": {},
            "geoData": {}
        }
        """;
        var jsonPath = CreateTempJsonFile(json);

        // Act
        var result = _parser.Parse(jsonPath);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task Parse_EmptyDescription_ReturnsNullDescription()
    {
        // Arrange
        var json = """
        {
            "title": "photo.jpg",
            "description": ""
        }
        """;
        var jsonPath = CreateTempJsonFile(json);

        // Act
        var result = _parser.Parse(jsonPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Description).IsNull();
    }

    [Test]
    public async Task Parse_WhitespaceOnlyTitle_ReturnsNullTitle()
    {
        // Arrange
        var json = """
        {
            "title": "   ",
            "photoTakenTime": {
                "timestamp": "1609455600"
            }
        }
        """;
        var jsonPath = CreateTempJsonFile(json);

        // Act
        var result = _parser.Parse(jsonPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Title).IsNull();
    }

    [Test]
    public async Task Parse_StringGpsCoordinates_ParsesCorrectly()
    {
        // Arrange - some exports might have GPS as strings
        var json = """
        {
            "title": "photo.jpg",
            "geoData": {
                "latitude": "40.7128",
                "longitude": "-74.006"
            }
        }
        """;
        var jsonPath = CreateTempJsonFile(json);

        // Act
        var result = _parser.Parse(jsonPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.HasGpsData).IsTrue();
        await Assert.That(result.Latitude).IsEqualTo(40.7128);
        await Assert.That(result.Longitude).IsEqualTo(-74.006);
    }

    #endregion

    #region Helper Methods

    private string CreateTempJsonFile(string content)
    {
        var filePath = Path.Combine(_tempDir, $"{Guid.NewGuid()}.json");
        File.WriteAllText(filePath, content);
        return filePath;
    }

    #endregion
}
