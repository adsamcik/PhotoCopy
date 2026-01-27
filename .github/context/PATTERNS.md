# Patterns and Conventions

## Coding Patterns

### Null Validation

Use `ArgumentNullException.ThrowIfNull()` at constructor start:

```csharp
public MyService(
    ILogger<MyService> logger,
    IFileSystem fileSystem,
    IOptions<PhotoCopyConfig> options)
{
    ArgumentNullException.ThrowIfNull(logger);
    ArgumentNullException.ThrowIfNull(fileSystem);
    ArgumentNullException.ThrowIfNull(options);

    _logger = logger;
    _fileSystem = fileSystem;
    _config = options.Value;
}
```

### Options Pattern

Configuration injected via `IOptions<T>`:

```csharp
private readonly PhotoCopyConfig _config;

public MyService(IOptions<PhotoCopyConfig> options)
{
    _config = options.Value;
}
```

### Async Method Naming

Always suffix async methods with `Async`:

```csharp
public async Task<Result> ProcessAsync(CancellationToken cancellationToken = default)
{
    // ...
}
```

### CancellationToken

Accept optional cancellation token, default to `default`:

```csharp
public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();
    // ...
}
```

### Interface Extraction

Services with I/O or testability needs have interfaces:

```csharp
// In Abstractions/ or local to component
public interface IMyService
{
    Task<Result> DoWorkAsync(CancellationToken cancellationToken = default);
}

// Implementation
public class MyService : IMyService
{
    // ...
}
```

## Naming Conventions

### Files and Types

| Type | Pattern | Example |
|------|---------|---------|
| Interface | `I{Name}` | `IFileSystem`, `IValidator` |
| Implementation | `{Name}` | `FileSystem`, `MinDateValidator` |
| Test class | `{ClassUnderTest}Tests` | `CopyCommandTests` |
| Factory | `{Product}Factory` | `FileFactory`, `ValidatorFactory` |
| Options | `{Command}Options` | `CopyOptions`, `ScanOptions` |

### Methods

| Type | Pattern | Example |
|------|---------|---------|
| Async methods | `{Verb}Async` | `CopyAsync`, `InitializeAsync` |
| Factory methods | `Create`, `Build` | `Create()`, `Build()` |
| Boolean getters | `Is{State}`, `Has{Feature}` | `IsValid`, `HasLocation` |
| Try pattern | `Try{Verb}` | `TryParse`, `TryGetValue` |

### Fields

```csharp
private readonly ILogger<MyService> _logger;      // Private readonly
private int _count;                                // Private mutable
public const int MaxRetries = 3;                  // Constants PascalCase
```

## Test Patterns

### Test Method Naming

```
{MethodName}_{Scenario}_{ExpectedResult}
```

Examples:
- `ExecuteAsync_WithValidInput_ReturnsZero`
- `Copy_WhenFileExists_SkipsFile`
- `Parse_InvalidFormat_ThrowsFormatException`

### Test Structure (Arrange-Act-Assert)

```csharp
[Test]
public async Task ExecuteAsync_WithValidInput_ReturnsSuccess()
{
    // Arrange
    var mockService = Substitute.For<IMyService>();
    mockService.DoWork().Returns("result");
    var sut = new MyCommand(mockService);

    // Act
    var result = await sut.ExecuteAsync();

    // Assert
    result.Should().Be((int)ExitCode.Success);
    await mockService.Received(1).DoWork();
}
```

### TUnit Attributes

```csharp
[Test]                          // Basic test
[Before(Test)]                  // Setup before each test
[After(Test)]                   // Cleanup after each test
[NotInParallel]                 // Disable parallel execution
[Arguments("a", 1)]             // Parameterized test
[Arguments("b", 2)]
```

### Mocking with NSubstitute

```csharp
// Create mock
var fileSystem = Substitute.For<IFileSystem>();

// Setup returns
fileSystem.FileExists(Arg.Any<string>()).Returns(true);
fileSystem.EnumerateFiles(Arg.Any<string>()).Returns(files);

// Setup async
service.ProcessAsync(Arg.Any<CancellationToken>())
    .Returns(Task.FromResult(result));

// Verify calls
fileSystem.Received(1).CopyFile(source, dest);
await service.Received().ProcessAsync(Arg.Any<CancellationToken>());
```

### Assertions with AwesomeAssertions

```csharp
// Equality
result.Should().Be(expected);
result.Should().NotBe(unexpected);

// Boolean
flag.Should().BeTrue();
flag.Should().BeFalse();

// Collections
list.Should().HaveCount(3);
list.Should().ContainSingle();
list.Should().BeEmpty();
list.Should().Contain(item);

// Null
obj.Should().BeNull();
obj.Should().NotBeNull();

// Exceptions
await Assert.ThrowsAsync<ArgumentException>(
    () => sut.InvalidMethodAsync());
```

## Error Handling Patterns

### Command Error Handling

```csharp
public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
{
    try
    {
        // Main logic
        return (int)ExitCode.Success;
    }
    catch (OperationCanceledException)
    {
        _logger.LogWarning("Operation was cancelled");
        return (int)ExitCode.Cancelled;
    }
    catch (UnauthorizedAccessException ex)
    {
        _logger.LogError(ex, "Permission denied");
        return (int)ExitCode.IOError;
    }
    catch (IOException ex)
    {
        _logger.LogError(ex, "I/O error occurred");
        return (int)ExitCode.IOError;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error");
        return (int)ExitCode.Error;
    }
}
```

### Result Aggregation

For operations that can partially fail:

```csharp
public record CopyResult(
    int FilesProcessed,
    int FilesFailed,
    int FilesSkipped,
    long BytesProcessed,
    IReadOnlyList<CopyError> Errors);
```

## Configuration Patterns

### Adding Configuration Properties

1. Add to `PhotoCopyConfig.cs`:

```csharp
/// <summary>
/// Description of what this setting does.
/// </summary>
public bool MyNewSetting { get; set; } = false;
```

2. Add to `appsettings.yaml`:

```yaml
photoCopy:
  # Description of what this setting does.
  # More details if needed.
  myNewSetting: false
```

3. If CLI argument needed, add to `CommandOptions.cs`:

```csharp
[Option("my-new-setting", HelpText = "Description")]
public bool? MyNewSetting { get; set; }
```

### Configuration Layering

Priority (highest to lowest):
1. Command-line arguments
2. Environment variables (`PHOTOCOPY_*`)
3. `appsettings.yaml`
4. `appsettings.json`
5. Default values in code

## Documentation Patterns

### XML Documentation

```csharp
/// <summary>
/// Brief description of the type or member.
/// </summary>
/// <remarks>
/// Additional details, usage notes, or examples.
/// </remarks>
/// <param name="source">Description of parameter.</param>
/// <returns>Description of return value.</returns>
/// <exception cref="ArgumentNullException">
/// Thrown when <paramref name="source"/> is null.
/// </exception>
public async Task<CopyResult> CopyAsync(string source)
```

### Inline Comments

```csharp
// Explains WHY, not WHAT
// Complex algorithms or non-obvious business logic should be documented
```
