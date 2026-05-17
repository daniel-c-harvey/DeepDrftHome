# CLAUDE.md - DeepDrftCli

Guidance for working in the DeepDrftCli project (the admin CLI tool).

See the root `CLAUDE.md` for full architecture overview. This file covers what is specific to this project.

## One-line purpose

Local admin tool. Adds and lists tracks. Two modes: classic CLI (`add` / `list` / `help`) and Terminal.Gui interactive interface (`gui`). Has direct access to both the SQL DB and the FileDatabase (it's not a network client — it runs on the same machine as the databases).

## Why this is allowed to bypass the API

CLI is a **local single-user admin tool**, run on the same machine as the databases by an admin. It consumes `DeepDrftWeb.Services` and `DeepDrftContent.Services` directly without going through HTTP. Browsers never get to bypass — the APIs are for them. **Don't extend this pattern to network clients.**

## Layout

```
DeepDrftCli/
├── Services/
│   ├── CliService.cs                  # Main CLI command dispatcher
│   └── GuiService.cs                  # Terminal.Gui interface
├── Models/
│   └── CliSettings.cs                 # Config POCO (ConnectionString, VaultPath)
├── Program.cs                         # Entry point, DI setup
├── environment/
│   ├── connections.json               # The actual config file (not appsettings.json)
│   └── config.json                    # Placeholder, currently unused
└── DeepDrftCli.csproj
```

## Configuration

`CliSettings` (loaded from `environment/connections.json`):

```csharp
public class CliSettings
{
    public required string ConnectionString { get; set; }  // SQLite path
    public required string VaultPath { get; set; }         // FileDatabase root
}
```

**The config file is `environment/connections.json`, not `appsettings.json`.** Example:

```json
{
  "CliSettings": {
    "ConnectionString": "Data Source=../Database/deepdrft.db",
    "VaultPath": "../Database/Vaults"
  }
}
```

Paths are resolved relative to `AppDomain.CurrentDomain.BaseDirectory`.

## DI wiring (Program.cs)

Registers:
- `DeepDrftContext` with SQLite (from connection string)
- `FileDatabase` singleton (awaited on init via `FileDatabase.FromAsync`)
- `TrackRepository`, `DeepDrftWeb.Services.TrackService` (SQL side)
- `AudioProcessor`, `DeepDrftContent.Services.TrackService` (content side)
- `CliService`, `GuiService`
- Console logging

All services are scoped or singletons. The app waits for `FileDatabase.FromAsync` on startup (blocking), so the vault is ready before any command runs.

## Mode dispatch

`Program.cs` checks the first argument:
- `gui` or `--gui`: runs `GuiService.RunAsync()` → Terminal.Gui interactive interface.
- Anything else: runs `CliService.RunAsync(args)` → classic CLI command parsing.

## CLI commands (classic mode)

### add <wav-file> <track-name> <artist> [album] [genre] [release-date]

Positional arguments. Adds a track:

```bash
DeepDrftCli add "/path/to/song.wav" "My Song" "Artist Name" "Album Title" "Rock" "2024-01-15"
```

Release date format: `YYYY-MM-DD`. Optional: album, genre, release date.

### add <wav-file> -i|--interactive [defaults...]

Interactive mode. Prompts for each field; command-line args become defaults:

```bash
DeepDrftCli add "/path/to/song.wav" -i "Artist Name"
```

Prompts: "Track Name? [default] → ", "Artist? [default] → ", etc.

### list

Formats and displays all tracks from SQL:

```bash
DeepDrftCli list
```

Table output: ID, Name, Artist, Album, Genre, ReleaseDate, EntryKey, ImagePath.

### help, --help, -h

Shows command list.

### gui (or --gui)

Launches the Terminal.Gui interactive interface (separate mode).

## GUI mode (Terminal.Gui)

Launches a full interactive terminal UI with:
- **DeepDrft brand color scheme** (Magenta/Purple/Pink).
- **Track list** (scrollable, selectable, deletable).
- **Status pane** showing feedback and errors.
- **Persistent hotkey legend** (F1=Help, A=Add, D=Delete, Q=Quit, etc.).
- **Add dialog** with file browser and track metadata entry.
- **Keyboard navigation**: arrow keys to navigate, Enter to select, escape to cancel.

`GuiService.RunAsync()` creates a `Window`, populates controls, and runs the main loop.

## Dual-database add flow (critical contract)

This is the seam where both databases are written — **must be idempotent and crash-safe**:

1. `DeepDrftContent.Services.TrackService.AddTrackFromWavAsync(filePath)`:
   - Validates file is `.wav` and readable.
   - Calls `AudioProcessor.ProcessWavFileAsync(filePath)` → `AudioBinary` (duration, bitrate extracted).
   - Generates a GUID entry key.
   - Ensures the `tracks` vault exists.
   - Calls `FileDatabase.RegisterResourceAsync("tracks", entryKey, audioBinary)`.
   - Returns a populated `TrackEntity` (with `Id = 0` since not yet in SQL).

2. `DeepDrftWeb.Services.TrackService.Create(trackEntity)`:
   - Saves the entity to SQLite.
   - Returns the persisted entity with `Id` assigned.

**If step 1 succeeds and step 2 fails, the audio is orphaned in the vault.** There is no compensating rollback today. The CLI logs both results and exits with status code indicating overall success/failure.

## File validation

- **Extension**: must be `.wav` (only WAV files are supported).
- **Existence**: checked before processing.
- **Readability**: attempted during processing.
- **WAV structure**: validated by `AudioProcessor` (RIFF/WAVE/PCM header parsing).

Non-WAV files are rejected with a clear error message.

## Release date format

Only `YYYY-MM-DD` is accepted. Parsing is strict:

```csharp
DateOnly.ParseExact(releaseDateStr, "yyyy-MM-dd")
```

Invalid dates result in an error. Optional fields (if not provided or invalid) are set to `null`.

## Publishing

Build configuration in `.csproj`:
- `PublishSingleFile=true`: Produces a single executable.
- `SelfContained` and `IncludeNativeLibrariesForSelfExtract` are **commented out** — publish is framework-dependent (requires .NET runtime installed).

To publish:

```bash
dotnet publish DeepDrftCli -c Release -o ./bin/Release/publish
```

Result is a single `.exe` (Windows) or binary (Linux) that consumes the host framework.

## Service registration patterns

All service calls return `Result` or `ResultContainer<T>`. CLI checks `Success` and `Messages` to display feedback:

```csharp
var result = await _webTrackService.Create(trackEntity);
if (result.Success && result.Value != null)
{
    Console.WriteLine($"✓ Saved to SQL with ID {result.Value.Id}");
}
else
{
    Console.WriteLine($"✗ SQL save failed: {string.Join("; ", result.Messages.Select(m => m.Message))}");
}
```

## Development commands

```bash
# Build
dotnet build DeepDrftCli

# Run classic CLI mode
dotnet run --project DeepDrftCli -- add "test.wav" "Test Track" "Test Artist"
dotnet run --project DeepDrftCli -- list
dotnet run --project DeepDrftCli -- help

# Run GUI mode
dotnet run --project DeepDrftCli -- gui

# Build for single-file publish
dotnet publish DeepDrftCli -c Release -o ./bin/Release/publish
```

## Important patterns

- **No HTTP**: CLI talks directly to databases. No HTTP clients needed.
- **Async/await**: All database operations are async. Even CLI mode runs in an async context.
- **Error swallowing in FileDatabase**: Vault operations return `null` / `false`. CLI must check return values.
- **Local-only**: CLI has no authentication or CORS. It runs locally as an admin tool.
- **Rollback**: Adding a track is not transactional across the two databases. If one fails, the other may have partially succeeded (orphaned audio in the vault). Plan accordingly.

## Configuration

- `environment/connections.json`: **required** at runtime. Must contain `CliSettings` with `ConnectionString` and `VaultPath`.
- `environment/config.json`: Placeholder, currently unused. Keep it as a placeholder for future expansion.
- No `appsettings.json` — don't add one.

When working with this project, focus on the dual-database consistency of the add flow, CLI argument parsing, and maintaining the interactive Terminal.Gui mode as a first-class interface (not a second-class citizen).
