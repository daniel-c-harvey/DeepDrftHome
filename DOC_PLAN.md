# DOC_PLAN.md — Folder-level CLAUDE.md plan

Brief for doc-keeper. Specifies, folder by folder, what each `CLAUDE.md` should cover and the conventions/patterns to surface. **Authored on a cold-storage read of the current code state on 2026-05-16.** See `CONTEXT.md` for the full architecture orientation.

---

## Ground rules for every folder-level CLAUDE.md

These apply across the whole sweep — do not restate them inside each file unless the local context genuinely differs.

1. **Target framework is `net10.0`** for every project. Do not write ".NET 9" anywhere. Package versions are `10.0.1` family across EF Core, ASP.NET, and Extensions; MudBlazor is `8.15.0`; NUnit is `4.4.0`.
2. **Lead with what's true now, not what's aspirational.** Folder CLAUDE.md files are operational guidance for an agent walking into that directory cold. If something is "potential future usage", omit it.
3. **Cross-reference the root.** Root `CLAUDE.md` already explains the solution shape, dual-database model, and high-level patterns. Folder CLAUDE.mds should pick up *from there* and document what makes that folder specific. No re-running the full architecture overview in every file.
4. **No CLAUDE.md in build output / migrations / leaf static-asset folders.** This plan is deliberately scoped to the subfolders below; do not add files for `obj/`, `bin/`, `Migrations/`, `wwwroot/`, `environment/`, `Properties/`. Migrations are EF-generated and not interesting for agent guidance.
5. **Reference paths, not contents, for code.** If the CLAUDE.md needs to show how something is used, prefer a one-line "see `path/file.cs`" over a copied 30-line snippet. The code is the source of truth — folder CLAUDE.mds drift the moment they replicate it. Small inline examples (a 3–5 line signature or call) are fine when the call shape itself is the convention being documented.
6. **The eight existing folder CLAUDE.mds need rewrites, not patches.** All eight contain framework-version drift and most contain structural drift (see CONTEXT.md §7). Treat the existing files as raw material, not as a baseline to incrementally edit.

---

## Files to write / rewrite

There are **eight** folder CLAUDE.mds in scope. Five already exist and need rewrites; three are new (the two `*.Services` libraries and a recommendation about the `FileDatabase` README being either retired or replaced with a CLAUDE.md).

| Folder | Status | Priority |
|---|---|---|
| `DeepDrftWeb/` | Rewrite — structural drift | high |
| `DeepDrftWeb.Client/` | Rewrite — major drift (page list, player stack) | high |
| `DeepDrftWeb.Services/` | **New** — no CLAUDE.md exists; domain logic lives here | high |
| `DeepDrftContent/` | Rewrite — FileDatabase tree has moved out | high |
| `DeepDrftContent.Services/` | **New** — no CLAUDE.md; FileDatabase + processors live here | high |
| `DeepDrftModels/` | Rewrite — `MediaPath`→`EntryKey` rename, framework version | medium |
| `DeepDrftCli/` | Rewrite — config file name, GUI/CLI dual mode is mostly accurate | medium |
| `DeepDrftTests/` | Rewrite — framework version, otherwise mostly accurate | low |

Plus one judgement call:

- `DeepDrftContent.Services/FileDatabase/README.md` is an in-tree developer README, not a CLAUDE.md. It is stale (`ImageDirectoryVault` is `ImageVault`; the `.csproj (.NET 9.0)` line is wrong since FileDatabase no longer has its own csproj). **Recommendation:** doc-keeper should ask Daniel whether to retire it in favour of a `DeepDrftContent.Services/FileDatabase/CLAUDE.md` or leave it as a long-form README and just refresh the inaccuracies. Default to leaving the README and not adding a separate CLAUDE.md inside the FileDatabase subtree — the parent `DeepDrftContent.Services/CLAUDE.md` should cover what an agent needs to know to operate inside `FileDatabase/`.

---

## Per-folder briefs

### `DeepDrftWeb/CLAUDE.md`

