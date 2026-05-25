# CLAUDE.md - DeepDrftContent

Guidance for working in the DeepDrftContent project (the binary content API host).

See the root `CLAUDE.md` for full architecture overview. This file covers what is specific to this project.

## One-line purpose

The dual-database authority for tracks: SQL metadata and FileDatabase binary. Seven endpoints expose track CRUD with upload+persist, delete+cleanup, paged listing, and metadata operations. ApiKey middleware, CORS, forwarded headers. **FileDatabase implementation lives in `DeepDrftContent.Services`; SQL services in `DeepDrftData`.**

## What lives here now (only)

- `Program.cs`, `Startup.cs`: HTTP host config, DI wiring, middleware setup, port binding.
- `Services/UnifiedTrackService.cs`: Host-internal orchestrator. Coordinates vault write + SQL persist for upload (`UploadAsync`), and SQL delete + vault remove for delete (`DeleteAsync`).
- `Controllers/TrackController.cs`: Seven endpoints (see below).
- `Middleware/ApiKeyAuthenticationMiddleware.cs`, `Middleware/ApiKeyAuthorizeAttribute.cs`: ApiKey validation logic.
- `Models/`: Settings POCOs only (`ApiKeySettings`, `CorsSettings`, `FileDatabaseSettings`). No domain code.
- `environment/filedatabase.json`: FileDatabase vault path config (loaded via CredentialTools, not in repo).
- `environment/apikey.json`: API key (loaded via CredentialTools, not in repo, must be created locally or at deployment).
- `environment/connections.json`: SQL connection string (loaded via CredentialTools, not in repo, format: `{ "ConnectionStrings": { "DefaultConnection": "..." } }`).

## What does NOT live here anymore

- `FileDatabase/`, `Processors/`, media models (`AudioBinary`, `ImageBinary`, etc.), `WavOffsetService` — all in `DeepDrftContent.Services`.
- EF Core context and repository — in `DeepDrftData`.
- **Hosts only own HTTP surface and wiring.** New domain code goes in `*.Services` (shared libraries) or host-internal `Services/` folders (e.g., `UnifiedTrackService` here for dual-database orchestration).

## The endpoint surface (seven endpoints)

### GET api/track/{trackId}?offset=0 (unauthenticated)

Returns the WAV bytes from the `tracks` vault with optional offset support.

- **Route parameter `trackId`** (string): the entry id inside the `tracks` vault (i.e. `TrackEntity.EntryKey`).
- **Query parameter `offset`** (optional, default 0): byte position to start streaming from.
- If `offset == 0`: streams the entire file directly from disk without buffering (so 100 MB WAVs do not force 100 MB LOH allocations per request).
- If `offset > 0`: `WavOffsetService.CreateOffsetStream` block-aligns the offset and synthesises a fresh 44-byte WAV header so the response is a valid standalone WAV starting from that byte position. This is load-bearing for seek-beyond-buffer — the player asks for a new stream at the offset it wants to seek to, gets back a valid WAV that starts there, and tears down/re-initialises the decoder.
- Returns 404 if track not found. Returns 500 if vault operations fail (with error swallowing — the vault returns `null`).

### PUT api/track/{trackId} ([ApiKeyAuthorize])

**Authenticated endpoint.** Writes pre-processed audio bytes to the `tracks` vault.

- **Header `ApiKey`**: required. Validated by `ApiKeyAuthenticationMiddleware`.
- **Route parameter `trackId`** (string): the entry id to store under.
- **Body**: `AudioBinaryDto` (base64 buffer + size + mime + duration + bitrate). This endpoint receives an already-processed audio DTO, not a raw WAV file.
- Validates MIME type (rejects unsupported types with `.bin` sentinel). Delegates to `FileDatabase.RegisterResourceAsync`.
- Rarely used in production (the CLI calls `FileDatabase.RegisterResourceAsync` directly). Exists for potential future web-side intake paths.
- Returns 200 on success, 401 if ApiKey invalid, 400 if MIME invalid.

### POST api/track/upload ([ApiKeyAuthorize])

**Authenticated endpoint.** Accepts a raw WAV upload + metadata as `multipart/form-data`, processes the WAV, stores it in the vault, and persists metadata to SQL. Returns the fully persisted `TrackEntity` with `Id` populated.

- **Header `ApiKey`**: required. Validated by `ApiKeyAuthenticationMiddleware`.
- **Form fields**:
  - `wav` (`IFormFile`, required): the WAV bytes. File name must end in `.wav`.
  - `trackName` (string, required)
  - `artist` (string, required)
  - `album` (string, optional)
  - `genre` (string, optional)
  - `releaseDate` (string, optional, format `YYYY-MM-DD`)
  - `createdByUserId` (long, required): audit trail — who uploaded this track.
- The upload stream is copied to a `.wav`-suffixed temp file under `Path.GetTempPath()` (the audio processor requires that extension and reads from disk). The temp file is always deleted in a `finally` block — success or failure.
- `[RequestSizeLimit(1 GB)]` + `[RequestFormLimits(MultipartBodyLengthLimit = 1 GB)]` lift the per-request ceiling above the framework default (~28 MB) so production-sized WAVs are accepted. The body is streamed to the temp file, not buffered in memory.
- Calls `UnifiedTrackService.UploadAsync`, which orchestrates: `TrackService.AddTrackFromWavAsync` (vault write) → `TrackManager` (SQL persist with `createdByUserId`).
- Returns 200 with the **persisted** `TrackEntity` JSON (Id populated) on success. Returns 400 for missing/invalid form fields. Returns 500 if processing fails.

