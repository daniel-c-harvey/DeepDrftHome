# PLAN — AuthBlocks startup separation + TrackManager / ITrackService cleanup

Working plan for two connected workstreams. Standalone execution doc; not part of the themed
`PLAN.md` roadmap. When this work lands, the relevant bodies move to `COMPLETED.md` per the
`CONTEXT.md §6` convention, and the `ITrackService` cross-cutting note in `PLAN.md` (line ~192)
should be retired since this plan resolves it.

**Status:** awaiting Daniel's approval. Engineering is not dispatched until then.

---

## Goal

Make **DeepDrftAPI** the AuthBlocks API host (own registration, migration/seed, and endpoint
mounting; Manager keeps only web-side auth), and enforce a clean layer boundary on the SQL side:
**`TrackRepository` outputs entities; `ITrackService` / `TrackManager` outputs DTOs** (via
`TrackConverter`). The current `ITrackService` returns `TrackEntity` everywhere — that is the defect.
The interface changes to return `TrackDto` for all query and mutation results, making `TrackConverter`
the single authoritative entity→DTO conversion path and putting it inside the service layer where it
belongs, rather than at scattered call sites.

---

## Scope note — a third consumer the brief did not name

The brief lists CLI and `UnifiedTrackService` as the non-HTTP `ITrackService` consumers. There is a
**third**: `DeepDrftPublic` consumes `ITrackService` in two places —

- `DeepDrftPublic/Services/TrackDirectDataService.cs` (server-side SSR prerender; calls `.GetPaged`).
- `DeepDrftPublic/Controllers/TrackController.cs` (the public `api/track/page` endpoint; calls `.GetPaged`).
- Registered in `DeepDrftPublic/Startup.cs` as `AddScoped<ITrackService>(sp => sp.GetRequiredService<TrackManager>())`.

Whatever happens to `ITrackService` must keep DeepDrftPublic compiling. This is load-bearing for the
wave plan below: under Daniel's clarified rule (repository → entity, service → DTO), an `ITrackService`
signature change is **no longer optional** — the interface itself flips to DTO return types, so every
consumer is in the blast radius, including all four projects (DeepDrftAPI, DeepDrftManager, DeepDrftCli,
DeepDrftPublic). The public host returns `TrackEntity` to its own WASM client today; whether that wire
format also moves to DTO is now an explicit **decision point** (see Workstream 2, Q2.3) rather than a
settled scope-out — the layer rule applies system-wide, so the default is that Public aligns too.

---

## Workstream 1 — AuthBlocks startup separation

### Current state

`DeepDrftManager/Program.cs` does all four AuthBlocks responsibilities:

1. `builder.Services.AddAuthBlocks(options => { ... })` (lines 35–63) — EF Identity, JWT services,
   email sender, admin-seed config. Reads `ConnectionStrings:Auth` + all `AuthBlocks:*` keys.
2. `AuthBlocksWeb.Startup.ConfigureAuthServices(builder.Services, baseUrl)` (line 68) — web-side
   cascading auth state + JWT client for the `/account/login` + `/account/logout` Razor pages.
3. `await app.Services.UseAuthBlocksStartupAsync()` (line 119) — EF migrate + seed roles/admin.
4. `app.MapAuthBlocks()` (line 143) — mounts `/api/auth/*`, `/api/users/*`.

`DeepDrftManager.csproj` references **both** `AuthBlocksLib` and `AuthBlocksWeb`. The
`authblocks.json` secrets file is loaded into Manager's config (lines 23–24).

`DeepDrftAPI/Program.cs` has **no** AuthBlocks anything. It already loads
`environment/connections.json` (line 46) for `DefaultConnection` (SQL metadata) and has a CORS
policy `ContentApiPolicy` driven by `CorsSettings.AllowedOrigins` from `appsettings.json`
(lines 22–37), applied via `app.UseCors("ContentApiPolicy")` (line 81).

### Target state

- **Manager**: keeps only step 2 (`ConfigureAuthServices`). Drops steps 1, 3, 4. Drops the
  `AuthBlocksLib` package reference; keeps `AuthBlocksWeb`. No longer loads `authblocks.json` *for
  AddAuthBlocks*, but see secrets migration below — it likely still needs the JWT validation subset.
- **DeepDrftAPI**: gains steps 1, 3, 4. Becomes the AuthBlocks API host — auth endpoints live
  alongside track endpoints. Gains the `AuthBlocksLib` package reference, an `Auth` connection
  string, and the `authblocks.json` secrets. CORS policy widened to allow the Manager origin so the
  Manager's browser client can call `/api/auth/*` cross-origin.

### Files touched

