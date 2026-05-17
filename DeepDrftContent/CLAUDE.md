# CLAUDE.md - DeepDrftContent

Guidance for working in the DeepDrftContent project (the binary content API host).

See the root `CLAUDE.md` for full architecture overview. This file covers what is specific to this project.

## One-line purpose

The binary content API host. ApiKey middleware, CORS, forwarded headers. Returns audio bytes (with optional WAV-aware offset) and accepts authenticated PUTs. **FileDatabase implementation lives in `DeepDrftContent.Services`, not here.**

## What lives here now (only)

- `Program.cs`, `Startup.cs`: HTTP host config, DI wiring, middleware setup, port binding.
- `Controllers/TrackController.cs`: Two endpoints (see below).
- `Middleware/ApiKeyAuthenticationMiddleware.cs`, `Middleware/ApiKeyAuthorizeAttribute.cs`: ApiKey validation logic.
- `Models/`: Settings POCOs only (`ApiKeySettings`, `CorsSettings`, `FileDatabaseSettings`). No domain code.
- `environment/filedatabase.json`: FileDatabase vault path config (required).
- `environment/apikey.json`: API key (not in repo, must be created locally or at deployment).

## What does NOT live here anymore

- Any `FileDatabase/`, `Services/`, or `Processors/` code — all moved to `DeepDrftContent.Services`.
- Media models (`AudioBinary`, `ImageBinary`, etc.) — in `DeepDrftContent.Services`.
- `WavOffsetService` — in `DeepDrftContent.Services`.
- Don't add new domain code to this project.

## The endpoint surface (exactly two endpoints)

### GET api/track/{trackId}?offset=0 (unauthenticated)

Returns the WAV bytes from the `tracks` vault.

- **Query parameter `offset`** (optional, default 0): byte position to start streaming from.
- If `offset == 0`: streams the entire file from the beginning.
- If `offset > 0`: `WavOffsetService.CreateOffsetStream` block-aligns the offset and synthesises a fresh 44-byte WAV header so the response is a valid standalone WAV starting from that byte position. This is load-bearing for seek-beyond-buffer — the player asks for a new stream at the offset it wants to seek to, gets back a valid WAV that starts there, and tears down/re-initialises the decoder.
- Returns 404 if track not found. Returns 500 if vault operations fail (with error swallowing — the vault returns `null`).

### PUT api/track/{trackId} ([ApiKeyAuthorize])

**Authenticated endpoint.** Writes audio bytes to the `tracks` vault.

- **Header `ApiKey`**: required. Validated by `ApiKeyAuthenticationMiddleware`.
- **Body**: `AudioBinaryDto` (base64 buffer + size + mime + duration + bitrate).
- `TrackService.AddTrackFromWavAsync` (via DI) is NOT called here — the endpoint just validates and writes to the vault. The caller (CLI) is responsible for then saving the returned `TrackEntity` to SQL.
- Actually: the endpoint is rarely used in production (the CLI calls `FileDatabase.RegisterResourceAsync` directly). But the endpoint exists for potential web-side uploads in future.
- Returns 200 on success, 401 if ApiKey invalid, 400 if body invalid.

**Do not add a third endpoint without product approval.** The surface is intentionally minimal.

## ApiKey middleware behaviour

`ApiKeyAuthenticationMiddleware` runs on every request but only enforces on endpoints with `[ApiKeyAuthorize]` metadata.

- Reads header `ApiKey`.
- Compares against `ApiKeySettings.ApiKey` from `environment/apikey.json`.
- Returns 401 with body `"API Key was not provided"` or `"Unauthorized client"` if validation fails.
- Endpoints without `[ApiKeyAuthorize]` skip the check entirely (e.g., `GET api/track/{id}` is unauthenticated).

## CORS configuration

`CorsSettings.AllowedOrigins` is **required** — the app throws on startup if missing. Policy is named `ContentApiPolicy`:

- `AllowCredentials()`
- `AllowAnyMethod()`
- `AllowAnyHeader()`

Configured in `Startup.ConfigureDomainServices()`, applied to all endpoints via `UseCors()`.

## Forwarded headers

**Enabled only in `Production` mode** (via `if (app.Environment.IsProduction())`). This differs from `DeepDrftWeb`, which enables them always. Be aware when debugging proxy issues.

`UseForwardedHeaders()` processes `X-Forwarded-For`, `X-Forwarded-Proto`, `X-Forwarded-Host` so the app knows its real client IP and scheme when sitting behind nginx.

## Startup wiring (Startup.ConfigureDomainServices)

1. Load `environment/filedatabase.json` and bind `FileDatabaseSettings`.
2. Await `FileDatabase.FromAsync(VaultPath)` to load or create the database.
3. Register `FileDatabase` as singleton.
4. Ensure the `tracks` vault exists (type `MediaVaultType.Audio`, created on first boot if missing).
5. Register singleton `WavOffsetService`.
6. Register named `IHttpClientFactory` client (for future use, e.g., fetching from external APIs).
7. Add MudBlazor (if shared components need it in future).
8. Add CORS policy.

The singleton `FileDatabase` is thread-safe for reads. Writes are atomic at the vault level (index updates are serialised). The `IndexWatcher` reloads the vault index if an external process (e.g., CLI) writes to it, so a long-running web host stays consistent.

## OpenAPI

Mapped in `Development` only. Swagger UI at `/swagger` for testing endpoints locally.

## Configuration files

- `appsettings.json`: Logging, hosting config. **Does not contain secrets.**
- `environment/filedatabase.json` (required):
  ```json
  {
    "FileDatabaseSettings": {
      "VaultPath": "../Database/Vaults"
    }
  }
  ```
- `environment/apikey.json` (required at runtime, not in repo):
  ```json
  {
    "ApiKeySettings": {
      "ApiKey": "your-secret-key"
    }
  }
  ```

## Development commands

```bash
# Run the content API (default https://localhost:5002)
dotnet run --project DeepDrftContent

# Watch during development
dotnet watch run --project DeepDrftContent

# Build
dotnet build DeepDrftContent

# Test endpoints (requires API key in environment/apikey.json)
curl -H "ApiKey: your-secret-key" -X PUT https://localhost:5002/api/track/test-id \
  -H "Content-Type: application/json" \
  -d '{"buffer":"base64-encoded-audio","size":1000,"mime":"audio/wav"}'

curl https://localhost:5002/api/track/test-id?offset=0
```

## Important patterns

- **Result types**: Controllers return `ActionResult<T>`. Service calls return `Result` or `ResultContainer<T>` from NetBlocks. The controller checks `Success` and returns 200/4xx/5xx accordingly.
- **Error swallowing**: FileDatabase operations return `null` or `false` on failure. The controller surfaces this as 500. Never throw — check return values.
- **Async/await**: All operations are async.
- **Vault operations**: Always use the injected `FileDatabase` singleton. Never construct a new one — it has the `IndexWatcher` and is the source of truth.

## The FileDatabase import

See `DeepDrftContent.Services/CLAUDE.md` for the FileDatabase API and semantics. This host only provides the HTTP surface over it.

When working with this project, focus on the HTTP surface (controllers, middleware, CORS, forwarded headers) and the wiring that connects the host to the FileDatabase. New domain logic goes in `DeepDrftContent.Services`.
