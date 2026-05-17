# CLAUDE.md - DeepDrftWeb.Client

Guidance for working in the DeepDrftWeb.Client project (the Blazor WebAssembly assembly).

See the root `CLAUDE.md` for full architecture overview. This file covers what is specific to this project.

## One-line purpose

All interactive UI for the site. Blazor WebAssembly. Pages, controls, the streaming audio player stack, theme/dark-mode plumbing, HTTP clients for both backends.

## Actual structure

- `Pages/`: Routable components. `Home.razor` (hero/about), `TracksView.razor` (track gallery with pagination/sorting). **No demo pages** (`Counter.razor`, `Weather.razor` do not exist).
- `Layout/`: `MainLayout.razor` (root layout, wraps in `AudioPlayerProvider`, hosts theme switcher), `DeepDrftMenu.razor` (branded menu bar), `NavMenu.razor` (nav list), `Pages.cs` (centralised nav index — `MenuPages` for header, `AllPages` for exhaustive list).
- `Controls/`: Reusable components.
  - `TrackCard.razor`: Individual track display (image, name, artist, album, genre, release date).
  - `TracksGallery.razor`: Responsive grid of `TrackCard` items (MudBlazor `MudGrid` with breakpoints).
  - `AppNavLink.razor`: Nav link with active-page highlight.
  - `AudioPlayerProvider.razor`: Cascading host for `IPlayerService`. Everything inside it gets the player via `[CascadingParameter]`.
  - `AudioPlayerBar.razor`: Dock UI at the bottom (play/pause/seek/volume).
  - `SpectrumVisualizer.razor`: Bar-graph spectrum display, driven by `getSpectrumData` JS callback.
- `Services/`: Audio player + dark-mode services.
  - `IPlayerService` / `IStreamingPlayerService`: Contracts exposed to UI.
  - `AudioPlayerService`: Abstract base (lifecycle, initialise, select track, play/pause/stop/seek/volume).
  - `StreamingAudioPlayerService`: Production implementation. Chunked stream from `TrackMediaClient`, adaptive 16–64 KB buffer, early-playback, **seek-beyond-buffer** via offset request to the content API.
  - `AudioInteropService`: JS interop wrapper over `window.DeepDrftAudio`. Manages `DotNetObjectReference` lifetimes for progress, end-of-playback, spectrum callbacks.
  - Dark-mode services: `DarkModeServiceBase` (cookie name constant), `DarkModeCookieService` (JS cookie read/write).
- `Clients/`: HTTP API clients.
  - `TrackClient`: SQL metadata API. Uses named `IHttpClientFactory` client `"DeepDrft.API"`. Async methods like `GetPageAsync(pageNumber, pageSize, sortColumn, sortDescending)` → `ApiResult<PagedResult<TrackEntity>>`.
  - `TrackMediaClient`: Content API. Uses named `IHttpClientFactory` client `"DeepDrft.Content"`. Methods like `GetAudioStreamAsync(trackId, offset)` → `Stream`.
- `ViewModels/`: Component state.
  - `TracksViewModel`: Scoped. Holds current page, page size, sort column, descending flag. `SetPage(pageNumber)` calls `TrackClient.GetPageAsync` and updates. Registered in `Startup.ConfigureDomainServices`.
- `Common/`: Shared utilities.
  - `DarkModeSettings.cs`: `[PersistentState]`-annotated class (single source of truth for dark mode in the client). Registered scoped.
  - `DDIcons.cs`: Hand-rolled SVG icons (gas-lamp lit/unlit for dark mode toggle).
- `Program.cs`: WASM entry point. Calls `Startup.ConfigureApiHttpClient`, `ConfigureContentServices`, `ConfigureDomainServices`.
- `_Imports.razor`: Global using statements and component imports.

## Two HTTP clients pattern

Both clients are configured in `Startup.cs` (static methods called from **both** server and WASM `Program.cs`):

- `TrackClient` uses `"DeepDrft.API"` (base address from `appsettings.json` `ApiUrls:SqlApi`). Fetches paginated metadata.
- `TrackMediaClient` uses `"DeepDrft.Content"` (base address from `appsettings.json` `ApiUrls:ContentApi`). Streams audio bytes, optionally with offset.

Both are configured with JSON serializer settings (case-insensitive property matching). The dual-client pattern keeps concerns separated: one for structured data, one for binary streaming.

## Audio player stack (deepest part of the codebase)

### Contracts
- `IPlayerService`: Initialize, SelectTrack, Play, Pause, Stop, Seek, SetVolume. Sync interface.
- `IStreamingPlayerService`: Extends above. SelectTrackStreaming(track) starts the chunked stream flow.

### Implementation
- `AudioPlayerService` (abstract base): Lifecycle. Stores current track, playback state, volume. Derived classes implement `SelectTrackStreaming` / `SelectTrackImmediate`.
- `StreamingAudioPlayerService` (production): Constructor takes `TrackMediaClient`, `AudioInteropService`, logger. `SelectTrackStreaming`:
  1. Calls `TrackMediaClient.GetAudioStreamAsync(trackId, offset: 0)`.
  2. `StreamingAudioPlayerService.StreamAudioAsync` reads chunks (16–64 KB adaptive), pushes each via `AudioInteropService.ProcessStreamingChunkAsync` (JS interop call).
  3. TypeScript `StreamDecoder` parses WAV header (first chunk), decodes subsequent chunks to `AudioBuffer`s.
  4. `PlaybackScheduler` schedules buffers on Web Audio `AudioContext`.
  5. Playback starts as soon as a configurable min buffer count is queued.
  6. **Seek beyond buffer**: if seek target is past the decoded range, `Seek(position)` calls `TrackMediaClient.GetAudioStreamAsync(trackId, offset: byteOffset)`. Server's `WavOffsetService` synthesises a new 44-byte WAV header and streams from the offset. Player tears down and re-initialises decoder for the new stream.

