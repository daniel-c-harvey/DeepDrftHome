# CMS-PLAN.md — DeepDrftHome admin CMS

Forward-looking plan for the in-site Blazor CMS that replaces `DeepDrftCli`. Sits alongside `PLAN.md` (general roadmap) and `CONTEXT.md` (architecture orientation). Per `CONTEXT.md §6`, items move from here to `COMPLETED.md` when work lands; do not delete completed entries.

This document **supersedes `PLAN.md §2.4` (Web-side track upload)** and its open question on authentication. The CMS is the home for that capability; once auth and the CMS surface land here, `PLAN.md §2.4` should be archived to `COMPLETED.md` with a forward-pointer to this file.

The plan is intentionally written before all open questions are resolved. Sections that depend on a Daniel decision are marked `[open question]` or `[TBD pending Daniel's input]`. The questions themselves are gathered in the section at the bottom — answer those and the marked sections collapse to commitments.

---

## 1. Goal and motivation

Replace `DeepDrftCli` with a browser-based admin surface (the **CMS**) living inside `DeepDrftWeb`, gated by a login. The CMS owns all track-management operations: add, list, edit, delete. After the CMS is proven, `DeepDrftCli` retires.

**Why now:**

- The CLI requires shell access to the host machine. Collective members without that access can't add content. The CMS removes that gating without weakening the architectural rule that the browser never reaches the database directly (the CMS still goes through the existing dual-database service boundary — it just runs server-side inside `DeepDrftWeb`, where direct service references are already permitted).
- `PLAN.md §2.4` already identified web upload as a near-term need but blocked on the authentication question. The CMS framing answers the wider question (auth is needed *for the CMS as a whole*, not just for upload) and unblocks the upload work.
- The auth surface introduced here is the first identity surface in the product. It is a precondition for several speculative items in `PLAN.md` cross-cutting (favourites, listening history, per-user playlists). Designing it deliberately now is cheaper than retrofitting it later (`feedback_design_for_adaptability`).

**Non-goals (explicit):**

- Public account signup. The CMS is for the collective, not the audience. Public-side identity (subscribe, favourites) is a separate future decision.
- Replacing the unauthenticated public `GET api/track/{id}` read path. Listeners continue to stream without auth.
- Reworking the dual-database split or the streaming substrate. The CMS is a new consumer of existing seams.

---

## 2. Solution structure (new projects)

### 2.1 New RCL: `DeepDrftCms` `[open question — name and exact split]`

Daniel said "if possible we will keep the CMS code in an RCL, with mountable pages for the CMS functionality." Recommended shape:

```
DeepDrftCms                 Razor Class Library (RCL).
                            CMS pages, components, view models, page-route registration.
                            References: DeepDrftModels, DeepDrftWeb.Services,
                            DeepDrftContent.Services, Cerebellum.AuthBlocks, MudBlazor.
                            Mounted into DeepDrftWeb via project reference + route discovery.
```

**What goes in the RCL:**

- Pages (`.razor`) under a `Pages/Cms/` folder, all routed under a single base path (recommended `/cms`, see open question).
- View models that compose `TrackEntity` editing state.
- A `CmsStartup` (or equivalent extension method) that the host calls to register CMS services, auth policies, and route fallbacks. Mirrors the existing `Startup.ConfigureDomainServices` pattern.
- CMS-specific components (track-edit form, upload dropzone, confirmation dialogs). Reuse `TrackCard` and other public-side components where they fit.

**What stays in `DeepDrftWeb`:**

- The new upload controller endpoint (`POST api/cms/track`, see §5). Controllers are host-owned per the existing convention.
- Auth middleware wiring, cookie config, login challenge redirects.
- Reference to the `DeepDrftCms` RCL plus the `app.MapRazorComponents<App>().AddAdditionalAssemblies(typeof(DeepDrftCms._Imports).Assembly)` registration.

**Why an RCL rather than a folder in `DeepDrftWeb.Client`:**

- Isolation of the auth-gated surface from the public client. The public WASM bundle does not need to ship CMS pages, components, or the AuthBlocks dependency. Smaller download for listeners.
- Reusability. If a future deployment wanted the CMS as a standalone admin host (different port, different process, different auth posture), the RCL is already self-contained.
- The mountable-page model gives a clean URL prefix without coupling CMS routing to the public site's `Pages.cs` nav source-of-truth.

