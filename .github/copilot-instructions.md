# Copilot Instructions

## About This Project

PhotoCopy is a .NET 10 CLI tool for batch organizing photos by metadata. It reads EXIF data, performs reverse geocoding, and copies/moves files to structured destination paths using variable templating.

## Code Style

- C# 14 with nullable reference types enabled
- File-scoped namespaces
- XML documentation comments on public APIs
- `ArgumentNullException.ThrowIfNull()` for null parameter validation
- PascalCase for public members, _camelCase for private fields
- Async methods suffixed with `Async`

## Patterns to Follow

### Dependency Injection

All services use constructor injection. Register in `ServiceCollectionExtensions.cs`:

```csharp
services.AddSingleton<IMyService, MyService>();
// or
services.AddTransient<IMyService, MyService>();
```

### Interface-Based Design

Always create an interface for services that have external dependencies or need mocking:

```csharp
public interface IMyService
{
    Task<Result> DoWorkAsync(CancellationToken cancellationToken = default);
}

public class MyService : IMyService
{
    private readonly ILogger<MyService> _logger;
    
    public MyService(ILogger<MyService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }
    
    public async Task<Result> DoWorkAsync(CancellationToken cancellationToken = default)
    {
        // Implementation
    }
}
```

### Command Pattern

CLI commands implement `ICommand`:

```csharp
public class MyCommand : ICommand
{
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        // Return ExitCode enum value
        return (int)ExitCode.Success;
    }
}
```

### Configuration

Configuration uses the Options pattern with `PhotoCopyConfig`:

```csharp
public MyService(IOptions<PhotoCopyConfig> options)
{
    _config = options.Value;
}
```

Add new settings to both:
1. `PhotoCopyConfig.cs` (property with XML doc)
2. `appsettings.yaml` (with explanatory comments)

## Testing Conventions

- Test files location: `PhotoCopy.Tests/[ComponentFolder]/[ClassName]Tests.cs`
- Testing framework: **TUnit** (not xUnit)
- Assertions: **AwesomeAssertions** (`Should().Be()`, `Should().BeTrue()`)
- Mocking: **NSubstitute**

### Test Structure

```csharp
using AwesomeAssertions;
using NSubstitute;

namespace PhotoCopy.Tests.MyComponent;

public class MyServiceTests
{
    private readonly IMyDependency _dependency;
    private readonly MyService _sut;

    public MyServiceTests()
    {
        _dependency = Substitute.For<IMyDependency>();
        _sut = new MyService(_dependency);
    }

    [Before(Test)]
    public void BeforeEachTest()
    {
        SharedLogs.Clear(); // If using FakeLogger
    }

    [Test]
    public async Task MethodName_Scenario_ExpectedResult()
    {
        // Arrange
        _dependency.DoWork().Returns("expected");

        // Act
        var result = await _sut.MyMethodAsync();

        // Assert
        result.Should().Be("expected");
    }
}
```

### Test Attributes

| Attribute | Usage |
|-----------|-------|
| `[Test]` | Mark a test method |
| `[Before(Test)]` | Run before each test |
| `[After(Test)]` | Run after each test |
| `[NotInParallel]` | Prevent parallel execution |
| `[Arguments(...)]` | Parameterized tests |

## File Organization

```
PhotoCopy/
├── Abstractions/       # Cross-cutting interfaces
├── Commands/           # CLI command implementations
├── Configuration/      # Config loading and models
├── Directories/        # Directory operations
├── Files/              # File abstraction and metadata
│   ├── Geo/           # Geocoding services
│   ├── Metadata/      # Metadata enrichment
│   └── Sidecar/       # Sidecar file handling
├── Hosting/            # DI and startup
├── Validators/         # Validation logic
└── ...

PhotoCopy.Tests/
├── [Same structure as main project]
└── TestingImplementation/  # Test doubles and helpers
```

## Naming Conventions

| Type | Convention | Example |
|------|------------|---------|
| Interfaces | `I` prefix | `IFileSystem`, `IDirectoryCopier` |
| Async methods | `Async` suffix | `CopyAsync`, `InitializeAsync` |
| Test classes | `Tests` suffix | `CopyCommandTests` |
| Test methods | `Method_Scenario_Expected` | `ExecuteAsync_WithValidInput_ReturnsZero` |
| Private fields | `_camelCase` | `_logger`, `_config` |

## Error Handling

Use `ExitCode` enum for command return values:

```csharp
public enum ExitCode
{
    Success = 0,
    Error = 1,
    Cancelled = 2,
    ConfigurationError = 3,
    ValidationError = 4,
    PartialSuccess = 5,
    IOError = 6,
    InvalidArguments = 7
}
```

Log errors appropriately:

```csharp
catch (IOException ex)
{
    _logger.LogError(ex, "Failed to copy file {Path}", filePath);
    return (int)ExitCode.IOError;
}
```

## Common Tasks

### Adding a new configuration option

1. Add property to `PhotoCopyConfig.cs` with XML documentation
2. Add to `appsettings.yaml` with comments
3. Wire up in `ConfigurationLoader.cs` if CLI override needed
4. Add to `CommandOptions.cs` if exposed as CLI argument

### Adding a new command

1. Create options class in `Commands/CommandOptions.cs`
2. Create command class implementing `ICommand`
3. Register in `CommandRouter.RouteAsync()` parse chain
4. Add routing method `Run[Name]CommandAsync`

### Adding a new service

1. Create interface (if needed for testability)
2. Create implementation
3. Register in `ServiceCollectionExtensions.cs`
4. Add tests in `PhotoCopy.Tests/[Component]/`
