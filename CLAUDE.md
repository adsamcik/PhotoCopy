# CLAUDE.md

## Project Overview

PhotoCopy is a .NET CLI application for batch organizing/copying/moving photos to structured destination paths based on metadata (date, location, camera, album). It extracts EXIF metadata, performs reverse geocoding using GPS coordinates, and supports powerful destination path templating with variable fallback chains and conditional thresholds.

## Tech Stack

- **Language**: C# 14 / .NET 10.0
- **Framework**: Console Application with Microsoft.Extensions.DependencyInjection
- **Testing**: TUnit + AwesomeAssertions + NSubstitute + Coverlet
- **Key Dependencies**:
  - `CommandLineParser` - CLI argument parsing
  - `MetadataExtractor` - EXIF/XMP/IPTC metadata extraction
  - `KdTree` - Spatial indexing for reverse geocoding
  - `Microsoft.Extensions.*` - DI, Configuration, Logging, Options

## Architecture

The application follows a clean, layered architecture with dependency injection:

```text
Program.cs → CommandRouter → Commands → Services → Abstractions
```

**Key Components:**

| Component     | Purpose                                                      |
| ------------- | ------------------------------------------------------------ |
| `Commands/`   | CLI command handlers (Copy, Scan, Validate, Config, Rollback)|
| `Directories/`| Directory scanning and file copying orchestration            |
| `Files/`      | File abstraction, metadata extraction, geocoding             |
| `Configuration/` | Config loading (YAML/JSON), destination variable parsing  |
| `Validators/` | Input/file validation pipeline                               |
| `Checkpoint/` | Resume support for interrupted operations                    |
| `Rollback/`   | Transaction logging for undo operations                      |

See [.github/context/ARCHITECTURE.md](.github/context/ARCHITECTURE.md) for detailed component maps.

## Development Commands

```bash
# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run tests
dotnet test

# Run tests with coverage
dotnet test /p:CollectCoverage=true

# Run the application
dotnet run --project PhotoCopy -- -i "/source" -o "/dest/{year}/{month}/{name}"

# Dry run (preview without changes)
dotnet run --project PhotoCopy -- -i "/source" -o "/dest/{year}/{name}" -d
```

## Key Patterns

1. **Interface-based abstractions**: All external dependencies (file system, geocoding) are behind interfaces for testability
2. **Options pattern**: Configuration via `IOptions<PhotoCopyConfig>`
3. **Command pattern**: Each CLI verb has a dedicated `ICommand` implementation
4. **Factory pattern**: `IFileFactory`, `IValidatorFactory` for creating domain objects
5. **Two-pass architecture**: For conditional variables, first scan collects statistics, second pass applies thresholds

## Code Conventions

- Nullable reference types enabled (`<Nullable>enable</Nullable>`)
- `ArgumentNullException.ThrowIfNull()` for null validation
- Async suffix for async methods
- File-scoped namespaces
- XML documentation on public APIs
- Tests use `[Test]` attribute (TUnit), not `[Fact]` (xUnit)

## Important Files

| File                                                                           | Purpose                            |
| ------------------------------------------------------------------------------ | ---------------------------------- |
| [PhotoCopy/Program.cs](PhotoCopy/Program.cs)                                   | Entry point, delegates to CommandRouter |
| [PhotoCopy/Hosting/CommandRouter.cs](PhotoCopy/Hosting/CommandRouter.cs)       | CLI argument parsing and command routing |
| [PhotoCopy/Commands/CopyCommand.cs](PhotoCopy/Commands/CopyCommand.cs)         | Main copy/move operation           |
| [PhotoCopy/Configuration/PhotoCopyConfig.cs](PhotoCopy/Configuration/PhotoCopyConfig.cs) | Configuration model     |
| [PhotoCopy/Directories/DirectoryCopier.cs](PhotoCopy/Directories/DirectoryCopier.cs) | File copying orchestration   |
| [PhotoCopy/Files/IFile.cs](PhotoCopy/Files/IFile.cs)                           | Core file abstraction              |
| [PhotoCopy/appsettings.yaml](PhotoCopy/appsettings.yaml)                       | Default configuration with comments|

## Gotchas

- **TUnit, not xUnit**: Tests use TUnit framework with `[Test]` attribute, not xUnit's `[Fact]`/`[Theory]`
- **AwesomeAssertions**: Use `Should().Be()` not FluentAssertions syntax
- **Geocoding initialization**: `IReverseGeocodingService.InitializeAsync()` must be called before use
- **Two-pass operations**: Conditional variables (`{city?min=10}`) require statistics collection first
- **Exit codes**: Return specific `ExitCode` enum values, not raw integers
- **Transaction logging**: Rollback support requires `EnableRollback = true` in config
- **Live Photo inheritance**: Companion .mov files can inherit GPS from paired .heic files

## Agent Instructions

When working in this codebase:

1. **Follow existing patterns**: Check similar components before implementing new features
2. **Interface first**: Add abstractions in `Abstractions/` or component-local interfaces
3. **Use DI**: Register new services in `ServiceCollectionExtensions.AddPhotoCopyServices()`
4. **Test coverage**: Add unit tests in parallel test folder structure (`PhotoCopy.Tests/[ComponentName]/`)
5. **Configuration changes**: Update both `PhotoCopyConfig.cs` and `appsettings.yaml` with comments
6. **Exit codes**: Use appropriate `ExitCode` enum values for error conditions
7. **Always run tests** before completing work: `dotnet test`