| File | Change |
|---|---|
| `DeepDrftManager/Program.cs` | Remove `AddAuthBlocks` block (35–63), `UseAuthBlocksStartupAsync` (119), `MapAuthBlocks` (143). Keep `ConfigureAuthServices` (68). Adjust `authblocks.json` load (23–24) to the JWT-validation subset or remove if unused — see open question Q1.1. |
| `DeepDrftManager/DeepDrftManager.csproj` | Remove `AuthBlocksLib` PackageReference. Keep `AuthBlocksWeb`. |
| `DeepDrftManager/CLAUDE.md` | (doc-keeper, post-land) reflect Manager as web-auth-only. *Not this plan's edit.* |
| `DeepDrftAPI/Program.cs` | Add `AddAuthBlocks` block reading `Auth` conn string + `AuthBlocks:*`. Add `await app.Services.UseAuthBlocksStartupAsync()` after `Build()`. Add `app.MapAuthBlocks()`. Add `app.UseAuthentication()`/`UseAuthorization()` if AuthBlocks endpoints require the auth middleware (verify — the ApiKey middleware is separate; see Q1.3). |
| `DeepDrftAPI/DeepDrftAPI.csproj` | Add `AuthBlocksLib` PackageReference (match the version Manager used). |
| `DeepDrftAPI/appsettings.json` | Add Manager's origin to `CorsSettings.AllowedOrigins`. |
| `DeepDrftAPI/environment/connections.json` | Add the `Auth` connection string (gitignored; deployment + local). |
| `DeepDrftAPI/environment/authblocks.json` | New: the JWT/email/admin secrets file (gitignored). Moved from Manager. |
| `DeepDrftManager/environment/authblocks.json` | Remove, or reduce to the JWT-validation subset — see Q1.1. |
| `*.example.json` templates | Update both hosts' example/templates to reflect the new shape (the Manager `Program.cs` header comment at lines 11–15 documents these; keep it accurate). |

### Migration path for secrets

`authblocks.json` today (Manager) carries: `AuthBlocks:Jwt:{Secret,Issuer,Audience}`,
`AuthBlocks:Email:{Host,Token}`, `AuthBlocks:Admin:{UserName,Email,Password}`, `AuthBlocks:SupportEmail`.

1. **DeepDrftAPI gets the full file.** It runs `AddAuthBlocks` + seed, so it needs all of it
   (JWT signing secret, email sender creds, admin seed creds). Place at
   `DeepDrftAPI/environment/authblocks.json`, loaded via `CredentialTools.ResolvePathOrThrow`
   exactly as Manager does today.
2. **The `Auth` connection string moves to DeepDrftAPI's `connections.json`.** DeepDrftAPI already
   loads `connections.json` for `DefaultConnection`; add the `Auth` key alongside it. The Auth
   schema stays in its own database, separate from `DefaultConnection`, exactly as today.
3. **Manager's residual need** is the open question — see Q1.1. The default position: Manager's
   `ConfigureAuthServices` is a *client* of the JWT (validates tokens read from browser
   localStorage, points its JWT client at the auth API base URL). If it needs the issuer/audience
   for client-side validation, Manager keeps a **reduced** `authblocks.json` with only the JWT
   validation subset (no signing secret, no email, no admin creds). If `ConfigureAuthServices`
   takes only a base URL (as the current call `ConfigureAuthServices(services, baseUrl)` suggests),
   Manager may need **no** `authblocks.json` at all. Engineering confirms against the AuthBlocksWeb
   API surface before deleting.

### Open questions — resolved

**Q1.1 — Does Manager still need any `authblocks.json`?**
Resolution: **Default to "no signing secret, possibly a JWT-validation subset."** The current
`ConfigureAuthServices(builder.Services, baseUrl)` signature takes only a base URL, which strongly
implies the web side does not need the signing secret. Engineering's first implementation step is
to read the `AuthBlocksWeb.Startup.ConfigureAuthServices` signature and its downstream JWT client
config: if it consumes `AuthBlocks:Jwt:*` from config, Manager keeps a reduced file with
`Issuer`/`Audience` only; if it does not, Manager drops `authblocks.json` entirely. The signing
secret (`Jwt:Secret`), email, and admin creds **never** stay on Manager. Capture the actual finding
in the commit and update the Manager `Program.cs` header comment accordingly.

**Q1.2 — Where do auth tokens get validated, given the Blazor circuit?**
Manager's existing comment (lines 145–155) documents that JWTs live in browser localStorage and
never reach the Manager server on navigation — page authz is via `AuthorizeRouteView`, and the
Manager API surface is `AllowAnonymous` at the endpoint layer. **This is unchanged by the split.**
The browser holds the token and presents it to whichever API it calls. After the split, the auth
*issuing* API is DeepDrftAPI, so the browser obtains its token from DeepDrftAPI's `/api/auth/*` and
presents it to DeepDrftAPI's protected endpoints. Manager's job is only to host the login UI and
the cascading auth-state provider that reads the stored token. No server-side token validation moves.

**Q1.3 — Does DeepDrftAPI need `UseAuthentication`/`UseAuthorization` added?**
Resolution: **Likely yes, and it must compose with the existing ApiKey middleware.** DeepDrftAPI
today authenticates track endpoints with a custom `ApiKeyAuthenticationMiddleware`
(`app.UseApiKeyAuthentication`), not ASP.NET auth middleware. `MapAuthBlocks` mounts endpoints that
expect JWT bearer auth, which requires `app.UseAuthentication()` + `app.UseAuthorization()` in the
pipeline. Engineering adds these and verifies ordering: the ApiKey middleware must continue to gate
only `[ApiKeyAuthorize]`-tagged track endpoints, and the JWT bearer scheme must gate the AuthBlocks
endpoints — the two auth mechanisms coexist on one host. **This is the highest-risk integration
point in Workstream 1**; flag for careful review. Confirm `AddAuthBlocks` registers the JWT bearer
scheme (it does in Manager today, which is why Manager calls `UseAuthentication`).

