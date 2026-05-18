# CMS-PLAN.md — DeepDrftHome admin CMS

Forward-looking plan for the in-site Blazor CMS that replaces `DeepDrftCli`. Sits alongside `PLAN.md` (general roadmap) and `CONTEXT.md` (architecture orientation). Per `CONTEXT.md §6`, items move from here to `COMPLETED.md` when work lands; do not delete completed entries.

This document **supersedes `PLAN.md §2.4` (Web-side track upload)** and its open question on authentication. The CMS is the home for that capability; once auth and the CMS surface land here, `PLAN.md §2.4` should be archived to `COMPLETED.md` with a forward-pointer to this file.

§3 (Authentication model) commits to `Cerebellum.AuthBlocks` as the substrate, based on a read of the library at `C:\Development\AuthBlocks\`. The remaining open questions (§9) are decisions only Daniel can make — render mode, URL prefix, dual-write transport, CLI retirement timing, soak duration, and the Postgres-vs-unify database call surfaced by the AuthBlocks reading.

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

### 2.1 New RCL: `DeepDrftCms` `[open question — exact name]`

Daniel said "if possible we will keep the CMS code in an RCL, with mountable pages for the CMS functionality." Recommended shape:

```
DeepDrftCms                 Razor Class Library (RCL).
                            CMS pages, components, view models, page-route registration.
                            References: DeepDrftModels, DeepDrftWeb.Services,
                            DeepDrftContent.Services, Cerebellum.AuthBlocks.Web,
                            Cerebellum.AuthBlocks.Models, MudBlazor.
                            Mounted into DeepDrftWeb via project reference + route discovery.
