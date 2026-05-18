# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.

## Architecture Overview

DeepDrftHome is a **net10.0** solution consisting of eight projects implementing a dual-database media management system for the DeepDrft electronic music collective.

### Core Projects

- **DeepDrftWeb**: ASP.NET Core host. Blazor Web App with Server + WASM render modes. Owns the SQL-backed `api/track/page` endpoint, MudBlazor theme prerender, and TypeScript→JS audio interop.
- **DeepDrftWeb.Client**: Blazor WebAssembly assembly. All interactive UI (pages, player stack, dark-mode plumbing, HTTP clients for both backends).
- **DeepDrftWeb.Services**: Class library. EF Core domain logic: `DeepDrftContext`, `TrackConfiguration`, `Migrations`, `TrackRepository`, `TrackService`. Sharable between web host and CLI.
- **DeepDrftContent**: ASP.NET Core host. Binary content API (`GET api/track/{id}` unauthenticated, `PUT api/track/{id}` ApiKey-protected). Returns audio bytes with optional WAV-aware offset streaming.
- **DeepDrftContent.Services**: Class library. The FileDatabase implementation in full (Models, Services, Utils, Abstractions, Constants), `WavOffsetService`, `AudioProcessor`, content-side `TrackService`. Consumed by host and CLI.
- **DeepDrftModels**: Shared contracts. `TrackEntity`, `TrackDto`, `PagingParameters<T>`, `PagedResult<T>`. Every project references this.
- **DeepDrftCli**: Console app. Two modes: classic CLI (`add` / `list` / `help`) and Terminal.Gui (`gui`). Direct access to both databases (local admin tool, not a network client).
- **DeepDrftTests**: NUnit test suite. Comprehensive FileDatabase tests (vault creation, media storage, indexing, factory patterns, utilities). Integration-focused with temp-directory test isolation.

