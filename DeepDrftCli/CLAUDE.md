# CLAUDE.md - DeepDrftCli

This file provides guidance to Claude Code (claude.ai/code) when working with the DeepDrftCli project.

## Project Overview

DeepDrftCli is a **console application** for managing audio tracks in the DeepDrft system. It provides command-line interface for adding WAV files to both the SQL database and FileDatabase, leveraging services from both DeepDrftWeb and DeepDrftContent projects.

## Architecture

### Technology Stack
- **.NET 9.0 Console Application**: Command-line interface
- **Microsoft.Extensions.Hosting**: Dependency injection and configuration
- **Entity Framework Core**: SQL database operations via DeepDrftWeb
- **FileDatabase**: Binary storage via DeepDrftContent

### Project Structure
```
DeepDrftCli/
├── Services/
│   ├── CliService.cs           # Main CLI command handler
│   └── [Processors moved to DeepDrftContent]
├── Program.cs                  # Application entry point with DI setup
├── appsettings.json           # Configuration
└── DeepDrftCli.csproj         # Project file with references
```

### Service Dependencies
- **DeepDrftWeb**: SQL database operations, TrackService, TrackRepository
- **DeepDrftContent**: FileDatabase, AudioProcessor (in /Processors)
- **DeepDrftModels**: Shared entities and DTOs
- **NetBlocks**: Result pattern (ResultContainer<T>)

## Audio Processing Architecture

### AudioProcessor (DeepDrftContent/Processors/)
Handles WAV file processing with full metadata extraction:
```csharp
public async Task<AudioBinary?> ProcessWavFileAsync(string filePath)
{
    // WAV header parsing for duration, bitrate, sample rate
    // Creates AudioBinary with extracted metadata
}
```

### TrackService (DeepDrftContent/Services/)
Orchestrates dual-database operations:
```csharp
public async Task<TrackEntity?> AddTrackFromWavAsync(
    string wavFilePath, string trackName, string artist, ...)
{
    // 1. Process WAV file → AudioBinary
    // 2. Store in FileDatabase → get trackId
    // 3. Create TrackEntity with MediaPath = trackId
    // 4. Return entity for SQL storage
}
```

## Command-Line Interface

### Available Commands

#### Add Track
```bash
DeepDrftCli add <wav-file-path> <track-name> <artist> [album] [genre] [release-date]
```
**Example:**
```bash
DeepDrftCli add "mysong.wav" "My Song" "Artist Name" "Album" "Rock" "2024-01-01"
```

#### List Tracks
```bash
DeepDrftCli list
```
Shows all tracks from SQL database with formatted table output.

#### Help
```bash
DeepDrftCli help
# or
DeepDrftCli --help
# or
DeepDrftCli -h
```

## Development Commands

### Building
```bash
# Build CLI project
dotnet build DeepDrftCli/

# Build entire solution
dotnet build DeepDrftHome.sln
```

### Running
```bash
# Run from project directory
cd DeepDrftCli
dotnet run -- add "song.wav" "Track Name" "Artist"

# Run from solution root
dotnet run --project DeepDrftCli -- list

# Run built executable
./DeepDrftCli/bin/Debug/net9.0/DeepDrftCli.exe add "song.wav" "Track" "Artist"
```

### Database Requirements
Ensure databases exist and are accessible:
```bash
# SQL Database: ../Database/deepdrft.db
# FileDatabase: ../Database/Vaults
```

## Configuration

### appsettings.json
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=../Database/deepdrft.db"
  },
  "FileDatabaseSettings": {
    "VaultPath": "../Database/Vaults"
  }
}
```

### Dependency Injection Setup
Program.cs configures full DI container:
- **SQL Context**: DeepDrftContext with SQLite
- **FileDatabase**: Singleton initialized from vault path
- **Services**: TrackRepository, TrackServices (both Web and Content), AudioProcessor
- **Logging**: Console logging for development

## Important Patterns

### Result Pattern (NetBlocks)
All service operations return ResultContainer<T>:
```csharp
var result = await _webTrackService.Create(trackEntity);
if (result.Success && result.Value != null)
{
    // Success handling
}
else
{
    var errorMessage = result.Messages.FirstOrDefault()?.Message ?? "Unknown error";
    // Error handling
}
```

### Dual Database Strategy
1. **Process WAV** → AudioBinary (metadata + buffer)
2. **Store in FileDatabase** → generates unique trackId
3. **Create TrackEntity** with MediaPath = trackId
4. **Store in SQL** → gets database ID and full entity

### WAV File Processing
- **RIFF/WAVE format parsing**: Header validation and chunk parsing
- **Metadata extraction**: Duration, bitrate, sample rate, channels
- **Fallback handling**: Default values if parsing fails
- **Binary preservation**: Full WAV file stored as-is

### Error Handling
- **File validation**: Existence and .wav extension checking
- **Graceful degradation**: Default metadata if parsing fails
- **User-friendly messages**: Clear error reporting for CLI users
- **Logging integration**: Structured logging for debugging

## CLI Usage Notes

### File Path Handling
- Supports absolute and relative paths
- Arguments with spaces must be quoted
- Only .wav files are supported currently

### Date Format
Release dates must be in YYYY-MM-DD format:
```bash
DeepDrftCli add "song.wav" "Title" "Artist" "Album" "Genre" "2024-01-01"
```

### Output Format
- Success: Detailed track information display
- List: Formatted table with ID, Name, Artist, Album, Genre
- Errors: Clear, actionable error messages

When working with this project, focus on maintaining the dual-database consistency and the established patterns for CLI argument handling and user feedback.