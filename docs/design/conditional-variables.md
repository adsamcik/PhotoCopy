# Conditional Variable Support in PhotoCopy Destination Patterns

## Executive Summary

This document proposes a comprehensive syntax and semantics for conditional variable support in PhotoCopy destination patterns, enabling advanced features like minimum photo thresholds and multi-level fallback chains.

---

## 1. Syntax Design

### 1.1 Current Syntax (Baseline)

PhotoCopy currently supports:
- **Simple variables**: `{city}`, `{country}`, `{year}`
- **Inline literal fallbacks**: `{city|Unknown}` → uses "Unknown" if city is empty

### 1.2 Proposed Extended Syntax

```
{variable[?condition][|fallback]}
```

**Components:**
| Component | Required | Description |
|-----------|----------|-------------|
| `variable` | Yes | Base variable name (city, country, etc.) |
| `?condition` | No | Condition modifier(s) |
| `\|fallback` | No | Fallback value or variable chain |

### 1.3 Syntax Examples

```
# Simple (existing)
{city}                          # City or global fallback
{city|Unknown}                  # City or literal "Unknown"

# Variable fallback chain (new)
{city|country}                  # City, or country if city empty
{district|city|country}         # Three-level chain
{city|country|Unknown}          # Chain ending with literal

# Threshold conditions (new)
{city?min=10}                   # City only if ≥10 photos there
{city?max=100}                  # City only if ≤100 photos there
{city?min=10|country}           # City if ≥10, else country

# Combined (new)
{city?min=5?max=50|country}     # City if 5-50 photos, else country
{district|city?min=10|country}  # District, then city (if ≥10), else country
```

---

## 2. Condition Types

### 2.1 Threshold Conditions

| Condition | Syntax | Description |
|-----------|--------|-------------|
| Minimum count | `?min=N` | Variable used only if ≥N photos match |
| Maximum count | `?max=N` | Variable used only if ≤N photos match |
| Range | `?min=N?max=M` | Variable used only if N ≤ photos ≤ M |

### 2.2 Future Extensible Conditions

Reserved for future implementation:

| Condition | Syntax | Description |
|-----------|--------|-------------|
| Contains | `?contains=pattern` | Variable value must contain pattern |
| Matches | `?matches=regex` | Variable value must match regex |
| Exists | `?exists` | Variable must have a non-empty value |
| Length | `?len>N`, `?len<N` | Variable value length constraints |

### 2.3 Scoping for Threshold Conditions

Thresholds are evaluated **per-unique-value** within the current copy operation:

```
Source files:
  - 15 photos in "Prague"
  - 3 photos in "Brno"
  - 8 photos in "Vienna"

Pattern: {country}/{city?min=10}/{name}{ext}

Result:
  - Prague photos → CZ/Prague/photo.jpg      (15 ≥ 10)
  - Brno photos   → CZ/photo.jpg             (3 < 10, city omitted)
  - Vienna photos → AT/photo.jpg             (8 < 10, city omitted)
```

---

## 3. Fallback Chain Semantics

### 3.1 Chain Resolution Order

```
{A|B|C|literal}
```

1. Evaluate `A` → if non-empty and conditions pass → use `A`
2. Evaluate `B` → if non-empty and conditions pass → use `B`
3. Evaluate `C` → if non-empty and conditions pass → use `C`
4. Use `literal` as final fallback

### 3.2 Variable vs Literal Detection

**Rule**: Known variable names are treated as variables; unknown names are literals.

| Token | Treatment | Reason |
|-------|-----------|--------|
| `city` | Variable | Known variable name |
| `country` | Variable | Known variable name |
| `Unknown` | Literal | Not a known variable |
| `NoLocation` | Literal | Not a known variable |

**Known Variables**: `year`, `month`, `day`, `name`, `namenoext`, `ext`, `directory`, `number`, `district`, `city`, `county`, `state`, `country`

### 3.3 Conditions on Fallback Variables

Conditions can be applied to any variable in the chain:

