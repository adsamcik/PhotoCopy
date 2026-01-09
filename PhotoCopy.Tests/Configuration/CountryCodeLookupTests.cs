using AwesomeAssertions;
using PhotoCopy.Configuration;

namespace PhotoCopy.Tests.Configuration;

/// <summary>
/// Unit tests for the CountryCodeLookup static class.
/// Tests cover country code to name conversion and validation.
/// </summary>
public class CountryCodeLookupTests
{
    #region GetCountryName - Valid Codes

    [Test]
    public void GetCountryName_WithUS_ReturnsUnitedStates()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("US");

        // Assert
        result.Should().Be("United States");
    }

    [Test]
    public void GetCountryName_WithGB_ReturnsUnitedKingdom()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("GB");

        // Assert
        result.Should().Be("United Kingdom");
    }

    [Test]
    public void GetCountryName_WithFR_ReturnsFrance()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("FR");

        // Assert
        result.Should().Be("France");
    }

    [Test]
    public void GetCountryName_WithDE_ReturnsGermany()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("DE");

        // Assert
        result.Should().Be("Germany");
    }

    [Test]
    public void GetCountryName_WithJP_ReturnsJapan()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("JP");

        // Assert
        result.Should().Be("Japan");
    }

    [Test]
    public void GetCountryName_WithCN_ReturnsChina()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("CN");

        // Assert
        result.Should().Be("China");
    }

    [Test]
    public void GetCountryName_WithAU_ReturnsAustralia()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("AU");

        // Assert
        result.Should().Be("Australia");
    }

    [Test]
    public void GetCountryName_WithBR_ReturnsBrazil()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("BR");

        // Assert
        result.Should().Be("Brazil");
    }

    #endregion

    #region GetCountryName - Case Insensitivity

    [Test]
    public void GetCountryName_WithLowercaseCode_ReturnsCorrectName()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("us");

        // Assert
        result.Should().Be("United States");
    }

    [Test]
    public void GetCountryName_WithMixedCaseCode_ReturnsCorrectName()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("Us");

        // Assert
        result.Should().Be("United States");
    }

    [Test]
    public void GetCountryName_WithLowercaseGB_ReturnsUnitedKingdom()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("gb");

        // Assert
        result.Should().Be("United Kingdom");
    }

    #endregion

    #region GetCountryName - Invalid/Unknown Codes

    [Test]
    public void GetCountryName_WithUnknownCode_ReturnsCodeAsIs()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("XX");

        // Assert
        result.Should().Be("XX");
    }

    [Test]
    public void GetCountryName_WithInvalidThreeLetterCode_ReturnsCodeAsIs()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("USA");

        // Assert
        result.Should().Be("USA");
    }

    [Test]
    public void GetCountryName_WithEmptyString_ReturnsEmptyString()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("");

        // Assert
        result.Should().Be("");
    }

    [Test]
    public void GetCountryName_WithNull_ReturnsNull()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName(null!);

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void GetCountryName_WithWhitespace_ReturnsWhitespace()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("  ");

        // Assert
        result.Should().Be("  ");
    }

    #endregion

    #region GetCountryName - Special Codes

    [Test]
    public void GetCountryName_WithKosovo_ReturnsKosovo()
    {
        // XK is a user-assigned code for Kosovo, widely used but not officially in ISO 3166-1
        // Act
        var result = CountryCodeLookup.GetCountryName("XK");

        // Assert
        result.Should().Be("Kosovo");
    }

    [Test]
    public void GetCountryName_WithHongKong_ReturnsHongKong()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("HK");

        // Assert
        result.Should().Be("Hong Kong");
    }

    [Test]
    public void GetCountryName_WithTaiwan_ReturnsTaiwan()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("TW");

        // Assert
        result.Should().Be("Taiwan");
    }

    [Test]
    public void GetCountryName_WithMacau_ReturnsMacao()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("MO");

        // Assert
        result.Should().Be("Macao");
    }

    #endregion

    #region GetCountryName - Countries with Updated Names

    [Test]
    public void GetCountryName_WithNorthMacedonia_ReturnsCorrectModernName()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("MK");

        // Assert
        result.Should().Be("North Macedonia");
    }

    [Test]
    public void GetCountryName_WithEswatini_ReturnsCorrectModernName()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("SZ");

        // Assert
        result.Should().Be("Eswatini");
    }

    [Test]
    public void GetCountryName_WithCzechRepublic_ReturnsCorrectName()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("CZ");

        // Assert
        result.Should().Be("Czech Republic");
    }

    #endregion

    #region GetCountryName - Countries with Special Characters

    [Test]
    public void GetCountryName_WithAlandIslands_ReturnsNameWithSpecialChars()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("AX");

        // Assert
        result.Should().Be("Åland Islands");
    }

    [Test]
    public void GetCountryName_WithReunion_ReturnsNameWithAccent()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("RE");

        // Assert
        result.Should().Be("Réunion");
    }

    [Test]
    public void GetCountryName_WithCuracao_ReturnsNameWithSpecialChars()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("CW");

        // Assert
        result.Should().Be("Curaçao");
    }

    [Test]
    public void GetCountryName_WithSaoTome_ReturnsNameWithSpecialChars()
    {
        // Act
        var result = CountryCodeLookup.GetCountryName("ST");

        // Assert
        result.Should().Be("São Tomé and Príncipe");
    }

    #endregion

    #region IsValidCode Tests

    [Test]
    public void IsValidCode_WithValidCode_ReturnsTrue()
    {
        // Act & Assert
        CountryCodeLookup.IsValidCode("US").Should().BeTrue();
        CountryCodeLookup.IsValidCode("GB").Should().BeTrue();
        CountryCodeLookup.IsValidCode("FR").Should().BeTrue();
    }

    [Test]
    public void IsValidCode_WithInvalidCode_ReturnsFalse()
    {
        // Act & Assert
        CountryCodeLookup.IsValidCode("XX").Should().BeFalse();
        CountryCodeLookup.IsValidCode("ZZ").Should().BeFalse();
        CountryCodeLookup.IsValidCode("123").Should().BeFalse();
    }

    [Test]
    public void IsValidCode_WithEmptyString_ReturnsFalse()
    {
        // Act
        var result = CountryCodeLookup.IsValidCode("");

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void IsValidCode_WithNull_ReturnsFalse()
    {
        // Act
        var result = CountryCodeLookup.IsValidCode(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void IsValidCode_IsCaseInsensitive()
    {
        // Act & Assert
        CountryCodeLookup.IsValidCode("us").Should().BeTrue();
        CountryCodeLookup.IsValidCode("Us").Should().BeTrue();
        CountryCodeLookup.IsValidCode("uS").Should().BeTrue();
    }

    [Test]
    public void IsValidCode_WithWhitespace_ReturnsFalse()
    {
        // Act
        var result = CountryCodeLookup.IsValidCode("  ");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Comprehensive Coverage Tests

    public static IEnumerable<(string code, string expectedName)> GetMajorCountries()
    {
        yield return ("AD", "Andorra");
        yield return ("AE", "United Arab Emirates");
        yield return ("CA", "Canada");
        yield return ("CH", "Switzerland");
        yield return ("ES", "Spain");
        yield return ("IE", "Ireland");
        yield return ("IL", "Israel");
        yield return ("IN", "India");
        yield return ("IT", "Italy");
        yield return ("KR", "South Korea");
        yield return ("MX", "Mexico");
        yield return ("NL", "Netherlands");
        yield return ("NO", "Norway");
        yield return ("NZ", "New Zealand");
        yield return ("PL", "Poland");
        yield return ("PT", "Portugal");
        yield return ("RU", "Russia");
        yield return ("SE", "Sweden");
        yield return ("SG", "Singapore");
        yield return ("TH", "Thailand");
        yield return ("TR", "Turkey");
        yield return ("UA", "Ukraine");
        yield return ("VN", "Vietnam");
        yield return ("ZA", "South Africa");
    }

    [Test]
    [MethodDataSource(nameof(GetMajorCountries))]
    public void GetCountryName_WithMajorCountries_ReturnsCorrectName((string code, string expectedName) testData)
    {
        // Act
        var result = CountryCodeLookup.GetCountryName(testData.code);

        // Assert
        result.Should().Be(testData.expectedName);
    }

    public static IEnumerable<(string code, string expectedName)> GetTerritories()
    {
        yield return ("PR", "Puerto Rico");
        yield return ("GU", "Guam");
        yield return ("VI", "U.S. Virgin Islands");
        yield return ("AS", "American Samoa");
        yield return ("GI", "Gibraltar");
        yield return ("BM", "Bermuda");
        yield return ("KY", "Cayman Islands");
        yield return ("VG", "British Virgin Islands");
    }

    [Test]
    [MethodDataSource(nameof(GetTerritories))]
    public void GetCountryName_WithTerritories_ReturnsCorrectName((string code, string expectedName) testData)
    {
        // Act
        var result = CountryCodeLookup.GetCountryName(testData.code);

        // Assert
        result.Should().Be(testData.expectedName);
    }

    #endregion
}
