# Architecture

## System Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                         PhotoCopy CLI                                │
├─────────────────────────────────────────────────────────────────────┤
│  Program.cs                                                          │
│      │                                                               │
│      ▼                                                               │
│  CommandRouter (Hosting/)                                            │
│      │ parses CLI args via CommandLineParser                         │
│      ├──────────┬──────────┬───────────┬───────────┬───────────┐    │
│      ▼          ▼          ▼           ▼           ▼           ▼    │
│  CopyCommand  ScanCommand  ValidateCmd  ConfigCmd  RollbackCmd ...   │
│      │                                                               │
│      ▼                                                               │
│  DirectoryCopier / DirectoryCopierAsync (Directories/)               │
│      │ scans source, builds plan, executes copy/move                 │
│      ▼                                                               │
│  IFileSystem + IFileFactory + IValidator pipeline                    │
│      │                                                               │
│      ▼                                                               │
│  File abstraction with metadata (Files/)                             │
│      │                                                               │
│      ▼                                                               │
│  IReverseGeocodingService (Files/Geo/)                               │
└─────────────────────────────────────────────────────────────────────┘
```

## Component Map

### Commands (`PhotoCopy/Commands/`)

| Component | Purpose | Key Dependencies |
|-----------|---------|------------------|
| `CopyCommand` | Main copy/move operation | `IDirectoryCopier`, `IValidatorFactory` |
| `ScanCommand` | Scan source and output statistics | `IDirectoryScanner`, JSON output |
| `ValidateCommand` | Validate files without copying | `IFileValidationService` |
| `ConfigCommand` | Display effective configuration | `ConfigurationDiagnostics` |
| `RollbackCommand` | Undo previous operations | `IRollbackService` |
| `ValidateConfigCommand` | Validate configuration file | `ConfigurationValidator` |

### Directories (`PhotoCopy/Directories/`)

| Component | Purpose |
|-----------|---------|
| `DirectoryCopier` | Synchronous file copying orchestration |
| `DirectoryCopierAsync` | Parallel async file copying |
| `DirectoryCopierBase` | Shared logic: plan building, path generation |
| `DirectoryScanner` | Enumerates files respecting depth limits |
| `CopyPlan` | Represents planned operations before execution |
| `VariableExpressionParser` | Parses `{var|fallback}` syntax |

### Files (`PhotoCopy/Files/`)

| Component | Purpose |
|-----------|---------|
| `IFile` / `GenericFile` | Core file abstraction with metadata |
| `FileFactory` | Creates `IFile` instances with enriched metadata |
| `FileMetadataExtractor` | Extracts EXIF/XMP/IPTC using MetadataExtractor |
| `IFileSystem` / `FileSystem` | Abstraction over System.IO operations |

### Files/Geo (`PhotoCopy/Files/Geo/`)

| Component | Purpose |
|-----------|---------|
| `IReverseGeocodingService` | Interface for GPS→location lookup |
| `TieredGeocodingService` | Main implementation with cell-based lazy loading |
| `BoundaryAwareGeocodingService` | Wraps tiered service with country boundary filtering |
| `CellCache` | In-memory cache for loaded geohash cells |
| `GeoIndexFormat` | Binary format for geodata files |

### Configuration (`PhotoCopy/Configuration/`)

| Component | Purpose |
|-----------|---------|
| `PhotoCopyConfig` | Main configuration model |
| `ConfigurationLoader` | Loads from YAML/JSON/CLI/env with layering |
| `DestinationVariables` | Enum of supported path variables |
| `CountryCodeLookup` | ISO 3166-1 alpha-2 → country name mapping |

### Validators (`PhotoCopy/Validators/`)

| Component | Purpose |
|-----------|---------|
| `IValidator` | Interface for file validators |
| `ValidatorFactory` | Creates validators from config |
| `MinDateValidator` / `MaxDateValidator` | Date range validation |
| `FileValidationService` | Orchestrates validation pipeline |
| `InputValidator` | Validates CLI inputs (paths exist, etc.) |

### Checkpoint (`PhotoCopy/Checkpoint/`)

Resume support for interrupted operations:

| Component | Purpose |
|-----------|---------|
| `ICheckpointStore` | Persist/load checkpoint state |
| `BinaryCheckpointStore` | Binary file implementation |
| `AsyncCheckpointWriter` | Non-blocking checkpoint writing |
| `ResumeOrchestrator` | Resume decision logic |

### Rollback (`PhotoCopy/Rollback/`)

Transaction logging for undo operations:

| Component | Purpose |
|-----------|---------|
| `ITransactionLogger` | Interface for logging operations |
| `TransactionLogger` | JSON-based transaction log |
| `RollbackService` | Reads logs and reverses operations |

## Data Flow: Copy Operation

```
1. CLI args parsed → CopyOptions
2. ConfigurationLoader merges YAML + JSON + CLI + env → PhotoCopyConfig
3. CommandRouter builds ServiceProvider via ServiceProviderFactory
4. IReverseGeocodingService.InitializeAsync() loads geodata
5. CopyCommand.ExecuteAsync() starts
   │
   ├─→ ValidatorFactory creates validators from config
   │
   └─→ DirectoryCopier[Async].Copy()
       │
       ├─→ IFileSystem.EnumerateFiles(source)
       │       │
       │       └─→ For each file:
       │               IFileFactory.CreateAsync()
       │                   ├─→ FileMetadataExtractor (EXIF)
       │                   └─→ IReverseGeocodingService (GPS→location)
       │
       ├─→ BuildCopyPlan() - validates, generates destination paths
       │       └─→ VariableExpressionParser evaluates {year}/{city|country}/...
       │
       └─→ ExecutePlan() - copies/moves files
               ├─→ IFileSystem.CopyFile() or MoveFile()
               └─→ TransactionLogger.LogOperation() if rollback enabled
```

## Two-Pass Architecture

For conditional variables (`{city?min=10|country}`):

```
Pass 1: Statistics Collection
├─→ Scan all files
├─→ Collect location counts per variable
└─→ Build LocationStatistics

Pass 2: Path Resolution
├─→ For each file, evaluate conditions against statistics
├─→ Variables meeting threshold use their value
├─→ Variables below threshold use fallback
└─→ Execute copy/move
```

## Dependency Injection

All services registered in `Extensions/ServiceCollectionExtensions.cs`:

```csharp
services.AddPhotoCopyServices(config)
    ├─→ AddPhotoCopyLogging()
    ├─→ AddPhotoCopyConfiguration()
    ├─→ AddPhotoCopyCoreServices()    // FileSystem, Geocoding, etc.
    ├─→ AddPhotoCopyCopiers()          // DirectoryCopier variants
    ├─→ AddPhotoCopyProgressReporter()
    └─→ AddPhotoCopyCheckpointServices()
```

## Key Abstractions

```
IFileSystem          → FileSystem             # File I/O
IFile                → GenericFile            # File with metadata
IFileFactory         → FileFactory            # Creates enriched files
IReverseGeocodingService → TieredGeocodingService  # GPS→location
IValidator           → MinDateValidator, ...  # File validation
ICommand             → CopyCommand, ...       # CLI commands
IDirectoryCopier     → DirectoryCopier        # Copy orchestration
ICheckpointStore     → BinaryCheckpointStore  # Resume state
ITransactionLogger   → TransactionLogger      # Rollback support
```

## Error Handling Strategy

1. **Commands** catch exceptions and return appropriate `ExitCode`
2. **Services** throw domain-specific exceptions or return result types
3. **Logging** via `ILogger<T>` at appropriate levels
4. **Partial failures** tracked in `CopyResult.Errors` collection