**One-line purpose:** The Blazor Web App host. Owns HTTP surface (one controller + render-mode wiring), MudBlazor theme prerender, TypeScript→JS audio interop, and the SQL-side `api/track/page` endpoint. **Domain logic lives elsewhere** (`DeepDrftWeb.Services`).

**Cover:**

- What lives here now (only): `Program.cs`, `Startup.cs`, `Controllers/TrackController.cs`, `Services/DarkModeService.cs`, `Components/App.razor` + `Components/Pages/Error.razor`, `Interop/` (TypeScript sources), `wwwroot/` (compiled JS, CSS, fonts, images).
- What does *not* live here anymore: `DeepDrftContext`, `TrackRepository`, `TrackService`, `Configurations/`, `Migrations/`. All moved to `DeepDrftWeb.Services`. Don't write new repositories or EF code in this project.
- The render-mode model: Blazor Web App with `AddInteractiveServerComponents()` + `AddInteractiveWebAssemblyComponents()`. The root component is `<Routes @rendermode="InteractiveAuto" />` from `Components/App.razor`, and the WASM render-mode loads `DeepDrftWeb.Client._Imports` as an additional assembly. New routable pages should land in `DeepDrftWeb.Client/Pages`, not here.
- The dark-mode prerender bridge: `DarkModeService` reads the `darkMode` cookie via `IHttpContextAccessor` in `App.razor`'s `OnInitialized` and seeds `DarkModeSettings.IsDarkMode` (registered in `DeepDrftWeb.Client.Startup.ConfigureDomainServices`). The setting carries over to WASM via `PersistentComponentState` (`MainLayout.razor`).
- The TypeScript interop pipeline: sources live in `Interop/audio/` (one module per responsibility — `AudioContextManager`, `StreamDecoder`, `PlaybackScheduler`, `SpectrumAnalyzer`, `AudioPlayer`, plus `index.ts` that exposes `window.DeepDrftAudio`). They compile to `wwwroot/js/` via `Microsoft.TypeScript.MSBuild`. `tsconfig.json` must not be copied to output. In development, raw `.ts` is served from `/Interop/` for source-map debugging.
- HTTP client wiring lives mostly in `DeepDrftWeb.Client.Startup`. Server-side `Program.cs` only adds MudBlazor, controllers, render-mode components, SignalR tuning, forwarded-headers config, and calls the client's `Startup.ConfigureApiHttpClient` / `ConfigureContentServices` / `ConfigureDomainServices`.
- Reverse-proxy support: `UseForwardedHeaders` runs first; HTTPS redirect is conditionally disabled via `ForwardedHeaders:DisableHttpsRedirection` so the app can sit behind nginx.
- The one controller (`TrackController`) is a thin wrapper: `GET api/track/page?pageNumber&pageSize&sortColumn&sortDescending` → `DeepDrftWeb.Services.TrackService.GetPaged` → `ApiResultDto<PagedResult<TrackEntity>>`. If you're adding new SQL endpoints, this is the file; if you're adding new logic, that goes in `DeepDrftWeb.Services`.

**Do NOT include:**

- Generic Blazor / EF Core tutorials.
- Anything implying repositories or DbContext belong in this project.
- Build/run commands for the whole solution — keep those at the root.

---

### `DeepDrftWeb.Client/CLAUDE.md`

**One-line purpose:** All interactive UI for the site. Blazor WebAssembly. Pages, controls, the streaming audio player stack, theme/dark-mode plumbing, HTTP clients for both backends.

**Cover:**

