# Two-Pass Processing Architecture for Conditional Variable Support

## Executive Summary

This document details the architecture for implementing conditional variable support (e.g., `{city?min=10|country}`) in PhotoCopy. The core challenge is that threshold conditions require **foreknowledge** of photo distribution—we must know "there are 15 photos in Prague" before deciding whether to use `Prague` or fall back to `CZ`.

---

## 1. Current Architecture Analysis

### 1.1 Current Flow

```
┌─────────────────────┐    ┌─────────────────────┐    ┌─────────────────────┐
│  IFileSystem.       │───►│  DirectoryCopier.   │───►│     Execute         │
│  EnumerateFiles()   │    │  BuildCopyPlan()    │    │                     │
└─────────────────────┘    └─────────────────────┘    └─────────────────────┘
         │                          │
         ▼                          ▼
   IFile objects with         GeneratePath(file)
   Location data              (per-file, no context)
```

### 1.2 Key Classes and Responsibilities

| Class | Responsibility | Data Flow |
|-------|----------------|-----------|
| `IFileSystem` | Abstracts file operations, delegates scanning to `IDirectoryScanner` | Returns `IEnumerable<IFile>` |
| `DirectoryScanner` | Enumerates files, creates `IFile` objects via `IFileFactory` | Files with metadata + location |
| `DirectoryCopierBase` | Base class with `GeneratePath()` and shared logic | Per-file path generation |
| `DirectoryCopier` | Sync implementation with `BuildCopyPlan()` and `ExecutePlan()` | Sequential processing |
| `DirectoryCopierAsync` | Async implementation with parallel execution | Parallel processing |

### 1.3 Current `GeneratePath()` Limitations

```csharp
public string GeneratePath(IFile file)
{
    // Current: Only has access to THIS file's data
    // Cannot answer: "How many other files are in this city?"
    var city = file.Location?.City;  // ✓ Has this
    var cityPhotoCount = ???;         // ✗ Missing this
}
```

---

## 2. Proposed Architecture: Option C - Deferred Path Generation

### 2.1 Architecture Options Comparison

| Option | Description | Pros | Cons |
|--------|-------------|------|------|
| **A: Scanner Collects Stats** | Scanner returns `(files, stats)` tuple | Single pass | Scanner knows nothing about patterns; may collect unused stats |
| **B: Separate Stats Phase** | Explicit stats collection phase | Clear separation | Extra iteration over files |
| **C: Deferred Path Generation** | Collect all files, aggregate stats, then generate paths | Optimal—stats collected during enumeration, paths generated after | Requires holding all files in memory |

### 2.2 Recommended: Option C - Deferred Path Generation with Lazy Stats

**Justification:**
1. Files are already fully enumerated before `BuildCopyPlan()` (see `DirectoryCopierAsync.BuildPlanAsync()`)
2. Statistics can be collected as a side effect of enumeration (single pass)
3. Stats only computed when pattern contains conditions (lazy/on-demand)
4. Minimal interface changes; backward compatible
5. Memory-efficient: stats are aggregated counters, not file duplicates

### 2.3 New Flow

```
┌─────────────────────┐
│  Scan Files         │
│  (enumerate all)    │
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│  Collect Stats      │  ← Only if pattern has conditions
│  (single iteration) │
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│  Build Copy Plan    │
│  (with stats ctx)   │
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│  Execute            │
└─────────────────────┘
```

---

## 3. Data Structures

### 3.1 LocationStatistics Class