**Render mode `[open question]`:** The CMS pages probably want `InteractiveServer` rather than `InteractiveAuto`/`InteractiveWebAssembly`. Reasons: (a) auth cookies and `HttpContext` are simpler server-side; (b) the CMS pages call services that already live server-side; (c) file uploads via `InputFile` are natively server-side. WASM CMS pages would have to call the new `POST api/cms/track` over HTTP with the auth cookie attached — workable, but no benefit over server-side rendering. Recommend **Server** for CMS pages; confirm.

### 2.2 Solution changes

- Add `DeepDrftCms` (RCL) to `DeepDrftHome.sln`.
- `DeepDrftWeb` adds a project reference to `DeepDrftCms`.
- `DeepDrftWeb` adds a NuGet (or absolute-path, NetBlocks-style) reference to `Cerebellum.AuthBlocks`. `DeepDrftCms` may also need it directly if it surfaces login UI components.
- `DeepDrftCli` and its `obj/` legacy `.NET 9` artefacts: removed in §8 (retirement).

---

## 3. Authentication model `[TBD pending Daniel's input on AuthBlocks]`

Daniel specified `Cerebellum.AuthBlocks` and "local login." The library is not on public NuGet and was not discoverable on GitHub; treat its surface as something Daniel must describe before this section commits. The plan below names the shape of the auth surface in terms the eventual integration must satisfy, regardless of which AuthBlocks primitives back it.

### 3.1 What "local login" must provide (functional requirements)

- A login page (likely `/cms/login` or `/login`) that accepts credentials and establishes a session.
- A session mechanism that survives the prerender → WASM boundary (cookie-based is the obvious match — already how dark mode persists; see `CONTEXT.md §3.6`).
- An authorization gate on every CMS page and every CMS-only API endpoint. Unauthenticated requests redirect to login (UI) or 401 (API).
- A logout affordance.
- A way to seed the first admin account at deploy time (config file, environment variable, or a `dotnet user-jwts`-style command). Required because there is no public signup.

### 3.2 Account model `[open question]`

Three plausible shapes — pick one:

1. **Shared admin password.** One credential, possibly stored in `environment/cms.json` next to `apikey.json`. Lowest ceremony; matches the CLI's "trusted operator" model. Audit trail is per-action with no "who did it" attribution.
2. **Individual accounts per collective member.** Real user table (hashed passwords), real `UserId` on mutations. Enables attribution, per-member permissions later, and is the precondition for any future audience-side identity work. Higher initial cost.
3. **Hybrid: single admin role today, designed for accounts tomorrow.** Schema and middleware assume `UserId`; v1 has one seeded admin and no signup flow; account-management UI is deferred. (`feedback_design_for_adaptability` — defer the feature, design the seam.)

**Recommendation: option 3.** Capture `UserId` on every CMS-side mutation (a nullable `CreatedByUserId` on `TrackEntity` and equivalent on any new entities) from day one, even if v1 has exactly one user. This avoids the backfill cliff if option 2 is ever revisited. The user table is small (one row) but real; the login flow is "username + password," not "shared password against a config string." This is the option the rest of the plan assumes.

If Daniel prefers option 1 for speed, the rest of this plan still holds — just drop the `UserId` column and the user table.

### 3.3 What we learn from AuthBlocks before committing

Concrete questions Daniel needs to answer (or point at docs for):

- Does AuthBlocks bring its own user store (EF Core entities, schema, migrations) or does it expect us to provide one?
- Does it bring a login UI / Razor components, or only the policy/middleware primitives?
- Cookie-based, JWT-based, or both? If both, which is the recommended posture for a server-rendered Blazor surface?
- Does it integrate with ASP.NET Core Identity, or is it a parallel/replacement system?
- Password hashing — provided by the library, or BYO?

Once answered, the auth section commits and the §4 page list inherits its specifics (login form fields, logout button placement, "current user" display).

---

## 4. CMS pages and features

The CMS replaces what the CLI does today (add, list, delete) and grows the small set of operations the CLI never offered (edit, image upload). All routes are under `/cms` (see open question on prefix).

### 4.1 Wave 1 surface — parity with the CLI

