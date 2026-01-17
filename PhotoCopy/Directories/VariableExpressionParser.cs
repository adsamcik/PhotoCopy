using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PhotoCopy.Directories;

/// <summary>
/// Represents a parsed variable expression with optional conditions and fallback.
/// Syntax: {variable} or {variable|fallback} or {variable?condition|fallback}
/// Conditions: min=N (minimum photo count), max=N (maximum photo count)
/// </summary>
public record VariableExpression
{
    /// <summary>
    /// The variable name (e.g., "city", "country", "year")
    /// </summary>
    public string VariableName { get; init; } = string.Empty;
    
    /// <summary>
    /// The original full expression including braces (e.g., "{city?min=10|country}")
    /// </summary>
    public string OriginalExpression { get; init; } = string.Empty;
    
    /// <summary>
    /// The fallback value if condition fails or variable is empty
    /// </summary>
    public string? Fallback { get; init; }
    
    /// <summary>
    /// Minimum photo count required for the condition to pass
    /// </summary>
    public int? MinimumCount { get; init; }
    
    /// <summary>
    /// Maximum photo count allowed for the condition to pass
    /// </summary>
    public int? MaximumCount { get; init; }
    
    /// <summary>
    /// Whether this expression has any conditions
    /// </summary>
    public bool HasConditions => MinimumCount.HasValue || MaximumCount.HasValue;
    
    /// <summary>
    /// Whether this expression has a fallback value
    /// </summary>
    public bool HasFallback => Fallback != null;
}

/// <summary>
/// Parses variable expressions with conditional syntax.
/// Supports: {variable}, {variable|fallback}, {variable?min=N}, {variable?max=N}, 
/// {variable?min=N,max=M}, {variable?min=N|fallback}
/// </summary>
public static class VariableExpressionParser
{
    // Pattern: {variableName} or {variableName|fallback} or {variableName?conditions} or {variableName?conditions|fallback}
    // variableName: word characters
    // conditions: min=N, max=N, or min=N,max=M
    // fallback: anything after the pipe
    private static readonly Regex ExpressionPattern = new(
        @"\{(?<variable>\w+)(?:\?(?<conditions>[^|}\s]+))?(?:\|(?<fallback>[^}]*))?\}",
        RegexOptions.Compiled);
    
    private static readonly Regex ConditionPattern = new(
        @"(?<key>min|max)=(?<value>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    /// <summary>
    /// Parses a template string and returns all variable expressions found.
    /// </summary>
    public static IEnumerable<VariableExpression> ParseAll(string template)
    {
        if (string.IsNullOrEmpty(template))
            yield break;
            
        foreach (Match match in ExpressionPattern.Matches(template))
        {
            yield return ParseMatch(match);
        }
    }
    
    /// <summary>
    /// Parses a single variable expression string (with braces).
    /// </summary>
    public static VariableExpression? Parse(string expression)
    {
        if (string.IsNullOrEmpty(expression))
            return null;
            
        var match = ExpressionPattern.Match(expression);
        if (!match.Success)
            return null;
            
        return ParseMatch(match);
    }
    
    private static VariableExpression ParseMatch(Match match)
    {
        var variableName = match.Groups["variable"].Value;
        var conditionsStr = match.Groups["conditions"].Success ? match.Groups["conditions"].Value : null;
        var fallback = match.Groups["fallback"].Success ? match.Groups["fallback"].Value : null;
        
        int? minCount = null;
        int? maxCount = null;
        
        if (!string.IsNullOrEmpty(conditionsStr))
        {
            foreach (Match condMatch in ConditionPattern.Matches(conditionsStr))
            {
                var key = condMatch.Groups["key"].Value.ToLowerInvariant();
                var value = int.Parse(condMatch.Groups["value"].Value);
                
                if (key == "min")
                    minCount = value;
                else if (key == "max")
                    maxCount = value;
            }
        }
        
        return new VariableExpression
        {
            VariableName = variableName,
            OriginalExpression = match.Value,
            Fallback = fallback,
            MinimumCount = minCount,
            MaximumCount = maxCount
        };
    }
    
    /// <summary>
    /// Evaluates whether a variable expression's conditions are met.
    /// Returns true if no conditions, or all conditions pass.
    /// </summary>
    public static bool EvaluateConditions(VariableExpression expression, string variableValue, IPathGeneratorContext? context)
    {
        // No conditions = always passes
        if (!expression.HasConditions)
            return true;
            
        // No context = can't evaluate conditions, treat as failed
        if (context == null)
            return false;
            
        // Check minimum condition
        if (expression.MinimumCount.HasValue)
        {
            if (!context.MeetsMinimum(expression.VariableName, variableValue, expression.MinimumCount.Value))
                return false;
        }
        
        // Check maximum condition
        if (expression.MaximumCount.HasValue)
        {
            if (!context.MeetsMaximum(expression.VariableName, variableValue, expression.MaximumCount.Value))
                return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Resolves a variable expression to its final value.
    /// Uses the variable value if conditions pass, otherwise uses fallback.
    /// </summary>
    public static string ResolveValue(VariableExpression expression, string variableValue, IPathGeneratorContext? context)
    {
        // If value is empty and there's a fallback, use fallback
        if (string.IsNullOrEmpty(variableValue))
            return expression.Fallback ?? string.Empty;
            
        // If conditions fail and there's a fallback, use fallback
        if (!EvaluateConditions(expression, variableValue, context))
            return expression.Fallback ?? string.Empty;
            
        // Conditions pass, use the value
        return variableValue;
    }
}