- Actual structure: `Pages/` (`Home.razor`, `TracksView.razor`), `Layout/` (`MainLayout.razor`, `DeepDrftMenu.razor`, `NavMenu.razor`, `Pages.cs`), `Controls/` (`TrackCard`, `TracksGallery`, `AppNavLink`, `AudioPlayerProvider`, `AudioPlayerBar/` cluster), `Services/` (audio player services + dark-mode services + streaming error handler), `Clients/` (`TrackClient` for SQL API, `TrackMediaClient` for content API), `ViewModels/` (`TracksViewModel`), `Common/` (`DarkModeSettings`, `DDIcons`).
- The "old demo pages" (`Counter.razor`, `Weather.razor`) do not exist. The site has exactly two routable pages today: `/` and `/tracks`. Nav is centralised in `Layout/Pages.cs` (`MenuPages` + `AllPages`).
- The two HTTP clients pattern: `TrackClient` uses named `IHttpClientFactory` client `"DeepDrft.API"`, `TrackMediaClient` uses `"DeepDrft.Content"`. The clients are configured in `Startup.ConfigureApiHttpClient` and `Startup.ConfigureContentServices` — both static methods called from BOTH the server `Program.cs` and the WASM `Program.cs` so prerender and runtime see the same DI.
- The audio player stack (this is the deepest part of the project and most likely to be edited):
  - `IPlayerService` / `IStreamingPlayerService` (the contracts exposed to UI).
  - `AudioPlayerService` (abstract base: lifecycle, initialise, select track, play/pause/stop/seek/volume).
  - `StreamingAudioPlayerService` (the production implementation: chunked stream from `TrackMediaClient`, adaptive 16–64 KB buffer, early-playback, **seek-beyond-buffer** via a new offset request).
  - `AudioInteropService` (JS interop wrapper over `window.DeepDrftAudio`; manages `DotNetObjectReference` lifetimes for progress, end-of-playback, and spectrum callbacks).
  - `AudioPlayerProvider.razor` is the cascading host; everything inside it gets the player via `[CascadingParameter]`. `AudioPlayerBar.razor` is the dock UI; `SpectrumVisualizer.razor` is the bar-graph driven by `getSpectrumData`. `MainLayout.razor` wraps the layout in `AudioPlayerProvider`, so the player survives navigation.
- Dark mode plumbing:
  - `DarkModeSettings` (in `Common/`) is `[PersistentState]`-annotated and registered scoped — it is the single source of truth in the client.
  - `DarkModeServiceBase` holds the cookie name constant.
  - `DarkModeCookieService` writes the cookie via JS `document.cookie` and reads `DarkModeSettings`.
  - The server-side `DarkModeService` (in `DeepDrftWeb`, NOT here) reads the cookie during prerender. The settings instance round-trips via `PersistentComponentState`.
- `TracksView.razor` flow: injects `TracksViewModel` + cascaded `IPlayerService`. `SetPage` calls `TrackClient.GetPage` and updates the VM. `PlayTrack` calls `PlayerService.SelectTrack(track)` — which under the hood resolves to `StreamingAudioPlayerService.SelectTrackStreaming(track)` and starts the chunked stream.
- MVVM convention: page state lives in a `ViewModel` (scoped DI), components only render and dispatch. `TracksViewModel` holds the current page, page size, sort. Add new VMs in `ViewModels/` and register in `Startup.ConfigureDomainServices`.
- Theming convention: bespoke `PaletteLight` / `PaletteDark` live inline in `MainLayout.razor`. CSS classes prefixed `deepdrft-` live in `DeepDrftWeb/wwwroot/styles/deepdrft-styles.css`. Custom SVG icons live in `Common/DDIcons.cs`.

**Do NOT include:**

- `Counter.razor` / `Weather.razor` references.
- "ASP.NET Core 9.0".
- Any claim that this project hosts a server or controllers — it is a class-libraryish WASM assembly consumed by `DeepDrftWeb`.

---

### `DeepDrftWeb.Services/CLAUDE.md` (new)

**One-line purpose:** SQL-side domain logic for tracks. EF Core context, configurations, migrations, repository, service, design-time factory. Consumed by both `DeepDrftWeb` and `DeepDrftCli`.

**Cover:**

