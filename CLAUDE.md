# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Architecture Overview

DeepDrftHome is a .NET 9 solution consisting of multiple projects that implement a dual-database media management system:

### Core Projects
- **DeepDrftWeb**: Blazor Server/WebAssembly hybrid web application using MudBlazor UI framework
- **DeepDrftWeb.Client**: Blazor WebAssembly client components
- **DeepDrftContent**: Web API project providing content management endpoints with API key authentication
- **DeepDrftModels**: Shared data models and entities
- **DeepDrftTests**: NUnit test project with comprehensive FileDatabase tests
- **NetBlocks**: External dependency located at `C:\lib\NetBlocks\`

### Database Architecture

The application uses a **dual-database approach**:

1. **SQL Database (SQLite)**: Stores metadata and track information via Entity Framework
   - Located: `../Database/deepdrft.db` 
   - Entity: `TrackEntity` with fields for MediaPath, TrackName, Artist, Album, Genre, etc.
   - Context: `DeepDrftContext` with SQLite provider

2. **FileDatabase**: Custom file-based storage system for binary media content
   - Located: `../Database/Vaults` (configurable via `filedatabase.json`)
   - Manages **MediaVaults** with different types: Media, Image, Audio
   - Supports structured binary storage with metadata (duration, bitrate, aspect ratio)

### FileDatabase System Details

The FileDatabase is the core innovation of this project:

- **Vault-based Organization**: Content organized into typed vaults (MediaVaultType: Media, Image, Audio)
- **Index System**: Uses JSON-based indexing with DirectoryIndex and VaultIndex structures
- **Media Types**: 
  - `AudioBinary`: Buffer + Duration + Bitrate + Extension
  - `ImageBinary`: Buffer + AspectRatio + Extension  
  - `MediaBinary`: Base buffer + Extension + MIME type support
- **Factory Pattern**: `MediaVaultFactory` creates appropriate vault types
- **Async Operations**: All database operations are async with error swallowing

## Development Commands

### Build and Test
```bash
# Build entire solution
dotnet build DeepDrftHome.sln

# Run all tests
dotnet test DeepDrftTests/

# Run specific test class
dotnet test DeepDrftTests/ --filter "FileDatabaseTests"

# Build specific project
dotnet build DeepDrftWeb/
dotnet build DeepDrftContent/
```

### Running Applications
```bash
# Run main web application
dotnet run --project DeepDrftWeb

# Run content API
dotnet run --project DeepDrftContent
```

### Entity Framework (SQL Database)
```bash
# Add migration
dotnet ef migrations add MigrationName --project DeepDrftWeb

# Update database
dotnet ef database update --project DeepDrftWeb
```

## Key Configuration Files

- `DeepDrftWeb/appsettings.json`: SQL connection string, logging configuration
- `DeepDrftContent/environment/filedatabase.json`: FileDatabase vault path configuration
- `DeepDrftContent/environment/apikey.json`: API authentication (not in repo)

## Important Patterns

### FileDatabase Usage
```csharp
// Initialize FileDatabase
var fileDatabase = await FileDatabase.FromAsync(rootPath);

// Register audio content
await fileDatabase.RegisterResourceAsync("tracks", trackId, audioBinary);

// Load audio content
var audio = await fileDatabase.LoadResourceAsync<AudioBinary>("tracks", trackId);
```

### Dependency Injection
- FileDatabase service registration occurs in startup configuration
- SQL context registered via `AddDbContext<DeepDrftContext>`
- Repository pattern with `TrackRepository` and `TrackService`

### Testing Strategy
- Comprehensive FileDatabase integration tests with temporary directories
- Uses NUnit framework with async test patterns
- Test isolation via unique temp directories per test
- Covers vault creation, media storage, and retrieval scenarios

## Project Dependencies

External dependencies include Entity Framework Core (SQLite), MudBlazor, and the custom NetBlocks library. The FileDatabase system is entirely custom-built and forms the backbone of the media storage architecture.