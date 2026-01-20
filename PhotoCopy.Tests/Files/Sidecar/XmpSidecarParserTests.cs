using System;
using System.IO;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PhotoCopy.Files.Sidecar;

namespace PhotoCopy.Tests.Files.Sidecar;

public class XmpSidecarParserTests
{
    private readonly ILogger<XmpSidecarParser> _logger;
    private readonly XmpSidecarParser _parser;
    private readonly string _tempDir;

    public XmpSidecarParserTests()
    {
        _logger = Substitute.For<ILogger<XmpSidecarParser>>();
        _parser = new XmpSidecarParser(_logger);
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
    public async Task CanParse_WithXmpExtension_ReturnsTrue()
    {
        // Act
        var result = _parser.CanParse(".xmp");

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task CanParse_WithUppercaseXmpExtension_ReturnsTrue()
    {
        // Act
        var result = _parser.CanParse(".XMP");

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task CanParse_WithMixedCaseXmpExtension_ReturnsTrue()
    {
        // Act
        var result = _parser.CanParse(".Xmp");

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task CanParse_WithJsonExtension_ReturnsFalse()
    {
        // Act
        var result = _parser.CanParse(".json");

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

    [Test]
    public async Task CanParse_WithXmlExtension_ReturnsFalse()
    {
        // Act
        var result = _parser.CanParse(".xml");

        // Assert
        await Assert.That(result).IsFalse();
    }

    #endregion

    #region Parse Valid XMP Tests

    [Test]
    public async Task Parse_ValidXmpWithAllFields_ReturnsCompleteMetadata()
    {
        // Arrange
        var xmp = """
        <?xml version="1.0" encoding="UTF-8"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
              xmlns:exif="http://ns.adobe.com/exif/1.0/"
              xmlns:xmp="http://ns.adobe.com/xap/1.0/"
              xmlns:dc="http://purl.org/dc/elements/1.1/"
              exif:GPSLatitude="40,42.768N"
              exif:GPSLongitude="74,0.36W"
              exif:GPSAltitude="10/1"
              xmp:CreateDate="2024-06-15T14:30:00">
              <dc:title>
                <rdf:Alt>
                  <rdf:li xml:lang="x-default">My Photo Title</rdf:li>
                </rdf:Alt>
              </dc:title>
              <dc:description>
                <rdf:Alt>
                  <rdf:li xml:lang="x-default">A beautiful sunset photo</rdf:li>
                </rdf:Alt>
              </dc:description>
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;
        var xmpPath = CreateTempXmpFile(xmp);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.DateTaken).IsNotNull();
        await Assert.That(result.DateTaken!.Value).IsEqualTo(new DateTime(2024, 6, 15, 14, 30, 0));
        await Assert.That(result.Latitude).IsNotNull();
        await Assert.That(result.Latitude!.Value).IsEqualTo(40.7128).Within(0.0001);
        await Assert.That(result.Longitude).IsNotNull();
        await Assert.That(result.Longitude!.Value).IsEqualTo(-74.006).Within(0.0001);
        await Assert.That(result.Altitude).IsEqualTo(10.0);
        await Assert.That(result.Title).IsEqualTo("My Photo Title");
        await Assert.That(result.Description).IsEqualTo("A beautiful sunset photo");
        await Assert.That(result.HasGpsData).IsTrue();
        await Assert.That(result.HasDateTaken).IsTrue();
    }

    [Test]
    public async Task Parse_ValidXmpWithOnlyGps_ReturnsMetadataWithGpsOnly()
    {
        // Arrange
        var xmp = """
        <?xml version="1.0" encoding="UTF-8"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
              xmlns:exif="http://ns.adobe.com/exif/1.0/"
              exif:GPSLatitude="51,30.444N"
              exif:GPSLongitude="0,7.668W">
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;
        var xmpPath = CreateTempXmpFile(xmp);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.HasGpsData).IsTrue();
        await Assert.That(result.Latitude!.Value).IsEqualTo(51.5074).Within(0.0001);
        await Assert.That(result.Longitude!.Value).IsEqualTo(-0.1278).Within(0.0001);
        await Assert.That(result.HasDateTaken).IsFalse();
        await Assert.That(result.DateTaken).IsNull();
    }

    [Test]
    public async Task Parse_ValidXmpWithOnlyDate_ReturnsMetadataWithDateOnly()
    {
        // Arrange
        var xmp = """
        <?xml version="1.0" encoding="UTF-8"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
              xmlns:xmp="http://ns.adobe.com/xap/1.0/"
              xmp:CreateDate="2024-06-15T14:30:00">
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;
        var xmpPath = CreateTempXmpFile(xmp);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.HasDateTaken).IsTrue();
        await Assert.That(result.DateTaken!.Value).IsEqualTo(new DateTime(2024, 6, 15, 14, 30, 0));
        await Assert.That(result.HasGpsData).IsFalse();
        await Assert.That(result.Latitude).IsNull();
        await Assert.That(result.Longitude).IsNull();
    }

    [Test]
    public async Task Parse_DmsFormatCoordinates_ParsesCorrectly()
    {
        // Arrange
        var xmp = """
        <?xml version="1.0" encoding="UTF-8"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
              xmlns:exif="http://ns.adobe.com/exif/1.0/"
              exif:GPSLatitude="40,42.768N"
              exif:GPSLongitude="74,0.36W">
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;
        var xmpPath = CreateTempXmpFile(xmp);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Latitude!.Value).IsEqualTo(40.7128).Within(0.0001);
        await Assert.That(result.Longitude!.Value).IsEqualTo(-74.006).Within(0.0001);
    }

    [Test]
    public async Task Parse_DecimalFormatCoordinates_ParsesCorrectly()
    {
        // Arrange
        var xmp = """
        <?xml version="1.0" encoding="UTF-8"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
              xmlns:exif="http://ns.adobe.com/exif/1.0/"
              exif:GPSLatitude="40.7128"
              exif:GPSLongitude="-74.006">
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;
        var xmpPath = CreateTempXmpFile(xmp);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Latitude!.Value).IsEqualTo(40.7128);
        await Assert.That(result.Longitude!.Value).IsEqualTo(-74.006);
    }

    [Test]
    public async Task Parse_DmsWithSecondsFormat_ParsesCorrectly()
    {
        // Arrange - 40 degrees, 42 minutes, 46.08 seconds
        var xmp = """
        <?xml version="1.0" encoding="UTF-8"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
              xmlns:exif="http://ns.adobe.com/exif/1.0/"
              exif:GPSLatitude="40,42,46.08N"
              exif:GPSLongitude="74,0,21.6W">
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;
        var xmpPath = CreateTempXmpFile(xmp);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Latitude!.Value).IsEqualTo(40.7128).Within(0.0001);
        await Assert.That(result.Longitude!.Value).IsEqualTo(-74.006).Within(0.0001);
    }

    [Test]
    public async Task Parse_SouthWestCoordinates_ParsesNegativeValues()
    {
        // Arrange - Sydney, Australia
        var xmp = """
        <?xml version="1.0" encoding="UTF-8"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
              xmlns:exif="http://ns.adobe.com/exif/1.0/"
              exif:GPSLatitude="33,51,32.4S"
              exif:GPSLongitude="151,12,32.4E">
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;
        var xmpPath = CreateTempXmpFile(xmp);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Latitude!.Value).IsLessThan(0); // South is negative
        await Assert.That(result.Longitude!.Value).IsGreaterThan(0); // East is positive
    }

    [Test]
    public async Task Parse_AltitudeFractionFormat_ParsesCorrectly()
    {
        // Arrange
        var xmp = """
        <?xml version="1.0" encoding="UTF-8"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
              xmlns:exif="http://ns.adobe.com/exif/1.0/"
              exif:GPSLatitude="40.7128"
              exif:GPSLongitude="-74.006"
              exif:GPSAltitude="305/10">
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;
        var xmpPath = CreateTempXmpFile(xmp);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Altitude).IsEqualTo(30.5);
    }

    [Test]
    public async Task Parse_AltitudeDecimalFormat_ParsesCorrectly()
    {
        // Arrange
        var xmp = """
        <?xml version="1.0" encoding="UTF-8"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
              xmlns:exif="http://ns.adobe.com/exif/1.0/"
              exif:GPSLatitude="40.7128"
              exif:GPSLongitude="-74.006"
              exif:GPSAltitude="30.5">
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;
        var xmpPath = CreateTempXmpFile(xmp);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Altitude).IsEqualTo(30.5);
    }

    [Test]
    public async Task Parse_PhotoshopDateCreated_ParsesCorrectly()
    {
        // Arrange
        var xmp = """
        <?xml version="1.0" encoding="UTF-8"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
              xmlns:photoshop="http://ns.adobe.com/photoshop/1.0/"
              photoshop:DateCreated="2024-06-15T14:30:00">
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;
        var xmpPath = CreateTempXmpFile(xmp);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.DateTaken!.Value).IsEqualTo(new DateTime(2024, 6, 15, 14, 30, 0));
    }