These are the minimum to retire `DeepDrftCli`.

**`/cms/login`** — credential form, post → session cookie set → redirect to `/cms/tracks`. Login failure stays on page with error message. Already-authenticated users skip to `/cms/tracks`.

**`/cms/tracks`** — track list. The CMS's mirror of `list`.

- Reads the same `PagedResult<TrackEntity>` that the public gallery reads (`GET api/track/page`). Per `user_one_source_multiple_views`, the CMS list is a different rendering of the same VM — table layout with admin affordances (edit, delete buttons), sort columns, optional row-level selection for bulk operations (see Wave 2).
- Empty state mirrors the CLI's "No tracks found in database."
- Server-rendered. Pagination via the existing endpoint; no new API needed for read.

**`/cms/tracks/new`** — add track form. The CMS's mirror of `add`.

- `InputFile` component for WAV selection (browser-side picker — no server file-path entry, that was a CLI affordance and is wrong for a web upload).
- Form fields matching the CLI: track name (required), artist (required), album, genre, release date.
- Submit posts to the new upload endpoint (see §5). On success, redirect to `/cms/tracks` with a flash-style confirmation.
- Validation mirrors the CLI: `.wav` extension required, required fields enforced, release date format `YYYY-MM-DD`.
- Streams the upload to the server rather than reading into memory client-side — large WAVs are common.

**`/cms/tracks/{id}`** — track detail / edit form. **New surface** (the CLI never had edit).

- Loads the `TrackEntity` from the SQL side. Read-write for the metadata fields. Read-only for `EntryKey` (replacing the binary is a separate "replace audio" action — see Wave 2 open question).
- Delete button with confirmation dialog. Calls the new delete endpoint (see §5).

**`/cms/logout`** — clears the session, redirects to public home `/`.

### 4.2 Wave 2 surface — operations the CLI did not offer

These were never in the CLI but are natural admin needs that appear immediately once the CMS exists. Land after Wave 1 is stable.

- **Image upload / cover art.** Pairs with `PLAN.md §2.1` (image vault wired through). The CMS is where image uploads should live — same auth gate, same dual-write pattern as audio. If §2.1 hasn't landed by Wave 2, this is the trigger to land it.
- **Replace audio for an existing track.** Re-upload a WAV against an existing `TrackEntity.Id`, keeping metadata. Writes a new vault entry, updates `EntryKey`, schedules the old entry for cleanup (see "dual-write rollback" in `PLAN.md §4.3`).
- **Bulk delete** of selected rows in the list. UX is simple; the underlying delete endpoint already exists from Wave 1.
- **Search and filter in the CMS list.** Folds into `PLAN.md §2.3` — same `GetPaged` extension serves both surfaces.

### 4.3 Wave 3 surface — speculative `[speculative]`

- **Account management UI** (if §3.2 option 2/3 is chosen and the collective grows beyond one user).
- **Audit log view** — every CMS mutation written to a small log table, viewable in `/cms/audit`. Cheap to build once mutations are funnelled through the CMS endpoints; valuable once more than one user can mutate.
- **Dead-letter view** — surface the `DeadLetterLog` (`PLAN.md §4.3`) so orphaned vault entries can be inspected and reaped from the CMS rather than via a maintenance script.

---

## 5. Server-side dual-write strategy `[open question on transport]`

The dual-write contract is unchanged from the CLI (see `CONTEXT.md §3.4` and the existing `CliService.HandleAddCommand` flow): `DeepDrftContent.Services.TrackService.AddTrackFromWavAsync` writes the audio to the vault and returns an unpersisted `TrackEntity`, then `DeepDrftWeb.Services.TrackService.Create` saves to SQL. The question is **where that orchestration runs** in the CMS world.

### Option A — In-process direct calls (recommended)

The CMS upload endpoint (`POST api/cms/track`) lives in `DeepDrftWeb`. `DeepDrftWeb` adds a project reference to `DeepDrftContent.Services` (it already exists as a class library — same way `DeepDrftCli` consumes it). The controller:

