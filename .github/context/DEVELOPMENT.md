# Development Guide

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git
- IDE: Visual Studio 2022, VS Code, or Rider

## Getting Started

```bash
# Clone the repository
git clone https://github.com/adsamcik/PhotoCopy.git
cd PhotoCopy

# Restore dependencies
dotnet restore

# Build
dotnet build

# Run tests
dotnet test
```

## Project Structure

```
PhotoCopy/
├── PhotoCopy/              # Main application
│   ├── Abstractions/       # Cross-cutting interfaces
│   ├── Commands/           # CLI command implementations
│   ├── Configuration/      # Config loading and models
│   ├── Directories/        # Directory scanning/copying
│   ├── Files/              # File abstraction and metadata
│   ├── Hosting/            # DI and startup
│   ├── data/               # Geodata files
│   └── ...
├── PhotoCopy.Tests/        # Test project
│   ├── Integration/        # Integration tests
│   ├── E2E/                # End-to-end tests
│   └── [Component]/        # Unit tests mirror main structure
├── docs/                   # Documentation
│   └── design/             # Technical design docs
└── tools/                  # Build/utility scripts
```

## Common Commands

### Building

```bash
# Debug build
dotnet build

# Release build
dotnet build -c Release

# Publish single-file executable
dotnet publish -c Release -r win-x64 --self-contained
```

### Testing

```bash
# Run all tests
dotnet test

# Run with verbose output
dotnet test --logger "console;verbosity=detailed"

# Run specific test class
dotnet test --filter "FullyQualifiedName~CopyCommandTests"

# Run with coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

### Running the Application

```bash
# Basic copy operation
dotnet run --project PhotoCopy -- \
  -i "/source/photos" \
  -o "/dest/{year}/{month}/{name}"

# Dry run (preview only)
dotnet run --project PhotoCopy -- \
  -i "/source" -o "/dest/{year}/{name}" -d

# With location organization
dotnet run --project PhotoCopy -- \
  -i "/source" \
  -o "/dest/{year}/{country}/{city|Unknown}/{name}"

# Move instead of copy
dotnet run --project PhotoCopy -- \
  -i "/source" -o "/dest/{year}/{name}" -m move
```

### CLI Commands

| Command | Description |
|---------|-------------|
| `copy` (default) | Copy/move files to destination |
| `scan` | Scan source and output statistics |
| `validate` | Validate files without copying |
| `config` | Display effective configuration |
| `rollback` | Undo previous operations |
| `validate-config` | Validate configuration file |

## Configuration

Configuration is loaded from multiple sources (highest to lowest priority):

1. Command-line arguments
2. Environment variables (`PHOTOCOPY_*`)
3. `appsettings.yaml` (in working directory)
4. `appsettings.json`
5. Default values

### Example appsettings.yaml

```yaml
photoCopy:
  source: "/path/to/photos"
  destination: "/organized/{year}/{month}/{name}"
  dryRun: false
  logLevel: Information
  locationGranularity: City
  unknownLocationFallback: "Unknown"
  enableLivePhotoInheritance: true
```

## Testing Strategy

### Unit Tests

- Located in `PhotoCopy.Tests/[Component]/`
- Test individual classes in isolation
- Mock all dependencies with NSubstitute
- Use AwesomeAssertions for assertions

### Integration Tests

- Located in `PhotoCopy.Tests/Integration/`
- Test component interactions
- May use real file system (temp directories)
- Test actual workflows end-to-end

### E2E Tests

- Located in `PhotoCopy.Tests/E2E/`
- Run the actual executable
- Test CLI argument parsing
- Test real file operations

### Test Helpers

Key test infrastructure in `TestingImplementation/`:

- `FakeLogger<T>` - Captures log entries for verification
- `SharedLogs` - Thread-safe log collection
- `TestBase` - Common test setup and DI configuration

## Debugging Tips

### Enable Verbose Logging

```bash
dotnet run --project PhotoCopy -- ... --logLevel verbose
```

### Dry Run Mode

Always test with `-d` (dry run) first to preview operations:

```bash
dotnet run --project PhotoCopy -- -i "/source" -o "/dest/{year}/{name}" -d
```

### Configuration Diagnostics

View effective configuration and sources:

```bash
dotnet run --project PhotoCopy -- config
```

## Adding New Features

### 1. New Configuration Option

1. Add property to `Configuration/PhotoCopyConfig.cs`
2. Add to `appsettings.yaml` with comments
3. Add CLI option to `Commands/CommandOptions.cs` if needed
4. Wire up override in `Configuration/ConfigurationLoader.cs`

### 2. New Command

1. Create options class in `Commands/CommandOptions.cs`
2. Create command class implementing `ICommand`
3. Register in `Hosting/CommandRouter.cs`
4. Add tests in `PhotoCopy.Tests/Commands/`

### 3. New Service

1. Create interface (if external dependency/testability needed)
2. Create implementation class
3. Register in `Extensions/ServiceCollectionExtensions.cs`
4. Add tests in appropriate test folder

### 4. New Validator

1. Implement `IValidator` interface
2. Register in `ValidatorFactory.Create()`
3. Add enable flag to config if conditional
4. Add tests

## Code Quality

### Static Analysis

The project uses nullable reference types. Ensure all nullable warnings are addressed.

### Documentation

- Public APIs should have XML documentation
- Complex logic should have explanatory comments
- Design decisions should be documented in `docs/design/`

## Troubleshooting

### "Geodata not found"

Ensure `data/geo.geoindex` and `data/geo.geodata` are present in the output directory. These are copied by the build.

### Tests failing with log assertions

Clear shared logs at the start of each test:

```csharp
[Before(Test)]
public void BeforeEachTest()
{
    SharedLogs.Clear();
}
```

### Permission errors in tests

Tests should use `DryRun = true` in config or use temp directories that get cleaned up.