**Q1.4 — CORS.** DeepDrftAPI's `ContentApiPolicy` already does `AllowAnyMethod/Header/Credentials`
with specific origins. Add `manage.deepdrft.com` (prod) and the Manager's dev origin to
`CorsSettings.AllowedOrigins`. Because the policy already `AllowCredentials()`, the cross-origin
`/api/auth/*` flow (which may set cookies or carry the Authorization header) works without a new
policy. No second CORS policy needed.

---

## Workstream 2 — TrackManager / ITrackService cleanup

### Current state

**The defect:** `ITrackService` returns `TrackEntity` everywhere (see `DeepDrftData/ITrackService.cs`),
so the service layer leaks entities to its callers and `TrackConverter` is never reached on the read
path. The layer boundary Daniel wants — repository → entity, service → DTO — is inverted in the
current code: the converter exists but the entity-typed `ITrackService` surface bypasses it.

`TrackManager : Manager<TrackEntity, TrackDto, TrackRepository, TrackConverter>, ITrackService`.
The BlazorBlocks `Manager<>` base (NuGet `Cerebellum.BlazorBlocks.Data` 10.3.30, namespace
`Data.Managers`) exposes a **DTO-shaped** surface using `TrackConverter` (e.g. `GetById → TrackDto`).
`TrackManager` *also* implements the **entity-shaped** `ITrackService`, bypassing the converter —
these entity-typed methods are exactly what the new DTO-typed interface eliminates:

- `ITrackService.GetById` — explicit impl (signature collides with base `GetById → TrackDto`).
- `GetAll`, `GetPaged`, `Create`, `Update` — direct public methods hitting `Repository` and
  returning `TrackEntity`. These **shadow/satisfy** the entity interface and never touch the converter.
- `Delete(long) → Result` — inherited from base; satisfies `ITrackService.Delete` by signature.

**DeepDrftAPI consumers of `ITrackService` (entity-typed, converter-bypassing):**
- `TrackController` injects `ITrackService _sqlTrackService`; calls `.GetPaged` (GetPage endpoint),
  `.GetById` (GetMeta, and inside UpdateMeta), `.Update` (UpdateMeta). Returns raw `TrackEntity` /
  `PagedResult<TrackEntity>` over the wire.
- `UnifiedTrackService` injects `ITrackService`; calls `.Create(TrackEntity)` (upload),
  `.GetById(long)` + `.Delete(long)` (delete orchestration).

**Other consumers (out of DeepDrftAPI, in blast radius):**
- `DeepDrftPublic` — `TrackDirectDataService.GetPaged`, `TrackController.GetPaged`. Returns
  `TrackEntity` to its own client.
- `DeepDrftCli` — `CliService` (`Create`, `GetAll`), `GuiService` (`Create`, `Update`, `Delete`, `GetAll`).

**`CmsTrackService` (Manager, deserialization side)** currently deserializes DeepDrftAPI responses
as `TrackEntity` / `PagedResult<TrackEntity>`: `ReadFromJsonAsync<TrackEntity>` (upload at line 96,
get-by-id at 229), `ReadFromJsonAsync<PagedResult<TrackEntity>>` (page at 180). Its
`ICmsTrackService` public contract returns `TrackEntity` / `PagedResult<TrackEntity>` to the Blazor
components.

### Target state

Daniel's clarified rule (verbatim): *"For workstream 2, ITrackService is where we need to output
DTOs. The repository outputs entities, the Service outputs DTOs."* This is a layer boundary, not a
controller tweak:

- **`TrackRepository`** → outputs `TrackEntity` (unchanged; it is the data-access layer).
- **`ITrackService` / `TrackManager`** → outputs `TrackDto` for **all** query and mutation results,
  invoking `TrackConverter` internally. This is the change.

The new `ITrackService` shape:

```csharp
public interface ITrackService
{
    Task<ResultContainer<TrackDto?>>             GetById(long id);
    Task<ResultContainer<List<TrackDto>>>        GetAll();
    Task<ResultContainer<PagedResult<TrackDto>>> GetPaged(int page, int pageSize, string? sortColumn, bool sortDescending, CancellationToken ct = default);
    Task<ResultContainer<TrackDto>>              Create(TrackDto newTrack);   // was Create(TrackEntity)
    Task<ResultContainer<TrackDto>>              Update(TrackDto track);      // was Update(TrackEntity)
    Task<Result>                                Delete(long id);             // unchanged
}
```

Consequences that fall out of this single rule:

- **The controller keeps injecting `ITrackService`** and gets DTOs automatically. No change to *which*
  type it injects — only the endpoint response types change from `TrackEntity` to `TrackDto`. This is
  *simpler* than the prior plan (which had the controller switch to `IManager<TrackEntity,TrackDto>`).
