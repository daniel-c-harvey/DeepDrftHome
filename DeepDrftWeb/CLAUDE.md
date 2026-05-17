# CLAUDE.md - DeepDrftWeb

Guidance for working in the DeepDrftWeb project (the Blazor Web App host).

See the root `CLAUDE.md` for full architecture overview. This file covers what is specific to this project.

## One-line purpose

The Blazor Web App host. Owns HTTP surface (one controller + render-mode wiring), MudBlazor theme prerender, TypeScript→JS audio interop, and the SQL-side `api/track/page` endpoint. **Domain logic lives in `DeepDrftWeb.Services`.**

## What lives here now (only)

- `Program.cs`, `Startup.cs`: HTTP host config, DI wiring, port binding.
- `Controllers/TrackController.cs`: Single controller. `GET api/track/page?pageNumber&pageSize&sortColumn&sortDescending` → service call → `ApiResultDto<PagedResult<TrackEntity>>`.
- `Services/DarkModeService.cs`: Server-side dark-mode prerender (reads `darkMode` cookie, seeds `DarkModeSettings.IsDarkMode` via `IHttpContextAccessor`, carries to WASM via `PersistentComponentState`).
- `Components/App.razor`: Root component with `@rendermode="InteractiveAuto"`. Calls `DarkModeService.InitializeAsync()` in `OnInitialized`.
- `Components/Pages/Error.razor`: Error fallback.
- `Interop/audio/`: TypeScript sources (one module per responsibility: `AudioContextManager.ts`, `StreamDecoder.ts`, `PlaybackScheduler.ts`, `SpectrumAnalyzer.ts`, `AudioPlayer.ts`, `index.ts`). Compiled to `wwwroot/js/audio/` via `Microsoft.TypeScript.MSBuild`. `tsconfig.json` **must not** be copied to output. In dev, raw `.ts` served from `/Interop/` for source-map debugging.
- `wwwroot/`: Static assets (compiled JS, CSS, fonts, images, favicons).

## What does NOT live here anymore

- `DeepDrftContext`, `TrackRepository`, `TrackService`, `Configurations/`, `Migrations/` — all moved to `DeepDrftWeb.Services`. Do not add new repositories or EF code to this project.
- Any FileDatabase code — that lives in `DeepDrftContent.Services`.

## Blazor Web App render modes

Hybrid Blazor with `AddInteractiveServerComponents()` + `AddInteractiveWebAssemblyComponents()`.

- Root component is `<Routes @rendermode="InteractiveAuto" />` from `Components/App.razor`.
- WASM render-mode loads `DeepDrftWeb.Client._Imports` as an additional assembly.
- **New routable pages go in `DeepDrftWeb.Client/Pages`, not here** — the client project owns the interactive UI.

Server-side prerender happens before WASM kicks in. Dark mode, CORS, forwarded headers, and MudBlazor setup must all tolerate this split.

## Dark-mode prerender bridge

`DarkModeService` in this project reads the `darkMode` cookie via `IHttpContextAccessor` in `App.razor`'s `OnInitialized` and seeds `DarkModeSettings.IsDarkMode`. This setting is registered in `DeepDrftWeb.Client.Startup.ConfigureDomainServices`. The setting carries over to WASM via `PersistentComponentState` in `MainLayout.razor`.

The flow ensures the first paint uses the correct theme (no flash).

## TypeScript interop pipeline

Audio interop is TypeScript, not raw JS:

- Sources live in `Interop/audio/` with one module per responsibility.
- Compiled to `wwwroot/js/` via `Microsoft.TypeScript.MSBuild`.
- `index.ts` exposes all modules onto `window.DeepDrftAudio` for Blazor to invoke.
- `tsconfig.json` configured for ES module interop and must **not** be copied to output.
- In development, raw `.ts` is served from `/Interop/` for source-map debugging.

Blazor calls TypeScript via `AudioInteropService.ts` (a JS interop wrapper in `DeepDrftWeb.Client`), which manages `DotNetObjectReference` lifetimes for progress, end-of-playback, and spectrum callbacks.

## HTTP client wiring

Mostly in `DeepDrftWeb.Client.Startup`:

- Named clients `"DeepDrft.API"` (SQL metadata) and `"DeepDrft.Content"` (binary audio).
- Base addresses passed in from `appsettings.json` (`ApiUrls:ContentApi`, `ApiUrls:SqlApi`).
- `Startup.ConfigureApiHttpClient` and `Startup.ConfigureContentServices` are static methods called from **both** the server `Program.cs` and the WASM `Program.cs` so prerender and runtime see the same DI.

Server-side `Program.cs` adds:
- MudBlazor (`AddMudServices`)
- Controllers
- Render-mode components
- SignalR tuning (if needed)
- Forwarded headers
- Calls to `Startup.ConfigureApiHttpClient` / `ConfigureContentServices` / `ConfigureDomainServices`

## Reverse-proxy support

`UseForwardedHeaders()` runs first in the pipeline. HTTPS redirect is conditionally disabled via `ForwardedHeaders:DisableHttpsRedirection` so the app can sit behind nginx without forcing HTTPS at the host level.

## The one controller

`TrackController` is thin — it just deserializes query parameters, calls `DeepDrftWeb.Services.TrackService.GetPaged`, and wraps the result:

```csharp
[HttpGet("api/track/page")]
public async Task<ActionResult<ApiResultDto<PagedResult<TrackEntity>>>> GetPage(
    [FromQuery] int pageNumber = 1,
    [FromQuery] int pageSize = 20,
    [FromQuery] string? sortColumn = null,
    [FromQuery] bool sortDescending = false)
```

If you're adding new SQL endpoints, this is the file. If you're adding new logic, that goes in `DeepDrftWeb.Services/TrackService.cs`.

## Development commands

```bash
# Run the web host (includes WASM from DeepDrftWeb.Client)
dotnet run --project DeepDrftWeb

# Watch during development
dotnet watch run --project DeepDrftWeb

# Build
dotnet build DeepDrftWeb

# Add migration (run from solution root; creates in DeepDrftWeb.Services)
dotnet ef migrations add MigrationName --project DeepDrftWeb.Services --startup-project DeepDrftWeb
```

## Configuration

- `appsettings.json`: `ConnectionStrings:DefaultConnection` (SQLite path), `ApiUrls:*` (backend base addresses), logging, logging config. Port binding via `Kestrel:Endpoints` or `ASPNETCORE_URLS`.
- MudBlazor theme (`MainLayout.razor` in client): bespoke light ("Charleston in the Day") and dark ("Lowcountry Summer Nights") palettes.
- No `wwwroot/` changes during normal development — TS → JS compilation is automatic.

## Important patterns

All service calls in the controller return `ResultContainer<T>` or `Result`. The controller doesn't catch — it checks `Success` and returns 200/4xx/5xx accordingly. See `DeepDrftWeb.Services` for the contract.

When working with this project, focus on the host surface (controllers, middleware, config) and prerender coordination. New domain logic goes in `DeepDrftWeb.Services`.
