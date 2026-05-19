# ARCHITECTURE-PROPOSAL.md — DeepDrftHome layer & host restructure

Forward-looking proposal. Not a commitment. Sits alongside `PLAN.md` (feature roadmap), `CMS-PLAN.md` (CMS roadmap), and `CONTEXT.md` (orientation). When Daniel chooses a direction, the relevant points get promoted into `PLAN.md` / `CMS-PLAN.md`; the rest stays here as the record of what was considered.

Answers three questions Daniel raised:

1. Does the current split of SQL-domain code into `DeepDrftWeb.Services` and FileDatabase-domain code into `DeepDrftContent.Services` make sense, or should the Data layer be unified?
2. Should the CMS and public site be separate applications, or is the RCL-into-`DeepDrftWeb` shape from `CMS-PLAN.md` the right call?
3. If a BlazorBlocks-style Data / API / Web layering is an improvement, what does it concretely look like for DeepDrftHome?

Recommendation up front, reasoning after.

---

## 0. Recommendation (TL;DR)

- **Keep** the SQL data layer and the FileDatabase data layer in **separate projects**. They are different storage systems with different invariants, not two repositories over one database. The split is load-bearing, not vestigial.
- **Rename** the two `*.Services` libraries to match their actual role and reduce ambiguity: `DeepDrftWeb.Services` → `DeepDrftData` (or `DeepDrftMeta.Data`), `DeepDrftContent.Services` → `DeepDrftContent.Data`. Lift their host-agnostic seams (repository / manager) onto the **BlazorBlocks Data + Models** base types so the SQL side gets `BaseEntity`, `IRepository`, `Manager`, and `ClassifiedDbError` for free.
- **Introduce** a new `DeepDrftApi` class library that holds the controller bases and DTOs the two ASP.NET hosts share. Hosts stay as hosts — `DeepDrftWeb` and `DeepDrftContent` — but their controllers thin out and inherit from `ModelController<,,>` (BlazorBlocks) or a DeepDrft-local extension of it.
- **Keep** the CMS as an RCL mounted into `DeepDrftWeb` (the `CMS-PLAN.md` decision). Do **not** split it into a separate host. Re-deciding that adds infra cost (a third host, separate deployment, separate auth boundary) for no current benefit. The RCL already buys us the option to extract it later without redesigning the data layer.
- **Defer** any deeper change (e.g., merging the two hosts, replacing FileDatabase with object storage) — those are separate decisions with their own evaluation costs and no current trigger.

Net effect: DeepDrft adopts the BlazorBlocks layering pattern where it pays off (Data/API base types, MudBlazor entity-management primitives on the CMS side), without forcing a structural split that the dual-database design already justifies keeping.

---

## 1. Why the SQL/FileDatabase split should stay

Daniel's framing — "the SQL database and FileDatabase are in different projects and that doesn't really make sense to me" — reads, on a second pass, as discomfort with the *naming and project shape* rather than with the underlying decision. They are physically different storage systems:

- **`DeepDrftContext` (SQLite → Postgres)** is a relational store with EF Core migrations, query translation, transactional semantics, and an EF change tracker. The unit of work is `SaveChangesAsync()`.
- **FileDatabase** is a typed binary vault on disk with per-vault JSON index files, a `FileSystemWatcher` for cross-process index reloads, and an explicit "swallow exceptions, return null/false" error contract ported from a TypeScript original.

The two have no shared invariants, no shared DbContext, no shared transaction, and no shared error model. Putting them in the same project would force one of:

1. **One project, two unrelated subsystems.** Saves a `.csproj`. Buys nothing. Forces every consumer (CLI, two hosts) to drag both subsystems' dependencies even when they only need one. `DeepDrftCli` is the only consumer that legitimately uses both.
2. **One project with an abstract `IRepository<T>` covering both.** Wrong abstraction. The FileDatabase isn't a key-value store with an EF-shaped interface; it's a typed-vault system with vault registration, index watchers, entry-key sanitization, and binary processors. Forcing it into `Add/Update/Delete/Find` either hides what it does or contorts what callers can ask of it. This is the cost of *false* unification.
3. **One project, no shared abstraction.** Same as option 1 minus the `.csproj` savings. Pure naming churn.