- **`UnifiedTrackService`** converts its unpersisted entity to a DTO before `Create`, and its
  `UploadAsync` now returns `ResultContainer<TrackDto>`.
- **CLI** converts entity → DTO at its `Create`/`Update` call sites (it already builds entities), and
  its list/display collections bind `TrackDto`.
- **DeepDrftPublic** — its `TrackDirectDataService` and own controller now receive `PagedResult<TrackDto>`
  from `ITrackService`, so its wire format to the WASM client changes accordingly. This was previously
  scoped out; it is now a **decision point** (Q2.3) — the rule applies system-wide, so the default is
  that Public aligns too.
- **`CmsTrackService`** deserializes `TrackDto` / `PagedResult<TrackDto>` (unchanged from the prior plan).

### Resolution of each design question

**Q2.1 — ITrackService fate: change the interface to DTO return types.**
The direction is settled by Daniel's rule. It is **neither "keep as-is" nor "split into two
interfaces"** — the three-direction exploration in the prior draft is retired. `ITrackService` stays
as a single interface, owned by `DeepDrftData`, but its return types flip from `TrackEntity` to
`TrackDto` (and its `Create`/`Update` *inputs* flip to `TrackDto` as well). This is the layer boundary
Daniel stated: the repository is the entity surface; the service is the DTO surface; `TrackConverter`
lives inside `TrackManager` and is the only place the conversion happens. Every consumer that
previously received entities now receives DTOs and adjusts at its own boundary (controller: serialize
the DTO; orchestration/CLI: convert their locally-built entities to DTOs before calling, and bind DTOs
on the way back).

**Q2.2 — TrackManager implementation.**
`TrackManager : Manager<TrackEntity, TrackDto, TrackRepository, TrackConverter>` — the BlazorBlocks
base very likely already provides DTO-returning `Create`/`Update`/`GetById`/`GetPaged` that run
`TrackConverter`. If so, the **explicit entity-typed implementations currently in `TrackManager.cs`
are removed** in favor of the base-class DTO surface satisfying the new `ITrackService`:

- `ITrackService.GetById` (the explicit interface impl, lines 37–48) — removed; base `GetById → TrackDto`
  now satisfies the interface directly (no signature collision once the interface is DTO-typed).
- entity-typed `GetAll` (50–61), `GetPaged` (63–95), `Create` (97–108), `Update` (118–132) — removed
  in favor of the base DTO methods.
- `Delete(long) → Result` — already inherited from the base; still satisfies the interface unchanged.

**The one piece that must survive is the sort-column→expression mapping** (the switch at
`TrackManager.GetPaged` lines 77–86: `TrackName`/`Artist`/`Album`/`Genre`/`ReleaseDate` → `OrderBy`,
nulls padded to end, default `Id`). This is product behavior, not boilerplate, and the base class may
not carry it. Engineering's first step (B1) confirms against the `Cerebellum.BlazorBlocks.Data`
10.3.30 surface — which is **not readable from this repo** — whether the base paged method accepts a
sort-column string and maps it, or only a pre-built `PagingParameters<>`:

- If the base supports a sort-column string with a configurable mapping: configure the five columns there.
- Otherwise: keep a DTO-returning `GetPaged` **override** on `TrackManager` that reuses the existing
  switch to build `PagingParameters<TrackEntity>`, calls `Repository.GetPagedAsync`, then converts the
  page to `PagedResult<TrackDto>` (per-item `TrackConverter.Convert`, or `PagedResult.From<>` — see the
  `DeepDrftModels` `PagedResult.From<TOther>` factory). Either way the switch stays in `TrackManager`.

This is the single most likely place Workstream 2 retains a hand-written method on `TrackManager`.

**Q2.3 — Controller response types, and the DeepDrftPublic decision point.**
The controller **keeps injecting `ITrackService`** (no DI change in DeepDrftAPI's `Program.cs` for the
track service). Because the interface is now DTO-typed:

- `GET api/track/page` returns `PagedResult<TrackDto>`.
- `GET api/track/meta/{id}` returns `TrackDto`.
- `PUT api/track/meta/{id}` — the lookup now returns a `TrackDto`; the controller mutates the DTO's
  fields and passes the DTO to `Update(TrackDto)`. `EntryKey` stays immutable (not in the request body).
- `POST api/track/upload` — `ActionResult<TrackEntity>` becomes `ActionResult<TrackDto>` (see Q2.5).

On the **Manager side**, `CmsTrackService` deserializes `TrackDto` / `PagedResult<TrackDto>`,
`ICmsTrackService` returns DTOs, and the Blazor components binding those results move from
`TrackEntity` to `TrackDto`. Because `TrackConverter` mirrors the entity field-for-field, **the JSON
payload is identical** — this is a type-level rename, not a payload change, so there is no
serialization risk. Engineering enumerates the affected components by grepping the `ICmsTrackService`
return types.