    [Test]
    public async Task Parse_DateWithTimezone_ParsesCorrectly()
    {
        // Arrange
        var xmp = """
        <?xml version="1.0" encoding="UTF-8"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
              xmlns:xmp="http://ns.adobe.com/xap/1.0/"
              xmp:CreateDate="2024-06-15T14:30:00+00:00">
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;
        var xmpPath = CreateTempXmpFile(xmp);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.DateTaken).IsNotNull();
    }

    [Test]
    public async Task Parse_TitleAndDescriptionNested_ParsesCorrectly()
    {
        // Arrange
        var xmp = """
        <?xml version="1.0" encoding="UTF-8"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
              xmlns:dc="http://purl.org/dc/elements/1.1/">
              <dc:title>
                <rdf:Alt>
                  <rdf:li xml:lang="x-default">Test Title</rdf:li>
                </rdf:Alt>
              </dc:title>
              <dc:description>
                <rdf:Alt>
                  <rdf:li xml:lang="x-default">Test Description</rdf:li>
                </rdf:Alt>
              </dc:description>
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;
        var xmpPath = CreateTempXmpFile(xmp);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Title).IsEqualTo("Test Title");
        await Assert.That(result.Description).IsEqualTo("Test Description");
    }

    [Test]
    public async Task Parse_TitleWithMultipleLanguages_UsesXDefault()
    {
        // Arrange
        var xmp = """
        <?xml version="1.0" encoding="UTF-8"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
              xmlns:dc="http://purl.org/dc/elements/1.1/">
              <dc:title>
                <rdf:Alt>
                  <rdf:li xml:lang="de">German Title</rdf:li>
                  <rdf:li xml:lang="x-default">Default Title</rdf:li>
                  <rdf:li xml:lang="en">English Title</rdf:li>
                </rdf:Alt>
              </dc:title>
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;
        var xmpPath = CreateTempXmpFile(xmp);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Title).IsEqualTo("Default Title");
    }

    [Test]
    public async Task Parse_TitleWithoutXDefault_UsesFirstLanguage()
    {
        // Arrange
        var xmp = """
        <?xml version="1.0" encoding="UTF-8"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
              xmlns:dc="http://purl.org/dc/elements/1.1/">
              <dc:title>
                <rdf:Alt>
                  <rdf:li xml:lang="en">English Title</rdf:li>
                  <rdf:li xml:lang="de">German Title</rdf:li>
                </rdf:Alt>
              </dc:title>
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;
        var xmpPath = CreateTempXmpFile(xmp);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Title).IsEqualTo("English Title");
    }

    [Test]
    public async Task Parse_GpsAsElements_ParsesCorrectly()
    {
        // Arrange - GPS data as child elements instead of attributes
        var xmp = """
        <?xml version="1.0" encoding="UTF-8"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
              xmlns:exif="http://ns.adobe.com/exif/1.0/">
              <exif:GPSLatitude>40.7128</exif:GPSLatitude>
              <exif:GPSLongitude>-74.006</exif:GPSLongitude>
              <exif:GPSAltitude>10/1</exif:GPSAltitude>
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;
        var xmpPath = CreateTempXmpFile(xmp);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Latitude!.Value).IsEqualTo(40.7128);
        await Assert.That(result.Longitude!.Value).IsEqualTo(-74.006);
        await Assert.That(result.Altitude).IsEqualTo(10.0);
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public async Task Parse_NonexistentFile_ReturnsNull()
    {
        // Arrange
        var nonexistentPath = Path.Combine(_tempDir, "nonexistent.xmp");

        // Act
        var result = _parser.Parse(nonexistentPath);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Parse_EmptyFile_ReturnsNull()
    {
        // Arrange
        var xmpPath = CreateTempXmpFile("");

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Parse_InvalidXml_ReturnsNull()
    {
        // Arrange
        var invalidXml = "This is not valid XML at all!";
        var xmpPath = CreateTempXmpFile(invalidXml);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Parse_MalformedXml_ReturnsNull()
    {
        // Arrange - missing closing tags
        var malformedXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
        """;
        var xmpPath = CreateTempXmpFile(malformedXml);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Parse_NullFilePath_ReturnsNull()
    {
        // Act
        var result = _parser.Parse(null!);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Parse_EmptyFilePath_ReturnsNull()
    {
        // Act
        var result = _parser.Parse("");

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Parse_WhitespaceFilePath_ReturnsNull()
    {
        // Act
        var result = _parser.Parse("   ");

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Parse_ValidXmlButNoMeaningfulData_ReturnsNull()
    {
        // Arrange - valid XMP structure but no usable metadata
        var xmp = """
        <?xml version="1.0" encoding="UTF-8"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about="">
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;
        var xmpPath = CreateTempXmpFile(xmp);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Parse_LatitudeOnlyNoLongitude_ReturnsMetadataWithoutGps()
    {
        // Arrange - only latitude, no longitude means incomplete GPS
        var xmp = """
        <?xml version="1.0" encoding="UTF-8"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
              xmlns:exif="http://ns.adobe.com/exif/1.0/"
              xmlns:dc="http://purl.org/dc/elements/1.1/"
              exif:GPSLatitude="40.7128">
              <dc:title>
                <rdf:Alt>
                  <rdf:li xml:lang="x-default">Photo with incomplete GPS</rdf:li>
                </rdf:Alt>
              </dc:title>
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;
        var xmpPath = CreateTempXmpFile(xmp);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.HasGpsData).IsFalse();
        await Assert.That(result.Latitude).IsNull();
        await Assert.That(result.Longitude).IsNull();
        await Assert.That(result.Title).IsEqualTo("Photo with incomplete GPS");
    }

    [Test]
    public async Task Parse_LongitudeOnlyNoLatitude_ReturnsMetadataWithoutGps()
    {
        // Arrange - only longitude, no latitude means incomplete GPS
        var xmp = """
        <?xml version="1.0" encoding="UTF-8"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
              xmlns:exif="http://ns.adobe.com/exif/1.0/"
              xmlns:dc="http://purl.org/dc/elements/1.1/"
              exif:GPSLongitude="-74.006">
              <dc:title>
                <rdf:Alt>
                  <rdf:li xml:lang="x-default">Photo with incomplete GPS</rdf:li>
                </rdf:Alt>
              </dc:title>
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;
        var xmpPath = CreateTempXmpFile(xmp);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.HasGpsData).IsFalse();
        await Assert.That(result.Latitude).IsNull();
        await Assert.That(result.Longitude).IsNull();
    }

    #endregion

    #region Real-World XMP Format Tests

    [Test]
    public async Task Parse_AdobeLightroomFormat_ParsesCorrectly()
    {
        // Arrange - typical Adobe Lightroom XMP format
        var xmp = """
        <?xml version="1.0" encoding="UTF-8"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/" x:xmptk="Adobe XMP Core 7.0">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
              xmlns:xmp="http://ns.adobe.com/xap/1.0/"
              xmlns:exif="http://ns.adobe.com/exif/1.0/"
              xmlns:tiff="http://ns.adobe.com/tiff/1.0/"
              xmlns:dc="http://purl.org/dc/elements/1.1/"
              xmlns:photoshop="http://ns.adobe.com/photoshop/1.0/"
              xmp:CreateDate="2024-06-15T14:30:00"
              xmp:ModifyDate="2024-06-15T18:00:00"
              exif:DateTimeOriginal="2024-06-15T14:30:00"
              exif:GPSLatitude="40,42.768N"
              exif:GPSLongitude="74,0.36W"
              exif:GPSAltitude="10/1"
              tiff:Make="Canon"
              tiff:Model="Canon EOS R5"
              photoshop:DateCreated="2024-06-15T14:30:00">
              <dc:title>
                <rdf:Alt>
                  <rdf:li xml:lang="x-default">NYC Skyline</rdf:li>
                </rdf:Alt>
              </dc:title>
              <dc:description>
                <rdf:Alt>
                  <rdf:li xml:lang="x-default">New York City skyline at sunset</rdf:li>
                </rdf:Alt>
              </dc:description>
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;
        var xmpPath = CreateTempXmpFile(xmp);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.HasGpsData).IsTrue();
        await Assert.That(result.HasDateTaken).IsTrue();
        await Assert.That(result.Title).IsEqualTo("NYC Skyline");
        await Assert.That(result.Description).IsEqualTo("New York City skyline at sunset");
    }

    [Test]
    public async Task Parse_MultipleDescriptionElements_ExtractsFromAll()
    {
        // Arrange - some XMP files have multiple rdf:Description elements
        var xmp = """
        <?xml version="1.0" encoding="UTF-8"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
              xmlns:exif="http://ns.adobe.com/exif/1.0/"
              exif:GPSLatitude="40.7128"
              exif:GPSLongitude="-74.006">
            </rdf:Description>
            <rdf:Description rdf:about=""
              xmlns:xmp="http://ns.adobe.com/xap/1.0/"
              xmp:CreateDate="2024-06-15T14:30:00">
            </rdf:Description>
            <rdf:Description rdf:about=""
              xmlns:dc="http://purl.org/dc/elements/1.1/">
              <dc:title>
                <rdf:Alt>
                  <rdf:li xml:lang="x-default">Multi-Description Title</rdf:li>
                </rdf:Alt>
              </dc:title>
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;
        var xmpPath = CreateTempXmpFile(xmp);

        // Act
        var result = _parser.Parse(xmpPath);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.HasGpsData).IsTrue();
        await Assert.That(result.HasDateTaken).IsTrue();
        await Assert.That(result.Title).IsEqualTo("Multi-Description Title");
    }

    #endregion

    #region Helper Methods

    private string CreateTempXmpFile(string content)
    {
        var filePath = Path.Combine(_tempDir, $"{Guid.NewGuid()}.xmp");
        File.WriteAllText(filePath, content);
        return filePath;
    }

    #endregion
}