```csharp
namespace PhotoCopy.Statistics;

/// <summary>
/// Holds aggregated photo counts per location value.
/// Thread-safe for parallel enumeration scenarios.
/// </summary>
public sealed class LocationStatistics
{
    // Per-value counts for each location level
    private readonly ConcurrentDictionary<string, int> _districtCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _cityCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _countyCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _stateCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _countryCounts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the photo count for a specific location variable value.
    /// </summary>
    /// <param name="variable">Variable name: district, city, county, state, country</param>
    /// <param name="value">The location value (e.g., "Prague", "CZ")</param>
    /// <returns>Number of photos with this location value, or 0 if not found</returns>
    public int GetCount(string variable, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        return variable.ToLowerInvariant() switch
        {
            "district" => _districtCounts.GetValueOrDefault(value, 0),
            "city" => _cityCounts.GetValueOrDefault(value, 0),
            "county" => _countyCounts.GetValueOrDefault(value, 0),
            "state" => _stateCounts.GetValueOrDefault(value, 0),
            "country" => _countryCounts.GetValueOrDefault(value, 0),
            _ => 0
        };
    }

    /// <summary>
    /// Records a file's location data, incrementing all applicable counters.
    /// Thread-safe for parallel processing.
    /// </summary>
    public void RecordLocation(LocationData? location)
    {
        if (location is null)
            return;

        IncrementIfNotEmpty(_districtCounts, location.District);
        IncrementIfNotEmpty(_cityCounts, location.City);
        IncrementIfNotEmpty(_countyCounts, location.County);
        IncrementIfNotEmpty(_stateCounts, location.State);
        IncrementIfNotEmpty(_countryCounts, location.Country);
    }

    private static void IncrementIfNotEmpty(ConcurrentDictionary<string, int> dict, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            dict.AddOrUpdate(value, 1, (_, count) => count + 1);
        }
    }

    /// <summary>
    /// Creates an empty statistics instance.
    /// </summary>
    public static LocationStatistics Empty => new();
}
```

### 3.2 Memory Efficiency Analysis

For a collection of 100,000 photos:

| Scenario | Memory Impact |
|----------|---------------|
| Unique cities: ~500 | ~500 × (string + int) ≈ 20 KB |
| Unique countries: ~50 | ~50 × (string + int) ≈ 2 KB |
| Total stats overhead | **< 100 KB** |

**Conclusion:** Statistics overhead is negligible compared to file metadata.

---

## 4. Interface Changes

### 4.1 New Interface: IStatisticsCollector

```csharp
namespace PhotoCopy.Statistics;

/// <summary>
/// Collects statistics from enumerated files.
/// </summary>
public interface IStatisticsCollector
{
    /// <summary>
    /// Builds statistics from the provided files.
    /// </summary>
    /// <param name="files">Files to analyze</param>
    /// <returns>Aggregated location statistics</returns>
    LocationStatistics Collect(IEnumerable<IFile> files);
}
```

### 4.2 New Interface: IPathGeneratorContext

```csharp
namespace PhotoCopy.Configuration;

/// <summary>
/// Provides context for path generation, including statistics for conditional evaluation.
/// </summary>
public interface IPathGeneratorContext
{
    /// <summary>
    /// Gets the location statistics for the current operation.
    /// May be empty if no conditional variables are used.
    /// </summary>
    LocationStatistics Statistics { get; }

    /// <summary>
    /// Evaluates whether a condition passes for a given variable value.
    /// </summary>
    /// <param name="variable">Variable name (city, country, etc.)</param>
    /// <param name="value">The value to check</param>
    /// <param name="conditions">Conditions to evaluate (min, max, etc.)</param>
    /// <returns>True if all conditions pass</returns>
    bool EvaluateConditions(string variable, string? value, IReadOnlyList<Condition> conditions);
}
```

### 4.3 Updated GeneratePath Signature

```csharp
// Before (in DirectoryCopierBase)
public string GeneratePath(IFile file)

// After - add optional context parameter with default
public string GeneratePath(IFile file, IPathGeneratorContext? context = null)
```

**Backward Compatibility:** Default `null` context means conditions are ignored (existing behavior preserved).

### 4.4 Updated IDirectoryCopier Interface

```csharp
namespace PhotoCopy.Directories;

public interface IDirectoryCopier
{
    CopyResult Copy(IReadOnlyCollection<IValidator> validators);
    
    // Existing - kept for backward compatibility
    string GeneratePath(IFile file);
    
    // New - with statistics context
    string GeneratePath(IFile file, IPathGeneratorContext context);
}
```

---

## 5. Implementation Classes

### 5.1 PathGeneratorContext Implementation