**DeepDrftPublic — decision point.** Under the prior plan Public was explicitly *out* of scope. Under
Daniel's system-wide rule it is now *in* by default: `TrackDirectDataService.GetPage` and Public's own
`api/track/page` controller call `ITrackService.GetPaged`, which now returns `PagedResult<TrackDto>`,
so Public's `ITrackDataService` contract and its WASM client deserialization move from `TrackEntity` to
`TrackDto`. The payload is again identical (converter mirrors the entity). **Flagged for Daniel:**

> The brief's clarification names "the Service" generically, which means DeepDrftPublic's consumption
> of `ITrackService` is also affected — its public wire format would move to `TrackDto`. Recommended:
> align Public in the same pass (the change is mechanical and the payload is unchanged), so the layer
> rule holds uniformly and there is no lingering entity-on-the-wire path. If Daniel wants Public's wire
> deferred, the *only* way to keep it on entities is to convert `TrackDto` → `TrackEntity` at Public's
> own boundary (`TrackDirectDataService` and controller) — extra work to preserve the old shape, and a
> conversion in the wrong direction. **Default: align Public.**

**Q2.4 — UnifiedTrackService.**
`UnifiedTrackService.UploadAsync` builds an unpersisted `TrackEntity` (from
`TrackContentService.AddTrackFromWavAsync`), sets `CreatedByUserId`, then calls `ITrackService.Create`.
Because `Create` now takes a `TrackDto`, it converts first:

```csharp
unpersisted.CreatedByUserId = createdByUserId;
var dto = TrackConverter.Convert(unpersisted);          // entity → DTO
var saveResult = await _sqlTrackService.Create(dto);    // returns ResultContainer<TrackDto>
// saveResult.Value is a TrackDto with Id populated
```

`UploadAsync`'s return type changes from `ResultContainer<TrackEntity>` to `ResultContainer<TrackDto>`
— the controller serializes it as-is, so no second conversion is needed. The orphan-logging path is
unchanged (it reads `EntryKey`, present on the DTO). `DeleteAsync` already only needs `GetById` (now
returns `TrackDto?`, still carries `EntryKey`) and `Delete(long)` (unchanged) — its logic is
untouched beyond the `EntryKey` source being a DTO.

**Q2.5 — UploadTrack response type.**
With `UnifiedTrackService.UploadAsync` returning `ResultContainer<TrackDto>`, the controller's
`UploadTrack` action returns the DTO directly: `ActionResult<TrackEntity>` → `ActionResult<TrackDto>`,
and `return Ok(result.Value)` already serializes the DTO. `CmsTrackService.UploadTrackAsync`
deserializes `TrackDto`; `ICmsTrackService.UploadTrackAsync` returns `ResultContainer<TrackDto>`. The
upload caller in the Manager component uses `Id`, `EntryKey`, etc. — all present on the DTO.

**Q2.6 — CLI change.**
`CliService` and `GuiService` (in `DeepDrftCli`) consume `ITrackService` directly: `.Create(entity)`,
`.Update(entity)`, `.GetAll()`, `.Delete(id)`. After the change:

- **Create** (`CliService.HandleAddCommand` ~line 168, `GuiService.ValidateAndAddTrackAsync` ~line 654):
  both build a `TrackEntity` via `TrackContentService.AddTrackFromWavAsync`, then call `Create`. They
  must convert to DTO first — `TrackConverter.Convert(trackEntity)` — and read `result.Value` as a
  `TrackDto` (the displayed `Id`, `TrackName`, `Artist`, `Album`, `Genre`, `ReleaseDate`, `EntryKey`
  are all on the DTO).
- **Update** (`GuiService.ValidateAndUpdateTrackAsync` ~line 712): builds an updated `TrackEntity`,
  calls `Update`. Convert to DTO before the call. Note `GuiService` constructs the entity by hand here;
  it could equally construct a `TrackDto` directly — engineering picks whichever is cleaner given the
  surrounding code, but `TrackConverter.Convert` keeps the call sites uniform.
- **GetAll / display** (`CliService.HandleListCommand`, `GuiService.RefreshTrackListAsync`,
  `_tracks` field, `DeleteTrackAsync`/`ShowEditTrackDialog`/`ShowTrackDetails` selected-track handling):
  `GetAll()` now returns `List<TrackDto>`, so `GuiService._tracks` becomes `List<TrackDto>` and every
  display/selection site binds `TrackDto`. Field access is identical (same property names), so this is
  a type-declaration change, not a logic change. `TrackConverter` and `TrackDto` are already in
  `DeepDrftData` / `DeepDrftModels`, both referenced by the CLI — no new dependency.

The CLI is a deliberate local entity-space admin tool, but the entity now lives only between
`AddTrackFromWavAsync` and the `TrackConverter.Convert` call; everything it reads back from the service
is a DTO. This is consistent with the layer rule.

**Q2.7 — PutTrack endpoint.** No change. It writes `AudioBinaryDto` straight to `FileDatabase` and
never touches `ITrackService`/`TrackManager`. Confirmed by reading the controller (lines 352–373).

### Files touched (Workstream 2)