```
{district|city?min=5|country}
```

Interpretation:
1. Try `district` (no conditions)
2. If empty → try `city` only if it has ≥5 photos
3. If `city` empty or below threshold → use `country`

### 3.4 Propagating Conditions

```
{city?min=10|country?min=5|Unknown}
```

Interpretation:
1. Use `city` if ≥10 photos in that city
2. Else use `country` if ≥5 photos in that country
3. Else use literal "Unknown"

---

## 4. Edge Cases and Behavior

### 4.1 All Variables Below Threshold

```
Pattern: {city?min=100|country?min=100}
Photos: 5 in Prague, 3 in Vienna
```

**Behavior**: Both variables fail threshold → use empty string (cleaned by path normalizer)

**Recommendation**: Always end chains with a guaranteed fallback:
```
{city?min=100|country?min=100|Other}
```

### 4.2 Empty Variable Values

| Scenario | Behavior |
|----------|----------|
| Variable has no value | Move to next in chain |
| Variable fails condition | Move to next in chain |
| All fail, no literal | Use global `UnknownLocationFallback` |
| All fail, has literal | Use literal |

### 4.3 Circular Fallback Prevention

**Not applicable** - Fallback chains are linear, not references that could create cycles.

### 4.4 Non-Location Variables

Conditions only apply to **location variables** (`district`, `city`, `county`, `state`, `country`).

For date/file variables, only simple fallbacks are allowed:
```
{year}              # Valid
{name|unnamed}      # Valid - simple fallback
{year?min=10}       # Invalid - threshold meaningless for dates
```

---

## 5. Parsing Approach

### 5.1 Tokenizer-Based Parsing

Recommended over pure regex for maintainability and error reporting.

```csharp
public record VariableToken(
    string Name,
    List<Condition> Conditions,
    FallbackChain? Fallback
);

public record Condition(
    ConditionType Type,
    string? Value
);

public record FallbackChain(
    List<VariableToken> Variables,
    string? LiteralFallback
);
```

### 5.2 Grammar (EBNF)

```ebnf
pattern       = "{" variable_expr "}"
variable_expr = variable_ref { "|" fallback_item }
variable_ref  = identifier { condition }
condition     = "?" condition_name "=" value
fallback_item = variable_ref | literal
identifier    = letter { letter | digit }
literal       = { any_char - "}" - "|" }
value         = { digit } | quoted_string
```

### 5.3 Regex Pattern (for simple matching)

For initial implementation, regex can be used:

```csharp
// Match the full variable expression
private static readonly Regex VariablePattern = new(
    @"\{(?<expr>[^}]+)\}",
    RegexOptions.Compiled);

// Parse the expression content
private static readonly Regex ExpressionPattern = new(
    @"^(?<name>\w+)(?<conditions>(?:\?(?<cond>\w+)=(?<val>[^?|]+))*)(?:\|(?<fallback>.+))?$",
    RegexOptions.Compiled);

// Parse individual conditions
private static readonly Regex ConditionPattern = new(
    @"\?(?<name>\w+)=(?<value>[^?|]+)",
    RegexOptions.Compiled);

// Parse fallback chain
private static readonly Regex FallbackItemPattern = new(
    @"(?<name>\w+)(?<conditions>(?:\?(?<cond>\w+)=(?<val>[^?|]+))*)",
    RegexOptions.Compiled);
```

### 5.4 Parser Class Structure

```csharp
public class VariableExpressionParser
{
    private static readonly HashSet<string> KnownVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "year", "month", "day", "name", "namenoext", "ext", 
        "directory", "number", "district", "city", "county", "state", "country"
    };

    public ParsedVariable Parse(string expression)
    {
        // Tokenize and validate
    }

    public bool IsVariable(string token) => KnownVariables.Contains(token);
}
```

---

## 6. Two-Pass Processing Architecture

### 6.1 Why Two Passes?

Threshold conditions require **foreknowledge** of photo distribution:

```
Pass 1: Scan all files → build location statistics
Pass 2: Generate paths using statistics for condition evaluation
```

### 6.2 Statistics Collection

```csharp
public class LocationStatistics
{
    // Per-value counts for each variable
    public Dictionary<string, int> DistrictCounts { get; }
    public Dictionary<string, int> CityCounts { get; }
    public Dictionary<string, int> CountyCounts { get; }
    public Dictionary<string, int> StateCounts { get; }
    public Dictionary<string, int> CountryCounts { get; }

    // Combined location key → count (for multi-variable grouping)
    public Dictionary<string, int> LocationKeyCounts { get; }
}
```

### 6.3 Path Generation Flow

```
┌─────────────────┐
│  Scan Files     │
└────────┬────────┘
         ▼
┌─────────────────┐
│ Build Location  │
│   Statistics    │
└────────┬────────┘
         ▼
┌─────────────────┐
│ Parse Pattern   │
│  (once)         │
└────────┬────────┘
         ▼
┌─────────────────────────────────────────┐
│  For each file:                          │
│  1. Get file's location values           │
│  2. Evaluate conditions using stats      │
│  3. Resolve fallback chain               │
│  4. Generate final path                  │
└─────────────────────────────────────────┘
```

---

## 7. Configuration Schema

### 7.1 Inline Pattern Syntax (Recommended)

All conditions embedded in the destination pattern string - no additional configuration needed:

```yaml
destination: "{year}/{city?min=10|country}/{name}{ext}"
```

### 7.2 Alternative: Expanded Configuration (Optional)

For complex rules, an optional configuration section:

```yaml
destination: "{year}/{location}/{name}{ext}"

conditionalVariables:
  location:
    primary: city
    conditions:
      - type: min
        value: 10
    fallback:
      - variable: country
      - literal: "Unknown"
```

**Recommendation**: Start with inline syntax only. Add configuration section if users request more complex scenarios.

---

## 8. Example Patterns and Expected Behavior

### 8.1 Basic Variable Fallback

```
Pattern: {country}/{city|country}/{name}{ext}
```

| Location | City | Country | Result Path |
|----------|------|---------|-------------|
| Prague | Prague | CZ | `CZ/Prague/photo.jpg` |
| (no GPS) | null | null | `Unknown/Unknown/photo.jpg` |

### 8.2 Threshold with Fallback

```
Pattern: {year}/{city?min=5|country}/{name}{ext}

Files: 10 in Prague (CZ), 2 in Brno (CZ), 8 in Vienna (AT)
```

| File Location | Count | Condition | Result Path |
|---------------|-------|-----------|-------------|
| Prague | 10 | 10 ≥ 5 ✓ | `2024/Prague/photo.jpg` |
| Brno | 2 | 2 ≥ 5 ✗ | `2024/CZ/photo.jpg` |
| Vienna | 8 | 8 ≥ 5 ✓ | `2024/Vienna/photo.jpg` |

### 8.3 Multi-Level Chain

```
Pattern: {district|city|country|Unknown}/{name}{ext}
```

| District | City | Country | Result |
|----------|------|---------|--------|
| Troja | Prague | CZ | `Troja/photo.jpg` |
| null | Prague | CZ | `Prague/photo.jpg` |
| null | null | CZ | `CZ/photo.jpg` |
| null | null | null | `Unknown/photo.jpg` |

### 8.4 Complex Threshold Chain

```
Pattern: {year}/{city?min=10|state?min=20|country}/{name}{ext}

Files: 
- 15 in Prague (Central Bohemia, CZ)
- 5 in Brno (South Moravia, CZ)  
- 25 in South Moravia total
- 3 in Vienna (Vienna, AT)
```

| File | City Count | State Count | Result |
|------|------------|-------------|--------|
| Prague | 15 ≥ 10 ✓ | - | `2024/Prague/photo.jpg` |
| Brno | 5 < 10 ✗ | 25 ≥ 20 ✓ | `2024/South-Moravia/photo.jpg` |
| Vienna | 3 < 10 ✗ | 3 < 20 ✗ | `2024/AT/photo.jpg` |

