using PhotoCopy.Configuration;
using PhotoCopy.Extensions;

namespace PhotoCopy.Tests.Extensions;

public class CasingFormatterTests
{
    #region ApplyCasing with PathCasing enum

    [Test]
    public async Task ApplyCasing_Original_ReturnsUnchanged()
    {
        var result = CasingFormatter.ApplyCasing("New York City", PathCasing.Original);
        await Assert.That(result).IsEqualTo("New York City");
    }

    [Test]
    public async Task ApplyCasing_Lowercase_ReturnsLowercase()
    {
        var result = CasingFormatter.ApplyCasing("New York City", PathCasing.Lowercase);
        await Assert.That(result).IsEqualTo("new york city");
    }

    [Test]
    public async Task ApplyCasing_Uppercase_ReturnsUppercase()
    {
        var result = CasingFormatter.ApplyCasing("New York City", PathCasing.Uppercase);
        await Assert.That(result).IsEqualTo("NEW YORK CITY");
    }

    [Test]
    public async Task ApplyCasing_TitleCase_ReturnsTitleCase()
    {
        var result = CasingFormatter.ApplyCasing("new york city", PathCasing.TitleCase);
        await Assert.That(result).IsEqualTo("New York City");
    }

    [Test]
    public async Task ApplyCasing_PascalCase_ReturnsPascalCase()
    {
        var result = CasingFormatter.ApplyCasing("New York City", PathCasing.PascalCase);
        await Assert.That(result).IsEqualTo("NewYorkCity");
    }

    [Test]
    public async Task ApplyCasing_CamelCase_ReturnsCamelCase()
    {
        var result = CasingFormatter.ApplyCasing("New York City", PathCasing.CamelCase);
        await Assert.That(result).IsEqualTo("newYorkCity");
    }

    [Test]
    public async Task ApplyCasing_SnakeCase_ReturnsSnakeCase()
    {
        var result = CasingFormatter.ApplyCasing("New York City", PathCasing.SnakeCase);
        await Assert.That(result).IsEqualTo("new_york_city");
    }

    [Test]
    public async Task ApplyCasing_KebabCase_ReturnsKebabCase()
    {
        var result = CasingFormatter.ApplyCasing("New York City", PathCasing.KebabCase);
        await Assert.That(result).IsEqualTo("new-york-city");
    }

    [Test]
    public async Task ApplyCasing_ScreamingSnakeCase_ReturnsScreamingSnakeCase()
    {
        var result = CasingFormatter.ApplyCasing("New York City", PathCasing.ScreamingSnakeCase);
        await Assert.That(result).IsEqualTo("NEW_YORK_CITY");
    }

    #endregion

    #region Null and Empty Handling

    [Test]
    public async Task ApplyCasing_NullInput_ReturnsEmptyString()
    {
        var result = CasingFormatter.ApplyCasing(null, PathCasing.PascalCase);
        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ApplyCasing_EmptyInput_ReturnsEmptyString()
    {
        var result = CasingFormatter.ApplyCasing("", PathCasing.PascalCase);
        await Assert.That(result).IsEqualTo(string.Empty);
    }

    #endregion

    #region Special Characters and International Names

    [Test]
    public async Task ToPascalCase_WithDiacritics_PreservesCharacters()
    {
        var result = CasingFormatter.ToPascalCase("Příbram");
        await Assert.That(result).IsEqualTo("Příbram");
    }

    [Test]
    public async Task ToKebabCase_WithDiacritics_PreservesCharacters()
    {
        var result = CasingFormatter.ToKebabCase("Biograd na Moru");
        await Assert.That(result).IsEqualTo("biograd-na-moru");
    }

    [Test]
    public async Task ToSnakeCase_WithGermanUmlauts_PreservesCharacters()
    {
        var result = CasingFormatter.ToSnakeCase("München");
        await Assert.That(result).IsEqualTo("münchen");
    }

    [Test]
    public async Task ToPascalCase_SingleWord_ReturnsPascalCase()
    {
        var result = CasingFormatter.ToPascalCase("prague");
        await Assert.That(result).IsEqualTo("Prague");
    }

    #endregion

    #region CamelCase/PascalCase Input Handling

    [Test]
    public async Task ToSnakeCase_FromPascalCase_SplitsWords()
    {
        var result = CasingFormatter.ToSnakeCase("NewYorkCity");
        await Assert.That(result).IsEqualTo("new_york_city");
    }

    [Test]
    public async Task ToKebabCase_FromCamelCase_SplitsWords()
    {
        var result = CasingFormatter.ToKebabCase("newYorkCity");
        await Assert.That(result).IsEqualTo("new-york-city");
    }

    [Test]
    public async Task ToPascalCase_FromSnakeCase_ConvertsCorrectly()
    {
        var result = CasingFormatter.ToPascalCase("new_york_city");
        await Assert.That(result).IsEqualTo("NewYorkCity");
    }

    [Test]
    public async Task ToCamelCase_FromKebabCase_ConvertsCorrectly()
    {
        var result = CasingFormatter.ToCamelCase("new-york-city");
        await Assert.That(result).IsEqualTo("newYorkCity");
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task ToPascalCase_AllUppercase_ConvertsCorrectly()
    {
        var result = CasingFormatter.ToPascalCase("NEW YORK");
        await Assert.That(result).IsEqualTo("NewYork");
    }

    [Test]
    public async Task ToTitleCase_MixedCase_NormalizesCorrectly()
    {
        var result = CasingFormatter.ToTitleCase("nEW yORK cITY");
        await Assert.That(result).IsEqualTo("New York City");
    }

    [Test]
    public async Task ToSnakeCase_MultipleSpaces_HandlesCorrectly()
    {
        var result = CasingFormatter.ToSnakeCase("New  York   City");
        await Assert.That(result).IsEqualTo("new_york_city");
    }

    [Test]
    public async Task ToKebabCase_MixedSeparators_HandlesCorrectly()
    {
        var result = CasingFormatter.ToKebabCase("New_York-City");
        await Assert.That(result).IsEqualTo("new-york-city");
    }

    #endregion

    #region Real World Location Names

    [Test]
    public async Task ToPascalCase_Reykjavik_PreservesAccents()
    {
        var result = CasingFormatter.ToPascalCase("Reykjavík");
        await Assert.That(result).IsEqualTo("Reykjavík");
    }

    [Test]
    public async Task ToKebabCase_ElPratDeLlobregat_ConvertsCorrectly()
    {
        var result = CasingFormatter.ToKebabCase("El Prat de Llobregat");
        await Assert.That(result).IsEqualTo("el-prat-de-llobregat");
    }

    [Test]
    public async Task ToSnakeCase_BiogradNaMoru_ConvertsCorrectly()
    {
        var result = CasingFormatter.ToSnakeCase("Biograd na Moru");
        await Assert.That(result).IsEqualTo("biograd_na_moru");
    }

    [Test]
    public async Task ToCamelCase_UnitedStates_ConvertsCorrectly()
    {
        var result = CasingFormatter.ToCamelCase("United States");
        await Assert.That(result).IsEqualTo("unitedStates");
    }

    #endregion
}