### Interop bridge
- `AudioInteropService.ProcessStreamingChunkAsync(chunk)` calls JS `window.DeepDrftAudio.processStreamingChunk(chunk)` and awaits the Promise.
- `AudioInteropService` also manages callback registrations for progress (fired by `PlaybackScheduler`), end-of-playback (fired by `PlaybackScheduler`), and spectrum data (fired by `SpectrumAnalyzer`). Each callback is a `DotNetObjectReference` to a delegate.

### Component integration
- `AudioPlayerProvider.razor` is the cascading host. It injects `IPlayerService` (resolved to `StreamingAudioPlayerService` in DI), stores it in a cascade, and keeps it alive across navigation.
- `AudioPlayerBar.razor` is the dock UI. It cascades the player, binds buttons to `Play()` / `Pause()` / `Seek()` / `SetVolume()`, and displays current time / duration / progress bar.
- `SpectrumVisualizer.razor` calls `AudioInteropService.GetSpectrumData()` on a timer, receives bar heights, renders via MudBlazor `MudChart` or custom canvas.
- `TracksView.razor` injects `TracksViewModel` + cascaded `IPlayerService`. `PlayTrack(track)` calls `PlayerService.SelectTrack(track)` (which resolves to `StreamingAudioPlayerService.SelectTrackStreaming(track)`).

## Dark-mode plumbing

- `DarkModeSettings` (`Common/`): `[PersistentState]`-annotated class with `IsDarkMode` property. Registered scoped in `Startup.ConfigureDomainServices`. Single source of truth in the client.
- `DarkModeServiceBase`: Holds the cookie name constant (`"darkMode"`).
- `DarkModeCookieService`: Reads/writes the cookie via JS (`document.cookie` interop). Calls `DarkModeSettings.IsDarkMode = value` when the cookie changes or user toggles the button.
- Server-side `DarkModeService` (in `DeepDrftWeb`, **not here**): Reads the cookie during prerender, seeds the `DarkModeSettings` instance, rounds it through `PersistentComponentState` to the client.
- `MainLayout.razor`: Wraps entire layout in `CascadingValue` of `DarkModeSettings`, so all children see the current dark-mode state. The dark-mode toggle button (hand-rolled lit/unlit gas-lamp icon from `DDIcons.cs`) calls `DarkModeCookieService.ToggleDarkModeAsync()`.

The flow ensures the first paint uses the correct theme (no flash), and toggling the button persists the setting to a 365-day cookie.

## MVVM convention

Component state lives in ViewModels (registered scoped in DI). Components render and dispatch only.

- `TracksViewModel`: Holds page number, page size, sort column, descending flag. `SetPage(pageNumber)` is the command. `TracksView.razor` injects it and calls `SetPage`.
- New VMs go in `ViewModels/` and register in `Startup.ConfigureDomainServices`.

## Theming convention

- Bespoke `PaletteLight` / `PaletteDark` defined inline in `MainLayout.razor` (MudBlazor theme objects).
- CSS classes prefixed `deepdrft-` live in `DeepDrftWeb/wwwroot/styles/deepdrft-styles.css` (shared across server and client).
- Custom SVG icons: `Common/DDIcons.cs` (hand-rolled gas-lamp, etc.).

## Development commands

```bash
# The client runs as part of the DeepDrftWeb host:
dotnet run --project DeepDrftWeb

# Watch during development (rebuilds WASM as you change .cs/.razor/.ts files):
dotnet watch run --project DeepDrftWeb

# Build just the client (for verification):
dotnet build DeepDrftWeb.Client

# Run client-specific tests (if any; currently none exist):
dotnet test DeepDrftTests/
```

## Configuration

- `Program.cs`: Entry point. Calls `Startup.ConfigureApiHttpClient` (registers named clients), `ConfigureContentServices` (same), `ConfigureDomainServices` (registers services like `TracksViewModel`, `DarkModeSettings`, `AudioPlayerService`).
- Both `Startup` methods are static and called from **both** the server `DeepDrftWeb/Program.cs` and the client `Program.cs`, ensuring prerender and runtime DI are identical.
- No `appsettings.json` in the WASM assembly — config comes from the server `appsettings.json` via HTTP or is hardcoded.

## Important patterns

- **Cascading parameters**: `AudioPlayerProvider` cascades `IPlayerService`. All children (including `MainLayout` and pages) access it via `[CascadingParameter] IPlayerService Player { get; set; }`.
- **Result types**: Clients return `ApiResult<T>` from NetBlocks. UI checks `Success` before using `Value`.
- **Async/await**: All operations are async.
- **Stream consumption**: `TrackMediaClient.GetAudioStreamAsync` returns a `Stream` (not fully buffered). `StreamingAudioPlayerService` reads it in chunks to avoid memory pressure on large files.

When working with this project, maintain the separation between presentation (Razor components) and logic (ViewModels/Clients), follow the established audio player architecture, and respect the dark-mode round-trip (cookie → DarkModeSettings → PersistentComponentState → client).
