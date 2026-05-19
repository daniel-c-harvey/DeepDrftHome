# TWO-APP-SPLIT.md — Public site / CMS architectural split

**Status:** Design draft. Daniel has committed to the split; this document is the shape, not the decision. Implementation is for staff-engineer once Daniel signs off on the open questions at §10.

**Companions:** `CONTEXT.md` (current architecture orientation), `PLAN.md` (forward roadmap), `CMS-PLAN.md` (CMS roadmap — most of its shape survives; the host-of-record changes).

**Cross-link from `PLAN.md`:** add a top-level entry "**Two-app split**" pointing here once Daniel signs off; the items below supersede pieces of `CMS-PLAN.md §2` and `§3`.

---

## 1. Why this design exists

### 1.1 The proximate problem

The current `DeepDrftWeb` host is **one** ASP.NET Core Blazor Web App serving both:

- The **public site** (`Home`, `TracksView`, the audio player stack) — anonymous, must prerender for first-paint quality, lives in the `DeepDrftWeb.Client` WASM assembly.
- The **CMS** (`/cms/*`, track admin) — `Admin`-gated via AuthBlocks, runs `InteractiveServer`, lives in the `DeepDrftCms` RCL referenced by `DeepDrftWeb`.

Both surfaces share:

- The same root `App.razor` → `Routes.razor` → `MainLayout.razor` chain.
- The same global `AddCascadingAuthenticationState` registration (server-side, from `AuthBlocksWeb.Startup.ConfigureAuthServices`).
- The same DI scope during prerender.

Empirically (verified this session): the moment **any** `[Inject]` property is added to `MainLayout.razor` while cascading-auth-state is registered globally, the public Home request hangs forever — `LayoutView` initialisation never completes. Strip every `[Inject]` and the page renders. The reduction in tree state today (`MainLayout.razor` is one line of `<div>@Body</div>` plus a single `IJSRuntime` injection — and the page still hangs with that one inject) is the surviving evidence: the layout has been gutted as a band-aid and the chrome is currently gone from the public site.

Root cause: the public layout takes a cascading auth state it has no business consuming, and the prerender DI scope for an anonymous, presentation-only page is now coupled to the auth subsystem's scoped services. That coupling is the deadlock.

### 1.2 Why band-aids stopped working

Every patch attempted this session has been a rendermode reshuffle around the same coupling:

- `@rendermode` migrating across `App` → `Routes` → `MainLayout` → individual pages.
- `[AllowAnonymous]` sprinkled across public pages to suppress the auth funnel.
- The `JwtAuthenticationStateProvider` rewritten to be prerender-safe.
- `Routes.razor` moved between the server assembly and the WASM assembly (commit `d31a08b` moved it server-side so the router could discover `AuthBlocksWeb` types; that move is what re-crossed the wires).
- Bumping `Cerebellum.AuthBlocks*` from 10.3.31 → 10.3.32 chasing prerender-safety fixes upstream.

None of these address the architectural shape: **two products, one host, one DI scope, one cascading-auth tree**. The fix is structural.

### 1.3 The fix Daniel chose

Two independent ASP.NET Core applications:

- **`DeepDrftPublic`** — customer-facing, anonymous, prerender-first, MudBlazor + (optionally) BlazorBlocks. No AuthBlocks. No cascading auth. No `@rendermode` chaos.
- **`DeepDrftManager`** — staff-facing, fully authenticated via AuthBlocks, built on BlazorBlocks scaffolding. `InteractiveServer` end-to-end. Accessible only via dedicated subdomain.

Each app gets its own host project, its own DI graph, its own deploy unit, its own URL surface. The two share storage and shared models, not Blazor plumbing.

> "Cramming these two usecases into the same blazor server host is the source of YOUR and MY confusion and all this wasted time." — Daniel, this session.

The hard constraints flowing into the design:

1. **Public site MUST prerender.** "Abandoning the pre-rendering benefits for the public portion of the site is exactly the WRONG decision." (Daniel.) First paint speed and SEO/share-card quality are first-class. This rules out a SPA-only or `InteractiveServer`-only public site.
2. **CMS gets AuthBlocks + BlazorBlocks.** The substrate is already chosen — design the host around it, not around what would be theoretically prettier.
3. **The dual-database storage layer does not change.** `DeepDrftWeb.Services` (EF Core / Postgres), `DeepDrftContent.Services` (FileDatabase), `DeepDrftContent` host, the streaming substrate — all stable, all stay.
4. **The single-source-of-truth rule survives.** The CMS list and the public gallery render the same `PagedResult<TrackEntity>`. Two hosts is not permission to fork the view model.

---

## 2. Two-app shape

### 2.1 Recommended project layout

Final naming locked by Daniel 2026-05-19.

