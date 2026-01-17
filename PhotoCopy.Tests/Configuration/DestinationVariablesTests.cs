using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using PhotoCopy.Configuration;

namespace PhotoCopy.Tests.Configuration;

/// <summary>
/// Unit tests for the DestinationVariables class containing destination path variable constants.
/// </summary>
public class DestinationVariablesTests
{
    private static readonly FieldInfo[] VariableFields = typeof(DestinationVariables)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
        .ToArray();

    [Test]
    public async Task AllVariableConstants_HaveCorrectBraceFormat()
    {
        // Assert all variable values start with { and end with }
        foreach (var field in VariableFields)
        {
            var value = (string?)field.GetValue(null);
            await Assert.That(value).IsNotNull();
            await Assert.That(value!.StartsWith("{")).IsTrue();
            await Assert.That(value!.EndsWith("}")).IsTrue();
        }
    }

    [Test]
    public async Task AllVariableNames_AreLowercase()
    {
        // Assert all variable names inside braces are lowercase
        foreach (var field in VariableFields)
        {
            var value = (string?)field.GetValue(null);
            var innerName = value!.Trim('{', '}');
            await Assert.That(innerName).IsEqualTo(innerName.ToLowerInvariant());
        }
    }

    [Test]
    public async Task AllVariableValues_AreUnique()
    {
        // Arrange
        var values = VariableFields
            .Select(f => (string?)f.GetValue(null))
            .ToList();

        var distinctValues = values.Distinct().ToList();

        // Assert
        await Assert.That(values.Count).IsEqualTo(distinctValues.Count);
    }

    [Test]
    public async Task DestinationVariables_HasExpectedConstants()
    {
        // Assert - verify expected constants exist with correct values
        var year = DestinationVariables.Year;
        var month = DestinationVariables.Month;
        var day = DestinationVariables.Day;
        var name = DestinationVariables.Name;
        var nameNoExt = DestinationVariables.NameNoExtension;
        var ext = DestinationVariables.Extension;
        var dir = DestinationVariables.Directory;
        var num = DestinationVariables.Number;
        var district = DestinationVariables.District;
        var city = DestinationVariables.City;
        var county = DestinationVariables.County;
        var state = DestinationVariables.State;
        var country = DestinationVariables.Country;

        await Assert.That(year).IsEqualTo("{year}");
        await Assert.That(month).IsEqualTo("{month}");
        await Assert.That(day).IsEqualTo("{day}");
        await Assert.That(name).IsEqualTo("{name}");
        await Assert.That(nameNoExt).IsEqualTo("{namenoext}");
        await Assert.That(ext).IsEqualTo("{ext}");
        await Assert.That(dir).IsEqualTo("{directory}");
        await Assert.That(num).IsEqualTo("{number}");
        await Assert.That(district).IsEqualTo("{district}");
        await Assert.That(city).IsEqualTo("{city}");
        await Assert.That(county).IsEqualTo("{county}");
        await Assert.That(state).IsEqualTo("{state}");
        await Assert.That(country).IsEqualTo("{country}");
    }

    [Test]
    public async Task DestinationVariables_HasExpectedCount()
    {
        // Assert - there should be exactly 13 variable constants
        await Assert.That(VariableFields.Length).IsEqualTo(13);
    }

    [Test]
    public async Task AllVariableValues_ContainOnlyValidCharacters()
    {
        // Assert - variable names should only contain lowercase letters
        var regex = new Regex("^[a-z]+$");
        
        foreach (var field in VariableFields)
        {
            var value = (string?)field.GetValue(null);
            var innerName = value!.Trim('{', '}');
            await Assert.That(regex.IsMatch(innerName)).IsTrue();
        }
    }
}