| File | Change |
|---|---|
| `DeepDrftData/ITrackService.cs` | **Signature change** — all return types `TrackEntity`→`TrackDto`; `Create`/`Update` inputs `TrackEntity`→`TrackDto`; `Delete(long)` unchanged. Update doc comment to state the layer rule (repository → entity, service → DTO). |
| `DeepDrftData/TrackManager.cs` | Remove the entity-typed `ITrackService` implementations (explicit `GetById`, and `GetAll`/`GetPaged`/`Create`/`Update`) in favor of the base-class DTO surface satisfying the DTO-typed interface. **Preserve the sort-column→expression switch** (lines 77–86): keep a DTO-returning `GetPaged` override that reuses it iff the base can't carry the mapping (B1 decides). Update the class doc comment — the dual-surface note is obsolete once there is one DTO surface. |
| `DeepDrftAPI/Program.cs` | **No change to the track-service registration** — `AddScoped<ITrackService>(sp => sp.GetRequiredService<TrackManager>())` stays; the controller keeps injecting `ITrackService` and now gets DTOs. |
| `DeepDrftAPI/Controllers/TrackController.cs` | Keep injecting `ITrackService`. GetPage returns `PagedResult<TrackDto>`; GetMeta returns `TrackDto`; UpdateMeta mutates the looked-up `TrackDto` and calls `Update(TrackDto)`; UploadTrack returns the DTO from `UnifiedTrackService` — `ActionResult<TrackEntity>` → `ActionResult<TrackDto>`. |
| `DeepDrftAPI/Services/UnifiedTrackService.cs` | `UploadAsync` converts the unpersisted entity via `TrackConverter.Convert` before `Create(dto)`; return type `ResultContainer<TrackEntity>` → `ResultContainer<TrackDto>`. `DeleteAsync` reads `EntryKey` off the `TrackDto?` lookup (logic otherwise unchanged). |
| `DeepDrftManager/Services/CmsTrackService.cs` | Deserialize `TrackDto` / `PagedResult<TrackDto>` instead of entity. |
| `DeepDrftManager/Services/ICmsTrackService.cs` | Return types `TrackEntity`→`TrackDto`, `PagedResult<TrackEntity>`→`PagedResult<TrackDto>`. |
| `DeepDrftManager/Components/Pages/**` (Tracks/Cms) | Components binding `ICmsTrackService` results move from `TrackEntity` to `TrackDto`. Enumerate via grep before editing. |
| `DeepDrftCli/Services/CliService.cs` | `Create` call site converts entity → `TrackDto` (`TrackConverter.Convert`); `GetAll`/list display binds `TrackDto`. Field access unchanged. |
| `DeepDrftCli/Services/GuiService.cs` | `Create`/`Update` call sites convert to `TrackDto`; `_tracks` field and all display/selection sites bind `TrackDto`. Field access unchanged. |
| `DeepDrftPublic/Services/TrackDirectDataService.cs`, `DeepDrftPublic/Controllers/TrackController.cs`, Public's `ITrackDataService` + WASM client | **Decision point (Q2.3).** Default: align to `PagedResult<TrackDto>` (mechanical; payload identical). If Daniel defers Public, these instead convert `TrackDto` → `TrackEntity` at Public's boundary to preserve the old wire shape. |
| `DeepDrftData/CLAUDE.md`, `DeepDrftAPI/CLAUDE.md`, `DeepDrftCli/CLAUDE.md`, `DeepDrftPublic/CLAUDE.md` | (doc-keeper, post-land) folder docs that describe `ITrackService` / endpoints returning `TrackEntity` become DTO. *Not this plan's edit.* |

---

## Wave plan

Two workstreams are **independent** and can run in parallel (different files, no shared edits except
both touch `DeepDrftAPI/Program.cs` — sequence those two edits or merge carefully). Within each
workstream, tracks are ordered.

### Wave A — AuthBlocks separation (Workstream 1)

- **A1 (sequential, first):** Read `AuthBlocksWeb.Startup.ConfigureAuthServices` + `AddAuthBlocks`
  signatures from the `AuthBlocksLib`/`AuthBlocksWeb` packages. Resolve Q1.1 (Manager's residual
  `authblocks.json`) and Q1.3 (auth-middleware ordering) against the real API. Output: a one-paragraph
  confirmation appended to this section by engineering before code.
- **A2 (after A1):** DeepDrftAPI gains AuthBlocks: csproj reference, `AddAuthBlocks`, secrets files
  (`authblocks.json` + `Auth` conn string), `UseAuthBlocksStartupAsync`, `MapAuthBlocks`,
  `UseAuthentication`/`UseAuthorization`, CORS origin. Verify ApiKey + JWT middleware coexist.
- **A3 (after A2, can overlap A2 tail):** Manager loses AuthBlocks: remove `AddAuthBlocks`/seed/map,
  drop `AuthBlocksLib` csproj ref, reduce/remove `authblocks.json` per A1 finding. Keep
  `ConfigureAuthServices`.
- **A4 (after A2 + A3):** End-to-end auth smoke: login from Manager UI obtains a token from
  DeepDrftAPI's `/api/auth/*`, protected DeepDrftAPI endpoints accept it, admin seed ran on
  DeepDrftAPI first boot.

### Wave B — TrackManager / ITrackService (Workstream 2)

