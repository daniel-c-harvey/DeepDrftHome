# FileDatabase C# Port

This is a C# port of the TypeScript file database system, maintaining architectural integrity while leveraging C# language features.

## Architecture Overview

The C# port preserves the original three-layer architecture:

### 1. **FileDatabase** (Main Orchestrator)
- **Location**: `Services/FileDatabase.cs`
- **Purpose**: Root-level manager coordinating multiple media vaults
- **Key Features**:
  - Manages collection of `MediaVault` instances using `StructuralMap<string, MediaVault>`
  - Provides async factory method `FromAsync()` for initialization
  - Handles vault creation, resource loading, and resource registration

### 2. **MediaVault System** (Storage Containers)
- **Location**: `Services/MediaVault.cs`
- **Components**:
  - `MediaVault` (Abstract base class)
  - `ImageDirectoryVault` (Concrete implementation for images)
- **Key Features**:
  - File path normalization and media key generation
  - Generic type-safe operations using `MediaVaultType` enum
  - Metadata association with stored files
  - Async factory pattern for initialization

### 3. **Index Management** (Metadata & Organization)
- **Location**: `Services/IndexSystem.cs`
- **Two-tier indexing**:
  - `DirectoryIndex`: Manages vault entries (what vaults exist)
  - `VaultIndex`: Manages media entries within vaults (what files exist + metadata)
- **Features**:
  - `IndexFactory` handles index creation/loading
  - Automatic JSON serialization to filesystem
  - Strong typing with generic constraints

## Key Components

### Models (`Models/` directory)
- **String-based keys**: Simple string identifiers for vault and entry management
- **`MetaData` hierarchy**: Base metadata → `ImageMetaData` with aspect ratio
- **Media Types**: `FileBinary` → `MediaBinary` → `ImageBinary`
- **Factory Classes**: Type-safe creation of media objects and metadata

### Utilities (`Utils/` directory)
- **`StructuralMap<TKey, TValue>`**: JSON-based structural equality for complex keys
- **`StructuralSet<T>`**: Set with structural equality semantics
- **`FileUtils`**: Async file I/O operations with chunked reading/writing

## C# Design Improvements

### SOLID Principles Applied
1. **Single Responsibility**: Each class handles one concern
2. **Open/Closed**: Extensible media types via generic constraints
3. **Liskov Substitution**: Proper inheritance hierarchies
4. **Interface Segregation**: Focused interfaces (`IIndex`)
5. **Dependency Inversion**: Abstract base classes and interfaces

### C# Language Features
- **Records**: Immutable data structures for `MetaData` hierarchy
- **Pattern Matching**: Switch expressions for type-safe factory methods
- **Nullable Reference Types**: Explicit nullability handling
- **Async/Await**: Full async support with `Task<T>` and `ValueTask<T>`
- **Generic Constraints**: Strong typing with `where` clauses

### DRY Implementation
- **Factory Pattern**: Centralized object creation logic
- **Generic Type Maps**: Reusable type mappings for different media types
- **Template Method Pattern**: Common functionality in base classes

## Usage Example

```csharp
// Initialize the database
var database = await FileDatabase.FromAsync("/path/to/database");

// Create a vault
var vaultId = "images";
await database.CreateVaultAsync(vaultId, MediaVaultType.Image);

// Store an image (MediaVaultType inferred from ImageBinary)
var imageData = new ImageBinary(new ImageBinaryParams(buffer, size, ".jpg", 1.5));
await database.RegisterResourceAsync("gallery", "photo1", imageData);

// Load an image (MediaVaultType inferred from ImageBinary generic type)
var loadedImage = await database.LoadResourceAsync<ImageBinary>("gallery", "photo1");
```

## Project Structure

```
FileDatabase/
├── Models/
│   ├── [EntryKey removed]     # Now using simple string keys
│   ├── MetaData.cs           # Metadata hierarchy
│   ├── MediaModels.cs        # Binary data classes
│   ├── MediaFactories.cs     # Factory pattern implementations
│   ├── MediaVaultType.cs     # Enum for vault types
│   ├── IIndex.cs            # Index interface
│   └── IndexData.cs         # Index implementations
├── Services/
│   ├── FileDatabase.cs      # Main orchestrator
│   ├── MediaVault.cs        # Vault system
│   └── IndexSystem.cs       # Index management
├── Utils/
│   ├── StructuralMap.cs     # Structural equality map
│   ├── StructuralSet.cs     # Structural equality set
│   └── FileUtils.cs         # File I/O utilities
└── FileDatabase.csproj      # Project file (.NET 9.0)
```

## Key Architectural Decisions

1. **Async-First Design**: All I/O operations are asynchronous
2. **Strong Type Safety**: Extensive use of generics and constraints
3. **Structural Equality**: JSON-based equality for composite keys
4. **Separation of Concerns**: Clear boundaries between indexing, storage, and media handling
5. **Factory-Based Initialization**: Handles complex async setup patterns
6. **Metadata-Driven**: Rich metadata system supporting extensible media types

## Differences from TypeScript Version

1. **Explicit Type Safety**: C# compiler enforces type constraints at compile time
2. **Memory Management**: Automatic garbage collection vs manual buffer management
3. **Serialization**: System.Text.Json instead of V8 serialization
4. **Error Handling**: Exceptions vs try/catch patterns (maintained original behavior)
5. **Nullability**: Explicit nullable reference types for better null safety

This port maintains the architectural integrity of the original TypeScript design while leveraging C#'s type system and language features for improved maintainability and performance.