```
DeepDrftPublic                NEW. Public-site host (ASP.NET Core, Blazor Web App).
                              Anonymous. Prerender + InteractiveAuto. MudBlazor.
                              No AuthBlocks. Owns api/track/page (see §5) and the
                              TypeScript audio interop.

DeepDrftPublic.Client         NEW. Public-site WASM assembly. Promoted from today's
                              DeepDrftWeb.Client. Contains Home, TracksView, the audio
                              player stack, dark-mode plumbing, HTTP clients.

DeepDrftManager               NEW. CMS host (ASP.NET Core, Blazor Server-only).
                              AuthBlocks + BlazorBlocks. InteractiveServer end-to-end.
                              References the existing DeepDrftCms RCL.

DeepDrftCms                   EXISTING (RCL). CMS pages, layouts, components. Becomes
                              consumed by DeepDrftManager only. Stops being referenced
                              from the public host. Staff-engineer: confirm naming
                              alignment (proposal: rename to DeepDrftManager.Cms).

DeepDrftShared.Client         NEW (RCL). Shared Razor components and client-side helpers
                              that BOTH apps render: TrackCard, TracksGallery (read-mode),
                              DDIcons, palette tokens, font loading helpers, audio player
                              stack (if the CMS plays previews — see §3.4 open question).
                              See §3 for what does and does not belong here.

DeepDrftWeb                   RETIRED. Functionality split across DeepDrftSite and
                              DeepDrftCmsHost. Project file removed from solution.

DeepDrftWeb.Client            RETIRED. Promoted/renamed to DeepDrftSite.Client.

DeepDrftData                  EXISTING. EF Core / Postgres / TrackManager / TrackRepository.
                              Referenced by whichever host owns the metadata API (see §5)
                              and by the CMS host for write paths.

DeepDrftContent               EXISTING. Binary content API. Unchanged.
DeepDrftContent.Data          EXISTING. FileDatabase + audio processing. Unchanged.

DeepDrftModels                EXISTING. Shared contracts. Unchanged.

DeepDrftCli                   EXISTING but retired per CMS-PLAN §8 once split lands.
                              Not relevant to the split itself.

DeepDrftTests                 EXISTING. May gain a few smoke tests around the new hosts
                              (out of scope here; flag for staff-engineer).
```

**One canonical solution file.** Today the repo carries `DeepDrftHome.sln` plus stray `WebAPI.sln`, `WebUI.sln`, `CLI.sln`. The split is the opportunity to delete the strays; keep `DeepDrftHome.sln` as the single solution.

### 2.2 Naming — locked by Daniel 2026-05-19

- `DeepDrftPublic` — public-site host. Renamed from the working name `DeepDrftSite`. (Daniel chose this over `DeepDrftWeb` to distinguish from the existing `DeepDrftWeb` project cleanly.)
- `DeepDrftManager` — CMS host. Renamed from the working name `DeepDrftCmsHost`.
- `DeepDrftShared.Client` — shared RCL. Confirmed as-is.

**Note on `DeepDrftCms` (RCL):** The existing `DeepDrftCms` is an RCL and stays that way (not promoted to a host project). Staff-engineer should evaluate whether to rename it to `DeepDrftManager.Cms` for alignment with the host family, or leave it standalone. This is a naming convention call, not an architectural change.

---

## 3. Shared layer policy

The split is only useful if shared concerns stay shared. The risk is duplication or, worse, two diverging copies of the audio player.

### 3.1 What stays in `DeepDrftModels` (unchanged)

`TrackEntity`, `TrackDto`, `PagedResult<T>`, `PagingParameters<T>`, `ApiResultDto<T>`. The split does not touch the contracts layer.

### 3.2 What stays in `DeepDrftData` / `DeepDrftContent.Data` (unchanged)

The EF Core domain layer and the FileDatabase implementation are class libraries today; they don't care which host references them. The CMS host references both for writes; whichever host owns the metadata API (§5) references `DeepDrftData` for reads.

### 3.3 What moves to `DeepDrftShared.Client` (new RCL, Wave 1 locked)

Things both apps render and want to keep visually consistent. **Daniel confirmed extraction in Wave 1** ("yes DRY and SOLID!") — this is locked for the first implementation phase.

Minimum surface in Wave 1:

- **`TrackCard.razor`** — both the public gallery and the CMS list render tracks. Today's CMS list (CMS Wave 1) renders a table; a future CMS view could embed `TrackCard` previews, and the public-side card must not drift visually. Move it here.
- **`TracksGallery.razor`** — used by the public gallery today. The CMS does not need the gallery layout, but if any CMS preview surface ever wants it (e.g. "preview as listener sees it"), pre-shared is cheaper than retroactive extraction.
- **`DDIcons.cs`** — hand-rolled icons, currently in `DeepDrftWeb.Client/Common/`. Both apps will want the lit/unlit gas-lamp, the brand logo, etc.
- **Palette tokens and CSS variables.** Today `deepdrft-styles.css` lives in `DeepDrftWeb/wwwroot/styles/`. Move the **palette layer** (the `:root` and `.deepdrft-theme-dark` custom properties) into a shared CSS file in `DeepDrftShared.Client/wwwroot/` so MudBlazor themes in both apps map to the same tokens. Page-specific CSS (home page, gallery scoped styles) stays in the public app.
- **MudBlazor palette objects.** Currently inline in `MainLayout.razor`. Promote to a `DeepDrftPalettes.cs` (static `MudTheme Light` / `MudTheme Dark`) in the shared RCL. The CMS host applies the same palettes so the staff side reads as the same product.
- **Font-loading `<link>` helpers.** A `DeepDrftFonts` static or `<DeepDrftFontLinks />` component so both apps emit the same Google Fonts request.

### 3.4 Audio player stack — Wave 1: public only, eventual extraction locked

The player stack today lives entirely in `DeepDrftWeb.Client/`:

- `IPlayerService`, `IStreamingPlayerService`, `AudioPlayerService`, `StreamingAudioPlayerService`, `AudioInteropService`, `StreamingErrorHandler`.
- `AudioPlayerProvider.razor`, `AudioPlayerBar.razor`, `PlayerControls.razor`, `TimestampLabel.razor`, `VolumeControls.razor`, `SpectrumVisualizer.razor`.
- `TrackMediaClient` (HTTP client for `DeepDrftContent`).

**Wave 1 decision:** Audio player stack lives in `DeepDrftPublic.Client` (public site only). The CMS does not preview audio in-browser during Wave 1. CMS users can verify uploads by navigating to the public site in another tab.

**Forward-looking constraint** (Daniel): "Previewing tracks using a shared library of audio streaming components is an eventual must." This means the audio-player stack's **eventual home** is a shared library (e.g. `DeepDrftShared.Audio` or similar), not `DeepDrftPublic.Client` in perpetuity. **Staff-engineer should design the Wave 1 placement (public-only stack structure) so that extraction to a shared library later is mechanical, not a rewrite.** The abstraction behind `IPlayerService` is already in place; use this to preserve the seam cleanly. When a future "preview in CMS" need confirms (likely in a later wave), the extraction is a move operation, not a rearchitecture.

**Wave 1 does not extract this to shared yet.** The complexity (streaming, seek, spectrum, dark-mode-aware visualiser, `DotNetObjectReference` lifetimes) and the CMS's current non-requirement keep this in place for now.

### 3.5 TypeScript interop — stays with the public host

`DeepDrftWeb/Interop/audio/*.ts` and its `Microsoft.TypeScript.MSBuild` pipeline ship with the audio player. Per §3.4 Option A, they ride with `DeepDrftSite` and do not appear in the CMS host. If §3.4 ever flips to Option B, the TS pipeline needs to move to `DeepDrftShared.Client` (or, simpler, the shared RCL serves the compiled JS as a static web asset and the source pipeline stays with the public host as the canonical compiler).

### 3.6 Theme prerender — stays with public host

`DarkModeService` (in `DeepDrftWeb/Services/`) reads the cookie during prerender; `DarkModeSettings` lives in `DeepDrftWeb.Client/Common/`. The dark-mode bridge is only meaningful where prerender happens. Since only the public site prerenders (the CMS is `InteractiveServer` over a logged-in circuit and does not need the cookie-prerender bridge), the dark-mode plumbing stays in the public app.

The CMS still gets dark mode — it just reads the cookie at circuit init the same way any normal Server component does, no `PersistentComponentState` round-trip needed. Implementation detail for staff-engineer.

---

## 4. Render-mode strategy

This section is the load-bearing one for "why this design avoids the deadlock."

### 4.1 Public site: prerender-first, `InteractiveAuto` pages

```
App.razor                           static SSR (host markup)
└── Routes.razor                    static SSR
    └── MainLayout.razor            static SSR  ← key change
        └── Page (Home/Tracks)      @rendermode InteractiveAuto
```

- **No `@rendermode` on `App` or `Routes` or `MainLayout`.** They render statically as the page shell.
- **`@rendermode InteractiveAuto` on each page** (as already attempted in commit `54865e7`, but that fix was incomplete because the auth coupling survived). Pages opt into interactivity individually.
- **MainLayout has no `[Inject]` properties that touch a scoped service participating in cascading auth.** It is presentation chrome only. Anything that needs `IJSRuntime`, `DarkModeSettings`, `IPlayerService` lives **inside** the page (or inside an interactive component the page renders), not in the layout.
  - The `DarkModeService.CheckDarkMode()` call currently in `App.razor.OnInitialized` survives — `App` is the right place for prerender-time cookie reads; it does not get a `@rendermode`.
  - `AudioPlayerProvider` (the cascading host for `IPlayerService`) moves from `MainLayout` to a wrapper inside each page that needs it (`TracksView`), or into a single interactive shell component that `MainLayout` renders **inside** its `@Body` slot without static-prerender coupling. Staff-engineer call; both shapes work.