1. Saves the uploaded `IFormFile` to a temp file (the existing `AddTrackFromWavAsync` is file-path oriented).
2. Calls `DeepDrftContent.Services.TrackService.AddTrackFromWavAsync(tempPath, ...)`. This writes to the FileDatabase **on the same machine as the running DeepDrftWeb process** — which means the vault path in `DeepDrftWeb`'s config must point at the same disk location `DeepDrftContent` reads from.
3. On success, calls `DeepDrftWeb.Services.TrackService.Create(trackEntity)`.
4. Deletes the temp file.
5. Returns the persisted entity (or an error if either step fails).

**Pros:** No API key dance. No extra HTTP hop. Mirrors the CLI's existing direct-call pattern exactly — same code path, same orchestration, same failure modes — just triggered by an HTTP request instead of a console command. The dual-write semantics that already exist (and the existing rollback gap from `PLAN.md §4.3`) carry over unchanged.

**Cons:** `DeepDrftWeb` now needs the vault path in its configuration and write access to the shared vault directory. If `DeepDrftWeb` and `DeepDrftContent` are ever deployed to different hosts, this option breaks. (Today they coexist on the same host; if that ever changes, option B becomes mandatory.)

### Option B — HTTP proxy through `DeepDrftContent.PUT api/track/{id}`

The CMS upload endpoint reads the WAV from the browser, processes it server-side into an `AudioBinaryDto`, then `PUT`s it to `DeepDrftContent` with the API key. Then it calls `DeepDrftWeb.Services.TrackService.Create` for the SQL side.

**Pros:** Preserves the host-boundary discipline. `DeepDrftWeb` never touches the vault disk path. Deployable on separate hosts without change.

**Cons:** `AddTrackFromWavAsync` is currently file-path-oriented — to call it from `DeepDrftWeb`, we'd either need to extract the WAV-processing logic into a stream-oriented overload (worth doing anyway) or duplicate the audio processing in the CMS endpoint. The existing `PUT api/track/{id}` controller comment in `DeepDrftContent/Controllers/TrackController.cs` flags that the endpoint receives an already-processed `AudioBinaryDto` and that "if a file-upload flow is added in future, route it through `TrackService` instead" — which is itself a vote for option A or for a parallel processing path. Also adds an API-key round-trip per upload.

### Option C — Mixed: in-process for audio, in-process for SQL, but with a `DeadLetterLog`

Same as option A but explicitly couples the CMS upload to landing the `PLAN.md §4.3` dead-letter mechanism in the same wave. The argument: every web upload narrows the time gap between content-side success and SQL-side failure becoming a real production concern (CLI users hit Ctrl-C; web users get HTTP 502 and try again).

### Recommendation

**Option A for v1.** It is the minimum-change path and matches the CLI's existing semantics exactly. Capture the dead-letter consideration as a hard prerequisite on Wave 2 (once the upload surface is open to non-shell-access operators, the orphaned-vault-entry risk grows). The "stream-oriented processing path" extraction is worth doing later as part of `PLAN.md §1.2` (audio format diversity), where it becomes mandatory anyway — not as a blocker for Wave 1.

`[open question]` Daniel to confirm option A vs B. If the two hosts will ever live on separate machines, the answer is B.

### Idempotency and rollback (carried-over constraint)

Whichever option lands, the dual-write rollback gap (`PLAN.md §4.3`) is unchanged: if step 1 succeeds and step 2 fails, audio is orphaned. The CLI lives with this today and so does the CMS in v1. Bringing the dead-letter log forward to Wave 1 is a defensible choice — flag it.

---

## 6. Phase decomposition

Themes, not dates. The order between waves is sequential (each depends on its predecessor); within a wave, items can run in parallel against the wave's foundation.

### Wave 1 — Auth + scaffolding + parity

**Goal:** A logged-in collective member can do everything the CLI does today, from a browser.