- **B1 (sequential, first):** Read the `Cerebellum.BlazorBlocks.Data` 10.3.30 `Manager`/`IManager`
  surface. Resolve Q2.2 — does the base provide DTO `Create`/`Update`/`GetById`/`GetPaged`, and does
  its paged method carry the sort-column→expression mapping or must `TrackManager` keep a `GetPaged`
  override for it? Output: confirmation appended here before code.
- **B2 (after B1):** DeepDrftData — flip `ITrackService` to DTO return/input types; remove the
  entity-typed `TrackManager` implementations in favor of the base DTO surface; keep the sort switch
  (as a `GetPaged` override if B1 requires it). This is the load-bearing edit — every B-track below
  depends on it compiling.
- **B3 (after B2):** DeepDrftAPI — `UnifiedTrackService.UploadAsync` converts to DTO before `Create`
  and returns `ResultContainer<TrackDto>`; controller endpoints return DTOs; UploadTrack returns
  `ActionResult<TrackDto>`. **No `Program.cs` DI change** for the track service.
- **B4 (after B2):** DeepDrftCli — convert at `Create`/`Update` call sites; `GetAll`/display bind
  `TrackDto`. Independent of B3 (different project), gated only on B2's interface change.
- **B5 (after B3):** Manager — `CmsTrackService` + `ICmsTrackService` + consuming components move to
  `TrackDto`. The largest single track by file count.
- **B6 (after B2, decision-gated):** DeepDrftPublic — align `TrackDirectDataService`, controller,
  `ITrackDataService`, and the WASM client to `TrackDto` (Q2.3 default), **or** add the
  `TrackDto`→`TrackEntity` conversion at Public's boundary if Daniel defers the public wire. Gated on
  Daniel's Q2.3 decision before it starts.
- **B7 (after B3 + B5):** CMS smoke: track browser lists, edit saves, upload returns the new row,
  delete works — all through the DTO wire.

### Parallelism

- **Wave A and Wave B run in parallel** by different engineers. Wave A touches
  `DeepDrftAPI/Program.cs` (auth wiring); Wave B no longer touches `Program.cs` for the track service
  (the DI registration is unchanged), so the two waves now have **no shared file** — conflict risk is
  effectively zero. (Wave B does touch `DeepDrftAPI` in the controller and `UnifiedTrackService`, but
  not `Program.cs`.)
- A1 and B1 (the package-surface reads) can both happen up front, in parallel, before any code.
- **Within Wave B, B2 is the gate:** the interface flip must land first; B3/B4/B5/B6 fan out from it
  across DeepDrftAPI, DeepDrftCli, DeepDrftManager, and (decision-gated) DeepDrftPublic — they can run
  in parallel once B2 compiles, since they are different projects. This is the real cost of the
  layer-rule change: it is **not** a single-controller edit but a fan-out to every `ITrackService`
  consumer. DeepDrftPublic and CLI are **in scope** now (they were not in the prior draft).

---

## Acceptance criteria

### Workstream 1

- DeepDrftManager builds without referencing `AuthBlocksLib` (only `AuthBlocksWeb`).
- DeepDrftManager's `Program.cs` contains no `AddAuthBlocks`, `UseAuthBlocksStartupAsync`, or
  `MapAuthBlocks` call; it retains `ConfigureAuthServices`.
- DeepDrftAPI references `AuthBlocksLib`, runs `AddAuthBlocks` + `UseAuthBlocksStartupAsync` on
  startup, and mounts `/api/auth/*` + `/api/users/*` via `MapAuthBlocks`.
- On first boot against an empty Auth database, DeepDrftAPI applies AuthBlocks EF migrations and
  seeds roles + the admin user.
- The signing secret, email creds, and admin creds exist only in DeepDrftAPI's `environment/`,
  never in Manager's.
- DeepDrftAPI's CORS policy includes the Manager origin; a browser served by Manager can call
  DeepDrftAPI `/api/auth/*` without a CORS rejection.
- ApiKey-protected track endpoints on DeepDrftAPI still enforce the ApiKey; AuthBlocks endpoints
  enforce JWT — neither auth path interferes with the other.

### Workstream 2

- `ITrackService` returns `TrackDto` for all queries and `Create`/`Update`; `Create`/`Update` take
  `TrackDto`; `Delete(long)` is unchanged. `TrackRepository` still returns entities.
- `TrackManager` no longer has entity-typed `ITrackService` methods; the DTO surface (base class +
  any retained `GetPaged` override) satisfies the interface, and `TrackConverter` runs inside it.
- `GET api/track/page`, `GET api/track/meta/{id}`, `PUT api/track/meta/{id}`, and
  `POST api/track/upload` produce DTOs (no raw `TrackEntity` crosses the wire from DeepDrftAPI).
- `CmsTrackService` deserializes DTOs; `ICmsTrackService` and its consuming components are DTO-typed;
  the CMS track browser, edit, upload, and delete flows work unchanged from the user's view.
- `UnifiedTrackService.UploadAsync` returns `ResultContainer<TrackDto>` and converts the unpersisted
  entity to a DTO via `TrackConverter` before `Create`.
- DeepDrftCli compiles against the DTO interface: add/edit convert at the call site, list/display bind
  `TrackDto`, behavior is unchanged from the user's view.