**Why this avoids the deadlock:** there is no AuthBlocks in this app. `AddCascadingAuthenticationState` is **not registered**. `MainLayout` has no auth state to cascade. The `[Inject]` deadlock symptom is bound to the auth-scoped-service initialisation, not to injection in general — remove the auth context and `[Inject]` becomes safe again.

If staff-engineer wants the additional belt-and-suspenders: `MainLayout` for the public site ships with **zero `[Inject]` properties**. Cosmetic deviation from typical layout patterns; cheap insurance against any future recurrence.

### 4.2 CMS: `InteractiveServer` end-to-end

```
App.razor                           static SSR (host markup, AuthorizeRouteView in Routes)
└── Routes.razor                    static SSR
    └── CmsLayout.razor             @rendermode InteractiveServer
        └── Page (/cms/*)           inherits InteractiveServer from layout
```

- **`AddInteractiveServerComponents()` only.** No WebAssembly render mode in the CMS host. No `InteractiveAuto`, no client assembly.
- **No prerender.** CMS pages are gated by `[HierarchicalRoleAuthorize("Admin")]`; prerendering them in an anonymous context is wrong on two counts (data leakage if the page bypasses the gate during prerender; pointless if it does not). The CMS opts out of prerender via `RenderMode(InteractiveServer, prerender: false)` at the layout or page level.
- **AuthBlocks fully in scope.** `AddAuthBlocks`, `MapAuthBlocks`, `AddCascadingAuthenticationState`, `AddAuthenticationStateDeserialization` — all live here, none of them leak into the public site.
- **`/account/login`, `/account/logout`** (the bundled AuthBlocks UI from `AuthBlocksWeb`) are served by the CMS host. Public site links to them by absolute URL (`https://cms.deepdrft.com/account/login?returnUrl=…` or path on the same domain if §6 picks path-routing).

**Why this avoids the deadlock:** the CMS deadlock symptom never appeared — the CMS rendered fine. The problem was always the **public** layout being dragged into the auth cascade. Once the public side has no auth at all, the CMS is free to use auth normally.

### 4.3 Render-mode crosswalk against today's mess

| Today (`dev` branch) | Two-app design |
|---|---|
| `MainLayout.razor` gutted to one-liner with `[Inject] IJSRuntime` to avoid deadlock | Public `MainLayout.razor` restored to full chrome (nav, theme switcher, footer). No `[Inject]`s of scoped auth services. |
| `Routes.razor` server-side with `AdditionalAssemblies` listing client + CMS + AuthBlocks assemblies | Public `Routes.razor`: only `typeof(DeepDrftSite.Client._Imports).Assembly`. CMS `Routes.razor`: only `typeof(DeepDrftCms._Imports).Assembly` + `typeof(AuthBlocksWeb._Imports).Assembly`. |
| `[AllowAnonymous]` decorating `Home.razor` and `TracksView.razor` to escape the global auth funnel | Removed. No global auth in the public host. |
| `@rendermode InteractiveAuto` on `MainLayout`, then off, then on individual pages | `@rendermode InteractiveAuto` on individual public pages. Layout stays static SSR. |
| `@rendermode InteractiveServer` explicitly declared on AuthBlocks pages | Removed. CMS host's default render mode is `InteractiveServer`; AuthBlocks pages inherit. |
| `JwtAuthenticationStateProvider` prerender-safe rewrite needed because public pages prerendered with the provider in scope | The provider is only registered in the CMS host. Public prerender does not see it. |

---

## 5. API ownership — locked by Daniel 2026-05-19

`DeepDrftWeb` today owns one controller — `TrackController` — exposing `GET api/track/page` against `DeepDrftContext` (Postgres metadata). It also owns three CMS-internal controllers (`CmsUploadController`, `CmsEditController`, `CmsDeleteController`).

After the split, the CMS controllers follow the CMS — they require `[Authorize(Roles="Admin")]` and live in `DeepDrftManager`. The public read endpoint ownership is locked:

### 5.1 Decision: Both hosts reference services directly

**Locked decision:** Neither host talks to the other via HTTP for metadata reads. Instead:

- **Public host (`DeepDrftPublic`)** references `DeepDrftData` for reads. The WASM client calls its own host's `GET api/track/page` endpoint. Same-origin, no CORS configuration needed.
- **CMS host (`DeepDrftManager`)** also references `DeepDrftData` directly (already required for write paths). For reading paged tracks in `/cms/tracks` list pages, the CMS host calls `TrackService` in-process, not cross-host via HTTP.