None of those improve on what exists. The current split is the right one; the names just don't communicate why. The rename in §0 fixes the naming without disturbing the decision.

**One unification *is* worth taking.** The SQL side today does **not** use BlazorBlocks's `BaseEntity` / `IRepository` / `Manager` / `ClassifiedDbError` machinery. That's pure missed leverage. `TrackEntity` could inherit from `BaseEntity` (gaining `CreatedAt` / `UpdatedAt` / `IsDeleted` for free, plus the audit-attribution column the CMS needs anyway), `TrackRepository` could inherit from `Repository<DeepDrftContext, TrackEntity>`, and `TrackService` could become a `Manager<TrackEntity, TrackDto, TrackRepository, ...>`. That's a one-time refactor that buys soft-delete, structured DB error classification, and uniform paging — all of which the CMS surface will exercise.

The FileDatabase side gets none of this and shouldn't pretend to. Its operations are not CRUD over an entity; they are vault-typed storage operations. Leave it alone.

---

## 2. Why the CMS should stay an RCL inside `DeepDrftWeb`

`CMS-PLAN.md §2.1` already commits the CMS to a Razor Class Library (`DeepDrftCms`) mounted into `DeepDrftWeb` at `/cms`. Daniel's question — "should the CMS and Public be separate sites?" — is reasonable to re-examine, but the cost/benefit comes out the same.

**What "separate sites" would mean concretely:**

- A new host `DeepDrftCmsHost` (third ASP.NET Core app) on its own port / domain, with its own `Program.cs`, its own AuthBlocks wiring, its own CORS surface, its own deployment pipeline.
- That host references `DeepDrftCms` (the RCL) and the two data libraries. It does **not** share a process with `DeepDrftWeb`.
- The public site stops needing AuthBlocks references entirely. The "Sign in" link moves to a cross-domain redirect.

**What it would buy:**

- True isolation of the auth-gated bundle from the public WASM payload. (The RCL already gives us most of this — public client doesn't reference CMS RCL or `AuthBlocksWeb`.)
- Independent deploy cadence. The CMS can ship without re-deploying the public site, and vice versa.
- A failure in the CMS host doesn't affect the public site, and vice versa.
- Clear ops boundary: the CMS process can be put behind a VPN / different ingress / different scaling rules.

**What it would cost:**

- A third ASP.NET host to operate, configure, monitor, and deploy.
- Either duplicated `DeepDrftWeb.Services` access (CMS host calls `Create` on the SQL context directly) or a thicker HTTP boundary (CMS host calls back through `DeepDrftWeb`).
- A second AuthBlocks installation, or a shared JWT issuer + audience config that both hosts trust. Either way, more auth surface to keep aligned.
- Cross-origin handling for any shared assets (fonts, logos, theme).
- Two render-mode configurations to keep in sync if the CMS ever needs to share a layout primitive with the public site.

**Trigger to revisit:** the CMS becomes a separate host when one of these is true:

- The collective grows to the point where CMS operators are a different population from public visitors and need a different ingress.
- The CMS develops features that don't share substrate with the public site (e.g. a heavy ingestion pipeline that wants its own scaling profile).
- A specific compliance requirement (data residency, audit isolation) forces process separation.

None of these are true now. The RCL is the cheaper-and-reversible answer — it keeps the door open to extraction without paying for it until then.

**Borrowed precedent:** GitHub's admin surface lives inside the same Rails monolith as the public site, gated by role; GitLab the same; Stripe Dashboard the same. The "separate admin host" pattern is reserved for products where the admin surface is *operationally different in kind* (e.g. Salesforce's setup UI). DeepDrft's CMS is "an authenticated set of pages over the same data" — that's RCL territory.