```csharp
namespace PhotoCopy.Configuration;

public sealed class PathGeneratorContext : IPathGeneratorContext
{
    public LocationStatistics Statistics { get; }

    public PathGeneratorContext(LocationStatistics statistics)
    {
        Statistics = statistics ?? LocationStatistics.Empty;
    }

    public bool EvaluateConditions(string variable, string? value, IReadOnlyList<Condition> conditions)
    {
        if (conditions.Count == 0)
            return true;

        var count = Statistics.GetCount(variable, value);

        foreach (var condition in conditions)
        {
            bool passes = condition.Type switch
            {
                ConditionType.Min => count >= condition.IntValue,
                ConditionType.Max => count <= condition.IntValue,
                _ => true // Unknown conditions pass (future-proofing)
            };

            if (!passes)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Creates a context with empty statistics (no conditional evaluation).
    /// </summary>
    public static PathGeneratorContext Empty => new(LocationStatistics.Empty);
}
```

### 5.2 StatisticsCollector Implementation

```csharp
namespace PhotoCopy.Statistics;

public sealed class StatisticsCollector : IStatisticsCollector
{
    public LocationStatistics Collect(IEnumerable<IFile> files)
    {
        var stats = new LocationStatistics();

        foreach (var file in files)
        {
            stats.RecordLocation(file.Location);
        }

        return stats;
    }
}
```

### 5.3 Optional: Pattern Analyzer for Lazy Stats

```csharp
namespace PhotoCopy.Configuration;

/// <summary>
/// Analyzes destination patterns to determine if statistics collection is needed.
/// </summary>
public sealed class PatternAnalyzer
{
    private static readonly Regex ConditionalPattern = new(
        @"\{(?<name>\w+)\?(?:min|max)=\d+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Determines if the pattern contains any threshold conditions.
    /// </summary>
    /// <param name="pattern">Destination pattern to analyze</param>
    /// <returns>True if statistics collection is required</returns>
    public bool RequiresStatistics(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return false;

        return ConditionalPattern.IsMatch(pattern);
    }

    /// <summary>
    /// Extracts which location variables have conditions.
    /// Used for optimized statistics collection.
    /// </summary>
    public IReadOnlySet<string> GetConditionalVariables(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return new HashSet<string>();

        var matches = ConditionalPattern.Matches(pattern);
        return matches
            .Cast<Match>()
            .Select(m => m.Groups["name"].Value.ToLowerInvariant())
            .ToHashSet();
    }
}
```

---

## 6. Updated BuildCopyPlan Flow

### 6.1 DirectoryCopierBase Changes

```csharp
public abstract class DirectoryCopierBase
{
    private readonly IStatisticsCollector _statisticsCollector;
    private readonly PatternAnalyzer _patternAnalyzer;

    // Add to constructor
    protected DirectoryCopierBase(
        IFileSystem fileSystem,
        IOptions<PhotoCopyConfig> options,
        ITransactionLogger transactionLogger,
        IFileValidationService fileValidationService,
        IStatisticsCollector? statisticsCollector = null)  // Optional, defaults to no-op
    {
        // ... existing code ...
        _statisticsCollector = statisticsCollector ?? new StatisticsCollector();
        _patternAnalyzer = new PatternAnalyzer();
    }

    /// <summary>
    /// Generates the destination path for a file using the configured pattern.
    /// </summary>
    /// <param name="file">The file to generate a path for</param>
    /// <param name="context">Optional context with statistics for conditional evaluation</param>
    public string GeneratePath(IFile file, IPathGeneratorContext? context = null)
    {
        var destPath = Config.Destination ?? string.Empty;
        var casing = Config.PathCasing;

        // Date variables (unchanged)
        destPath = destPath.Replace(DestinationVariables.Year, file.FileDateTime.DateTime.Year.ToString());
        destPath = destPath.Replace(DestinationVariables.Month, file.FileDateTime.DateTime.Month.ToString("00"));
        destPath = destPath.Replace(DestinationVariables.Day, file.FileDateTime.DateTime.Day.ToString("00"));

        // Location variables with conditional support
        if (file.Location != null)
        {
            destPath = ReplaceLocationVariablesWithConditions(destPath, file.Location, context, casing);
        }
        else
        {
            destPath = ReplaceLocationVariablesWithFallback(destPath, casing);
        }

        // File variables (unchanged)
        // ... existing code ...

        return NormalizeDestinationPath(destPath);
    }

    /// <summary>
    /// Replaces location variables, evaluating conditions if context is provided.
    /// </summary>
    private string ReplaceLocationVariablesWithConditions(
        string path,
        LocationData location,
        IPathGeneratorContext? context,
        PathCasing casing)
    {
        // Parse and resolve each variable expression with fallback chains
        // This is where the new parser integrates
        // See Section 7 for detailed implementation
    }
}
```

