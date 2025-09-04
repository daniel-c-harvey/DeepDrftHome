# CLAUDE.md - DeepDrftContent

This file provides guidance to Claude Code (claude.ai/code) when working with the DeepDrftContent project.

## Project Overview

DeepDrftContent is a **Web API project** that serves as the content management backend for the DeepDrft system. It provides secure API endpoints for managing the FileDatabase system and handles binary media content storage and retrieval.

## Architecture

### Technology Stack
- **ASP.NET Core Web API 9.0**: RESTful API framework
- **Custom FileDatabase System**: Binary file storage with vault management
- **API Key Authentication**: Secure endpoint protection
- **OpenAPI/Swagger**: Development API documentation

### Project Structure
```
DeepDrftContent/
├── Controllers/            # API endpoint controllers
│   ├── TrackController.cs  # Track media management
│   └── WeatherForecastController.cs # Demo endpoint
├── FileDatabase/           # Core FileDatabase implementation
│   ├── Services/           # Database and vault services
│   ├── Models/            # Data models and DTOs
│   ├── Utils/             # Utility classes
│   └── Abstractions/      # Interfaces and contracts
├── Middleware/            # Custom middleware
│   ├── ApiKeyAuthenticationMiddleware.cs
│   └── ApiKeyAuthorizeAttribute.cs
├── Models/               # Application models
├── environment/          # Configuration files
│   ├── filedatabase.json # FileDatabase settings
│   └── apikey.json      # API authentication (not in repo)
└── Program.cs           # Application entry point
```

## Core FileDatabase System

### Key Components

#### FileDatabase Service
Main orchestration class managing multiple MediaVaults:
```csharp
public class FileDatabase : DirectoryIndexDirectory
{
    // Factory creation
    public static async Task<FileDatabase?> FromAsync(string rootPath)
    
    // Vault management
    public async Task CreateVaultAsync(string vaultId, MediaVaultType vaultType)
    public MediaVault? GetVault(string vaultId)
    
    // Resource operations
    public async Task<T?> LoadResourceAsync<T>(string vaultId, string entryId) where T : FileBinary
    public async Task<bool> RegisterResourceAsync(string vaultId, string entryId, FileBinary media)
}
```

#### MediaVault Types
- **MediaVaultType.Media**: General binary media storage
- **MediaVaultType.Audio**: Audio-specific storage with duration/bitrate
- **MediaVaultType.Image**: Image storage with aspect ratio

#### Media Models Hierarchy
```
FileBinary (base)
├── MediaBinary (+ Extension, MIME type)
    ├── AudioBinary (+ Duration, Bitrate)  
    └── ImageBinary (+ AspectRatio)
```

### Index System
- **DirectoryIndex**: Manages vault organization
- **VaultIndex**: Individual vault metadata and type information
- **JSON-based storage**: All indexes stored as JSON files

## API Authentication

### Custom API Key Middleware
```csharp
// Usage in controllers
[ApiKeyAuthorize]
[HttpPut("{trackId}")]
public async Task<ActionResult> PutTrack([FromQuery] string trackId, [FromBody] AudioBinaryDto track)
```

### Authentication Flow
1. Client sends request with `ApiKey` header
2. `ApiKeyAuthenticationMiddleware` validates against configured key
3. Endpoints marked with `[ApiKeyAuthorize]` require valid API key
4. Returns 401 for missing/invalid keys

## Development Commands

### Running the API
```bash
# Run development server
dotnet run --project DeepDrftContent

# Run with specific environment
dotnet run --project DeepDrftContent --environment Development
```

### Building
```bash
# Build project
dotnet build DeepDrftContent

# Publish for deployment
dotnet publish DeepDrftContent -c Release
```

### Testing FileDatabase
```bash
# Run FileDatabase-specific tests
dotnet test DeepDrftTests/ --filter "FileDatabaseTests"
```

## Configuration

### FileDatabase Settings
`environment/filedatabase.json`:
```json
{
  "FileDatabaseSettings": {
    "VaultPath": "../Database/Vaults"
  }
}
```

### API Key Configuration
`environment/apikey.json` (not in repository):
```json
{
  "ApiKeySettings": {
    "ApiKey": "your-secret-api-key"
  }
}
```

## Key Patterns

### Factory Pattern
MediaVault creation uses factory pattern:
```csharp
var directoryVault = await MediaVaultFactory.From(path, vaultType);
```

### Async/Await Throughout
All FileDatabase operations are async with consistent error handling:
```csharp
// Swallow exceptions and return null/false for failed operations
try { /* operation */ }
catch { return null; } // or return false;
```

### Type-Safe Media Handling
Generic methods ensure type safety for media operations:
```csharp
var audio = await fileDatabase.LoadResourceAsync<AudioBinary>("tracks", trackId);
```

### MIME Type Management
Automatic extension/MIME type conversion via `MimeTypeExtensions`:
```csharp
// Supported audio formats: mp3, wav, flac, aac, ogg, m4a
// Supported image formats: jpg, png, gif, webp, svg, bmp
```

## Important Notes

### Vault Organization
- Each vault is a directory with its own index
- Entry IDs are sanitized to create safe filenames
- Media keys generated via regex: `[^a-zA-Z0-9]` → `-`

### Error Handling Philosophy
FileDatabase uses "swallow and return null" pattern to match TypeScript behavior:
- Failed operations return `null` or `false`
- No exceptions propagated to callers
- Consistent behavior across all async operations

### Binary Data Handling
All media stored as `byte[]` buffers with associated metadata (size, extension, duration, etc.)

When working with this project, focus on the FileDatabase system architecture and maintain the established patterns for vault management, async operations, and API security.