# DeepDrftHome — Project Context

Living orientation doc for what this repo is, how it is currently shaped, and where it appears headed. Sits alongside the root `CLAUDE.md` (operational guidance) — this file is the product/architecture view.

> **Drift notice.** The root `CLAUDE.md` and every folder-level `CLAUDE.md` currently in the tree describe the project as `.NET 9`. The most recent commit upgraded all projects to `.NET 10` (every `.csproj` now targets `net10.0`, packages pinned at `10.0.1`). Until those docs are refreshed, treat any framework-version claim in them as stale. The other staleness items are listed at the bottom of this file.

---

## 1. What this project is

DeepDrftHome is the home + listening surface for **DeepDrft**, a two-person electronic music collective based in Charleston, SC (per `DeepDrftWeb.Client/Pages/Home.razor`). The product is, at minimum:

- A public-facing site (hero, about, "experience" features).
- A **track gallery** that browses a library of WAV recordings, plays them in-browser with a persistent dock-style player, and supports seek (including seek beyond what's been streamed so far).
- An admin CLI for adding tracks (Terminal.Gui or scripted), running locally against the same dual-database substrate the site uses.

The interesting engineering bet is the **dual-database split**: structured track metadata in SQLite via EF Core, and binary media + per-vault indexes in a hand-rolled `FileDatabase` that lives on disk. The split is enforced across two ASP.NET Core hosts so that the browser never reaches the database directly.

---

## 2. Solution shape (current)

Eight projects in `DeepDrftHome.sln`, plus an external `NetBlocks` referenced from `C:\lib\NetBlocks\`.

```
DeepDrftWeb                 ASP.NET Core host. Blazor Web App (Server + WASM render modes).
                            Owns the SQL-backed API (api/track/page), MudBlazor theme/host,
                            TypeScript→JS audio interop sources under Interop/.
DeepDrftWeb.Client          Blazor WebAssembly assembly. All interactive UI lives here —
                            pages, controls, player services, dark-mode/theme plumbing,
                            HTTP clients for both backends.
DeepDrftWeb.Services        Class library. EF Core: DeepDrftContext, TrackConfiguration,
                            Migrations, TrackRepository, TrackService. Sharable between
                            the web host and the CLI (avoids duplicating data-access).

DeepDrftContent             ASP.NET Core host. Binary content API (api/track/{id}).
                            ApiKey middleware, CORS, ForwardedHeaders. Returns audio bytes
                            (with optional byte offset) and accepts PUT of AudioBinaryDto.
DeepDrftContent.Services    Class library. The FileDatabase implementation in full
                            (Models, Services, Utils, Abstractions, Constants),
                            WavOffsetService, AudioProcessor, TrackService (the content-side
                            orchestrator that processes WAVs and stores them in a vault).

DeepDrftModels              Shared contracts: TrackEntity, TrackDto, PagingParameters<T>,
                            PagedResult<T>. The only project all three layers reference.

DeepDrftCli                 Console app. Two modes: classic `add` / `list` / `help` and
                            `gui` (Terminal.Gui). Consumes BOTH service libraries directly
                            (it's a local admin tool, not a network client).

DeepDrftTests               NUnit. Covers the FileDatabase, MediaVault, IndexSystem,
                            MediaVaultFactory, SimpleMediaTypeRegistry, utility code, and
                            model behaviour. References DeepDrftContent.Services.

NetBlocks (external)        Result patterns: Result, ResultContainer<T>, ApiResult<T>,
                            ApiResultDto<T>. Referenced via absolute path.
```

Two stray .sln files (`WebAPI.sln`, `WebUI.sln`, `CLI.sln`) exist at the root alongside `DeepDrftHome.sln`. `DeepDrftHome.sln` is the canonical solution; the others appear to be subsets.

---

## 3. Architectural decisions worth remembering

### 3.1 Dual-database, two hosts, one client

The system is partitioned so the browser never touches storage directly:

```
DeepDrftWeb.Client (WASM)
    │
    ├── HttpClient "DeepDrft.API"     ──►  DeepDrftWeb host   ──►  EF Core / SQLite
    │   (paged track metadata)
    │
    └── HttpClient "DeepDrft.Content" ──►  DeepDrftContent    ──►  FileDatabase / disk
        (audio bytes + offset stream)      (ApiKey required for PUT)
```

The two HttpClients are named, configured in `DeepDrftWeb.Client.Startup`, and base addresses are passed in from server-host configuration (`ApiUrls:ContentApi`). Read endpoints in DeepDrftContent (`GET api/track/{id}`) are **unauthenticated** — only mutating endpoints carry `[ApiKeyAuthorize]`. CORS is enforced per `CorsSettings.AllowedOrigins`.

### 3.2 Service projects vs. host projects

The split between `DeepDrftWeb` / `DeepDrftWeb.Services` (and the same for Content) is deliberate: the host projects own the HTTP surface, configuration, and DI wiring, while the *Services* projects are plain class libraries holding the domain logic. This is what lets `DeepDrftCli` reuse both `DeepDrftWeb.Services.TrackService` and `DeepDrftContent.Services.TrackService` without taking a dependency on either ASP.NET host. New domain logic should land in the *.Services projects unless it is genuinely host-specific (controllers, middleware, render-mode configuration, theme prerender).

### 3.3 FileDatabase as a first-class subsystem

The `FileDatabase` (in `DeepDrftContent.Services/FileDatabase/`) is the unusual piece. It is a port of a TypeScript system (see in-tree `FileDatabase/README.md`) and is the only place binary content lives.

- A **FileDatabase** is a directory containing typed **MediaVaults**.
- Each MediaVault has its own JSON `index` file that lists entries and their per-entry metadata.
- Vaults are typed via `MediaVaultType` (`Media | Image | Audio`). The vault type is recorded in `VaultIndex` and rehydrated on startup.
- Entry keys are user-supplied strings, sanitised to `[a-zA-Z0-9]` plus `-` and concatenated with the file extension to produce on-disk filenames.
- Resources implement a binary hierarchy: `FileBinary → MediaBinary (+ Extension/MIME) → AudioBinary (+ Duration, Bitrate) | ImageBinary (+ AspectRatio)`. DTO variants ship as base64-over-JSON.
- An `IndexWatcher` (FileSystemWatcher) reloads a vault's index when an external process (e.g. the CLI) writes to it, so the web host stays consistent without restart.
- Error-handling philosophy: load/register operations **swallow exceptions and return `null`/`false`** to match the TypeScript original. Callers must check return values, not catch.

The only authorised callers of FileDatabase APIs are the DeepDrftContent host and the CLI — anything that runs in the browser must go through the HTTP endpoints.

### 3.4 TrackEntity is a join, not a content blob

`TrackEntity` (in `DeepDrftModels`) holds *only* metadata. The link to binary content is the `EntryKey` string, which is the entry id inside the `tracks` audio vault in the FileDatabase. The current schema:

```csharp
public class TrackEntity
{
    public long Id { get; set; }
    public required string EntryKey { get; set; }   // FileDatabase entry id, vault = "tracks"
    public required string TrackName { get; set; }
    public required string Artist { get; set; }
    public string? Album { get; set; }
    public string? Genre { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public string? ImagePath { get; set; }
}
```

Note: many existing docs (every folder `CLAUDE.md`, plus the FileDatabase README) still refer to the older `MediaPath` name. The migration `20250904233927_Initial.cs` already ships the `entry_key` column, so `MediaPath` is purely a docs-staleness artefact.

### 3.5 Streaming-first audio playback

The player is not a fetch-then-play model. The flow is:

1. Client calls `GET api/track/{id}` on DeepDrftContent and receives the WAV bytes as a stream (`HttpCompletionOption.ResponseHeadersRead`).
2. `StreamingAudioPlayerService` reads the stream in adaptive 16–64 KB chunks and pushes each chunk into JS via `AudioInteropService.processStreamingChunk`.
3. The TypeScript `StreamDecoder` parses the WAV header, decodes chunks to `AudioBuffer`s, and the `PlaybackScheduler` schedules them on a single Web Audio graph.
4. Playback starts as soon as a configurable minimum number of buffers is queued; UI duration is set from the parsed WAV header (not waiting for the full file).
5. **Seek beyond buffer**: if a seek target is past what's been decoded, the client issues a new `GET api/track/{id}?offset={byteOffset}` request. The server's `WavOffsetService` block-aligns the offset, synthesises a fresh 44-byte WAV header sized for the remaining data, and streams `[new header][data from offset]`. The player tears down and re-initialises its decoder for the new stream.

This is the seam where the FileDatabase's "everything is a complete blob in memory" model meets the browser's "I need to stream and seek" model. Keep this seam clean — it is the most architecturally load-bearing part of the playback path.

### 3.6 Theming, dark mode, and prerender

- MudBlazor is the UI framework. Light and dark palettes are bespoke ("Charleston in the Day" / "Lowcountry Summer Nights") and defined inline in `MainLayout.razor`.
- Dark mode toggles via a long-lived cookie (`darkMode`, 365 days), set client-side via JS interop.
- During server prerender, `DarkModeService` (in `DeepDrftWeb`) reads the cookie and seeds `DarkModeSettings.IsDarkMode`, which carries the value into the WASM render via `PersistentComponentState`. This is what avoids a "wrong theme flash" on initial paint. The `DarkModeSettings` lives in `DeepDrftWeb.Client.Common` because it is consumed by both the server `DarkModeService` and client components.
- Typography uses three Google Fonts (Bodoni Moda, Cormorant, DM Sans). The hand-rolled gas-lamp icon (lit/unlit) lives in `DDIcons.cs` and is what swaps in the dark-mode toggle.

### 3.7 TypeScript interop, not raw JS

The audio interop is authored in TypeScript under `DeepDrftWeb/Interop/audio/` and compiled into `wwwroot/js/audio/` via `Microsoft.TypeScript.MSBuild`. The split is intentional: AudioContextManager / StreamDecoder / PlaybackScheduler / SpectrumAnalyzer / AudioPlayer are each their own module, and `index.ts` glues them onto `window.DeepDrftAudio` for Blazor to invoke. The `tsconfig.json` is configured for ES module interop and is **not** copied to output.

In dev, the host serves the original `.ts` sources at `/Interop/...` for source-map debugging.

---

## 4. Where the system is today

Recent commits (newest first):

- `style simplification and publish upgrades for dotnet 10`
- `Styles & Home Page Content Cleanup Mobile Menu System & Dark Mode Cookie Theme Draft`
- `Theming Draft 2`
- `2026 Deep DRFT Theme Draft 1 WIP`
- `Spectrum Visualizer for player & Layout`

Three observations:

1. **The current arc is presentation, not capability.** The last five commits are framework upgrade, theming, content/layout cleanup, mobile menu, dark-mode persistence, and the spectrum visualiser. The playback substrate, streaming, and seek-beyond-buffer machinery landed earlier and is stable enough to support cosmetic iteration on top.
2. **The "Track Gallery" is the only real page.** `/tracks` is the working surface; `/` is marketing copy. Nav (in `Pages.cs`) defines only `Home` + `Track Gallery`.
3. **Content surface is narrow on purpose.** The DeepDrftContent API exposes exactly two routes: `GET api/track/{id}` (with optional `offset`) and `PUT api/track/{id}` (ApiKey). There is no listing endpoint there; listing lives on DeepDrftWeb because listings are SQL queries.

---

## 5. Likely directions (inferred, not committed)

Captured here so the next round of planning has a starting point — none of this is decided.

- **More vault types in active use.** `MediaVaultType.Image` exists end-to-end (tests cover it) but the production surface only registers a `tracks` vault of type `Audio`. The path to releases/albums probably runs through images first (cover art via `ImagePath`, which is currently a free-form URL string).
- **More than one collection view.** The `TrackCard` already conditionally renders `ImagePath`, `Album`, `Genre`, `ReleaseDate` — the data shape supports album-grouped or genre-filtered views without schema work.
- **Upload from the web side, not just the CLI.** The CLI is currently the only producer of tracks. A web-side upload would re-use `DeepDrftContent.Services.TrackService.AddTrackFromWavAsync` and pair it with a `TrackService.Create` on the SQL side. The `[ApiKeyAuthorize]` middleware on `PUT api/track/{id}` is already in place.
- **Live/session content.** The home page advertises "Live Sessions" and "Video Content (coming soon)". No data model exists for these yet; they would likely need new vault types (`MediaVaultType.Media` is the obvious home for video) and new entity tables.
- **Non-WAV formats.** Today the producer side is WAV-only (`AudioProcessor.ProcessWavFileAsync` validates RIFF/WAVE/PCM). `MimeTypeExtensions` already knows mp3/flac/aac/ogg/m4a — the gap is a processor per format and a decoder strategy in the JS player (currently WAV-specific).
- **Search / filter on the gallery.** `TracksViewModel` exposes `SortBy` / `IsDescending` but no filter. `TrackService.GetPaged` accepts only sort, not filter. Adding filter would be a natural next step on the same pagination contract.

---

## 6. Conventions that should hold

- **Plan docs at the repo root.** `CONTEXT.md` (this file), and when work in flight warrants them, `PLAN.md` / `TODO.md` / `COMPLETED.md`. Completed `PLAN.md` items move to `COMPLETED.md` rather than being deleted.
- **`*.Services` libraries own domain logic.** Host projects only do HTTP/wiring/render concerns. New repositories, services, processors, and storage code go in `DeepDrftWeb.Services` or `DeepDrftContent.Services`.
- **Shared contracts in `DeepDrftModels` only.** If a type crosses a project boundary, it lives in `DeepDrftModels`. Project-specific DTOs (e.g. `AudioBinaryDto` for the content API) stay local to where they serialise.
- **No direct DB from network clients.** The browser must not import EF or speak to SQLite. All access goes through the two API hosts. (The CLI is allowed direct DB access because it is not a network client.)
- **One source of truth per concept, multiple views over it.** The dark-mode flow (cookie → `DarkModeSettings` → server `DarkModeService` for prerender + client `DarkModeCookieService` for runtime + persistent state across the boundary) is a small example of this: one truth, distinct rendering paths. New view modes for tracks should consume the same `TrackEntity` / `PagedResult<TrackEntity>` and differ only at the rendering layer.
- **Result types from NetBlocks.** Services return `Result`, `ResultContainer<T>`, or `ApiResult<T>` rather than throwing for expected failure modes. Catch at the service boundary, surface via the result.

---

## 7. Staleness in existing docs (for doc-keeper to address)

Captured so the next sweep of folder-level `CLAUDE.md` files can correct in one pass.

- Every folder `CLAUDE.md` says ".NET 9" / "ASP.NET Core 9.0"; reality is `net10.0` across the board.
- `DeepDrftModels/CLAUDE.md` and `DeepDrftContent.Services/FileDatabase/README.md` reference `TrackEntity.MediaPath`; the field is `EntryKey` and the column is `entry_key`.
- `DeepDrftContent/CLAUDE.md` describes a `FileDatabase/` tree inside `DeepDrftContent/`; that tree has moved entirely to `DeepDrftContent.Services/FileDatabase/`. The DeepDrftContent host now contains only `Controllers/`, `Middleware/`, `Models/` (settings POCOs), `environment/`, `Program.cs`, `Startup.cs`.
- `DeepDrftContent/CLAUDE.md` documents only the PUT endpoint; the production API now also has `GET api/track/{id}?offset=` (unauthenticated read, with `WavOffsetService` for offset streaming).
- `DeepDrftWeb/CLAUDE.md` describes EF Core, repositories, services, migrations as living inside `DeepDrftWeb/Data` and `DeepDrftWeb/Services`. They have all moved to `DeepDrftWeb.Services`. The only things still in `DeepDrftWeb` are `Controllers/TrackController.cs`, `Services/DarkModeService.cs`, `Startup.cs`, `Program.cs`, `Components/`, `Interop/`, `wwwroot/`.
- `DeepDrftWeb.Client/CLAUDE.md` lists the `Pages/` directory as containing `Counter.razor` / `Weather.razor` (demo); those are gone. The real client structure is `Pages/Home.razor` + `Pages/TracksView.razor`, plus the `Controls/AudioPlayerBar/` cluster, `Controls/AudioPlayerProvider.razor`, `Services/AudioInteropService.cs` + `AudioPlayerService.cs` + `StreamingAudioPlayerService.cs` + `IPlayerService.cs` + dark-mode services, `Common/DarkModeSettings.cs` + `Common/DDIcons.cs`, and `Layout/Pages.cs` + `Layout/DeepDrftMenu.razor`.
- The `DeepDrftWeb.Services` and `DeepDrftContent.Services` projects have **no** `CLAUDE.md` yet — they are where most of the domain logic actually lives, so this is the biggest gap.
- `DeepDrftCli/CLAUDE.md` references `appsettings.json`; the CLI actually loads `environment/connections.json` into `CliSettings` (with `ConnectionString` and `VaultPath`). The "Available Commands" section is otherwise current, including the `gui` Terminal.Gui mode and interactive `add`.
- `DeepDrftContent.Services/FileDatabase/README.md` (an in-tree dev README, not a CLAUDE.md) refers to `ImageDirectoryVault`; the type is `ImageVault`. It also describes `EntryKey` as removed in favour of strings, which is accurate, but its diagram still says "FileDatabase.csproj (.NET 9.0)" — the FileDatabase no longer has its own csproj at all (it's a subdirectory of `DeepDrftContent.Services`).