- Why this project exists: separating domain from host so the CLI can reuse `TrackService` / `TrackRepository` / `DeepDrftContext` without referencing the ASP.NET host. New SQL-side domain code goes here, not in `DeepDrftWeb`.
- Layout: `Data/DeepDrftContext.cs`, `Data/DeepDrftContextFactory.cs` (design-time, hard-codes `../Database/deepdrft.db` for `dotnet ef`), `Data/Configurations/TrackConfiguration.cs`, `Migrations/`, `Repositories/TrackRepository.cs`, `TrackService.cs`.
- The Controller → Service → Repository → DbContext shape. Services return `Result` / `ResultContainer<T>` from NetBlocks; repositories throw / return raw types. Exceptions are caught at the service boundary.
- `TrackEntity` field reality: `Id`, `EntryKey` (required, max 100, the FileDatabase entry id), `TrackName` (max 200), `Artist` (max 200), `Album?` (max 200), `Genre?` (max 100), `ReleaseDate?` (`DateOnly`), `ImagePath?` (max 500). Column names are snake_case. Table is `track` (singular).
- Pagination convention: `TrackService.GetPaged` builds a `PagingParameters<TrackEntity>` with an `OrderBy` `Expression<Func<T, object>>`. Switch in `GetPaged` maps a string sort column to the expression. New sort columns extend this switch. Nulls sort to the end (via padded sentinel strings / `DateOnly.MaxValue`).
- EF migration commands (run from solution root): `dotnet ef migrations add <Name> --project DeepDrftWeb.Services --startup-project DeepDrftWeb` and `dotnet ef database update --project DeepDrftWeb.Services --startup-project DeepDrftWeb`. The design-time factory means you can also run `dotnet ef ... --project DeepDrftWeb.Services` standalone for local development.
- The migrations namespace is `DeepDrftWeb.Migrations` (a legacy name, retained for migration history continuity).
- Connection string source: `appsettings.json` → `ConnectionStrings:DefaultConnection` on the web side; `environment/connections.json` → `CliSettings:ConnectionString` on the CLI side. Both point at `../Database/deepdrft.db`.

**Do NOT include:**

- ASP.NET Core lifecycle, controllers, middleware — those are not in scope here.
- HTTP client setup or theming.

---

### `DeepDrftContent/CLAUDE.md`

**One-line purpose:** The binary content API host. ApiKey middleware, CORS, forwarded headers. Returns audio bytes (with optional WAV-aware offset) and accepts authenticated PUTs. **FileDatabase implementation lives in `DeepDrftContent.Services`, not here.**

**Cover:**

- What lives here now (only): `Program.cs`, `Startup.cs`, `Controllers/TrackController.cs`, `Middleware/ApiKeyAuthenticationMiddleware.cs`, `Middleware/ApiKeyAuthorizeAttribute.cs`, `Models/` (settings POCOs: `ApiKeySettings`, `CorsSettings`, `FileDatabaseSettings`), `environment/filedatabase.json`, `environment/apikey.json` (not in repo).
- What does *not* live here anymore: any `FileDatabase/`, `Services/`, `Processors/`, or media model code. All moved to `DeepDrftContent.Services`. Don't add new domain code in this project.
- The endpoint surface (exactly two, do not add a third without product approval):
  - `GET api/track/{trackId}?offset=0` — **unauthenticated**, returns the WAV bytes from the `tracks` vault. If `offset > 0`, `WavOffsetService.CreateOffsetStream` block-aligns the offset and synthesises a fresh 44-byte WAV header so the response is a valid standalone WAV starting from that byte position.
  - `PUT api/track/{trackId}` — **`[ApiKeyAuthorize]`**, accepts `AudioBinaryDto` (base64 buffer + size + mime + duration + bitrate), writes via `FileDatabase.RegisterResourceAsync` into the `tracks` vault.
- ApiKey middleware behaviour: only enforces on endpoints with `[ApiKeyAuthorize]` metadata. Reads header `ApiKey`. Returns 401 with body `"API Key was not provided"` or `"Unauthorized client"`. Configured via `environment/apikey.json` (`ApiKeySettings.ApiKey`).
- CORS: `CorsSettings.AllowedOrigins` is required (the app throws on startup if missing). Policy is named `ContentApiPolicy`. AllowCredentials + AllowAnyMethod + AllowAnyHeader.
- Forwarded headers are enabled only in `Production` (this differs from `DeepDrftWeb` which enables them always — be aware when debugging proxy issues).
- Startup wiring (`Startup.ConfigureDomainServices`): adds singleton `WavOffsetService`, loads `environment/filedatabase.json`, awaits `FileDatabase.FromAsync(VaultPath)`, registers it as singleton, and ensures the `tracks` vault exists (`MediaVaultType.Audio`). The vault is created on first boot, then reused.
- OpenAPI is only mapped in `Development`.