External: **NetBlocks** (absolute path `C:\lib\NetBlocks\`). Provides `Result`, `ResultContainer<T>`, `ApiResult<T>`, `ApiResultDto<T>`.

### Database Architecture

**Dual-database approach** — browser never reaches storage directly:

```
DeepDrftWeb.Client (WASM)
    ├── HttpClient "DeepDrft.API"     ──►  DeepDrftWeb host   ──►  EF Core / SQLite (metadata)
    └── HttpClient "DeepDrft.Content" ──►  DeepDrftContent    ──►  FileDatabase / disk (binary)
```

1. **SQL Database (SQLite)**: Metadata and track info via Entity Framework
   - Location: `../Database/deepdrft.db`
   - Entity: `TrackEntity` with `Id`, `EntryKey`, `TrackName`, `Artist`, `Album?`, `Genre?`, `ReleaseDate?`, `ImagePath?`
   - Context: `DeepDrftContext` in `DeepDrftWeb.Services`

2. **FileDatabase**: Custom file-based binary storage system
   - Location: `../Database/Vaults` (configurable via `filedatabase.json`)
   - Root contains typed **MediaVaults** (Media, Image, Audio)
   - Each vault has a JSON `index` file listing entries + per-entry metadata
   - Entries are user-supplied strings sanitized to `[a-zA-Z0-9-]` + file extension
   - Binary hierarchy: `FileBinary` → `MediaBinary` (+ Extension/MIME) → `AudioBinary` (+ Duration/Bitrate) | `ImageBinary` (+ AspectRatio)
   - **Error-handling philosophy**: public operations swallow exceptions and return `null`/`false` — callers must check return values, not catch.

## Key Architectural Decisions

### Service projects vs. host projects

The split between `DeepDrftWeb` / `DeepDrftWeb.Services` (and the same for Content) is deliberate: hosts own HTTP surface, config, DI wiring; `*.Services` are plain class libraries holding domain logic. This lets `DeepDrftCli` reuse both `TrackService` implementations without taking ASP.NET dependencies.

**New domain logic goes in `*.Services` unless genuinely host-specific** (controllers, middleware, render-mode config, theme prerender).

### TrackEntity is a join, not a content blob

`TrackEntity` holds *only* metadata. The link to binary content is `EntryKey` (string) — the entry id inside the `tracks` vault in FileDatabase. Dual-database add flow:

1. `DeepDrftContent.Services.TrackService.AddTrackFromWavAsync` processes WAV, generates entry GUID, stores audio in vault, returns unpersisted `TrackEntity`.
2. `DeepDrftWeb.Services.TrackService.Create` saves to SQLite and returns the persisted entity with `Id`.

If step 1 succeeds and step 2 fails, audio is orphaned in the vault (no rollback today).

### Streaming-first audio playback

The player is not fetch-then-play:

1. Client calls `GET api/track/{id}` on DeepDrftContent and receives WAV bytes as a stream (`HttpCompletionOption.ResponseHeadersRead`).
2. `StreamingAudioPlayerService` reads in adaptive 16–64 KB chunks, pushes each via `AudioInteropService.processStreamingChunk`.
3. TypeScript `StreamDecoder` parses WAV header, decodes chunks to `AudioBuffer`s. `PlaybackScheduler` schedules them on a Web Audio graph.
4. Playback starts as soon as a min buffer is queued; UI duration from parsed header (not waiting for full file).
5. **Seek beyond buffer**: if seek target is past what's decoded, client issues `GET api/track/{id}?offset={byteOffset}`. Server's `WavOffsetService` block-aligns offset, synthesises a fresh 44-byte WAV header, streams `[new header][data from offset]`. Player tears down and re-initialises decoder for the new stream.

Keep this seam clean — it is the most architecturally load-bearing part of the playback path.

### Theming and dark mode

- MudBlazor is the UI framework. Light and dark palettes (bespoke "Charleston in the Day" / "Lowcountry Summer Nights") defined inline in `MainLayout.razor`.
- Dark mode toggles via cookie (`darkMode`, 365 days). Client-side via JS interop.
- During server prerender, `DarkModeService` (in `DeepDrftWeb`) reads the cookie and seeds `DarkModeSettings.IsDarkMode`, which carries into WASM render via `PersistentComponentState`. Avoids "wrong theme flash" on initial paint.
- `DarkModeSettings` lives in `DeepDrftWeb.Client.Common` (consumed by both server prerender and client components).
- Typography: Google Fonts (Bodoni Moda, Cormorant, DM Sans). Hand-rolled gas-lamp icon (lit/unlit) lives in `DDIcons.cs`.

### TypeScript interop, not raw JS

Audio interop authored in TypeScript under `DeepDrftWeb/Interop/audio/`, compiled to `wwwroot/js/audio/` via `Microsoft.TypeScript.MSBuild`. One module per responsibility (AudioContextManager, StreamDecoder, PlaybackScheduler, SpectrumAnalyzer, AudioPlayer), plus `index.ts` exposing `window.DeepDrftAudio`. `tsconfig.json` is **not** copied to output. In dev, raw `.ts` served from `/Interop/` for source-map debugging.

## Development Commands

### Build and Test
```bash
# Build entire solution
dotnet build DeepDrftHome.sln

# Run all tests
dotnet test DeepDrftTests/

# Run specific test class
dotnet test DeepDrftTests/ --filter "ClassName=FileDatabaseTests"
```

### Running Applications
```bash
# Run main web application
dotnet run --project DeepDrftWeb

# Run content API
dotnet run --project DeepDrftContent

# Run CLI (classic mode)
dotnet run --project DeepDrftCli -- list

# Run CLI (GUI mode)
dotnet run --project DeepDrftCli -- gui
```

### Entity Framework (SQL Database)
```bash
# Add migration (from solution root)
dotnet ef migrations add MigrationName --project DeepDrftWeb.Services --startup-project DeepDrftWeb

# Update database
dotnet ef database update --project DeepDrftWeb.Services --startup-project DeepDrftWeb
```

## Key Configuration Files

All projects load secrets via `CredentialTools.ResolvePathOrThrow()` from gitignored `environment/` files:

- `DeepDrftWeb/appsettings.json`: Logging and URL config. Secrets loaded from `environment/apikey.json` (DeepDrftContent API key), `environment/connections.json` (SQL and Auth connection strings), `environment/authblocks.json` (AuthBlocks settings).
- `DeepDrftContent/environment/filedatabase.json`: FileDatabase vault path. Loaded via CredentialTools.
- `DeepDrftContent/environment/apikey.json`: API key. Loaded via CredentialTools (not in repo).
- `DeepDrftCli/environment/connections.json`: CLI config (`ConnectionString`, `VaultPath`). Loaded via CredentialTools.

## Folder-Level Guidance

Folder-level `CLAUDE.md` files provide specifics on structure, patterns, and commands for each project. Start with the project's folder `CLAUDE.md` when entering that directory. The root CLAUDE.md here sets the architectural context; folder files answer "what's in this folder and how do I work here?"

## Important Patterns

### Result Pattern (NetBlocks)

Services return `Result`, `ResultContainer<T>`, or `ApiResult<T>` rather than throwing for expected failure modes. Catch at service boundary, surface via result.

### Error Swallowing in FileDatabase

Public `Load*` / `Register*` operations in FileDatabase swallow exceptions and return `null` / `false`. Callers must check return values. This matches the TypeScript original and is load-bearing — do not change without a design pass.

### Pagination

Services build `PagingParameters<T>` with an `OrderBy` expression. Switch in the service maps a string sort column to the expression. New sort columns extend this switch. Nulls sort to end (padded sentinel strings / `DateOnly.MaxValue`).

## External Dependencies

- Entity Framework Core 10.0.1 (SQLite)
- MudBlazor 8.15.0
- NUnit 4.4.0
- NetBlocks (Result patterns)
- Microsoft.TypeScript.MSBuild (TS compilation)

See individual project files for detailed dependency lists and versions.