**Recommendation: hold the RCL decision in `CMS-PLAN.md`. No change.** Capture the trigger conditions above so the option stays visible.

---

## 3. Restructuring proposal (concrete shape)

This is what the BlazorBlocks-pattern-applied solution looks like. Names are placeholders; the structure is the point.

### 3.1 Project map (proposed)

```
DeepDrftHome.sln
├── DeepDrftModels                 (existing, augmented)
│   References: Cerebellum.BlazorBlocks.Models
│   - TrackEntity : BaseEntity     (gains CreatedAt/UpdatedAt/IsDeleted/CreatedByUserId)
│   - TrackDto : BaseModel
│   - TrackInputModel : InputModelBase
│   - PagedResult<T>, PagingParameters<T> — REMOVE, use BlazorBlocks.Models.Common
│   - AudioBinaryDto, ImageBinaryDto — stay here (cross-host contracts)
│
├── DeepDrftData                   (renamed from DeepDrftWeb.Services)
│   References: DeepDrftModels, Cerebellum.BlazorBlocks.Data, Cerebellum.BlazorBlocks.Data.Postgres
│   - DeepDrftContext : DbContext
│   - TrackConfiguration : BaseEntityConfiguration<TrackEntity>
│   - TrackRepository : Repository<DeepDrftContext, TrackEntity>
│   - TrackManager : Manager<TrackEntity, TrackDto, TrackRepository, TrackConverter>
│   - Migrations/ (Postgres)
│
├── DeepDrftContent.Data           (renamed from DeepDrftContent.Services)
│   References: DeepDrftModels
│   - FileDatabase/ subtree (unchanged)
│   - WavOffsetService, AudioProcessor, content-side TrackService
│   - Stays standalone — does NOT inherit BlazorBlocks data primitives
│
├── DeepDrftApi                    (NEW)
│   References: DeepDrftModels, DeepDrftData, Cerebellum.BlazorBlocks.Api
│   - TrackController : ModelController<TrackEntity, TrackDto, TrackManager>
│     (the SQL-side track controller, today's api/track/page logic)
│   - Base classes / shared filters for the two hosts to reuse
│   - Does NOT host. No Program.cs.
│
├── DeepDrftWeb                    (existing, slimmed)
│   References: DeepDrftApi, DeepDrftCms (RCL), Cerebellum.AuthBlocks*, ...
│   - Program.cs, Startup.cs, MainLayout
│   - AuthBlocks wiring, MapAuthBlocks, theme prerender (DarkModeService)
│   - TypeScript audio interop
│   - The CMS controllers (api/cms/track, image upload proxy) live here
│   - app.MapControllers picks up TrackController from DeepDrftApi
│
├── DeepDrftWeb.Client             (existing, augmented)
│   References: DeepDrftModels, Cerebellum.BlazorBlocks.Web,
│                Cerebellum.AuthBlocks.Web
│   - Existing player / gallery / dark-mode plumbing
│   - Track-management UI inherits/composes ModelPageViewModel where applicable
│
├── DeepDrftCms                    (NEW per CMS-PLAN.md, unchanged by this proposal)
│   References: DeepDrftModels, DeepDrftData, DeepDrftContent.Data,
│                Cerebellum.BlazorBlocks.Web, Cerebellum.AuthBlocks.Web
│   - CMS pages and view models. Mounted at /cms in DeepDrftWeb.
│   - Reuses ModelPageViewModel / ModelView / EditModelModal scaffolding.
│
├── DeepDrftContent                (existing, slimmed)
│   References: DeepDrftApi (for shared base controllers/filters), DeepDrftContent.Data
│   - Program.cs, Startup.cs, ApiKey middleware, CORS, ForwardedHeaders
│   - TrackController (binary): GET/POST endpoints (POST api/track/upload added per CMS-PLAN W1.4)
│   - WavOffsetService usage stays in the controller seam
│
├── DeepDrftTests                  (existing)
│   References: DeepDrftContent.Data, DeepDrftData, DeepDrftModels
│   - FileDatabase tests unchanged
│   - Add coverage for the new TrackManager / TrackRepository surface
│
└── DeepDrftCli                    (RETIRED per CMS-PLAN.md §8)
```