### 6.2 DirectoryCopier.BuildCopyPlan Changes

```csharp
private CopyPlan BuildCopyPlan(IEnumerable<IFile> files, IReadOnlyCollection<IValidator> validators)
{
    var fileList = files.ToList();  // Materialize once
    
    // NEW: Collect statistics if pattern has conditions
    IPathGeneratorContext context = PathGeneratorContext.Empty;
    if (_patternAnalyzer.RequiresStatistics(Config.Destination))
    {
        var stats = _statisticsCollector.Collect(fileList);
        context = new PathGeneratorContext(stats);
        _logger.LogDebug("Collected location statistics: {Cities} cities, {Countries} countries",
            stats.UniqueCityCount, stats.UniqueCountryCount);
    }

    var operations = new List<FileCopyPlan>();
    var skipped = new List<ValidationFailure>();
    var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    long totalBytes = 0;

    foreach (var file in fileList)
    {
        // ... validation logic unchanged ...

        // CHANGED: Pass context to GeneratePath
        var destinationPath = GeneratePath(file, context);
        
        // ... rest unchanged ...
    }

    return new CopyPlan(operations, skipped, directories, totalBytes);
}
```

---

## 7. Integration with Variable Expression Parser

### 7.1 Parser Integration Point

The `ReplaceLocationVariablesWithConditions` method uses the new parser:

```csharp
private readonly VariableExpressionParser _parser = new();

private string ReplaceLocationVariablesWithConditions(
    string path,
    LocationData location,
    IPathGeneratorContext? context,
    PathCasing casing)
{
    // Match all variable expressions in the path
    return VariablePattern.Replace(path, match =>
    {
        var expression = match.Groups["expr"].Value;
        var parsed = _parser.Parse(expression);

        // Get the location value for this variable
        var value = GetLocationValue(parsed.Name, location);

        // If no context or no conditions, use simple logic
        if (context is null || parsed.Conditions.Count == 0)
        {
            return ResolveValueOrFallback(parsed, value, location, casing);
        }

        // Evaluate conditions
        if (!string.IsNullOrEmpty(value) && 
            context.EvaluateConditions(parsed.Name, value, parsed.Conditions))
        {
            return ApplyCasing(value, casing);
        }

        // Try fallback chain
        return ResolveFallbackChain(parsed.Fallback, location, context, casing);
    });
}

private string ResolveFallbackChain(
    FallbackChain? fallback,
    LocationData location,
    IPathGeneratorContext context,
    PathCasing casing)
{
    if (fallback is null)
        return Config.UnknownLocationFallback;

    foreach (var item in fallback.Variables)
    {
        if (item.IsLiteral)
            return ApplyCasing(item.Name, casing);

        var value = GetLocationValue(item.Name, location);
        if (!string.IsNullOrEmpty(value) &&
            context.EvaluateConditions(item.Name, value, item.Conditions))
        {
            return ApplyCasing(value, casing);
        }
    }

    return fallback.LiteralFallback ?? Config.UnknownLocationFallback;
}

private static string? GetLocationValue(string variable, LocationData location)
{
    return variable.ToLowerInvariant() switch
    {
        "district" => location.District,
        "city" => location.City,
        "county" => location.County,
        "state" => location.State,
        "country" => location.Country,
        _ => null
    };
}
```

---