### 8.5 Maximum Threshold (Prevent Over-Grouping)

```
Pattern: {year}/{city?max=50|country}/{name}{ext}

Purpose: If a city has too many photos, group by country instead
```

| City | Count | Condition | Result |
|------|-------|-----------|--------|
| Prague | 30 | 30 ≤ 50 ✓ | `2024/Prague/photo.jpg` |
| Tokyo | 500 | 500 ≤ 50 ✗ | `2024/JP/photo.jpg` |

---

## 9. Priority and Evaluation Order

### 9.1 Operator Precedence

1. **Condition evaluation** (`?condition`) - evaluated first
2. **Fallback chain** (`|fallback`) - evaluated left-to-right

### 9.2 Short-Circuit Evaluation

Fallback chain stops at first successful match:

```
{A|B|C}
```
- If `A` is non-empty and passes conditions → return `A`, don't evaluate `B` or `C`

### 9.3 Condition Binding

Conditions bind to the **immediately preceding variable**:

```
{city?min=10|country}     # min=10 applies to city only
{city|country?min=20}     # min=20 applies to country only
{city?min=10|country?min=5}  # Each has its own condition
```

---

## 10. Error Handling

### 10.1 Parse Errors

| Error | Example | Message |
|-------|---------|---------|
| Unknown condition | `{city?foo=5}` | "Unknown condition 'foo'. Valid: min, max" |
| Invalid value | `{city?min=abc}` | "Condition 'min' requires numeric value" |
| Empty variable name | `{?min=5}` | "Variable name required before conditions" |
| Unclosed brace | `{city` | "Unclosed variable expression" |

### 10.2 Runtime Warnings

| Warning | Scenario | Behavior |
|---------|----------|----------|
| All fallbacks exhausted | Every option fails | Use global fallback, log warning |
| Threshold on non-location | `{year?min=5}` | Ignore condition, log warning |
| Condition value out of range | `?min=-5` | Treat as 0, log warning |

---

## 11. Implementation Phases

### Phase 1: Variable Fallback Chains
- Parse `{A|B|C}` syntax
- Distinguish variables from literals
- Implement chain resolution

### Phase 2: Threshold Conditions
- Add statistics collection pass
- Parse `?min=N` and `?max=N`
- Implement condition evaluation

### Phase 3: Extended Conditions (Future)
- `?contains=pattern`
- `?matches=regex`
- `?exists`

---

## 12. Backwards Compatibility

| Existing Pattern | New Behavior |
|------------------|--------------|
| `{city}` | Unchanged |
| `{city\|Unknown}` | Unchanged - "Unknown" is not a variable name |
| `{city\|NoLocation}` | Unchanged - "NoLocation" is not a variable name |
| `{city\|country}` | **NEW**: Falls back to country variable |

**Breaking Change Risk**: Low. Only affects patterns where fallback text happens to match a variable name exactly (unlikely: "city", "country", etc. are unusual fallback texts).

---

## 13. Appendix: Full Grammar

```ebnf
(* Top-level pattern matching *)
destination_pattern = { literal_text | variable_expression } ;

variable_expression = "{" variable_chain "}" ;

variable_chain = variable_ref { "|" chain_item } ;

chain_item = variable_ref | literal_fallback ;

variable_ref = identifier { condition } ;

condition = "?" condition_name "=" condition_value ;

condition_name = "min" | "max" | "contains" | "matches" | "exists" ;

condition_value = integer | quoted_string | unquoted_value ;

identifier = letter { letter | digit | "_" } ;

literal_fallback = { printable_char - "}" - "|" } ;

(* Terminals *)
letter = "a" | ... | "z" | "A" | ... | "Z" ;
digit = "0" | ... | "9" ;
integer = digit { digit } ;
quoted_string = '"' { any_char - '"' } '"' ;
unquoted_value = { printable_char - "?" - "|" - "}" } ;
```