### 3.2 What moves

| From | To | Why |
|---|---|---|
| `DeepDrftWeb.Services/*` | `DeepDrftData/*` | Rename for clarity. Lift onto BlazorBlocks primitives. |
| `DeepDrftContent.Services/*` | `DeepDrftContent.Data/*` | Rename for symmetry. No code changes beyond namespace. |
| `DeepDrftWeb/Controllers/TrackController.cs` | `DeepDrftApi/Controllers/TrackController.cs` | The SQL-side track controller is reusable; host-specific concerns (auth wiring) come from the host. |
| `DeepDrftModels/PagedResult.cs`, `PagingParameters.cs` | (delete) — use `Cerebellum.BlazorBlocks.Models.Common.*` | Eliminate the parallel paging contract. |
| `TrackEntity` (free POCO) | `TrackEntity : BaseEntity` | Audit columns from day one (`feedback_design_for_adaptability`). Soft delete becomes free. |
| `TrackService` (DeepDrftWeb.Services) | `TrackManager : Manager<...>` | Inherits structured DB error classification, paged results, exists checks, classified-failure mapping. |
| Custom result handling in `DeepDrftWeb/TrackController` | Inherited from `ModelController<,,>` | Less code to keep in sync. |
| Content-side `TrackService` (DeepDrftContent.Services) | unchanged | This is *not* a CRUD-over-an-entity service. It is a content-orchestration service. Leave it as-is. |

### 3.3 What stays

- The **two-host architecture**: `DeepDrftWeb` for metadata + UI + CMS mount, `DeepDrftContent` for binary content. The dual-database boundary is preserved exactly.
- **The streaming substrate** (`StreamingAudioPlayerService`, `StreamDecoder`, `PlaybackScheduler`, `WavOffsetService`, the `?offset=` path). Untouched by this restructure. `PLAN.md §0` baseline holds.
- **The CMS-as-RCL** decision. `DeepDrftCms` mounts into `DeepDrftWeb` as already committed.
- **The HTTP proxy for uploads** (`CMS-PLAN.md §5 Option B`). The new `POST api/track/upload` on `DeepDrftContent` is unchanged.
- **The FileDatabase contract.** Public load/register operations still swallow exceptions and return null/false. This is load-bearing per `CONTEXT.md §3.3` — the restructure does not touch it.

### 3.4 Acquired benefits

- **Free audit columns.** `BaseEntity` gives `CreatedAt`, `UpdatedAt`, `IsDeleted`. The CMS plan's `CreatedByUserId` joins them. Soft delete becomes the default deletion semantics for `TrackEntity` — a net win (the CMS's "deleted a track by accident" case becomes recoverable). Hard delete remains available for the dead-letter cleanup path.
- **Structured DB error surface.** `ClassifiedDbError` + the `DbErrorStatusMapping` in BlazorBlocks gives the CMS proper "duplicate entry key" / "constraint violation" / "not found" responses without hand-rolling them. Critical when the CMS lets users do things the CLI couldn't.
- **Uniform paging contract.** One `PagedQuery` / `PagedResult<T>` across the codebase instead of DeepDrft's parallel `PagingParameters<T>` + `PagedResult<T>`. The public gallery, the CMS list, and any future view all consume the same shape. Honours `user_one_source_multiple_views`.
- **Less boilerplate on the next controller.** When `api/image/*` lands (`PLAN.md §2.1`), the SQL-side image-metadata controller is `ImageController : ModelController<ImageEntity, ImageDto, ImageManager>` and inherits the full read/write surface. The image processor and vault wiring on the content side remain bespoke.
- **CMS scaffolding for free.** `DeepDrftCms` pages can use `ModelPageViewModel`, `ModelView.razor`, `EditModelModal.razor`, `ConfirmDeleteModal.razor`, `ModelClient`. The CMS Wave 1 surface (list / edit / delete) collapses to wiring rather than authoring.