```

**What goes in the RCL:**

- Pages (`.razor`) under a `Pages/Cms/` folder, all routed under a single base path (recommended `/cms`, see open question).
- View models that compose `TrackEntity` editing state.
- A `CmsStartup` (or equivalent extension method) that the host calls to register CMS-specific services, view models, and route fallbacks. Mirrors the existing `Startup.ConfigureDomainServices` pattern. **Auth wiring lives in `DeepDrftWeb` (see §2.2)** — the RCL itself does not call `AddAuthBlocks` because that brings in the `AuthDbContext`, EF migrations, and JWT middleware, all of which are host concerns.
- CMS-specific components (track-edit form, upload dropzone, confirmation dialogs). Reuse `TrackCard` and other public-side components where they fit.
- A `[HierarchicalRoleAuthorize("Admin")]` attribute (from `AuthBlocksWeb.HierarchicalAuthorize`) on every CMS page component, so `Admin` and any descendant role are admitted by the bundled hierarchical role handler.

**What stays in `DeepDrftWeb`:**

- The new upload controller endpoint (`POST api/cms/track`, see §5). Controllers are host-owned per the existing convention. Protected by `[Authorize(Roles = "Admin")]` — the JWT bearer middleware AuthBlocks installs validates the access token on each request.
- The `AddAuthBlocks(...)` call in `Program.cs` and the matching `await app.Services.UseAuthBlocksStartupAsync()` post-build hook. This installs JWT bearer middleware, the hierarchical role authorization handler, the `AuthDbContext`, the EF migrations, and seeds system roles plus the configured admin user on first boot.
- The `app.MapAuthBlocks()` call that registers `/api/auth/*`, `/api/users/*`, `/api/roles/*`, `/api/user-roles/*`, and `/api/pending-registrations/*` minimal-API endpoints. The CMS UI uses `/api/auth/login`, `/api/auth/logout`, `/api/auth/refresh`, and `/api/auth/me`; the rest are available if Wave 3 account-management ever lands.
- Reference to the `DeepDrftCms` RCL plus the `app.MapRazorComponents<App>().AddAdditionalAssemblies(typeof(DeepDrftCms._Imports).Assembly)` registration. The `AuthBlocksWeb` login/logout/register pages are picked up the same way once that assembly is added to `AddAdditionalAssemblies`, exposing `/account/login` and `/account/logout` for free.

**Why an RCL rather than a folder in `DeepDrftWeb.Client`:**

- Isolation of the auth-gated surface from the public client. The public WASM bundle does not need to ship CMS pages, components, or the AuthBlocks UI dependency. Smaller download for listeners.
- Reusability. If a future deployment wanted the CMS as a standalone admin host (different port, different process), the RCL is already self-contained.
- The mountable-page model gives a clean URL prefix without coupling CMS routing to the public site's `Pages.cs` nav source-of-truth.

**Render mode `[open question]`:** AuthBlocks's bundled UI (`AuthBlocksWeb` pages) is server-rendered MudBlazor with `JwtAuthenticationStateProvider` reading tokens from browser `localStorage` via JS interop. CMS pages can render in any mode that supports `AuthorizeView` / `[HierarchicalRoleAuthorize]`, but `InteractiveServer` is the cleanest fit: (a) it matches what the bundled login UI uses, (b) `InputFile` uploads are natively server-side, (c) CMS endpoints already live in the `DeepDrftWeb` process so no extra HTTP hop. Recommend **InteractiveServer** for CMS pages; confirm.

### 2.2 Solution changes

- Add `DeepDrftCms` (RCL) to `DeepDrftHome.sln`.
- `DeepDrftWeb` adds a project reference to `DeepDrftCms`.
- `DeepDrftWeb` references three AuthBlocks packages (NuGet, published as `Cerebellum.AuthBlocks*` at version 10.3.16+):
  - `Cerebellum.AuthBlocks` — the `AddAuthBlocks`/`UseAuthBlocksStartupAsync`/`MapAuthBlocks` integration surface, JWT services, hierarchical role authorization handler.
  - `Cerebellum.AuthBlocks.Web` — the bundled MudBlazor login/logout/register pages, `JwtAuthenticationStateProvider`, `TokenService` (localStorage), and the `HierarchicalRoleAuthorizeAttribute` / `HierarchicalRoleAuthorizeView` used by the RCL.
  - `Cerebellum.AuthBlocks.Models` — `ApplicationUser`, `ApplicationRole`, `SystemRole` constants. Transitively pulled by the other two; reference explicitly if `DeepDrftCms` or `DeepDrftWeb.Services` need the entity types.
- `DeepDrftWeb.Client` references `Cerebellum.AuthBlocks.Web` and calls `AuthBlocksWeb.Client.Startup.ConfigureServices(builder.Services)` to register `AddAuthorizationCore()`, `AddCascadingAuthenticationState()`, and `AddAuthenticationStateDeserialization()`. This is the prerender → WASM bridge for auth state, equivalent to what `DarkModeSettings` does today (see `CONTEXT.md §3.6`).
- `DeepDrftCms` references `Cerebellum.AuthBlocks.Web` (for the authorize attribute / view) and `Cerebellum.AuthBlocks.Models` (for `SystemRoleConstants.Admin`).
- A new `DeepDrftWeb/environment/authblocks.json` (or appsettings section) holds the JWT secret, issuer, audience, Mailtrap email connection, admin seed credentials, and Postgres connection string. Follows the same pattern as `apikey.json` — not in repo.
- **New infrastructure dependency: PostgreSQL.** AuthBlocks is PG-only (see §3.5). The existing SQLite metadata DB (`../Database/deepdrft.db`) is unaffected; the auth DB is a separate context. Local dev gains a `docker-compose.yml` Postgres service or assumes a local PG install.
- `DeepDrftCli` and its `obj/` legacy `.NET 9` artefacts: removed in §8 (retirement).

---

## 3. Authentication model

DeepDrft adopts `Cerebellum.AuthBlocks` (v10.3.16, source at `C:\Development\AuthBlocks\`, published as Cerebellum-prefixed NuGets) as a plug-and-play account system. AuthBlocks is built on ASP.NET Core Identity, hands us a complete user/role/registration substrate, and ships its own MudBlazor login UI. The CMS is the first consumer of this substrate in DeepDrftHome.

### 3.1 What AuthBlocks provides

Concretely, from reading the library source:

- **User store.** Bundled. `ApplicationUser : IdentityUser<long>` and `ApplicationRole : IdentityRole<long>` (the long-keyed Identity entities) live in `AuthBlocksModels`. The `AuthDbContext` extends `IdentityDbContext<...>` with the full eight-table layout (`users`, `roles`, `user_roles`, `user_claims`, `role_claims`, `user_logins`, `user_tokens`, plus AuthBlocks's own `pending_registrations` and `refresh_tokens`) under an `auth` schema. EF migrations ship in the package and run automatically on `UseAuthBlocksStartupAsync()`. We bring nothing — we just call the extension methods.
- **Login / UI.** Bundled. `Cerebellum.AuthBlocks.Web` ships server-rendered MudBlazor pages: `/account/login`, `/account/logout`, `/account/register` (registration-code flow), `/account/super-register` (admin-creates-user), plus admin pages under `/user-admin/*` for user, role, registration, and permission management. A `RedirectToLogin` component handles unauthenticated → login redirects with `returnUrl` preservation, and a `LogoutButton` is exposed for menus.
- **Session mechanism.** JWT bearer, not cookie. Access tokens (default 60 min) plus refresh tokens (default 7 days) issued by AuthBlocks's minimal-API routes; stored in browser `localStorage` by `TokenService`; refreshed automatically by `JwtAuthenticationStateProvider`. Server-side, the standard `[Authorize]` / `[Authorize(Roles=...)]` attributes work because AuthBlocks configures `JwtBearerDefaults.AuthenticationScheme` as the default authenticate/challenge scheme.
- **Identity relationship.** AuthBlocks **sits on top of** ASP.NET Core Identity (`AddIdentityCore<ApplicationUser>().AddRoles<...>().AddEntityFrameworkStores<AuthDbContext>()`), but does **not** install `SignInManager` (no cookie-based sign-in) and replaces the default role authorization handler with `HierarchicalRolesAuthorizationHandler`. Password hashing is the standard ASP.NET Core Identity `IPasswordHasher<TUser>` — provided, not BYO. Default password policy is relaxed (length 6, no required character classes); we can tighten via the standard Identity options if needed.
- **Hierarchical roles.** `SystemRole.Admin` (id 1) is the parent of `SystemRole.UserAdmin` (id 2). Hierarchy is seeded on startup. `[HierarchicalRoleAuthorize("UserAdmin")]` admits any user assigned `Admin` or `UserAdmin`. The hierarchy is extensible by editing `SystemRole.cs` upstream — for v1 we use what's there.
- **Admin seeding.** A single `AdminUserSettings { UserName, Email, Password }` on the options object causes `UseAuthBlocksStartupAsync` to create (or repair) one admin user on first boot, assigned the `Admin` system role. This is exactly the "seed the first admin at deploy time" capability the CMS needs.
- **Email.** AuthBlocks's registration flow requires an outbound email provider (Mailtrap; `EmailConnection.Host` + `EmailConnection.Token` are required options). For v1 we wire this even though the CMS does not exercise the `/account/register` invitation flow — the options validator throws on startup if it is missing. Wave 3 account-management is when this matters; for Wave 1 we point it at a Mailtrap sandbox.

### 3.2 Account model

**Committed: hierarchical-role accounts via AuthBlocks, seeded with one `Admin` user from config.** This is the option-3 shape from the prior draft and it happens to be exactly what AuthBlocks gives us out of the box:

- Real per-user accounts (`ApplicationUser` table). No shared password.
- One seeded admin on first boot via `AdminUserSettings`. Username, email, password come from `DeepDrftWeb/environment/authblocks.json` (gitignored, same pattern as `apikey.json`).
- No public signup in Wave 1. The `/account/register` page that AuthBlocks bundles requires a registration code (generated by an admin via `/api/pending-registrations`). We do not surface `/account/register` in any nav until Wave 3 account management lands; the route exists but is uninteresting until then.
- **Mutation attribution.** `TrackEntity` gains a nullable `CreatedByUserId : long?` column in the W1.2 migration. Populated on every CMS-originated mutation; null for historical CLI-added rows and for any pre-CMS data. Captures attribution from day one even though Wave 1 has exactly one user (`feedback_design_for_adaptability`).
- **Role gate.** Every CMS page and every `api/cms/*` endpoint requires the `Admin` system role. We use `Admin` rather than introducing a new `CmsAdmin` role because the collective is small and the existing hierarchy already covers the case; if Wave 3 ever needs finer grain (e.g. a `ContentEditor` role that can edit but not delete), that is a `SystemRole.cs` edit upstream, not a redesign here.

### 3.3 Session and prerender bridge

AuthBlocks's JWT-in-localStorage posture interacts with Blazor's prerender → WASM handoff:

- **Server prerender** of a CMS page asks `AuthenticationStateProvider` for state. Server-side, that is satisfied by the JWT bearer middleware reading the `Authorization` header — which is not present on the initial page navigation. The bundled `JwtAuthenticationStateProvider` runs *client-side* (it needs `IJSRuntime` to read localStorage), so during prerender, the user appears anonymous and `[Authorize]` pages redirect to `/account/login`.
- **Solution:** CMS pages render as `InteractiveServer` (no prerender bypass needed since the same circuit handles auth). For the public site's `InteractiveAuto` pages, AuthBlocks's `AddAuthenticationStateDeserialization()` (called in `DeepDrftWeb.Client.Startup`) is the bridge — it carries serialized auth state from prerender into the WASM render and back. This is the same shape as the dark-mode `PersistentComponentState` bridge described in `CONTEXT.md §3.6`.
- A "Sign in" link in the public-site nav points at `/account/login`; AuthBlocks's login page returns the user to `ReturnUrl` on success. The CMS landing page is the natural return target after CMS login.

### 3.4 Authorization wiring (concrete)

- **Razor pages in the RCL:** `@attribute [HierarchicalRoleAuthorize("Admin")]` at the top of every `/cms/*` page. The bundled handler walks the role hierarchy.
- **API endpoints in DeepDrftWeb:** `[Authorize(Roles = "Admin")]` on the new `api/cms/track` controller. The hierarchical handler is registered globally so the standard `[Authorize(Roles=...)]` attribute participates in hierarchy walks too.
- **Anonymous public surface:** unaffected. `GET api/track/page` and `GET api/track/{id}` remain unauthenticated. The public gallery, player, and home page do not require login. Auth state on the public side is "anonymous or signed-in admin"; signed-in state surfaces only as a "CMS" link in the nav.
- **Login UI:** consume the bundled pages at `/account/login`, `/account/logout`. Do not author CMS-specific login pages. `RedirectToLogin` handles the "I tried to visit `/cms/tracks` while anonymous" case.
- **First-run experience:** Daniel runs the app, AuthBlocks applies migrations against the Postgres `auth` schema, seeds `Admin` + `UserAdmin` roles, creates the admin user from `authblocks.json`. Daniel visits `/account/login`, authenticates, lands on `/cms/tracks`.

### 3.5 Database conflict — AuthBlocks is Postgres-only

**This is the load-bearing surprise from the AuthBlocks reading.** AuthBlocks's data layer hard-codes `UseNpgsql(...)` in `AddAuthBlocksDataForWebApi`, the bundled migrations are PostgreSQL-specific (`NOW()`, `timestamp with time zone`, identity columns via `Npgsql:ValueGenerationStrategy`), and the DbContext sets `HasDefaultSchema("auth")`. DeepDrft's existing metadata DB is SQLite. There is no clean "use SQLite for everything" path.

Three options, in order of preference:

1. **Run Postgres alongside SQLite (recommended).** Add a `docker-compose.yml` Postgres service for local dev; production deploys against a managed PG. SQLite continues to back `DeepDrftContext` (track metadata); Postgres backs `AuthDbContext` (identity). The two DbContexts never share a transaction — track mutations and audit attribution are recorded in SQLite with a `CreatedByUserId : long?` foreign-key-in-name-only to the Postgres `auth.users.Id`. Cost: a new infra dependency. Benefit: zero forks of AuthBlocks.
2. **Migrate `DeepDrftContext` to Postgres too.** Unify on a single DB engine. Requires rewriting EF migrations, re-seeding from `deepdrft.db`, and accepting Postgres in every dev environment. Larger lift; cleaner end-state.
3. **Fork AuthBlocks to add SQLite support.** Replace `UseNpgsql` with a provider-agnostic registration, rewrite migrations, maintain the fork. Highest cost, perpetual maintenance burden. Not recommended.

**Recommendation: option 1 for Wave 1.** It minimises change to DeepDrft and avoids touching `DeepDrftContext`. Capture option 2 as a deferred consideration if Postgres-for-auth-only feels operationally annoying after a few months of running it. Option 3 is a non-starter unless AuthBlocks gains upstream SQLite support.

`[open question]` Daniel to confirm option 1 vs option 2. Option 1 is the path of least resistance for shipping the CMS; option 2 is the cleaner long-term posture.

### 3.6 What this section commits

- `Cerebellum.AuthBlocks` (+ `.Web`, + `.Models`) is the auth substrate. We do not write our own user store, password hashing, login pages, or JWT plumbing.
- The CMS is gated by the `Admin` system role via `[HierarchicalRoleAuthorize("Admin")]` on pages and `[Authorize(Roles="Admin")]` on API endpoints.
- The login UI lives at `/account/login` (bundled). Logout at `/account/logout`. The CMS does not author its own.
- One admin user is seeded from `DeepDrftWeb/environment/authblocks.json` on first boot.
- `TrackEntity.CreatedByUserId : long?` is added in W1.2 for attribution.
- Postgres becomes a runtime dependency for the auth context, alongside the existing SQLite track-metadata context (pending §3.5 confirmation).

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
- **W1.2 AuthBlocks integration + login.** Reference `Cerebellum.AuthBlocks`, `Cerebellum.AuthBlocks.Web`, `Cerebellum.AuthBlocks.Models` from `DeepDrftWeb`; reference `Cerebellum.AuthBlocks.Web` from `DeepDrftWeb.Client`. Call `AddAuthBlocks(...)` in `Program.cs` with JWT secret/issuer/audience, Mailtrap email connection, Postgres connection string, and `AdminUserSettings` from `environment/authblocks.json`. Call `await app.Services.UseAuthBlocksStartupAsync()` post-build. Call `app.MapAuthBlocks()` to mount `/api/auth/*` routes. Add the `AuthBlocksWeb` assembly to `AddAdditionalAssemblies` so the bundled `/account/login` and `/account/logout` pages resolve. In `DeepDrftWeb.Client.Startup`, call `AuthBlocksWeb.Client.Startup.ConfigureServices(builder.Services)` for the prerender→WASM auth-state bridge. Add `CreatedByUserId : long?` column to `TrackEntity` via a nullable migration. Provision local Postgres (docker-compose) and document the dev setup. Verify: anonymous visit to `/cms/anything` redirects to `/account/login`; authenticated `Admin` lands successfully.
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
- **`CONTEXT.md §3.6` (dark-mode prerender bridge).** AuthBlocks's `AddAuthenticationStateDeserialization()` (called in `DeepDrftWeb.Client.Startup` per §2.2) is the analogue of the dark-mode `PersistentComponentState` bridge — it carries serialized auth state across the prerender → WASM boundary. For pure-`InteractiveServer` CMS pages this is unused; it matters for the public-site `InteractiveAuto` pages that need to render "Sign in" vs "CMS" links consistently.
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

These are blockers on specific sections of the plan. Numbered for terse reply. The §3 AuthBlocks questions from the prior draft are resolved — the library was read and §3 now commits.

1. **Postgres-for-auth-only vs. unify on Postgres (§3.5).** Option 1: run Postgres alongside SQLite, AuthBlocks gets PG, `DeepDrftContext` stays on SQLite (recommended, minimum-change). Option 2: migrate `DeepDrftContext` to Postgres too, unify on one engine (cleaner end-state, larger lift). Option 3 (fork AuthBlocks for SQLite) is not recommended.
2. **RCL project name and URL prefix.** Project name `DeepDrftCms` (recommended) or something else? Route prefix `/cms` (recommended), `/admin`, `/manage`, or other? Note: login/logout live at `/account/login` and `/account/logout` (bundled, not negotiable without forking AuthBlocks).
3. **Render mode for CMS pages.** `InteractiveServer` (recommended — matches bundled AuthBlocks UI, native `InputFile`) or `InteractiveAuto`/`InteractiveWebAssembly` (consistency with the public client)?
4. **Dual-write transport.** Option A (in-process direct calls, recommended), option B (HTTP through `DeepDrftContent`), or option C (A plus dead-letter log in Wave 1)? If the two hosts will ever deploy to separate machines, the answer is B.
5. **CMS scope confirmation.** Wave 1 = parity (add, list, edit, delete). Wave 2 = image upload, replace audio, bulk delete, dead-letter view, search/filter. Anything missing? Anything to demote out of Wave 1?
6. **CLI retirement.** Drop Terminal.Gui mode entirely (recommended), keep `DeepDrftCli` indefinitely as a secondary path, or extract Terminal.Gui as a separate tool?
7. **Soak duration.** How long does the CMS run alongside the CLI before the CLI is removed? Time-based, release-based, or "I'll tell you when"?
8. **Mailtrap (or alternative) for the AuthBlocks email channel.** AuthBlocks's options validator requires an `EmailConnection.Host` + `Token` on startup even though Wave 1 does not use the registration-email flow. Acceptable to point at a Mailtrap sandbox (free tier) and defer real email until Wave 3, or do you want a different SMTP provider wired from day one?

Answer these in any order. Each unblocks the corresponding section.

---

## 10. Working with this file

- **Same conventions as `PLAN.md`.** Items move to `COMPLETED.md` when they land; do not delete. Original "What / Why / Shape" body preserved. Open questions belong in the item that raises them — they expire when the item does. The exception is §9, which is a single rendezvous point for all blocking decisions; entries there are removed as Daniel answers them (and the marked-up sections elsewhere collapse to commitments at the same time).
- **Markers.** `[open question]` = a decision point inside an otherwise-committed item. `[speculative]` = direction inferred, not committed. (Previous draft used `[TBD pending Daniel's input]` for whole sections that could not commit; the §3 auth section that carried it has since been resolved against the AuthBlocks source.)
- **Relationship to `PLAN.md`.** When the CMS work touches an item in `PLAN.md` (notably §2.1 image vault, §2.3 search/filter, §2.4 web upload, §4.3 dead-letter), this document is the place to coordinate the joint landing. Cross-reference rather than duplicating the item's body.