**Why:** Both hosts already need `DeepDrftData` (public for reads, CMS for reads+writes). Having the CMS read via direct service call instead of HTTP keeps the architecture simpler and removes a cross-host dependency. The "one source of truth, multiple views" rule is preserved — both hosts consume the same `PagedResult<TrackEntity>` contract; only the transport path differs (WASM → HTTP for public, in-process for CMS).

### 5.2 What changed from the design's earlier three-option sketch

The three options (§5.1–5.3 in the original draft) have been collapsed into this single locked shape. Host-boundary discipline is achieved at the architecture level (two separate applications) rather than enforced at the service-call level.

---

## 6. Auth wiring (CMS only)

All present in `CMS-PLAN.md` and landed in CMS Wave 1; preserved here for completeness in the new host. **Nothing about the auth model changes in the split.**

- **AuthBlocks substrate** (`Cerebellum.AuthBlocks*` 10.3.32) — `AddAuthBlocks(...)`, `await app.Services.UseAuthBlocksStartupAsync()`, `app.MapAuthBlocks()` all in `DeepDrftManager/Program.cs`.
- **AuthBlocksWeb pages** (`/account/login`, `/account/logout`) — exposed by adding `typeof(AuthBlocksWeb._Imports).Assembly` to the CMS host's `AddAdditionalAssemblies`.
- **JWT in localStorage** via `JwtAuthenticationStateProvider`. CMS host calls `AuthBlocksWeb.Startup.ConfigureAuthServices` server-side; no WASM client means no `AddAuthenticationStateDeserialization` bridge needed.
- **Auth DB separate from main DB.** `ConnectionStrings:Auth` and `ConnectionStrings:DefaultConnection` are different Postgres databases (or the same instance with different DBs — they share an engine, not a context). The CMS host wires both.
- **`Admin` role gate** via `[HierarchicalRoleAuthorize("Admin")]` on every CMS page and `[Authorize(Roles = "Admin")]` on every CMS API endpoint.
- **Stealth routing (obsolete).** Previous design used stealth routing (404 on unauthorized `/cms/*` hits) for access control in a mixed host. With the CMS now in a separate host, access control is at the host boundary itself via the nginx deployment topology (§7). The `CmsStealthRoutingHandler` middleware is no longer necessary and should be removed from the codebase as part of the split.

**Admin seeding** (`AdminUserSettings` in `authblocks.json`) carries over unchanged. The seed user is created on first boot of the CMS host.

---

## 7. Deployment topology — locked by Daniel 2026-05-19

The repo currently deploys two systemd services (`deepdrft-content`, `deepdrft-web`) on each host (dch6 beta, prod.cerebellumsoftworks.com prod) via `dch5-publish-deploy.sh`. The split adds a third unit.

### 7.1 Locked decision — subdomain split

```
deepdrft.com           → DeepDrftPublic (public site)
manage.deepdrft.com    → DeepDrftManager (CMS host)
content.deepdrft.com   → DeepDrftContent (binary API)
```

Three systemd units per host (`deepdrft-public`, `deepdrft-manager`, `deepdrft-content`). nginx terminates TLS for both main domain and subdomains, routes by hostname:

- `deepdrft.com` and `*.deepdrft.com` → appropriate upstream (public, manager, or content).
- Each upstream gets its own nginx server block.