### 3.5 Acquired costs

- **Refactor cost.** Lifting `TrackEntity` onto `BaseEntity` is a schema migration (three new columns), a controller-base swap, a `TrackService` → `TrackManager` swap. Non-trivial; not enormous. Best done as part of the Postgres migration (CMS-PLAN W1.0) — both touch the same migration boundary.
- **BlazorBlocks coupling.** DeepDrft becomes a downstream of BlazorBlocks. Versions need to be tracked. This is already true once `Cerebellum.AuthBlocks` lands per CMS-PLAN; the data + API + web BlazorBlocks packages just extend the surface.
- **Mental model.** The `Manager<TEntity, TModel, TRepository, TConverter>` signature is heavier than `TrackService`. The payoff is uniformity across however many entities follow `TrackEntity`. With one entity it's overhead; with three or more it pays.
- **Test churn.** Existing tests don't cover `TrackService` / `TrackRepository`; they cover FileDatabase. So this is mostly net-new coverage for the SQL side, which is going to be needed once the CMS exists regardless.

### 3.6 What conflicts with CMS-PLAN.md

- **`CMS-PLAN.md §2` (Solution structure).** References `DeepDrftWeb.Services` and `DeepDrftContent.Services` by their current names. If this proposal is adopted, the rename happens *before* CMS Wave 1.2 lands (the RCL references the renamed projects). Wave 1.0 (Postgres migration) is the natural moment to do the rename and the BlazorBlocks lift together — same files are already being edited.
- **`CMS-PLAN.md §5` (dual-write).** Unchanged. Option B (HTTP proxy through `DeepDrftContent`) still applies. `DeepDrftWeb` still does not reference `DeepDrftContent.Data` directly. (The new `POST api/track/upload` controller on `DeepDrftContent` is the *only* writer that touches `DeepDrftContent.Data`.)
- **`CMS-PLAN.md §3.2` (CreatedByUserId).** Subsumed by the `BaseEntity` lift if it includes a `CreatedByUserId` field on `BaseEntity` itself, or added separately if `BaseEntity` doesn't have it. Either way, the column lands in the same migration.
- **`CMS-PLAN.md §6 Wave 1`.** Wave 1.0 (Postgres) becomes "Postgres migration **+ BlazorBlocks data lift + project rename**" if Daniel approves this proposal. Wave 1.1 onwards is unaffected in shape, but the references update.

No commitments in CMS-PLAN.md are *broken* by this proposal. The §3.6 list is reorganisation, not contradiction. The `[InteractiveServer]` decision, the AuthBlocks integration shape, the Option B upload transport, the RCL mount path, the Postgres choice — all unchanged.

---

## 4. Alternatives considered

For completeness; not recommended.

### 4.1 Full unification — one Data project covering SQL and FileDatabase

Single `DeepDrftData` project with both subsystems. Rejected for the reasons in §1 — the two storage systems have no shared invariants and forcing a shared abstraction is the worst of both worlds. Unification would only be defensible if FileDatabase were retired (see §4.3).

### 4.2 Two-host monolith — collapse `DeepDrftWeb` and `DeepDrftContent` into one host

A single ASP.NET Core process owning both controllers, both DbContexts (well, one DbContext and one FileDatabase root), and both APIs. The browser still talks to "one server" instead of two named HttpClients.

**Pros:** Simpler deployment. One config surface. One auth pipeline. No dual-host coordination.

