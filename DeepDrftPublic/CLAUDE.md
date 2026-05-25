# CLAUDE.md - DeepDrftPublic

Guidance for working in the DeepDrftPublic project (the Blazor Web App host).

See the root `CLAUDE.md` for full architecture overview. This file covers what is specific to this project.

## One-line purpose

The Blazor Web App host. Owns a browser-facing proxy controller for `api/track/*` (metadata and audio streaming), MudBlazor theme prerender, and TypeScript→JS audio interop.

## What lives here now (only)

- `Program.cs`, `Startup.cs`: HTTP host config, DI wiring, port binding.
- `Controllers/TrackProxyController.cs`: Thin proxy controller at `[Route("api/track")]`. Two actions: `GET api/track/page` (proxies paged track metadata) and `GET api/track/{trackId}` (proxies audio streaming without buffering, forwards `offset` query param for seek-beyond-buffer). Uses `RegisterForDispose` for clean connection cleanup.
- `Services/DarkModeService.cs`: Server-side dark-mode prerender (reads `darkMode` cookie, seeds `DarkModeSettings.IsDarkMode` via `IHttpContextAccessor`, carries to WASM via `PersistentComponentState`).
- `Components/App.razor`: Root component with `@rendermode="InteractiveAuto"`. Calls `DarkModeService.InitializeAsync()` in `OnInitialized`.
- `Components/Pages/Error.razor`: Error fallback.
- `Interop/audio/`: TypeScript sources (one module per responsibility: `AudioContextManager.ts`, `StreamDecoder.ts`, `PlaybackScheduler.ts`, `SpectrumAnalyzer.ts`, `AudioPlayer.ts`, `index.ts`). Compiled to `wwwroot/js/audio/` via `Microsoft.TypeScript.MSBuild`. `tsconfig.json` **must not** be copied to output. In dev, raw `.ts` served from `/Interop/` for source-map debugging.
- `wwwroot/`: Static assets (compiled JS, CSS, fonts, images, favicons).

## What does NOT live here anymore

- `TrackDirectDataService` — deleted; no in-process data adapter.
- `DeepDrftContext`, `TrackRepository`, `TrackService`, `Configurations/`, `Migrations/` — all in `DeepDrftData` (consumed only by DeepDrftAPI).
- Any FileDatabase code — that lives in `DeepDrftContent`.
- EF Core registration, SQL connection string — DeepDrftPublic has no data layer.

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

Dual-mode wiring in `DeepDrftPublic.Client.Startup`:

- Named clients `"DeepDrft.API"` (SQL metadata) and `"DeepDrft.Content"` (binary audio).
- **Server-side** (SSR prerender): Both clients point directly at DeepDrftAPI via base addresses from `appsettings.json` (`ApiUrls:ContentApi`, `ApiUrls:SqlApi`). These are loaded from `environment/api.json` (via `CredentialTools.ResolvePathOrThrow`) in `Program.cs`.
- **WASM runtime**: Both clients point to `HostEnvironment.BaseAddress` (the Blazor host), so browser requests proxy through `TrackProxyController` to DeepDrftAPI.
- `Startup.ConfigureApiHttpClient` and `Startup.ConfigureContentServices` are static methods called from both the server `Program.cs` and the WASM `Program.cs`. At runtime, WASM overrides the base address to `HostEnvironment.BaseAddress`.

Server-side `Program.cs` adds:
- MudBlazor (`AddMudServices`)
- Controllers (`AddControllers()` and `MapControllers()`)
- Render-mode components
- SignalR tuning (if needed)
- Forwarded headers
- Calls to `Startup.ConfigureApiHttpClient` / `ConfigureContentServices` / `ConfigureDomainServices`

## Reverse-proxy support

`UseForwardedHeaders()` runs first in the pipeline. HTTPS redirect is conditionally disabled via `ForwardedHeaders:DisableHttpsRedirection` so the app can sit behind nginx without forcing HTTPS at the host level.

## The proxy controller

`TrackProxyController` in `Controllers/` is the only HTTP controller. It is a thin proxy only — no domain logic, no data layer. The WASM client points both named HttpClients (`"DeepDrft.API"` and `"DeepDrft.Content"`) at the Blazor host's base address, so all browser requests route through this controller to DeepDrftAPI. Server-side SSR calls DeepDrftAPI directly (server-to-server) via the same named clients — no proxy hop on the server side.

The proxy forwards two public, unauthenticated routes:
- `GET api/track/page` — paged metadata listing
- `GET api/track/{trackId}` — WAV audio streaming (handles `offset` param for seek-beyond-buffer)

Both actions use `HttpCompletionOption.ResponseHeadersRead` for streaming efficiency. Audio streaming registers the upstream response with `HttpContext.Response.RegisterForDispose()` so the stream is properly cleaned up after the response body is sent.

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

- `appsettings.json`: Dev defaults for `ApiUrls:*` (DeepDrftAPI base addresses), `Logging:*`, `AllowedHosts`, `ForwardedHeaders`. Port binding via `Kestrel:Endpoints` or `ASPNETCORE_URLS`.
- `environment/api.json`: Secrets file (via `CredentialTools.ResolvePathOrThrow`) with production DeepDrftAPI URLs (`ApiUrls:ContentApi`, `ApiUrls:SqlApi`).
- MudBlazor theme (`MainLayout.razor` in client): bespoke light ("Charleston in the Day") and dark ("Lowcountry Summer Nights") palettes.
- No `wwwroot/` changes during normal development — TS → JS compilation is automatic.

## Important patterns

This project is a Blazor host with a proxy layer. The proxy controller is a thin HTTP boundary — no domain logic, no data layer. All domain operations happen in DeepDrftAPI; this controller only forwards public, unauthenticated track routes. When working here, focus on the render surface (components, middleware, config), prerender coordination, and keeping the proxy transparent. New domain logic belongs in `DeepDrftData` / `DeepDrftAPI`.