**Topology notes:**
- Public WASM calls `GET /api/track/page` on its own host (deepdrft.com — same origin).
- Public WASM calls `GET /api/track/{id}` cross-origin to `content.deepdrft.com` (existing CORS config applies).
- Naming: Daniel proposed `manage.deepdrft.com` for the CMS subdomain (alternatives like `admin.` or `cms.` are also workable; final subdomain is Daniel's call if different).

**Advantages of subdomain over path-based:**
- Each host has one nginx server block. Cleaner config.
- CMS can be IP-restricted at the nginx layer (`allow <home-IP>; deny all;` on the subdomain) without affecting the public site. Layered defence on top of the auth gate.
- Unambiguous in browser history and link shares.
- The separate host boundary *is* the access-control mechanism; stealth routing is no longer needed.

### 7.2 Certificate and DNS

- Use a **wildcard cert** (`*.deepdrft.com`) to cover both public and all subdomains, or **separate certs** if the hosting provider charges per-cert. Confirm with Daniel.
- DNS records: `deepdrft.com` A record, `manage.deepdrft.com` CNAME or A record, `content.deepdrft.com` CNAME or A record.

### 7.3 Why stealth routing is now obsolete

Access control in the old design relied on stealth routing (404 on unauthorized `/cms/*` hits) because the CMS shared a host with the public site. With separate hosts on separate subdomains, unauthorized access to `manage.deepdrft.com` is **already blocked at the host boundary** — the user would need to resolve the CMS subdomain in the first place (network-level restriction or nginx auth) or pass an auth token (AuthBlocks JWT). The 404 masking is no longer necessary for security and adds no value.

### 7.4 Deploy-script changes (locked by §8.2 Phase 5)

`dch5-publish-deploy.sh` publishes two projects today. The split makes it three:

- Publish `DeepDrftPublic` (was `DeepDrftWeb`; renamed in Phase 4).
- Publish `DeepDrftManager` (new; created in Phase 1).
- Publish `DeepDrftContent` (unchanged).
- Three `*.tar.gz` artefacts, three `scp`/`tar`/`restart` cycles.
- Three EF migration paths to consider — `DeepDrftContext` migrations apply against the metadata DB (driven by the public host for reads and the CMS host for writes), `AuthDbContext` migrations apply against the auth DB on first run of the CMS host (`UseAuthBlocksStartupAsync` handles this automatically).

The deploy script and nginx config edits are finalized in Phase 5.

---

## 8. Migration / rollout plan

The temptation is to do the split atomically. Don't. The split has too many moving parts (project renames, file moves, deploy-script edits, nginx config) and atomic-everything would mean an unreviewable single change.

### 8.1 Phase 0 — no-op (band-aid code survives; split fixes the runtime)

**Locked decision (Daniel):** Revert the diagnostic edits made during this session so the band-aid code survives in `dev` intact. The source will remain broken until the split lands, but the diagnostic commits are reverted so they don't pollute the history.

The current `MainLayout.razor` (`<div>@Body</div>` one-liner) is unshippable, but the split fixes this. Phase 0 is "do nothing and wait for the split" — no restoration work on `dev` itself. When the split lands (§8.2 Phase 2), `MainLayout` is restored as part of stripping AuthBlocks from the public host.

**Why:** The split work is the architectural fix. Putting chrome back on `dev` now would be a temporary fix that Phase 2 would immediately undo. The revert-for-safety approach means the split's intermediate commits are clean (no conflicting band-aid removals to reconcile).

### 8.2 Phased split (worktree-friendly)

**Phase 1: Stand up `DeepDrftManager` as a new project alongside the existing `DeepDrftWeb`.**

- New `DeepDrftManager` project. `Microsoft.NET.Sdk.Web`. References `DeepDrftCms` (existing RCL), `DeepDrftData`, `DeepDrftModels`, AuthBlocks packages.
- Copy `DeepDrftWeb/Program.cs` + `Startup.cs` into `DeepDrftManager/`, strip out everything that isn't CMS (the public-site MudBlazor host, the `DeepDrftWeb.Client` reference, the `TrackController`, the TypeScript pipeline). **Do not copy `CmsStealthRoutingHandler`** — it is obsolete in the subdomain topology (§7).
- Wire `app.MapRazorComponents<App>().AddInteractiveServerRenderMode().AddAdditionalAssemblies(typeof(DeepDrftCms._Imports).Assembly, typeof(AuthBlocksWeb._Imports).Assembly)`. **No** `AddInteractiveWebAssemblyRenderMode`. **No** client assembly.
- Spin it up on a third port locally. Verify `/cms/tracks` works against the existing Postgres metadata DB and the existing Auth DB.
- `DeepDrftWeb` is unchanged — both hosts can run concurrently against the same DB during this phase. The CMS lives in both hosts temporarily; that's fine, they share the underlying data.

**Exit criterion:** CMS host stands up, `/account/login` works against the seeded admin, `/cms/tracks` renders. Public site continues to run from `DeepDrftWeb` exactly as today (still entangled, still using band-aid MainLayout).

**Phase 2: Strip AuthBlocks out of `DeepDrftWeb`.**

- Remove the `AddAuthBlocks(...)`, `MapAuthBlocks()`, `UseAuthBlocksStartupAsync()` from `DeepDrftWeb/Program.cs`.
- Remove `AddCascadingAuthenticationState` (the implicit registration via `AuthBlocksWeb.Startup.ConfigureAuthServices`).
- Remove the `AuthBlocksWeb` assembly from `AddAdditionalAssemblies`.
- Remove the `DeepDrftCms` reference from `DeepDrftWeb` — the CMS now lives only in `DeepDrftManager`.
- Remove the CMS controllers (`CmsUploadController`, `CmsEditController`, `CmsDeleteController`) from `DeepDrftWeb/Controllers/` — they move to the CMS host.
- Remove `[AllowAnonymous]` attributes from `Home.razor` / `TracksView.razor` (no longer needed).
- Remove the `JwtAuthenticationStateProvider` registration from `DeepDrftWeb.Client.Startup` (and from `DeepDrftWeb.Client.Program.cs`'s `AuthBlocksWeb.Client.Startup.ConfigureServices(builder.Services)` call) — only the CMS needs it.
- Restore `MainLayout.razor` to its full chrome state from before the band-aids (the rendition that existed pre-band-aid era, adapted to current pages). With AuthBlocks gone, `[Inject]`s in the layout work normally.

**Exit criterion:** Public site (`DeepDrftWeb` still by its old name) renders home page with chrome, prerenders correctly, no hangs. CMS continues to work from `DeepDrftManager`. The hosts run side-by-side.

**Phase 3: Extract `DeepDrftShared.Client` (optional, can run in parallel with 2).**

- New RCL `DeepDrftShared.Client`.
- Move `TrackCard.razor`, `TracksGallery.razor`, `DDIcons.cs`, palette objects, font helpers.
- Both `DeepDrftWeb.Client` and (eventually) the CMS RCL reference it.

**Exit criterion:** Both apps render `TrackCard` from the shared RCL. Visual parity confirmed.

**Phase 4: Rename `DeepDrftWeb` → `DeepDrftPublic`, `DeepDrftWeb.Client` → `DeepDrftPublic.Client`.**

- Project file renames, namespace renames, csproj reference updates, solution file edits.
- Deploy script edits.
- `dch6` directory rename (`/deepdrft/web` → `/deepdrft/public`) + systemd unit rename (`deepdrft-web` → `deepdrft-public`).

**Exit criterion:** All references converge on the new names. The rename is a single staff-engineer pass; doing it last means the split has been validated under the old names first.

**Phase 5: nginx and deploy topology.**

- Edit `dch5-publish-deploy.sh` to publish three projects: `DeepDrftPublic`, `DeepDrftManager`, `DeepDrftContent`.
- Add the `DeepDrftManager` systemd unit (`deepdrft-manager`).
- nginx config: add subdomain server blocks for `manage.deepdrft.com` (or Daniel's chosen subdomain) and `content.deepdrft.com`, routing each to the appropriate upstream.
- Confirm with Daniel on the CMS subdomain name (proposed: `manage.deepdrft.com`).
- Verify on `dch6` first; promote to prod when stable.

### 8.3 Can the public site stand up first while CMS stays in the entangled host?

**Yes — that's exactly Phase 1's shape.** Phase 1 builds the CMS host as a parallel deployment. The public site doesn't move until Phase 2. During Phase 1, the CMS exists in *two* places simultaneously (the old entangled host and the new dedicated host) — that's acceptable because they share storage and the duplication is short-lived.

The inverse — public site stands up dedicated first, CMS stays in the entangled host — is *not* recommended. The deadlock is in the public path, and ripping out the public bits while leaving the CMS bound to the host that *has* the broken MainLayout creates a worse intermediate state than the current `dev`.

---

## 9. What to throw away

The band-aid work since the entanglement was discovered exists only to make the entangled host limp along. Once the split lands, these become removable. **None of these are wrong** — they're correct fixes for the wrong-shaped architecture. The work survives in `Cerebellum.AuthBlocks` upstream where relevant; only the DeepDrftHome-side adaptations are scoped here.

### 9.1 Removable from `DeepDrftWeb` / `DeepDrftPublic` once split lands

- `[AllowAnonymous]` attributes on `Home.razor` and `TracksView.razor`. The public host has no `[Authorize]` policy to escape.
- The `AuthBlocksWeb` assembly entry in `AddAdditionalAssemblies`. The public host does not need to know about `/account/login` — it links to it cross-subdomain.
- `AuthBlocksWeb.Startup.ConfigureAuthServices(builder.Services, baseUrl)` call.
- `AuthBlocksWeb.Client.Startup.ConfigureServices(builder.Services)` call in `DeepDrftWeb.Client.Program.cs`.
- The `Cerebellum.AuthBlocks*` package references in `DeepDrftWeb.csproj` and `DeepDrftWeb.Client.csproj`.
- The `DeepDrftCms` project reference in `DeepDrftWeb.csproj`.
- The CMS-specific configuration in `DeepDrftWeb/Startup.cs` (the `ContentCmsHttpClientName` client setup — moves to the CMS host).
- The `environment/authblocks.json` config file in `DeepDrftWeb/environment/` — moves to the CMS host's environment dir.
- The `ConnectionStrings:Auth` entry in `DeepDrftWeb/environment/connections.json` — moves.
- The `JwtAuthenticationStateProvider` and related auth-prerender-bridge wiring on the client.
- The `@rendermode InteractiveServer` declarations on AuthBlocks pages (those pages aren't in this host anymore).
- The `<NotAuthorized>` redirect-to-login branch in `Routes.razor` (no auth in this host).
- **`CmsStealthRoutingHandler` middleware** (from `DeepDrftWeb/Middleware/` or wherever it lives). Stealth routing is obsolete in a subdomain topology; access control is at the host boundary.

### 9.2 Removable from the entangled `MainLayout.razor`

Once restored from the one-liner band-aid:

- Any defensive try/catch around `[Inject]` properties.
- Any `OnAfterRenderAsync`-only guards around what should be server-renderable.
- The `JwtAuthenticationStateProvider`-prerender-safety detection logic (the provider isn't in scope here anymore).

### 9.3 What stays (and why)

- `DarkModeService` + `DarkModeSettings` + `DarkModeCookieService` + the `PersistentComponentState` bridge. The prerender-cookie pattern is good architecture and unrelated to the auth problem.
- `[ApiKeyAuthorize]` on `DeepDrftContent`'s `PUT api/track/{id}` and `POST api/track/upload`. Different auth surface, unaffected.
- The full streaming substrate (`StreamingAudioPlayerService`, `WavOffsetService`, the TS interop). Unrelated to render-mode wiring.
- The CMS itself, in full. Moves wholesale to the new host with no functional change.

### 9.4 What stays in AuthBlocks upstream

The prerender-safety rewrites to `JwtAuthenticationStateProvider` in `Cerebellum.AuthBlocks.Web.Client` (versions 10.3.31 and 10.3.32) are real improvements to the library. They stay. The DeepDrftHome-side concession (consuming the newer versions) is what becomes unnecessary in the public host — the CMS host keeps consuming them and benefits from the fixes.

---

## 10. Decisions — resolved by Daniel 2026-05-19

All open questions from the design draft have been resolved. Answers are folded throughout the document above. Summary for reference:

| Q | Answer | Where locked |
|---|--------|---|
| 1. Project names | `DeepDrftPublic` (public host), `DeepDrftManager` (CMS host), `DeepDrftShared.Client` (shared RCL). Rename `DeepDrftCms` RCL to `DeepDrftManager.Cms` is staff-engineer's call. | §2.1–2.2 |
| 2. Shared RCL in Wave 1 | **Yes, locked.** Extraction of `TrackCard`, `TracksGallery`, `DDIcons`, palette objects, font helpers to `DeepDrftShared.Client` is mandatory in Wave 1. (Daniel: "yes DRY and SOLID!") | §3.3 |
| 3. Per-app `App.razor` | Confirmed. Public and CMS hosts diverge at the `App.razor` level (dark-mode prerender bridge in public; auth cascade in CMS). | §4.1–4.2 |
| 4. Audio player in CMS | **Not in Wave 1.** Stack lives in `DeepDrftPublic.Client`. **Forward-looking constraint (locked):** eventual extraction to a shared `*.Audio` library is required; staff-engineer must design Wave 1 placement to make extraction mechanical. | §3.4 |
| 5. CMS metadata reads | **Both hosts reference services directly.** Public host calls `TrackService` for `/api/track/page` reads; CMS host calls `TrackService` in-process for list pages. No cross-host HTTP for metadata. Hosts do not talk to each other. | §5 |
| 6. Stealth routing | **Obsolete.** Access control is now at the host boundary (subdomain topology). `CmsStealthRoutingHandler` is removed. | §6, §9.1 |
| 7. Deploy topology | **Subdomain split, locked.** `deepdrft.com` → public, `manage.deepdrft.com` → CMS (final subdomain name is Daniel's call), `content.deepdrft.com` → content. Nginx routes by hostname, not path. | §7 |
| 8. CMS prerender | **Off.** CMS pages (`InteractiveServer`) do not prerender. The auth gate is enforced at the circuit level, not at a prerender hook. | §4.2 |
| 9. BlazorBlocks adoption | **Staff-engineer decision at implementation time.** Directive: adopt BlazorBlocks where the fit is good; no mandate to refactor existing CMS pages in Phase 1 just to adopt it. Evaluate depth during Phase 1 planning. | §8.2 Phase 1 |
| 10. Phase 0 chrome on `dev` | **No restoration.** Revert the diagnostic edits so `dev` stays broken but clean. The split (Phase 2) fixes the runtime. Restoring chrome now would be temporary and immediately undone. | §8.1 |

---

## 11. Working with this document

**Status as of 2026-05-19:** All decisions locked (§10). This document is now the authoritative design for the two-app split. Staff-engineer executes per §8's phased plan.

- This doc captures the **shape**, not the implementation. Staff-engineer follows §8's phased rollout plan and executes.
- **The design is locked.** §10 contains the resolved decisions (named by Daniel 2026-05-19). Do not re-open these questions unless Daniel changes direction.
- When phases land, archive their entries here to `COMPLETED.md` with the original "What / Why / Shape" body preserved (per `CONTEXT.md §6`).
- The supersession against `CMS-PLAN.md §2` (which placed the CMS inside `DeepDrftWeb`) takes effect now. Doc-keeper updates `CMS-PLAN.md` to point here for the host shape; the CMS-PLAN's auth/wave content remains authoritative for the CMS feature roadmap.
- Cross-reference rather than duplicate. If `PLAN.md` adds an item that touches the split (e.g. metadata-API extension), the new item points here for the host context.