**Cons:** Loses the deployment seam — if the binary content service ever needs to scale differently from the metadata service (a real concern once non-WAV formats land and uploads get heavier), splitting them back out costs more than keeping them split. Also, the `ApiKey` boundary on `PUT api/track/{id}` exists *because* the content host is a distinct trust boundary; collapsing the hosts requires re-deciding how mutations are authorised. (The CMS plan's Option B specifically chose to *preserve* this seam.)

Not recommended now. Possibly worth revisiting once HTTP Range + CDN caching (`PLAN.md §4.1`) lands and the content host becomes a thin streamer in front of cacheable assets.

### 4.3 Replace FileDatabase with object storage (S3/MinIO/Azure Blob)

The deeper architectural question hiding behind "the storage projects don't make sense to me." FileDatabase is a hand-rolled vault system that could be replaced by a battle-tested object store. `EntryKey` becomes the object key; the vault index becomes either a separate DB table or is dropped entirely (object stores have listing APIs); `WavOffsetService` becomes HTTP Range. `IndexWatcher` disappears.

**Pros:** Drops a sizable chunk of code (the FileDatabase subtree). Native HTTP range. Native cache. Native CDN. Native multipart upload. Mature ops tooling.

**Cons:** New infra dependency (matching the Postgres dependency the CMS plan already adds). Loses the typed-vault structure that the codebase is currently built around. Migration is non-trivial — existing on-disk content has to be re-keyed. The "everything is local files" simplicity that suits a small collective today is real value, not just legacy weight.

**Not recommended now.** Capture as a future option to revisit alongside `PLAN.md §4.1` (HTTP Range + CDN) — the two questions point at the same architectural pivot. If `4.1` lands as "stream from disk with `enableRangeProcessing`," it defers `4.3`; if `4.1` requires object storage to be tractable at scale, `4.3` becomes the path.

---

## 5. Sequencing if Daniel approves

Not a commitment; a suggested order if direction is accepted.

1. **Promote the rename + BlazorBlocks data lift into CMS-PLAN.md Wave 1.0** (or a new W1.0a if Daniel wants it visible as a distinct step). Same migration boundary as the SQLite→Postgres move.
2. **Update `CMS-PLAN.md §2` references** to the new project names (`DeepDrftData`, `DeepDrftContent.Data`).
3. **Land `DeepDrftApi`** as part of the same wave — controllers move out of `DeepDrftWeb` and into the shared library, hosts thin out.
4. **Land the rest of CMS Wave 1** (`W1.1` RCL skeleton, `W1.2` AuthBlocks, `W1.3+` upload) on the new base. The CMS Wave 1 surface authoring becomes lighter because of the `ModelPageViewModel` / `ModelView` scaffolding now available.
5. **Image vault wiring (`PLAN.md §2.1`)** lands next, gaining `ImageEntity : BaseEntity` and a `ModelController`-based `ImageController` essentially for free.

The CMS surface gets cheaper to build, not more expensive — the upfront cost is paid in the Postgres-migration wave that was happening anyway.

---

## 6. Decision points for Daniel

These are the load-bearing yes/nos. The rest of the proposal flexes around them.

1. **Adopt BlazorBlocks data primitives for the SQL side?** (`BaseEntity`, `Repository`, `Manager`, `ClassifiedDbError`, BlazorBlocks `PagedResult`/`PagedQuery`.) — Recommended yes. The biggest payoff for the least churn.
2. **Add `DeepDrftApi` as a shared controller library?** — Recommended yes, but lower priority. Could be deferred until the second SQL-side controller (Image) is on the horizon, since with one controller it's premature.
3. **Rename `DeepDrftWeb.Services` and `DeepDrftContent.Services`?** — Recommended yes if (1) is approved. The rename is a cheap clarity win at the same migration boundary; doing it standalone is more churn than reward.
4. **Keep the CMS as an RCL inside `DeepDrftWeb`?** — Recommended yes. No change from `CMS-PLAN.md`. Captured the trigger conditions for revisiting in §2.
5. **Keep the two hosts (`DeepDrftWeb` + `DeepDrftContent`) split?** — Recommended yes. The dual-database / dual-host shape is right.

If (1) is no, the rest of this proposal mostly evaporates — the existing structure is fine and the discomfort is real but cosmetic. If (1) is yes, (2)–(3) follow naturally and (4)–(5) are confirmations of decisions already taken.