**Do NOT include:**

- The internals of how `FileDatabase` works — that's the job of `DeepDrftContent.Services/CLAUDE.md`.
- A directory tree showing `FileDatabase/Services/` inside this project; it is not there.

---

### `DeepDrftContent.Services/CLAUDE.md` (new)

**One-line purpose:** Binary-content domain logic. The FileDatabase implementation, audio processing, WAV stream-with-offset, and the content-side track service. Consumed by `DeepDrftContent` (the host) and `DeepDrftCli`.

**Cover:**

- Layout: `FileDatabase/` (the subsystem — `Abstractions/`, `Models/`, `Services/`, `Utils/`), `Audio/WavOffsetService.cs`, `Processors/AudioProcessor.cs`, `Constants/VaultConstants.cs`, `TrackService.cs`.
- FileDatabase model (refer to `DeepDrftContent.Services/FileDatabase/README.md` for the long-form rationale — it's a port of a TypeScript system):
  - `FileDatabase` is the root. Created via `FileDatabase.FromAsync(rootPath)`. Holds a `StructuralMap<string, MediaVault>` and an `IndexWatcher`. Implements `IDisposable`.
  - Each `MediaVault` is a subdirectory with its own JSON `index` file. Types: `Media | Image | Audio`. Concrete subclasses are `ImageVault` and `AudioVault` (the README's mention of `ImageDirectoryVault` is stale — the type is `ImageVault`).
  - Entry filenames: `{sanitized-key}{extension}`, where sanitisation is `Regex.Replace(entryKey, @"[^a-zA-Z0-9]", "-")`.
  - Binary hierarchy: `FileBinary → MediaBinary (+ Extension, MIME via `MimeTypeExtensions`) → AudioBinary (+ Duration, Bitrate) | ImageBinary (+ AspectRatio)`. Each has a matching `*Dto` for base64 JSON transport.
  - Index lifecycle: `IndexFactoryService` loads-or-creates `DirectoryIndex` (the root index) and `VaultIndex` (per-vault, records `MediaVaultType`). Saved as JSON via `FileUtils.PutObjectAsync`.
  - `IndexWatcher` uses `FileSystemWatcher` to detect external writes to a vault's `index` file (the CLI is the typical writer) and triggers `MediaVault.ReloadIndexAsync` so a long-running web host stays consistent.
  - **Error-handling philosophy** (load-bearing): public `Load*` / `Register*` operations swallow exceptions and return `null` / `false` to match the TypeScript original. Callers must check return values. Do not change this without a deliberate design pass.
- WAV offset service: `WavOffsetService.CreateOffsetStream(buffer, byteOffset)` parses the WAV header, block-aligns the offset, synthesises a new 44-byte header for the remaining data, and returns `[new header][data from offset]` as a `MemoryStream`. Used by the content API to serve seek-beyond-buffer requests. Block alignment is *required* for clean audio — do not bypass it.
- Audio processor: `AudioProcessor.ProcessWavFileAsync(filePath)` validates the RIFF/WAVE/PCM structure, parses fmt and data chunks, computes duration + bitrate, falls back to defaults (180s / 1411 kbps / 44.1 kHz / 16-bit stereo) on parse failure with a `Console.WriteLine` warning. PCM-only today; other formats are unsupported.
- Content-side `TrackService` (orchestrator): `AddTrackFromWavAsync` reads a WAV from disk, generates a GUID entry key, ensures the `tracks` vault exists, stores the audio, and returns a populated `TrackEntity` (without saving it to SQL — caller does that). `GetAudioBinaryAsync` reads it back. `InitializeTracksVaultAsync` is a safety call.
- `VaultConstants.Tracks = "tracks"` — the one vault name in production use. New vault names go here.

**Do NOT include:**

- The HTTP surface of `DeepDrftContent` — that belongs in `DeepDrftContent/CLAUDE.md`.
- A reproduction of the FileDatabase README's full design discussion — link to it instead.

---

### `DeepDrftModels/CLAUDE.md`

**One-line purpose:** Shared contracts. Entities, DTOs, pagination types. Every project references this; nothing else references the projects that reference this.

**Cover:**

- Layout: `Entities/TrackEntity.cs`, `DTOs/TrackDto.cs`, `Models/PagingParameters.cs`, `Models/PagedResult.cs`.
- `TrackEntity` fields (verbatim from current code — replace any older field list): `Id` (long PK), `EntryKey` (required string, FileDatabase entry id), `TrackName` (required), `Artist` (required), `Album?`, `Genre?`, `ReleaseDate?` (`DateOnly`), `ImagePath?`. **No `MediaPath` field exists.**
- `TrackDto` mirrors `TrackEntity`. Used only where DTO/entity separation is needed for serialisation; in practice both flow over the wire today.
- `PagingParameters` (base): `Page` (1-based, default 1), `PageSize` (default 20, capped at 100). The cap is a hard ceiling.
- `PagingParameters<T>`: adds `OrderBy: Expression<Func<T, object>>?` and `IsDescending`. `Skip` is computed `(Page - 1) * PageSize`. Type-safe sort expressions — strings only at the API boundary.
- `PagedResult<T>`: `Items`, `TotalCount`, `Page`, `PageSize`. Derived: `TotalPages`, `HasNextPage`, `HasPreviousPage`. Includes `PagedResult<T>.From<TOther>` for cross-type mapping.
- Convention: required reference fields use `required` modifier; optional reference fields are `?`. Don't relax `required` — it's the compile-time guarantee that prevents half-built entities reaching the database.
- This project also references `NetBlocks` only transitively (the actual NetBlocks types — `Result`, `ResultContainer<T>`, `ApiResult<T>`, `ApiResultDto<T>` — come into the services and clients directly).

**Do NOT include:**

- "Potential future usage in DeepDrftContent" — content already uses these.
- Generic C# tutorials on `required` or `Expression<T>`.

---

### `DeepDrftCli/CLAUDE.md`

**One-line purpose:** Local admin tool. Adds and lists tracks. Two modes: classic CLI (`add` / `list` / `help`) and a Terminal.Gui interactive interface (`gui`). Has direct access to both the SQL DB and the FileDatabase (it's not a network client).

**Cover:**

- Why this is allowed to bypass the API: it is a local single-user admin tool, run on the same machine as the databases. Browsers never get to bypass — the API is for them. Don't extend this pattern to network clients.
- Layout: `Program.cs`, `Services/CliService.cs`, `Services/GuiService.cs`, `Models/CliSettings.cs`, `environment/connections.json` (the actual config file — *not* `appsettings.json`).
- `CliSettings`: `ConnectionString` (SQLite path) + `VaultPath` (FileDatabase root). Loaded from `environment/connections.json` resolved against `AppDomain.CurrentDomain.BaseDirectory`. `environment/config.json` is also copied to output but currently unused — leave it as a placeholder.
- DI wiring (`Program.cs`): `DeepDrftContext` (SQLite), `FileDatabase` (singleton via `FromAsync`, blocking on init), `TrackRepository`, `DeepDrftWeb.Services.TrackService`, `AudioProcessor`, `DeepDrftContent.Services.TrackService`, `CliService`, `GuiService`. Logging is console-only.
- Mode dispatch: `gui` / `--gui` runs `GuiService.RunAsync`; everything else runs `CliService.RunAsync(args)`.
- CLI commands:
  - `add <wav-file> <track-name> <artist> [album] [genre] [release-date]` — classic positional.
  - `add <wav-file> -i|--interactive [...defaults]` — interactive prompts, command-line args become defaults.
  - `list` — formatted table.
  - `help` / `--help` / `-h`.
- GUI mode: Terminal.Gui application with a custom dark/brand colour scheme, list of tracks, status pane, persistent hotkey legend, and an add-track dialog.
- Dual-database flow for `add` (this is the critical contract — do not skip):
  1. `DeepDrftContent.Services.TrackService.AddTrackFromWavAsync` processes the WAV, generates an entry GUID, stores audio in the `tracks` vault, returns a populated `TrackEntity` (not yet saved to SQL).
  2. `DeepDrftWeb.Services.TrackService.Create` saves the entity to SQLite and returns the persisted version with `Id` assigned.
  3. If step 1 succeeds and step 2 fails, the audio is orphaned in the vault. There is no compensating rollback today — flag this if it comes up in planning.
- Publishing: `PublishSingleFile=true`. `SelfContained` and `IncludeNativeLibrariesForSelfExtract` are commented out — current publish is framework-dependent single file.
- Release date format is `YYYY-MM-DD`. Only `.wav` is accepted.

**Do NOT include:**

- Any claim that `appsettings.json` is the config file.
- Coverage of all GuiService implementation details — just the entry point and that it exists.

---

### `DeepDrftTests/CLAUDE.md`

**One-line purpose:** NUnit test project. Covers the FileDatabase subsystem end-to-end (the only piece of the codebase with non-trivial test coverage). References `DeepDrftContent.Services`.

**Cover:**

- Layout: one test class per subsystem area — `FileDatabaseTests`, `MediaVaultTests`, `MediaVaultFactoryTests`, `IndexSystemTests`, `SimpleMediaTypeRegistryTests`, `UtilityTests`, `ModelTests`. `TestData.cs` holds shared fixtures (a real 16x16 PNG byte buffer; factory helpers for `ImageBinary` / `AudioBinary`; named constants under `TestKeys` / `TestFiles`). `environment/filedatabase.json` is a test-config file copied at build.
- Test isolation pattern (used by `FileDatabaseTests` and others): each test creates a unique directory under `Path.GetTempPath() + "DeepDrftTests/{Guid}"` in `[SetUp]` and best-effort deletes it in `[TearDown]`. New tests touching the filesystem must follow this pattern — do not write into the real `Database/Vaults` path.
- The tests are the load-bearing documentation of the FileDatabase's behaviour. When changing FileDatabase semantics (especially the swallow-and-return-null contract, the entry-key sanitisation regex, the vault-type round-trip on index, or the index-watcher reload), the tests are where intent is anchored. Update them in the same change.
- `TestData.CreateTestAudioBinary` deliberately reuses PNG bytes as a mock audio buffer — the FileDatabase does not parse audio content, so any byte buffer with valid metadata round-trips correctly. Real WAV parsing is exercised by the CLI / content host, not the FileDatabase tests.
- Run commands:
  - All: `dotnet test DeepDrftTests/`.
  - One class: `dotnet test DeepDrftTests/ --filter "ClassName=FileDatabaseTests"`.
  - One method: `dotnet test DeepDrftTests/ --filter "Name=FileDatabase_CanBeCreatedAtSpecifiedLocation"`.
- What is NOT tested (be honest): `TrackService` (Web or Content), `TrackController` (Web or Content), `TrackClient` / `TrackMediaClient`, the audio player services, the dark-mode round-trip, the WAV offset service, the audio processor. Any planned work in those areas should consider whether tests need to land alongside.

**Do NOT include:**

- A claim that the test suite has "comprehensive coverage". It is comprehensive for FileDatabase and not present elsewhere.
- Code coverage instructions if they are not in active use — only document the commands actually run.

---

## Order of operations for doc-keeper

1. Rewrite `DeepDrftWeb/CLAUDE.md` and `DeepDrftWeb.Client/CLAUDE.md` first — they are the most-touched directories during ongoing UI work and have the highest drift.
2. Create `DeepDrftWeb.Services/CLAUDE.md` and `DeepDrftContent.Services/CLAUDE.md` — these are the biggest content gaps (no docs exist; most domain logic lives there).
3. Rewrite `DeepDrftContent/CLAUDE.md` — high drift, but the surface is small.
4. Rewrite `DeepDrftModels/CLAUDE.md` — small file, but the `MediaPath`→`EntryKey` correction matters everywhere it's wrong.
5. Rewrite `DeepDrftCli/CLAUDE.md` and `DeepDrftTests/CLAUDE.md` — lower priority; mostly polish + framework version.
6. Pause and check in with Daniel about `DeepDrftContent.Services/FileDatabase/README.md` — retire, keep, or replace.