- **W1.1 `DeepDrftCms` RCL skeleton.** Project created, added to solution, referenced from `DeepDrftWeb`. Empty `Pages/Cms/Index.razor` mounted at `/cms` returning a "CMS — under construction" placeholder, proving the mount works.
- **W1.2 AuthBlocks integration + login.** `Cerebellum.AuthBlocks` referenced, configured, seeded with one admin account (config-driven). `/cms/login` page, `/cms/logout`, session cookie. `[Authorize]`-style gate on `/cms/*`. Auth schema columns (`CreatedByUserId`) added to `TrackEntity` as a nullable migration even though they will not be populated by historical CLI-added rows.
- **W1.3 CMS track list.** `/cms/tracks` consuming the same `GET api/track/page` endpoint as the public gallery. Different rendering (table with admin affordances), same VM. No new SQL endpoint.
- **W1.4 CMS upload endpoint + add page.** New `POST api/cms/track` on `DeepDrftWeb` (auth-gated, see §5 for the transport decision). `/cms/tracks/new` page wires `InputFile` to the endpoint.
- **W1.5 CMS delete endpoint + delete UI.** New `DELETE api/cms/track/{id}` on `DeepDrftWeb`. Removes the SQL row and the vault entry; logs orphans if vault delete fails after SQL delete succeeds. Delete button + confirmation in the list and detail pages.
- **W1.6 CMS edit endpoint + edit page.** New `PUT api/cms/track/{id}` (metadata only — no binary replacement in Wave 1). `/cms/tracks/{id}` page.

### Wave 2 — Operations the CLI never had

**Goal:** Make the CMS materially better than the CLI, not just equivalent.

- **W2.1 Image vault wiring.** Fold in `PLAN.md §2.1` if it has not landed independently. Image upload UI in the CMS, `GET/PUT api/image/{entryKey}` on `DeepDrftContent`, `ImagePath` semantics shift from URL to entry key.
- **W2.2 Replace audio.** Re-upload WAV for an existing track. Old vault entry queued for cleanup via dead-letter log (see W2.4).
- **W2.3 Bulk delete + selection.** Multi-row affordances on the list. No new endpoint — reuses the per-row delete in a loop.
- **W2.4 Dual-write rollback / dead-letter log.** Land `PLAN.md §4.3` here at the latest. Surface orphaned vault entries in a `/cms/dead-letter` view, with a "delete" action.
- **W2.5 Search and filter.** Fold in `PLAN.md §2.3`. The CMS list and the public gallery both gain filter via the same `GetPaged` extension.

### Wave 3 — Speculative

`[speculative]` items from §4.3 plus anything that emerges from Wave 1/2 usage. Do not commit until Daniel signals interest.

---

## 7. Constraints and integration points

Things this plan must honour without re-deciding them.

- **`CONTEXT.md §3.2` (service projects vs. host projects).** Controllers, middleware, auth wiring live in `DeepDrftWeb`. CMS view models, pages, and components live in the RCL. No domain logic in either host beyond HTTP concerns.
- **`CONTEXT.md §3.4` (TrackEntity is a join, not a content blob).** The new edit endpoint mutates SQL-side metadata only. Binary replacement is a separate operation against the vault.
- **`feedback_no_direct_db_from_network_clients`.** The CMS runs server-side in `DeepDrftWeb` and is therefore in the trusted process. The browser still does not reach the database — it talks to CMS endpoints, which call the existing services, which call the databases. The architectural rule is preserved.
- **`user_one_source_multiple_views`.** The CMS list is a *different rendering* of the same `PagedResult<TrackEntity>` the public gallery uses. Do not introduce a parallel `GetCmsTrackPage` endpoint or a parallel VM. If the CMS needs additional fields, extend the existing VM, don't fork it.
- **`feedback_design_for_adaptability`.** Capture `CreatedByUserId` on mutations from day one, even with one user. Do not introduce schema columns later as a retrofit.
- **`CONTEXT.md §3.6` (dark-mode prerender bridge).** Auth cookie reads in server prerender follow the same pattern as the dark-mode cookie. If AuthBlocks needs to read auth state server-side and carry it into WASM, it should integrate with `PersistentComponentState` the same way `DarkModeSettings` does. (Likely irrelevant if CMS pages are server-rendered.)
- **`PLAN.md §0` (audit baseline).** The streaming substrate is stable; the CMS does not touch it. Anything the CMS reads from `DeepDrftContent` goes through the existing `GET api/track/{id}` path.
- **Supersession of `PLAN.md §2.4`.** When W1.4 lands, doc-keeper archives `PLAN.md §2.4` to `COMPLETED.md` with a note "subsumed by CMS-PLAN.md Wave 1." This document is the authoritative roadmap for the upload capability.

---

## 8. Retirement plan for `DeepDrftCli`

The CLI does not get deleted on day one. Sequence:

1. **Wave 1 lands.** CMS reaches parity. CLI continues to work — both producers write to the same dual-database, both can be used.
2. **Soak period.** Daniel uses the CMS exclusively for some interval (his call — a week, a release, a feel). During soak, the CLI is the fallback if the CMS exhibits issues.
3. **Deprecation.** `DeepDrftCli/CLAUDE.md` gains a deprecation banner pointing at the CMS. No new CLI features land.
4. **Removal.** `DeepDrftCli` project is removed from `DeepDrftHome.sln`. Its directory is deleted. The Terminal.Gui dependency goes with it. `DeepDrftCli/CLAUDE.md` is deleted. Stray `obj/Debug/net9.0/` artefacts also disappear. The root `CLAUDE.md` and `CONTEXT.md §2` lose the CLI entry — that is a doc-keeper task that lands with the removal commit.

**Open question on the Terminal.Gui mode.** The `gui` subcommand provides a tactile interactive interface that some operators may prefer to a browser. Three options:

- **Drop it entirely.** Simplest. The CMS subsumes the use case.
- **Keep `DeepDrftCli` indefinitely as a secondary admin path.** Costs us a project to maintain.
- **Extract Terminal.Gui as a separate tool.** Overkill unless Daniel has an active reason to keep it.

**Recommendation:** Drop it. The browser CMS covers the same operations with richer affordances, and maintaining two admin surfaces dilutes both. If Daniel disagrees, option 2 is fine — just commit to it explicitly so the CLI doesn't drift into a half-supported limbo.

---

## 9. Open questions for Daniel

These are blockers on specific sections of the plan. Numbered for terse reply.

1. **`Cerebellum.AuthBlocks` shape.** What does this library actually provide? Specifically:
   - User store (EF entities + migrations) — bundled, or BYO?
   - Login UI components — bundled, or build our own?
   - Cookie-based, JWT, or both? Default posture for server-rendered Blazor?
   - Relationship to ASP.NET Core Identity — sits on top, replaces, or parallel?
   - Where do I read the docs / source? (It is not on public NuGet or discoverable on GitHub.)
2. **Account model.** Option 1 (shared admin password), option 2 (per-member accounts), or option 3 (one user today, schema designed for many tomorrow — recommended)?
3. **RCL project name and URL prefix.** Project name `DeepDrftCms` (recommended) or something else? Route prefix `/cms` (recommended), `/admin`, `/manage`, or other?
4. **Render mode for CMS pages.** `InteractiveServer` (recommended for auth + uploads) or `InteractiveAuto`/`InteractiveWebAssembly` (consistency with the public client)?
5. **Dual-write transport.** Option A (in-process direct calls, recommended), option B (HTTP through `DeepDrftContent`), or option C (A plus dead-letter log in Wave 1)? If the two hosts will ever deploy to separate machines, the answer is B.
6. **CMS scope confirmation.** Wave 1 = parity (add, list, edit, delete). Wave 2 = image upload, replace audio, bulk delete, dead-letter view, search/filter. Anything missing? Anything to demote out of Wave 1?
7. **CLI retirement.** Drop Terminal.Gui mode entirely (recommended), keep `DeepDrftCli` indefinitely as a secondary path, or extract Terminal.Gui as a separate tool?
8. **Soak duration.** How long does the CMS run alongside the CLI before the CLI is removed? Time-based, release-based, or "I'll tell you when"?

Answer these in any order. Each unblocks the corresponding section.

---

## 10. Working with this file

- **Same conventions as `PLAN.md`.** Items move to `COMPLETED.md` when they land; do not delete. Original "What / Why / Shape" body preserved. Open questions belong in the item that raises them — they expire when the item does. The exception is §9, which is a single rendezvous point for all blocking decisions; entries there are removed as Daniel answers them (and the marked-up sections elsewhere collapse to commitments at the same time).
- **Markers.** `[open question]` = a decision point inside an otherwise-committed item. `[TBD pending Daniel's input]` = an entire section that cannot commit until §9 is answered. `[speculative]` = direction inferred, not committed.
- **Relationship to `PLAN.md`.** When the CMS work touches an item in `PLAN.md` (notably §2.1 image vault, §2.3 search/filter, §2.4 web upload, §4.3 dead-letter), this document is the place to coordinate the joint landing. Cross-reference rather than duplicating the item's body.
