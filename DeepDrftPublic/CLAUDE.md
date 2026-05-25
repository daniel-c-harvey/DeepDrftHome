# CLAUDE.md - DeepDrftPublic

Guidance for working in the DeepDrftPublic project (the Blazor Web App host).

See the root `CLAUDE.md` for full architecture overview. This file covers what is specific to this project.

## One-line purpose

The Blazor Web App host. Pure HTTP render surface (render-mode wiring, no controllers), MudBlazor theme prerender, TypeScript→JS audio interop. Fetches track metadata from DeepDrftAPI via HTTP.

## What lives here now (only)

- `Program.cs`, `Startup.cs`: HTTP host config, DI wiring, port binding.
- `Services/DarkModeService.cs`: Server-side dark-mode prerender (reads `darkMode` cookie, seeds `DarkModeSettings.IsDarkMode` via `IHttpContextAccessor`, carries to WASM via `PersistentComponentState`).
- `Components/App.razor`: Root component with `@rendermode="InteractiveAuto"`. Calls `DarkModeService.InitializeAsync()` in `OnInitialized`.
- `Components/Pages/Error.razor`: Error fallback.
- `Interop/audio/`: TypeScript sources (one module per responsibility: `AudioContextManager.ts`, `StreamDecoder.ts`, `PlaybackScheduler.ts`, `SpectrumAnalyzer.ts`, `AudioPlayer.ts`, `index.ts`). Compiled to `wwwroot/js/audio/` via `Microsoft.TypeScript.MSBuild`. `tsconfig.json` **must not** be copied to output. In dev, raw `.ts` served from `/Interop/` for source-map debugging.
- `wwwroot/`: Static assets (compiled JS, CSS, fonts, images, favicons).

## What does NOT live here anymore

- `TrackController` — deleted; track metadata now comes from DeepDrftAPI via HTTP.
- `TrackDirectDataService` — deleted; no in-process data adapter.
- `DeepDrftContext`, `TrackRepository`, `TrackService`, `Configurations/`, `Migrations/` — all in `DeepDrftData` (consumed only by DeepDrftAPI).
- Any FileDatabase code — that lives in `DeepDrftContent`.
- EF Core registration, SQL connection string — DeepDrftPublic has no data layer.
- `environment/connections.json` dependency — removed.

## Blazor Web App render modes

Hybrid Blazor with `AddInteractiveServerComponents()` + `AddInteractiveWebAssemblyComponents()`.

- Root component is `<Routes @rendermode="InteractiveAuto" />` from `Components/App.razor`.
- WASM render-mode loads `DeepDrftPublic.Client._Imports` as an additional assembly.
- **New routable pages go in `DeepDrftPublic.Client/Pages`, not here** — the client project owns the interactive UI.

Server-side prerender happens before WASM kicks in. Dark mode, CORS, forwarded headers, and MudBlazor setup must all tolerate this split.

## Dark-mode prerender bridge

`DarkModeService` in this project reads the `darkMode` cookie via `IHttpContextAccessor` in `App.razor`'s `OnInitialized` and seeds `DarkModeSettings.IsDarkMode`. This setting is registered in `DeepDrftPublic.Client.Startup.ConfigureDomainServices`. The setting carries over to WASM via `PersistentComponentState` in `MainLayout.razor`.

The flow ensures the first paint uses the correct theme (no flash).

## TypeScript interop pipeline

Audio interop is TypeScript, not raw JS:

- Sources live in `Interop/audio/` with one module per responsibility.
- Compiled to `wwwroot/js/` via `Microsoft.TypeScript.MSBuild`.
- `index.ts` exposes all modules onto `window.DeepDrftAudio` for Blazor to invoke.
- `tsconfig.json` configured for ES module interop and must **not** be copied to output.
- In development, raw `.ts` is served from `/Interop/` for source-map debugging.

Blazor calls TypeScript via `AudioInteropService.ts` (a JS interop wrapper in `DeepDrftPublic.Client`), which manages `DotNetObjectReference` lifetimes for progress, end-of-playback, and spectrum callbacks.

## HTTP client wiring

Configured in `DeepDrftPublic.Client.Startup`:

- Named clients `"DeepDrft.API"` (SQL metadata) and `"DeepDrft.Content"` (binary audio).
- Both clients point to DeepDrftAPI. Base addresses passed in from `appsettings.json` (`ApiUrls:ContentApi`, `ApiUrls:SqlApi`).
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

## No controllers

DeepDrftPublic has no HTTP controllers. It is a pure Blazor render host. Track metadata endpoints are served by DeepDrftAPI (see `DeepDrftAPI/CLAUDE.md` for endpoint details).

## Development commands

```bash
# Run the web host (includes WASM from DeepDrftPublic.Client)
dotnet run --project DeepDrftPublic

# Watch during development
dotnet watch run --project DeepDrftPublic

# Build
dotnet build DeepDrftPublic
```

## Configuration

- `appsettings.json`: `ApiUrls:*` (DeepDrftAPI base addresses), `Logging:*`, `AllowedHosts`, `ForwardedHeaders`. Port binding via `Kestrel:Endpoints` or `ASPNETCORE_URLS`.
- No secrets files — DeepDrftPublic has no data layer or API credentials.
- MudBlazor theme (`MainLayout.razor` in client): bespoke light ("Charleston in the Day") and dark ("Lowcountry Summer Nights") palettes.
- No `wwwroot/` changes during normal development — TS → JS compilation is automatic.

## Important patterns

This project is a render host only. All data operations go through HTTP to DeepDrftAPI. When working with this project, focus on the render surface (components, middleware, config) and prerender coordination. New domain logic belongs in `DeepDrftData` / `DeepDrftAPI`.
