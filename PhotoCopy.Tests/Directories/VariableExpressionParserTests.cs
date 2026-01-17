using AwesomeAssertions;
using PhotoCopy.Directories;
using PhotoCopy.Files;

namespace PhotoCopy.Tests.Directories;

public class VariableExpressionParserTests
{
    #region Parse Tests

    [Test]
    public void Parse_SimpleVariable_ReturnsCorrectExpression()
    {
        // Arrange
        var expression = "{city}";
        
        // Act
        var result = VariableExpressionParser.Parse(expression);
        
        // Assert
        result.Should().NotBeNull();
        result!.VariableName.Should().Be("city");
        result.OriginalExpression.Should().Be("{city}");
        result.HasConditions.Should().BeFalse();
        result.HasFallback.Should().BeFalse();
    }

    [Test]
    public void Parse_VariableWithFallback_ReturnsCorrectExpression()
    {
        // Arrange
        var expression = "{city|Unknown}";
        
        // Act
        var result = VariableExpressionParser.Parse(expression);
        
        // Assert
        result.Should().NotBeNull();
        result!.VariableName.Should().Be("city");
        result.Fallback.Should().Be("Unknown");
        result.HasFallback.Should().BeTrue();
        result.HasConditions.Should().BeFalse();
    }

    [Test]
    public void Parse_VariableWithMinCondition_ReturnsCorrectExpression()
    {
        // Arrange
        var expression = "{city?min=10}";
        
        // Act
        var result = VariableExpressionParser.Parse(expression);
        
        // Assert
        result.Should().NotBeNull();
        result!.VariableName.Should().Be("city");
        result.MinimumCount.Should().Be(10);
        result.MaximumCount.Should().BeNull();
        result.HasConditions.Should().BeTrue();
    }

    [Test]
    public void Parse_VariableWithMaxCondition_ReturnsCorrectExpression()
    {
        // Arrange
        var expression = "{city?max=100}";
        
        // Act
        var result = VariableExpressionParser.Parse(expression);
        
        // Assert
        result.Should().NotBeNull();
        result!.VariableName.Should().Be("city");
        result.MaximumCount.Should().Be(100);
        result.MinimumCount.Should().BeNull();
        result.HasConditions.Should().BeTrue();
    }

    [Test]
    public void Parse_VariableWithMinAndMaxConditions_ReturnsCorrectExpression()
    {
        // Arrange
        var expression = "{city?min=5,max=50}";
        
        // Act
        var result = VariableExpressionParser.Parse(expression);
        
        // Assert
        result.Should().NotBeNull();
        result!.VariableName.Should().Be("city");
        result.MinimumCount.Should().Be(5);
        result.MaximumCount.Should().Be(50);
        result.HasConditions.Should().BeTrue();
    }

    [Test]
    public void Parse_VariableWithConditionAndFallback_ReturnsCorrectExpression()
    {
        // Arrange
        var expression = "{city?min=10|country}";
        
        // Act
        var result = VariableExpressionParser.Parse(expression);
        
        // Assert
        result.Should().NotBeNull();
        result!.VariableName.Should().Be("city");
        result.MinimumCount.Should().Be(10);
        result.Fallback.Should().Be("country");
        result.HasConditions.Should().BeTrue();
        result.HasFallback.Should().BeTrue();
    }

    [Test]
    public void Parse_EmptyFallback_ReturnsEmptyFallback()
    {
        // Arrange
        var expression = "{city|}";
        
        // Act
        var result = VariableExpressionParser.Parse(expression);
        
        // Assert
        result.Should().NotBeNull();
        result!.Fallback.Should().Be("");
        result.HasFallback.Should().BeTrue();
    }

