# CLAUDE.md - DeepDrftContent.Services

Guidance for working in the DeepDrftContent.Services project (the binary-content domain logic).

See the root `CLAUDE.md` for full architecture overview. This file covers what is specific to this project.

## One-line purpose

Binary-content domain logic. The FileDatabase implementation in full (Models, Services, Utils, Abstractions, Constants), WAV stream-with-offset, audio processing, and the content-side track service. Consumed by `DeepDrftContent` (the host) and `DeepDrftCli` (the admin CLI).

## Layout

```
DeepDrftContent.Services/
├── FileDatabase/                       # The subsystem (port of TypeScript system)
│   ├── Abstractions/                   # Interfaces
│   ├── Models/                         # Data models, DTOs, enums
│   ├── Services/                       # FileDatabase, MediaVault, IndexSystem, IndexWatcher
│   └── Utils/                          # StructuralMap, StructuralSet, FileUtils
├── Audio/
│   └── WavOffsetService.cs             # Byte offset → valid WAV stream
├── Processors/
│   └── AudioProcessor.cs               # WAV file parsing, metadata extraction
├── Constants/
│   └── VaultConstants.cs               # Vault name definitions
├── TrackService.cs                     # Content-side orchestrator
└── DeepDrftContent.Services.csproj
```

## FileDatabase model (high-level)

See `FileDatabase/README.md` for the long-form design discussion — it's a port of a TypeScript system and has deep rationale. This section covers the essentials for an agent walking in cold.

### Core structure