## 8. Class Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           PhotoCopy.Statistics                           │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌───────────────────────┐         ┌──────────────────────────────────┐ │
│  │ IStatisticsCollector  │         │ LocationStatistics               │ │
│  ├───────────────────────┤         ├──────────────────────────────────┤ │
│  │ + Collect(files)      │────────►│ - _districtCounts: ConcurrentDict│ │
│  └───────────────────────┘         │ - _cityCounts: ConcurrentDict    │ │
│           ▲                        │ - _countyCounts: ConcurrentDict  │ │
│           │                        │ - _stateCounts: ConcurrentDict   │ │
│  ┌────────┴──────────────┐         │ - _countryCounts: ConcurrentDict │ │
│  │ StatisticsCollector   │         │ + GetCount(variable, value)      │ │
│  └───────────────────────┘         │ + RecordLocation(location)       │ │
│                                    └──────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│                        PhotoCopy.Configuration                           │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌──────────────────────────────┐    ┌───────────────────────────────┐  │
│  │ IPathGeneratorContext        │    │ PatternAnalyzer               │  │
│  ├──────────────────────────────┤    ├───────────────────────────────┤  │
│  │ + Statistics: LocationStats  │    │ + RequiresStatistics(pattern) │  │
│  │ + EvaluateConditions(...)    │    │ + GetConditionalVariables()   │  │
│  └──────────────────────────────┘    └───────────────────────────────┘  │
│           ▲                                                              │
│           │                                                              │
│  ┌────────┴─────────────────────┐                                        │
│  │ PathGeneratorContext         │                                        │
│  ├──────────────────────────────┤                                        │
│  │ + Statistics: LocationStats  │                                        │
│  │ + EvaluateConditions(...)    │                                        │
│  │ + Empty: PathGeneratorContext│                                        │
│  └──────────────────────────────┘                                        │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│                          PhotoCopy.Directories                           │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │ DirectoryCopierBase                                                 │ │
│  ├────────────────────────────────────────────────────────────────────┤ │
│  │ - _statisticsCollector: IStatisticsCollector                        │ │
│  │ - _patternAnalyzer: PatternAnalyzer                                 │ │
│  │ + GeneratePath(file): string                    # Backward compat   │ │
│  │ + GeneratePath(file, context): string           # New with stats    │ │
│  └────────────────────────────────────────────────────────────────────┘ │
│           ▲                                                              │
│           │                                                              │
│  ┌────────┴───────────────┐    ┌────────────────────────────────────┐   │
│  │ DirectoryCopier        │    │ DirectoryCopierAsync               │   │
│  ├────────────────────────┤    ├────────────────────────────────────┤   │
│  │ + BuildCopyPlan(...)   │    │ + BuildPlanAsync(...)              │   │
│  │   - Collects stats     │    │   - Collects stats                 │   │
│  │   - Passes context     │    │   - Passes context                 │   │
│  └────────────────────────┘    └────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 9. Migration Path

### Phase 1: Foundation (Non-Breaking)

1. **Add `LocationStatistics` class** - standalone, no dependencies
2. **Add `IStatisticsCollector` interface and implementation** - standalone
3. **Add `IPathGeneratorContext` interface and implementation** - standalone
4. **Add `PatternAnalyzer`** - standalone

**Test:** Unit tests for each new class in isolation.

### Phase 2: Integration (Non-Breaking)

1. **Add optional `context` parameter to `GeneratePath()`** with default `null`
2. **Update `DirectoryCopierBase` constructor** to accept optional `IStatisticsCollector`
3. **Update DI registration** to wire up new dependencies

**Test:** Existing tests continue to pass (no context = existing behavior).

### Phase 3: Statistics Collection (Non-Breaking)

1. **Modify `BuildCopyPlan()` methods** to:
   - Check if pattern requires statistics
   - Collect statistics if needed
   - Pass context to `GeneratePath()`

**Test:** Test with patterns that have no conditions → same behavior as before.

### Phase 4: Parser Integration

1. **Implement `VariableExpressionParser`** (from conditional-variables.md)
2. **Update `GeneratePath()`** to use parser for conditional logic
3. **Handle fallback chains**

**Test:** Full integration tests with conditional patterns.

### Phase 5: Optimization (Optional)

