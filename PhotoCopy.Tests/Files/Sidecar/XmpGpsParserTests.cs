namespace PhotoCopy.Tests.Files.Sidecar;

public class XmpGpsParserTests
{
    #region ParseCoordinate - Latitude Tests

    [Test]
    public async Task ParseCoordinate_DmsFormatNorth_ReturnsCorrectValue()
    {
        // Arrange
        var value = "40,42.768N";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate(value, isLongitude: false);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value).IsEqualTo(40.7128).Within(0.0001);
    }

    [Test]
    public async Task ParseCoordinate_DmsFormatSouth_ReturnsNegativeValue()
    {
        // Arrange
        var value = "33,51.54S";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate(value, isLongitude: false);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value).IsEqualTo(-33.859).Within(0.0001);
    }

    [Test]
    public async Task ParseCoordinate_DmsWithSecondsNorth_ReturnsCorrectValue()
    {
        // Arrange - 40 degrees, 42 minutes, 46.08 seconds N
        var value = "40,42,46.08N";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate(value, isLongitude: false);

        // Assert
        await Assert.That(result).IsNotNull();
        // 40 + 42/60 + 46.08/3600 = 40.7128
        await Assert.That(result!.Value).IsEqualTo(40.7128).Within(0.0001);
    }

    [Test]
    public async Task ParseCoordinate_DmsWithSecondsSouth_ReturnsNegativeValue()
    {
        // Arrange - 33 degrees, 51 minutes, 32.4 seconds S
        var value = "33,51,32.4S";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate(value, isLongitude: false);

        // Assert
        await Assert.That(result).IsNotNull();
        // -(33 + 51/60 + 32.4/3600) = -33.859
        await Assert.That(result!.Value).IsEqualTo(-33.859).Within(0.0001);
    }

    [Test]
    public async Task ParseCoordinate_DecimalFormat_ReturnsCorrectValue()
    {
        // Arrange
        var value = "40.7128";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate(value, isLongitude: false);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value).IsEqualTo(40.7128);
    }

    [Test]
    public async Task ParseCoordinate_NegativeDecimalFormat_ReturnsNegativeValue()
    {
        // Arrange
        var value = "-33.859";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate(value, isLongitude: false);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value).IsEqualTo(-33.859);
    }

    [Test]
    public async Task ParseCoordinate_ZeroValue_ReturnsZero()
    {
        // Arrange
        var value = "0.0";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate(value, isLongitude: false);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value).IsEqualTo(0.0);
    }

    [Test]
    public async Task ParseCoordinate_LatitudeOutOfRange_ReturnsNull()
    {
        // Arrange - latitude cannot exceed 90
        var value = "91.5";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate(value, isLongitude: false);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ParseCoordinate_LatitudeNegativeOutOfRange_ReturnsNull()
    {
        // Arrange - latitude cannot be less than -90
        var value = "-91.5";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate(value, isLongitude: false);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region ParseCoordinate - Longitude Tests

    [Test]
    public async Task ParseCoordinate_DmsFormatWest_ReturnsNegativeValue()
    {
        // Arrange
        var value = "74,0.36W";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate(value, isLongitude: true);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value).IsEqualTo(-74.006).Within(0.0001);
    }

    [Test]
    public async Task ParseCoordinate_DmsFormatEast_ReturnsPositiveValue()
    {
        // Arrange
        var value = "139,39.018E";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate(value, isLongitude: true);

        // Assert
        await Assert.That(result).IsNotNull();
        // 139 + 39.018/60 = 139.6503
        await Assert.That(result!.Value).IsEqualTo(139.6503).Within(0.0001);
    }

    [Test]
    public async Task ParseCoordinate_DmsWithSecondsWest_ReturnsNegativeValue()
    {
        // Arrange - 74 degrees, 0 minutes, 21.6 seconds W
        var value = "74,0,21.6W";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate(value, isLongitude: true);

        // Assert
        await Assert.That(result).IsNotNull();
        // -(74 + 0/60 + 21.6/3600) = -74.006
        await Assert.That(result!.Value).IsEqualTo(-74.006).Within(0.0001);
    }

    [Test]
    public async Task ParseCoordinate_LongitudeDecimalNegative_ReturnsNegativeValue()
    {
        // Arrange
        var value = "-74.006";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate(value, isLongitude: true);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value).IsEqualTo(-74.006);
    }

    [Test]
    public async Task ParseCoordinate_LongitudeOutOfRange_ReturnsNull()
    {
        // Arrange - longitude cannot exceed 180
        var value = "181.5";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate(value, isLongitude: true);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ParseCoordinate_LongitudeNegativeOutOfRange_ReturnsNull()
    {
        // Arrange - longitude cannot be less than -180
        var value = "-181.5";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate(value, isLongitude: true);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ParseCoordinate_WrongDirectionForLatitude_ReturnsNull()
    {
        // Arrange - using E/W direction for latitude
        var value = "40,42.768E";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate(value, isLongitude: false);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ParseCoordinate_WrongDirectionForLongitude_ReturnsNull()
    {
        // Arrange - using N/S direction for longitude
        var value = "74,0.36N";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate(value, isLongitude: true);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region ParseCoordinate - Edge Cases

    [Test]
    public async Task ParseCoordinate_NullValue_ReturnsNull()
    {
        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate(null, isLongitude: false);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ParseCoordinate_EmptyString_ReturnsNull()
    {
        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate("", isLongitude: false);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ParseCoordinate_WhitespaceOnly_ReturnsNull()
    {
        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate("   ", isLongitude: false);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ParseCoordinate_InvalidFormat_ReturnsNull()
    {
        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate("not a coordinate", isLongitude: false);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ParseCoordinate_WithLeadingWhitespace_ParsesCorrectly()
    {
        // Arrange
        var value = "  40.7128";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate(value, isLongitude: false);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value).IsEqualTo(40.7128);
    }

    [Test]
    public async Task ParseCoordinate_WithTrailingWhitespace_ParsesCorrectly()
    {
        // Arrange
        var value = "40.7128  ";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate(value, isLongitude: false);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value).IsEqualTo(40.7128);
    }

    [Test]
    public async Task ParseCoordinate_DmsWithLowercaseDirection_ParsesCorrectly()
    {
        // Arrange
        var value = "40,42.768n";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseCoordinate(value, isLongitude: false);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value).IsEqualTo(40.7128).Within(0.0001);
    }

    #endregion

    #region ParseAltitude Tests

    [Test]
    public async Task ParseAltitude_FractionFormat_ReturnsCorrectValue()
    {
        // Arrange
        var value = "10/1";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseAltitude(value);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value).IsEqualTo(10.0);
    }

    [Test]
    public async Task ParseAltitude_FractionFormatWithDenominator_ReturnsCorrectValue()
    {
        // Arrange
        var value = "305/10";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseAltitude(value);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value).IsEqualTo(30.5);
    }

    [Test]
    public async Task ParseAltitude_DecimalFormat_ReturnsCorrectValue()
    {
        // Arrange
        var value = "30.5";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseAltitude(value);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value).IsEqualTo(30.5);
    }

    [Test]
    public async Task ParseAltitude_IntegerFormat_ReturnsCorrectValue()
    {
        // Arrange
        var value = "100";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseAltitude(value);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value).IsEqualTo(100.0);
    }

    [Test]
    public async Task ParseAltitude_NegativeValue_ReturnsNegativeValue()
    {
        // Arrange - below sea level
        var value = "-10.5";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseAltitude(value);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value).IsEqualTo(-10.5);
    }

    [Test]
    public async Task ParseAltitude_NullValue_ReturnsNull()
    {
        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseAltitude(null);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ParseAltitude_EmptyString_ReturnsNull()
    {
        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseAltitude("");

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ParseAltitude_ZeroDenominator_ReturnsNull()
    {
        // Arrange - division by zero
        var value = "10/0";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseAltitude(value);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ParseAltitude_InvalidFormat_ReturnsNull()
    {
        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseAltitude("not an altitude");

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ParseAltitude_WithWhitespace_ParsesCorrectly()
    {
        // Arrange
        var value = "  30.5  ";

        // Act
        var result = PhotoCopy.Files.Sidecar.XmpGpsParser.ParseAltitude(value);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value).IsEqualTo(30.5);
    }

    #endregion
}
