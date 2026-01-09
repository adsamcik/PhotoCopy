using PhotoCopy.Extensions;

namespace PhotoCopy.Tests.Extensions;

public class PathSanitizerTests
{
    [Test]
    [Arguments("New York", "New York")]
    [Arguments("Los Angeles", "Los Angeles")]
    [Arguments("St. John's", "St. John's")]
    [Arguments("München", "München")]
    public async Task SanitizePathSegment_ValidNames_ReturnsUnchanged(string input, string expected)
    {
        var result = PathSanitizer.SanitizePathSegment(input);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    [Arguments("City: Name", "City_ Name")]
    [Arguments("City<>Name", "City__Name")]
    [Arguments("City\"Name\"", "City_Name_")]
    [Arguments("City/Name", "City_Name")]
    [Arguments("City\\Name", "City_Name")]
    [Arguments("City|Name", "City_Name")]
    [Arguments("City?Name", "City_Name")]
    [Arguments("City*Name", "City_Name")]
    public async Task SanitizePathSegment_InvalidChars_ReplacesWithUnderscore(string input, string expected)
    {
        var result = PathSanitizer.SanitizePathSegment(input);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    [Arguments("CON", "CON_")]
    [Arguments("PRN", "PRN_")]
    [Arguments("AUX", "AUX_")]
    [Arguments("NUL", "NUL_")]
    [Arguments("COM1", "COM1_")]
    [Arguments("LPT1", "LPT1_")]
    [Arguments("con", "con_")]
    [Arguments("Con", "Con_")]
    public async Task SanitizePathSegment_ReservedNames_AppendsUnderscore(string input, string expected)
    {
        var result = PathSanitizer.SanitizePathSegment(input);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    [Arguments("Name.", "Name")]
    [Arguments("Name...", "Name")]
    [Arguments("  Name  ", "Name")]
    [Arguments("  Name...  ", "Name")]
    public async Task SanitizePathSegment_TrailingDotsAndSpaces_Trimmed(string input, string expected)
    {
        var result = PathSanitizer.SanitizePathSegment(input);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task SanitizePathSegment_Null_ReturnsEmpty()
    {
        var result = PathSanitizer.SanitizePathSegment(null);
        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task SanitizePathSegment_EmptyOrWhitespace_ReturnsEmpty(string input)
    {
        var result = PathSanitizer.SanitizePathSegment(input);
        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    [Arguments("New York", "New York")]
    [Arguments("City:Name", "City_Name")]
    public async Task SanitizeOrFallback_ValidInput_ReturnsSanitized(string input, string expected)
    {
        var result = PathSanitizer.SanitizeOrFallback(input);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task SanitizeOrFallback_Null_ReturnsFallback()
    {
        var result = PathSanitizer.SanitizeOrFallback(null);
        await Assert.That(result).IsEqualTo("Unknown");
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task SanitizeOrFallback_EmptyOrWhitespace_ReturnsFallback(string input)
    {
        var result = PathSanitizer.SanitizeOrFallback(input);
        await Assert.That(result).IsEqualTo("Unknown");
    }

    [Test]
    public async Task SanitizeOrFallback_CustomFallback_ReturnsCustomValue()
    {
        var result = PathSanitizer.SanitizeOrFallback(null, "NoLocation");
        await Assert.That(result).IsEqualTo("NoLocation");
    }

    [Test]
    [Arguments("City:Name", true)]
    [Arguments("City<Name>", true)]
    [Arguments("City\"Name", true)]
    [Arguments("City/Name", true)]
    [Arguments("City\\Name", true)]
    [Arguments("City|Name", true)]
    [Arguments("City?Name", true)]
    [Arguments("City*Name", true)]
    [Arguments("New York", false)]
    [Arguments("München", false)]
    [Arguments("St. John's", false)]
    public async Task ContainsInvalidChars_ReturnsCorrectResult(string input, bool expected)
    {
        var result = PathSanitizer.ContainsInvalidChars(input);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task ContainsInvalidChars_Null_ReturnsFalse()
    {
        var result = PathSanitizer.ContainsInvalidChars(null);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ContainsInvalidChars_Empty_ReturnsFalse()
    {
        var result = PathSanitizer.ContainsInvalidChars(string.Empty);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task SanitizePathSegment_MultipleInvalidChars_ReplacesAll()
    {
        var input = "City:Name<>Test|File?*";
        var result = PathSanitizer.SanitizePathSegment(input);
        await Assert.That(result).IsEqualTo("City_Name__Test_File__");
    }

    [Test]
    public async Task SanitizePathSegment_ControlCharacters_Removed()
    {
        var input = "City\x00Name\x1FTest";
        var result = PathSanitizer.SanitizePathSegment(input);
        await Assert.That(result).IsEqualTo("City_Name_Test");
    }

    [Test]
    [Arguments("CON.txt", "CON.txt_")]
    [Arguments("nul.doc", "nul.doc_")]
    public async Task SanitizePathSegment_ReservedNamesWithExtension_AppendsUnderscore(string input, string expected)
    {
        var result = PathSanitizer.SanitizePathSegment(input);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task SanitizePathSegment_CustomReplacement_UsesCustomChar()
    {
        var input = "City:Name";
        var result = PathSanitizer.SanitizePathSegment(input, "-");
        await Assert.That(result).IsEqualTo("City-Name");
    }
}