1. **Selective statistics collection** - only collect for variables with conditions
2. **Parallel statistics collection** - use Parallel.ForEach
3. **Streaming statistics** - collect during initial file enumeration

---

## 10. Performance Considerations

### 10.1 Single-Pass Statistics Collection

Since files are already fully enumerated before `BuildCopyPlan()`:

```csharp
// Current (files already materialized)
var files = FileSystem.EnumerateFiles(Config.Source).ToList();  // Already happens

// Adding stats collection = iterating the list once more
var stats = _statisticsCollector.Collect(files);  // O(n) iteration
```

**Cost:** One additional O(n) iteration for statistics collection.

### 10.2 Lazy Statistics

When pattern has no conditions, skip statistics entirely:

```csharp
if (!_patternAnalyzer.RequiresStatistics(Config.Destination))
{
    // No stats needed - use empty context
    context = PathGeneratorContext.Empty;
}
```

**Benefit:** Zero overhead for users not using conditional features.

### 10.3 Thread Safety

`LocationStatistics` uses `ConcurrentDictionary` for thread-safe increments:

```csharp
// Safe for parallel enumeration
Parallel.ForEach(files, file => stats.RecordLocation(file.Location));
```

---

## 11. Testing Strategy

### 11.1 Unit Tests

```csharp
// LocationStatistics
[Fact]
public void RecordLocation_IncrementsCorrectCounters()
{
    var stats = new LocationStatistics();
    stats.RecordLocation(new LocationData("District1", "Prague", null, null, "CZ"));
    stats.RecordLocation(new LocationData("District2", "Prague", null, null, "CZ"));
    stats.RecordLocation(new LocationData("District3", "Vienna", null, null, "AT"));
    
    Assert.Equal(2, stats.GetCount("city", "Prague"));
    Assert.Equal(1, stats.GetCount("city", "Vienna"));
    Assert.Equal(2, stats.GetCount("country", "CZ"));
}

// PathGeneratorContext
[Fact]
public void EvaluateConditions_MinThreshold_ReturnsCorrectResult()
{
    var stats = new LocationStatistics();
    for (int i = 0; i < 15; i++)
        stats.RecordLocation(new LocationData("D", "Prague", null, null, "CZ"));
    
    var context = new PathGeneratorContext(stats);
    var conditions = new List<Condition> { new(ConditionType.Min, "10") };
    
    Assert.True(context.EvaluateConditions("city", "Prague", conditions));  // 15 >= 10
    Assert.False(context.EvaluateConditions("city", "Brno", conditions));    // 0 >= 10
}
```

### 11.2 Integration Tests

```csharp
[Fact]
public void BuildCopyPlan_WithMinThreshold_UsesCorrectFallback()
{
    // Setup: 15 photos in Prague, 3 in Brno
    var files = CreateTestFiles(
        ("Prague", 15),
        ("Brno", 3)
    );
    
    var config = new PhotoCopyConfig
    {
        Destination = @"C:\Photos\{year}\{city?min=10|country}\{name}{ext}"
    };
    
    var copier = CreateCopier(config);
    var plan = copier.BuildCopyPlan(files, validators);
    
    // Prague photos → .../Prague/...
    // Brno photos → .../CZ/...
    Assert.Contains(plan.Operations, op => op.DestinationPath.Contains("Prague"));
    Assert.DoesNotContain(plan.Operations, op => op.DestinationPath.Contains("Brno"));
}
```

---

## 12. Summary

| Aspect | Recommendation |
|--------|----------------|
| **Architecture** | Option C: Deferred Path Generation with Lazy Stats |
| **Stats Collection** | After file enumeration, before path generation |
| **Memory** | Negligible (~100 KB for typical workloads) |
| **Performance** | Zero overhead when no conditions; O(n) when conditions used |
| **Backward Compatibility** | Full - optional context parameter |
| **Interface Changes** | Additive only - new optional parameters and interfaces |
| **Implementation Order** | Foundation → Integration → Collection → Parser → Optimize |

This architecture enables PhotoCopy to support complex conditional patterns like `{city?min=10|country}` while maintaining full backward compatibility and minimal performance impact for users not using the feature.