- **FileDatabase**: Root object. Created via `FileDatabase.FromAsync(rootPath)`. Holds a collection of `MediaVault` instances and an `IndexWatcher`. Implements `IDisposable`. Singleton in the host.
- **MediaVault**: A subdirectory under the FileDatabase root. Each vault has its own JSON `index` file listing entries and per-entry metadata. Typed via `MediaVaultType` enum (`Media | Image | Audio`).
  - Concrete implementations: `ImageVault` (for images), `AudioVault` (for audio). Do not use `ImageDirectoryVault` (that's stale docs) — the type is `ImageVault`.
- **Entry filenames**: `{sanitized-key}{extension}`, where sanitisation is `Regex.Replace(entryKey, @"[^a-zA-Z0-9]", "-")`. So entry id `"my-song"` with extension `.wav` → filename `my-song.wav`.

### Binary hierarchy

```
FileBinary (base: byte buffer)
  └── MediaBinary (+ Extension: string, MIME type inferred via MimeTypeExtensions)
      ├── AudioBinary (+ Duration: double, Bitrate: int)
      └── ImageBinary (+ AspectRatio: double)
```

Each has a matching `*Dto` variant for base64 JSON transport (e.g., `AudioBinaryDto` with buffer encoded as base64).

### Index lifecycle

- **DirectoryIndex**: Root index file (at `{rootPath}/index`). Tracks which vaults exist.
- **VaultIndex**: Per-vault index (at `{vaultPath}/index`). Records `MediaVaultType` and lists all entries in that vault.
- Both are JSON files. Created/loaded via `IndexFactoryService`.
- When a file is written externally (e.g., the CLI calls `FileDatabase.RegisterResourceAsync` directly), the **IndexWatcher** detects the write to the vault's `index` file and triggers `MediaVault.ReloadIndexAsync`, so a long-running web host stays consistent without restart.

## Error-handling philosophy (load-bearing)

Public `Load*` / `Register*` operations **swallow exceptions and return `null` / `false`** to match the TypeScript original.

```csharp
public async Task<T?> LoadResourceAsync<T>(string vaultId, string entryId) where T : FileBinary
{
    try { /* load and deserialize */ }
    catch { return null; }  // Swallow, return null
}

public async Task<bool> RegisterResourceAsync(string vaultId, string entryId, FileBinary media)
{
    try { /* store and update index */ }
    catch { return false; }  // Swallow, return false
}
```

**Callers must check return values.** Do not change this without a deliberate design pass — it's embedded in all FileDatabase tests and client code.

## WAV offset service

`WavOffsetService.CreateOffsetStream(buffer, byteOffset)`:

1. Parses the WAV header from the buffer.
2. Block-aligns the byte offset to the nearest block boundary (required for clean audio — misalignment causes clicks).
3. Synthesises a new 44-byte WAV header sized for the remaining data (from offset to EOF).
4. Returns a `MemoryStream` containing `[new header][data from offset]`.

Used by the content API to serve seek-beyond-buffer requests. The player asks for a new stream at the byte offset it wants to seek to; the server returns a valid WAV that starts there.

**Block alignment is critical.** Do not bypass it. The WAV fmt chunk tells you the block size; use it.

## Audio processor

`AudioProcessor.ProcessWavFileAsync(filePath)`:

1. Validates the RIFF/WAVE/PCM structure.
2. Parses the fmt and data chunks.
3. Extracts duration (sample count / sample rate) and bitrate (file size / duration).
4. Returns `AudioBinary` with all metadata.
5. **Fallback**: If parsing fails, logs a warning and returns defaults (180s / 1411 kbps / 44.1 kHz / 16-bit stereo).

PCM-only today. Other formats (mp3, flac, aac, ogg, m4a) are listed in `MimeTypeExtensions` but not implemented. The processor validates RIFF/WAVE/PCM format — anything else is rejected.

## Content-side TrackService (orchestrator)

### AddTrackFromWavAsync(filePath)

1. Reads a WAV file from disk.
2. Calls `AudioProcessor.ProcessWavFileAsync` → `AudioBinary`.
3. Generates a GUID entry key (via `Guid.NewGuid().ToString()`).
4. Ensures the `tracks` vault exists (creates if missing).
5. Calls `FileDatabase.RegisterResourceAsync("tracks", entryKey, audioBinary)`.
6. Returns a populated `TrackEntity` (with `Id = 0` since it's not yet in SQL).

**Note**: The caller (CLI or web) is responsible for then saving this entity to SQL via `DeepDrftWeb.Services.TrackService.Create`. If the vault write succeeds and SQL write fails, audio is orphaned (no compensating rollback).

### GetAudioBinaryAsync(entryKey)

Reads audio from the `tracks` vault via `FileDatabase.LoadResourceAsync<AudioBinary>("tracks", entryKey)`. Returns `null` if not found or read fails.

### InitializeTracksVaultAsync()

Safety call to ensure the `tracks` vault exists (creates if missing). Called on host startup.

## Vault constants

`VaultConstants.Tracks = "tracks"` — the one vault name in production use. New vault names go here when adding new vault types (e.g., `VaultConstants.Images = "images"` if image uploads are added).

## Service registration

In `DeepDrftContent/Startup.ConfigureDomainServices()` and `DeepDrftCli/Program.cs`:

```csharp
services.AddSingleton<WavOffsetService>();
services.AddSingleton<FileDatabase>(/* from FileDatabase.FromAsync */);
services.AddScoped<AudioProcessor>();
services.AddScoped<TrackService>();  // DeepDrftContent.Services.TrackService
```

## Development commands

```bash
# Build
dotnet build DeepDrftContent.Services

# Run tests (FileDatabase tests cover vault/index/factory/utilities thoroughly)
dotnet test DeepDrftTests/

# Run CLI (which consumes this service)
dotnet run --project DeepDrftCli -- add myfile.wav "Track Name" "Artist"
```

## Important patterns

- **Async/await**: All FileDatabase operations are async. No sync methods.
- **Type safety**: Generic `LoadResourceAsync<T>` ensures callers know what they're loading.
- **Vault lifecycle**: Vaults are created on first boot, then reused. The `FileDatabase` singleton holds them in memory with live `IndexWatcher`es.
- **Entry sanitisation**: Keys are sanitised client-side (in the CLI and web host) *and* by `MediaVault` (defensive). Always sanitise before registering — it's the only way to ensure safe filenames.
- **Metadata hierarchy**: Use the appropriate media type (`AudioBinary`, `ImageBinary`) so downstream code can rely on the metadata. Don't store an audio file as a generic `MediaBinary` — use `AudioBinary` with duration/bitrate.

## What does NOT live here

- HTTP controllers or middleware — that's `DeepDrftContent`.
- SQL database code — that's `DeepDrftWeb.Services`.
- Blazor components or UI logic — that's `DeepDrftWeb.Client`.
- Configuration files (`appsettings.json`, `filedatabase.json`) — those are in the host project.

When working with this project, focus on the FileDatabase subsystem (the most complex piece of the codebase), audio processing, and the orchestration logic that bridges binary and SQL databases. The tests (in `DeepDrftTests/`) are the load-bearing documentation of FileDatabase behaviour — consult them when FileDatabase semantics are unclear.