- DeepDrftPublic: per the Q2.3 decision — either its wire format is `PagedResult<TrackDto>` (default)
  or it converts at its own boundary to keep the existing shape. Either way the public gallery loads
  identically.
- `TrackConverter` is the single entity↔DTO conversion path; it lives inside `TrackManager` /
  `UnifiedTrackService`, not at scattered controller or page call sites.

---

## Test cases

### Workstream 1

1. **Cold-start seed.** Point DeepDrftAPI at an empty Auth DB, boot. Verify migrations applied,
   system roles present, admin user created with the seeded creds.
2. **Idempotent re-boot.** Boot DeepDrftAPI a second time against the seeded DB. No duplicate admin,
   no migration error.
3. **Login round-trip.** From Manager's login page, authenticate against DeepDrftAPI `/api/auth/*`;
   confirm a JWT is issued and stored client-side.
4. **Protected DeepDrftAPI endpoint with JWT.** Call an AuthBlocks-protected endpoint with the
   issued token → 200; without it → 401.
5. **ApiKey endpoints unaffected.** `GET api/track/page` with a valid ApiKey → 200; with none → 401.
   Confirm the JWT middleware does not start rejecting ApiKey-only requests.
6. **CORS preflight.** Browser OPTIONS preflight from the Manager origin to DeepDrftAPI `/api/auth/*`
   returns the allow headers; a disallowed origin is rejected.
7. **Manager has no secrets.** Grep Manager's deployed `environment/` — no signing secret / admin
   password present.

### Workstream 2

1. **Page endpoint shape.** `GET api/track/page` returns `PagedResult<TrackDto>` JSON; `Items` carry
   the same field values as before (compare a row against its `TrackEntity` pre-change).
2. **Sort preserved.** Each supported `sortColumn` (TrackName, Artist, Album, Genre, ReleaseDate)
   and `sortDescending` produce the same ordering as before the change — proves the sort-column→
   expression mapping survived the move to the DTO path.
3. **Get-meta shape.** `GET api/track/meta/{id}` returns `TrackDto`; 404 for a missing id; 500 path
   on a forced query error still returns 500.
4. **Update round-trip.** `PUT api/track/meta/{id}` updates fields, clears optionals on null,
   leaves `EntryKey` immutable; subsequent get reflects the change.
5. **Upload returns DTO with Id.** `POST api/track/upload` returns `TrackDto` with `Id > 0` and the
   correct `EntryKey`; the CMS upload flow reads back the new row.
6. **CMS browser/edit/delete.** Through the Manager UI: list paginates and sorts, edit saves,
   delete removes the row and the vault entry, upload adds a row — all against DTO responses.
7. **Public behaves identically.** DeepDrftPublic's gallery loads and paginates the same as before,
   whichever Q2.3 branch was taken (DTO wire by default, or boundary conversion if deferred). Payload
   values match a pre-change capture.
8. **CLI behavior unchanged.** `DeepDrftCli list` / `add` / GUI add+edit+delete operate the same from
   the user's view; the displayed fields (Id, Name, Artist, Album, Genre, ReleaseDate, EntryKey) are
   correct, now sourced from `TrackDto`.
9. **Converter is the only path.** Confirm (by inspection/coverage) no DeepDrftAPI metadata response
   serializes a `TrackEntity` directly, and the conversion happens inside `TrackManager` /
   `UnifiedTrackService`, not in a controller or Razor component.

---

## Risks / trade-offs

- **Highest risk (W1):** ApiKey + JWT middleware coexistence on one host (Q1.3). If misordered, one
  auth scheme can short-circuit the other. Needs deliberate review and test cases 4–5.
- **Highest cost (W2):** the consumer fan-out from B2, not the interface edit itself. Flipping
  `ITrackService` to DTO is a small file; the cost is that **every** consumer changes — DeepDrftAPI
  controller + `UnifiedTrackService`, DeepDrftCli (two services), DeepDrftManager (`CmsTrackService`
  + components), and DeepDrftPublic (decision-gated). Payload is identical field-for-field, so there
  is no runtime serialization risk, but the breadth is the real budget. This is a larger blast radius
  than the prior draft, which kept CLI and Public out — Daniel's system-wide rule pulls them in.
- **The sort switch is the one piece of real logic at risk (B1/B2).** Everything else is a type
  rename; the sort-column→expression mapping is product behavior that must not be lost in the move to
  the DTO path. Test case W2-2 exists specifically to guard it.
- **Unknown until B1/A1:** the BlazorBlocks `Manager`/`IManager` surface and the AuthBlocks package
  signatures are not readable from this repo. Both waves open with a package-surface read. For W2 the
  open question is narrow: does the base DTO `GetPaged` carry a configurable sort mapping, or must
  `TrackManager` keep a `GetPaged` override? The plan holds either way.
- **Open decision (W2):** whether DeepDrftPublic's public wire format moves to `TrackDto` (default,
  recommended) or stays `TrackEntity` via a boundary conversion (Q2.3). B6 is gated on Daniel's call;
  the rest of Wave B does not depend on it.