    [Test]
    public void Parse_InvalidExpression_ReturnsNull()
    {
        // Arrange
        var expression = "city"; // No braces
        
        // Act
        var result = VariableExpressionParser.Parse(expression);
        
        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void Parse_EmptyString_ReturnsNull()
    {
        // Act
        var result = VariableExpressionParser.Parse("");
        
        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void Parse_NullString_ReturnsNull()
    {
        // Act
        var result = VariableExpressionParser.Parse(null!);
        
        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region ParseAll Tests

    [Test]
    public void ParseAll_MultipleVariables_ReturnsAllExpressions()
    {
        // Arrange
        var template = @"C:\Photos\{year}\{country}\{city?min=10|Unknown}\{name}{extension}";
        
        // Act
        var results = VariableExpressionParser.ParseAll(template).ToList();
        
        // Assert
        results.Should().HaveCount(5);
        results[0].VariableName.Should().Be("year");
        results[1].VariableName.Should().Be("country");
        results[2].VariableName.Should().Be("city");
        results[2].MinimumCount.Should().Be(10);
        results[2].Fallback.Should().Be("Unknown");
        results[3].VariableName.Should().Be("name");
        results[4].VariableName.Should().Be("extension");
    }

    [Test]
    public void ParseAll_EmptyTemplate_ReturnsEmpty()
    {
        // Act
        var results = VariableExpressionParser.ParseAll("").ToList();
        
        // Assert
        results.Should().BeEmpty();
    }

    [Test]
    public void ParseAll_NoVariables_ReturnsEmpty()
    {
        // Act
        var results = VariableExpressionParser.ParseAll(@"C:\Photos\2024\").ToList();
        
        // Assert
        results.Should().BeEmpty();
    }

    #endregion

    #region EvaluateConditions Tests

    [Test]
    public void EvaluateConditions_NoConditions_ReturnsTrue()
    {
        // Arrange
        var expression = VariableExpressionParser.Parse("{city}")!;
        
        // Act
        var result = VariableExpressionParser.EvaluateConditions(expression, "London", null);
        
        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void EvaluateConditions_WithConditionsButNoContext_ReturnsFalse()
    {
        // Arrange
        var expression = VariableExpressionParser.Parse("{city?min=10}")!;
        
        // Act
        var result = VariableExpressionParser.EvaluateConditions(expression, "London", null);
        
        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void EvaluateConditions_MinConditionMet_ReturnsTrue()
    {
        // Arrange
        var expression = VariableExpressionParser.Parse("{city?min=5}")!;
        var stats = new LocationStatistics();
        // Add 10 files for London
        for (int i = 0; i < 10; i++)
            stats.RecordFile(new PhotoCopy.Files.LocationData("Central London", "London", "London", "England", "GB"));
        var context = new PathGeneratorContext(stats, 10);
        
        // Act
        var result = VariableExpressionParser.EvaluateConditions(expression, "London", context);
        
        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void EvaluateConditions_MinConditionNotMet_ReturnsFalse()
    {
        // Arrange
        var expression = VariableExpressionParser.Parse("{city?min=20}")!;
        var stats = new LocationStatistics();
        // Add only 5 files for London
        for (int i = 0; i < 5; i++)
            stats.RecordFile(new PhotoCopy.Files.LocationData("Central London", "London", "London", "England", "GB"));
        var context = new PathGeneratorContext(stats, 5);
        
        // Act
        var result = VariableExpressionParser.EvaluateConditions(expression, "London", context);
        
        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void EvaluateConditions_MaxConditionMet_ReturnsTrue()
    {
        // Arrange
        var expression = VariableExpressionParser.Parse("{city?max=50}")!;
        var stats = new LocationStatistics();
        // Add 10 files for London
        for (int i = 0; i < 10; i++)
            stats.RecordFile(new PhotoCopy.Files.LocationData("Central London", "London", "London", "England", "GB"));
        var context = new PathGeneratorContext(stats, 10);
        
        // Act
        var result = VariableExpressionParser.EvaluateConditions(expression, "London", context);
        
        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void EvaluateConditions_MaxConditionNotMet_ReturnsFalse()
    {
        // Arrange
        var expression = VariableExpressionParser.Parse("{city?max=5}")!;
        var stats = new LocationStatistics();
        // Add 10 files for London - exceeds max
        for (int i = 0; i < 10; i++)
            stats.RecordFile(new PhotoCopy.Files.LocationData("Central London", "London", "London", "England", "GB"));
        var context = new PathGeneratorContext(stats, 10);
        
        // Act
        var result = VariableExpressionParser.EvaluateConditions(expression, "London", context);
        
        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void EvaluateConditions_BothConditionsMet_ReturnsTrue()
    {
        // Arrange
        var expression = VariableExpressionParser.Parse("{city?min=5,max=50}")!;
        var stats = new LocationStatistics();
        // Add 10 files for London - within range
        for (int i = 0; i < 10; i++)
            stats.RecordFile(new PhotoCopy.Files.LocationData("Central London", "London", "London", "England", "GB"));
        var context = new PathGeneratorContext(stats, 10);
        
        // Act
        var result = VariableExpressionParser.EvaluateConditions(expression, "London", context);
        
        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region ResolveValue Tests

    [Test]
    public void ResolveValue_EmptyValue_ReturnsFallback()
    {
        // Arrange
        var expression = VariableExpressionParser.Parse("{city|Unknown}")!;
        
        // Act
        var result = VariableExpressionParser.ResolveValue(expression, "", null);
        
        // Assert
        result.Should().Be("Unknown");
    }

    [Test]
    public void ResolveValue_EmptyValueNoFallback_ReturnsEmpty()
    {
        // Arrange
        var expression = VariableExpressionParser.Parse("{city}")!;
        
        // Act
        var result = VariableExpressionParser.ResolveValue(expression, "", null);
        
        // Assert
        result.Should().Be("");
    }

    [Test]
    public void ResolveValue_ConditionsPassed_ReturnsValue()
    {
        // Arrange
        var expression = VariableExpressionParser.Parse("{city?min=5|Unknown}")!;
        var stats = new LocationStatistics();
        for (int i = 0; i < 10; i++)
            stats.RecordFile(new PhotoCopy.Files.LocationData("Central London", "London", "London", "England", "GB"));
        var context = new PathGeneratorContext(stats, 10);
        
        // Act
        var result = VariableExpressionParser.ResolveValue(expression, "London", context);
        
        // Assert
        result.Should().Be("London");
    }

    [Test]
    public void ResolveValue_ConditionsFailed_ReturnsFallback()
    {
        // Arrange
        var expression = VariableExpressionParser.Parse("{city?min=20|Unknown}")!;
        var stats = new LocationStatistics();
        for (int i = 0; i < 5; i++)
            stats.RecordFile(new PhotoCopy.Files.LocationData("Central London", "London", "London", "England", "GB"));
        var context = new PathGeneratorContext(stats, 5);
        
        // Act
        var result = VariableExpressionParser.ResolveValue(expression, "London", context);
        
        // Assert
        result.Should().Be("Unknown");
    }

    #endregion
}