### DELETE api/track/{id:long} ([ApiKeyAuthorize])

**Authenticated endpoint.** Removes a track: SQL row first, then vault entry. `UnifiedTrackService` owns the ordering.

- **Header `ApiKey`**: required. Validated by `ApiKeyAuthenticationMiddleware`.
- **Route parameter `id`** (long): the SQL track ID (not EntryKey).
- Calls `UnifiedTrackService.DeleteAsync`, which: looks up SQL row → deletes SQL row → deletes vault entry via EntryKey.
- Returns 200 on success, 404 if track not found, 500 if deletion fails.

### GET api/track/page ([ApiKeyAuthorize])

**Authenticated endpoint.** Paged metadata list from SQL. Used by CMS track browser.

- **Header `ApiKey`**: required. Validated by `ApiKeyAuthenticationMiddleware`.
- **Query parameters**:
  - `page` (int, optional, default 1): 1-based page number.
  - `pageSize` (int, optional, default 20): tracks per page.
  - `sortColumn` (string, optional): sort field. Supported: `"TrackName"`, `"Artist"`, `"Album"`, `"Genre"`, `"ReleaseDate"`. Defaults to `Id`.
  - `sortDescending` (bool, optional, default false): sort direction.
- Calls `ITrackService.GetPaged` (via DI), which is actually `TrackManager` from `DeepDrftData`.
- Returns 200 with `PagedResult<TrackEntity>` JSON (`Items`, `TotalCount`, `PageNumber`, `PageSize`). Returns 500 on query error.

### GET api/track/meta/{id:long} ([ApiKeyAuthorize])

**Authenticated endpoint.** Single track metadata from SQL by ID.

- **Header `ApiKey`**: required. Validated by `ApiKeyAuthenticationMiddleware`.
- **Route parameter `id`** (long): the SQL track ID.
- Calls `ITrackService.GetById`, which returns the track or null.
- Returns 200 with `TrackEntity` JSON on success. Returns 404 if not found. Returns 500 on query error.

### PUT api/track/meta/{id:long} ([ApiKeyAuthorize])

**Authenticated endpoint.** Updates track metadata in SQL. EntryKey (the vault link) is immutable.

- **Header `ApiKey`**: required. Validated by `ApiKeyAuthenticationMiddleware`.
- **Route parameter `id`** (long): the SQL track ID.
- **Body**: `UpdateTrackMetadataRequest` with fields: `TrackName`, `Artist`, `Album?`, `Genre?`, `ReleaseDate?`.
- Looks up SQL row by ID, updates the provided fields (nulls in the request clear optional fields), and persists via `ITrackService.Update`.
- Returns 200 on success. Returns 404 if track not found. Returns 500 on update error.

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

## Startup wiring (Startup.ConfigureDomainServices + Program.cs)

**In `Startup.ConfigureDomainServices`** (FileDatabase + binary services):

1. Load `environment/filedatabase.json` via `CredentialTools.ResolvePathOrThrow("filedatabase", ...)` and bind `FileDatabaseSettings`.
2. Await `FileDatabase.FromAsync(VaultPath)` to load or create the database.
3. Register `FileDatabase` as singleton.
4. Ensure the `tracks` vault exists (type `MediaVaultType.Audio`, created on first boot if missing).
5. Register singletons: `WavOffsetService`, `AudioProcessor`, `TrackService` (the `DeepDrftContent.Data` version for vault operations).

**In `Program.cs`** (SQL + wiring):

6. Load `environment/connections.json` via `CredentialTools.ResolvePathOrThrow("connections", ...)`.
7. Register `DbContext<DeepDrftContext>` (scoped) with connection string from config.
8. Register scoped: `TrackRepository`, `TrackManager`, `ITrackService` (factory resolves to `TrackManager`), `UnifiedTrackService`.
9. Configure forwarded headers (production-only) for reverse proxy support.
10. Load `environment/apikey.json` and register API key middleware.
11. Configure CORS policy (`ContentApiPolicy`): AllowAnyMethod, AllowAnyHeader, AllowCredentials, specific origins from config.

The singleton `FileDatabase` is thread-safe for reads. Writes are atomic at the vault level (index updates are serialised). The `IndexWatcher` reloads the vault index if an external process (e.g., CLI) writes to it, so a long-running web host stays consistent. SQL services are scoped (DbContext not thread-safe).

## OpenAPI

Mapped in `Development` only. Swagger UI at `/swagger` for testing endpoints locally.

## Configuration files

- `appsettings.json`: Logging, hosting, and CORS config. **Does not contain secrets.**
  - `Logging`: standard ASP.NET structure.
  - `CorsSettings.AllowedOrigins`: array of origin URLs allowed to call the API (required; throws on startup if missing).
- `environment/filedatabase.json` (required, loaded via CredentialTools, not in repo):
  ```json
  {
    "FileDatabaseSettings": {
      "VaultPath": "../Database/Vaults"
    }
  }
  ```
- `environment/apikey.json` (required at runtime, loaded via CredentialTools, not in repo):
  ```json
  {
    "ApiKeySettings": {
      "ApiKey": "your-secret-key"
    }
  }
  ```
- `environment/connections.json` (required, loaded via CredentialTools, not in repo):
  ```json
  {
    "ConnectionStrings": {
      "DefaultConnection": "Host=localhost;Database=deepdrft;Username=postgres;Password=..."
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
