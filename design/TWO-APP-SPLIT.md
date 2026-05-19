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

- **Public site** — customer-facing, anonymous, prerender-first, MudBlazor + (optionally) BlazorBlocks. No AuthBlocks. No cascading auth. No `@rendermode` chaos.
- **CMS** — staff-facing, fully authenticated via AuthBlocks, built on BlazorBlocks scaffolding. `InteractiveServer` end-to-end. Stealth-routed (`404` to unauthorized callers).

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

Working names below. Final naming is at §10 Q1.

```
DeepDrftSite                  NEW. Public-site host (ASP.NET Core, Blazor Web App).
                              Anonymous. Prerender + InteractiveAuto. MudBlazor.
                              No AuthBlocks. Owns api/track/page (see §5) and the
                              TypeScript audio interop.

DeepDrftSite.Client           NEW. Public-site WASM assembly. Promoted from today's
                              DeepDrftWeb.Client. Contains Home, TracksView, the audio
                              player stack, dark-mode plumbing, HTTP clients.

DeepDrftCmsHost               NEW. CMS host (ASP.NET Core, Blazor Server-only).
                              AuthBlocks + BlazorBlocks. InteractiveServer end-to-end.
                              References the existing DeepDrftCms RCL.

DeepDrftCms                   EXISTING (RCL). CMS pages, layouts, components. Becomes
                              consumed by DeepDrftCmsHost only. Stops being referenced
                              from the public host.

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

### 2.2 Naming — flagged as Daniel's call

- `DeepDrftSite` vs. `DeepDrftPublic` vs. `DeepDrftWeb` (renaming the existing host instead of retiring it). Recommendation: **rename `DeepDrftWeb` → `DeepDrftSite`** rather than retiring it and standing up a new project. The current `DeepDrftWeb` already has the TypeScript pipeline, `wwwroot`, the `App.razor` host, the `MainLayout` history, and the deploy script entries. Renaming preserves that history; standing up a fresh project loses it. **Counter-argument:** the rename is a heavy git-log churn moment and doesn't compose with the band-aid commits already on `dev` — see §8.
- `DeepDrftCmsHost` vs. `DeepDrftCms.Host` vs. just promoting `DeepDrftCms` to be the host directly (changing its Sdk from `Microsoft.NET.Sdk.Razor` to `Microsoft.NET.Sdk.Web`). Recommendation: **new project `DeepDrftCmsHost`**, keep `DeepDrftCms` as an RCL. Reason: an RCL+host pair preserves the option to mount the CMS into a different host later (e.g. embedding CMS into an admin-portal aggregator host); collapsing them forecloses that.
- `DeepDrftShared.Client` vs. `DeepDrftUi.Shared` vs. `DeepDrftWeb.Shared`. Recommendation: **`DeepDrftShared.Client`** — clearly an RCL, clearly client-side. Other names invite ambiguity about whether it can hold server-only code.

All three are Q1 in §10.

---

## 3. Shared layer policy

The split is only useful if shared concerns stay shared. The risk is duplication or, worse, two diverging copies of the audio player.

### 3.1 What stays in `DeepDrftModels` (unchanged)

`TrackEntity`, `TrackDto`, `PagedResult<T>`, `PagingParameters<T>`, `ApiResultDto<T>`. The split does not touch the contracts layer.

### 3.2 What stays in `DeepDrftData` / `DeepDrftContent.Data` (unchanged)

The EF Core domain layer and the FileDatabase implementation are class libraries today; they don't care which host references them. The CMS host references both for writes; whichever host owns the metadata API (§5) references `DeepDrftData` for reads.

### 3.3 What moves to `DeepDrftShared.Client` (new RCL)

Things both apps render and want to keep visually consistent:

- **`TrackCard.razor`** — both the public gallery and the CMS list render tracks. Today's CMS list (CMS Wave 1) renders a table; a future CMS view could embed `TrackCard` previews, and the public-side card must not drift visually. Move it here.
- **`TracksGallery.razor`** — used by the public gallery today. The CMS does not need the gallery layout, but if any CMS preview surface ever wants it (e.g. "preview as listener sees it"), pre-shared is cheaper than retroactive extraction.
- **`DDIcons.cs`** — hand-rolled icons, currently in `DeepDrftWeb.Client/Common/`. Both apps will want the lit/unlit gas-lamp, the brand logo, etc.
- **Palette tokens and CSS variables.** Today `deepdrft-styles.css` lives in `DeepDrftWeb/wwwroot/styles/`. Move the **palette layer** (the `:root` and `.deepdrft-theme-dark` custom properties) into a shared CSS file in `DeepDrftShared.Client/wwwroot/` so MudBlazor themes in both apps map to the same tokens. Page-specific CSS (home page, gallery scoped styles) stays in the public app.
- **MudBlazor palette objects.** Currently inline in `MainLayout.razor`. Promote to a `DeepDrftPalettes.cs` (static `MudTheme Light` / `MudTheme Dark`) in the shared RCL. The CMS host applies the same palettes so the staff side reads as the same product.
- **Font-loading `<link>` helpers.** A `DeepDrftFonts` static or `<DeepDrftFontLinks />` component so both apps emit the same Google Fonts request.

### 3.4 Audio player stack — open question

The player stack today lives entirely in `DeepDrftWeb.Client/`:

- `IPlayerService`, `IStreamingPlayerService`, `AudioPlayerService`, `StreamingAudioPlayerService`, `AudioInteropService`, `StreamingErrorHandler`.
- `AudioPlayerProvider.razor`, `AudioPlayerBar.razor`, `PlayerControls.razor`, `TimestampLabel.razor`, `VolumeControls.razor`, `SpectrumVisualizer.razor`.
- `TrackMediaClient` (HTTP client for `DeepDrftContent`).

The public site needs all of it. Whether the **CMS** needs it is the open question (Q4 in §10):

- **Option A — Public site only.** Audio player stack moves into `DeepDrftSite.Client`. The CMS never previews audio in-browser. CMS users wanting to verify an upload navigate to the public site in another tab. **Simplest.**
- **Option B — Shared via `DeepDrftShared.Client`.** The audio player stack moves into the shared RCL and is consumed by both apps. The CMS detail page (`/cms/tracks/{id}`) gains an in-place preview player. **More work; modestly more useful.**

Recommendation: **Option A in Wave 1, leave Option B as a future move.** The audio player carries non-trivial complexity (streaming, seek, spectrum, dark-mode-aware visualiser, `DotNetObjectReference` lifetimes) and consolidating it into a shared RCL for a CMS use case that isn't on the immediate roadmap is unnecessary work today. If a future "preview in CMS" need arises, the player moves to shared then — its surface is already abstracted behind `IPlayerService`.

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

## 5. API ownership — where does `api/track/page` live?

`DeepDrftWeb` today owns one controller — `TrackController` — exposing `GET api/track/page` against `DeepDrftContext` (Postgres metadata). It also owns three CMS-internal controllers (`CmsUploadController`, `CmsEditController`, `CmsDeleteController`).

After the split, the CMS controllers follow the CMS — they require `[Authorize(Roles="Admin")]` and live in `DeepDrftCmsHost`. The question is the public read endpoint.

### 5.1 Option A — Metadata API stays with the public site (recommended)

`GET api/track/page` lives in `DeepDrftSite` (the renamed/repurposed `DeepDrftWeb`). The public WASM client calls its own host. The CMS host, when it needs to read paged tracks, calls **across to the public site** via HTTP.

**Pros:**
- Public site is fully self-contained: it hosts its WASM bundle **and** the API that bundle calls. No cross-host dependency for the listener experience.
- Anonymous endpoint stays anonymous; no auth middleware to step around.
- Same-origin: WASM → `/api/track/page` works with no CORS configuration.
- Mirrors today's shape — minimum surgery.

**Cons:**
- CMS reads cross a host boundary (extra hop for `/cms/tracks` list). In practice negligible — paged metadata is small, and the CMS list is a low-traffic surface.
- Public site now serves both the WASM bundle and an API. Two responsibilities in one host. Defensible (the API exists to feed the bundle) but not single-purpose.

### 5.2 Option B — Dedicated metadata-API host (`DeepDrftApi` or `DeepDrftMetadata`)

A third ASP.NET Core project owns `GET api/track/page`. Both the public site (WASM → cross-host) and the CMS host (server-side → cross-host) call into it.

**Pros:**
- Each host is single-purpose: public site is presentation, CMS is admin, metadata API is data. Clean separation.
- Future-proofs against "what if a mobile app wants metadata too" — the API exists independently of any UI.

**Cons:**
- Three hosts instead of two. More deploy units, more nginx routes, more systemd services, more cert wiring.
- The public WASM bundle now does **cross-origin** calls to the metadata API. CORS needs configuration. Either that or nginx-rewrite the public site's `/api/track/page` to the metadata host (which trades the CORS problem for a path-routing problem).
- Justified only if the API is reused outside the public site. Today it isn't.

### 5.3 Option C — Metadata API lives in the CMS host

`GET api/track/page` is hosted on `DeepDrftCmsHost`. Public site calls cross-host.

**Pros:**
- The CMS host already references `DeepDrftData` for writes; adding the read endpoint there is zero new wiring.
- Public site becomes presentation-only (no `DeepDrftData` reference).

**Cons:**
- The CMS host is auth-gated and `InteractiveServer`. Adding an anonymous controller alongside is fine technically (controllers and Blazor pages share the host but not the auth pipeline) but conceptually muddles the host's role.
- The CMS host becomes a critical-path dependency for **anonymous** public traffic. If the CMS host goes down, the public site's track gallery breaks.
- Same cross-origin / CORS issue as Option B from the WASM client's perspective.

### 5.4 Recommendation

**Option A.** Public site owns the metadata read endpoint. It's the lightest surgery, preserves the same-origin contract, and matches the principle that the host that serves the UI also serves the UI's data API. Revisit only if a non-UI client (mobile, third-party) starts consuming metadata.

The CMS host's `/cms/tracks` list pages call `GET api/track/page` cross-host (a named `IHttpClientFactory` client pointed at the public site, like the existing `DeepDrft.Content.Cms` client points at `DeepDrftContent`). No auth header needed — the endpoint is anonymous.

**Implication for the CMS host's `DeepDrftData` reference:** it still needs `DeepDrftData` for the write path (Edit/Delete) but could read paged tracks via HTTP. Two choices:
- **Read via HTTP** for consistency with how the CMS reaches `DeepDrftContent`. Cleaner conceptually.
- **Read via direct service call** (CMS host has `DeepDrftData` referenced anyway). Faster path, no HTTP roundtrip for the CMS list page.

The "one source of truth, multiple views" rule (`user_one_source_multiple_views`) is preserved either way — the VM contract is `PagedResult<TrackEntity>` either way; only the transport differs. **Recommend: direct service call for the CMS** since the CMS host already has the dependency, and reserve the HTTP path for the public WASM. Flag as Q5 in §10 if Daniel wants the stricter host-boundary discipline.

---

## 6. Auth wiring (CMS only)

All present in `CMS-PLAN.md` and landed in CMS Wave 1; preserved here for completeness in the new host. **Nothing about the auth model changes in the split.**

- **AuthBlocks substrate** (`Cerebellum.AuthBlocks*` 10.3.32) — `AddAuthBlocks(...)`, `await app.Services.UseAuthBlocksStartupAsync()`, `app.MapAuthBlocks()` all in `DeepDrftCmsHost/Program.cs`.
- **AuthBlocksWeb pages** (`/account/login`, `/account/logout`) — exposed by adding `typeof(AuthBlocksWeb._Imports).Assembly` to the CMS host's `AddAdditionalAssemblies`.
- **JWT in localStorage** via `JwtAuthenticationStateProvider`. CMS host calls `AuthBlocksWeb.Startup.ConfigureAuthServices` server-side; no WASM client means no `AddAuthenticationStateDeserialization` bridge needed.
- **Auth DB separate from main DB.** `ConnectionStrings:Auth` and `ConnectionStrings:DefaultConnection` are different Postgres databases (or the same instance with different DBs — they share an engine, not a context). The CMS host wires both.
- **`Admin` role gate** via `[HierarchicalRoleAuthorize("Admin")]` on every CMS page and `[Authorize(Roles = "Admin")]` on every CMS API endpoint.
- **Stealth routing.** Unauthorized hits on `/cms/*` return 404, not 401, not redirect. `CmsStealthRoutingHandler` (today in `DeepDrftWeb/Middleware/`) follows into the CMS host. Path scope is now naturally `/cms/*` because the CMS host serves nothing else routable from outside auth.

**One thing to verify** (Q6 in §10): the stealth-routing 404 behaviour currently applies only to `/cms/*` because that was the only protected surface in a mixed host. In the dedicated CMS host, more of the surface is protected by default — but `/account/login` is anonymous, the auth API endpoints under `/api/auth/*` are anonymous, and the host's static assets are anonymous. The 404 handler must still target only the `[HierarchicalRoleAuthorize]`-failed cases, not all 401s, or `/account/login` discovery breaks (a legitimate anonymous user with no JWT would get 404 on the login page itself, which is wrong).

**Admin seeding** (`AdminUserSettings` in `authblocks.json`) carries over unchanged. The seed user is created on first boot of the CMS host.

---

## 7. Deployment topology

The repo currently deploys two systemd services (`deepdrft-content`, `deepdrft-web`) on each host (dch6 beta, prod.cerebellumsoftworks.com prod) via `dch5-publish-deploy.sh`. The split adds a third unit.

### 7.1 Recommendation — path-based routing on a single domain

```
                            nginx (Let's Encrypt cert for deepdrft.com)
                              │
            ┌─────────────────┼─────────────────┬───────────────────┐
            │                 │                 │                   │
        location /        location /cms     location /api/      location /api/
                                            track/{id}          auth, /api/users,
                                                                etc.  (proxied
                                                                from CMS host)
            │                 │                 │                   │
            ▼                 ▼                 ▼                   ▼
       DeepDrftSite      DeepDrftCmsHost   DeepDrftContent     DeepDrftCmsHost
       systemd unit      systemd unit      systemd unit        (same as /cms)
       :5001             :5002             :5003               :5002
```

Three systemd units per host (`deepdrft-site`, `deepdrft-cms`, `deepdrft-content`). nginx terminates TLS once, routes by path:

- `/` and `/_framework/*` and `/api/track/page` → `DeepDrftSite`.
- `/cms/*` and `/account/*` (login/logout) and `/api/auth/*`, `/api/users/*`, `/api/roles/*` → `DeepDrftCmsHost`.
- `/api/track/{id}` and `/api/track/upload` → `DeepDrftContent`.

**Pros:**
- One domain, one cert. No CORS between public WASM and the metadata API (same origin).
- Browser sees one site; the architecture is invisible.
- The "CMS is on the same domain at `/cms`" matches the stealth-routing intent (a snooper hitting `https://deepdrft.com/cms/tracks` gets 404, identical to any unmatched path).
- Path routing is what nginx is good at; no new tooling.

**Cons:**
- Two locations on the CMS host (`/cms/*` and `/account/*`). nginx config has to know about both.
- A misconfigured `/account/login` link from the public site (linking to `/account/login` rather than `/cms/account/login`) needs nginx to route `/account/*` to the CMS host explicitly — easy to forget when the CMS host is mostly known by its `/cms` prefix.

### 7.2 Alternative — subdomain split

```
deepdrft.com           → DeepDrftSite (public)
cms.deepdrft.com       → DeepDrftCmsHost (staff)
content.deepdrft.com   → DeepDrftContent (binary API)
api.deepdrft.com       → DeepDrftSite's track/page endpoint (optional)
```

**Pros:**
- Each host has one nginx server block. Cleaner config.
- CMS can be IP-restricted at the nginx layer (`allow <home-IP>; deny all;` on `cms.deepdrft.com`) without affecting the public site. Layered defence on top of the auth gate.
- "cms.deepdrft.com" is unambiguous in browser history and link shares.

**Cons:**
- Three certs (or one wildcard, which costs more / requires DNS-challenge ACME).
- Public WASM bundle calls **cross-origin** to anything on `content.deepdrft.com` (already cross-origin today, so the CORS config exists). If the metadata API is hosted on `api.deepdrft.com`, that's a new cross-origin path.
- Stealth routing changes flavour — the CMS hostname *does* announce its existence by DNS. Mitigated by binding `cms.deepdrft.com` to a non-routable IP behind a VPN, but that's a bigger lift than the current setup.

### 7.3 Recommendation summary

**Recommend path-based routing (§7.1).** The current deploy already nginx-proxies; adding a third location is small. Subdomain split is the right call **if** Daniel wants IP-restrictable CMS access as a hard requirement; until then, the stealth-routing 404 is enough.

Q7 in §10.

### 7.4 Deploy-script changes

`dch5-publish-deploy.sh` publishes two projects today. The split makes it three:

- Publish `DeepDrftSite` (was `DeepDrftWeb`).
- Publish `DeepDrftCmsHost` (new).
- Publish `DeepDrftContent` (unchanged).
- Three `*.tar.gz` artefacts, three `scp`/`tar`/`restart` cycles.
- Three EF migration paths to consider — `DeepDrftContext` migrations apply against the metadata DB (driven by whichever host owns the read endpoint and the writes), `AuthDbContext` migrations apply against the auth DB on first run of the CMS host (`UseAuthBlocksStartupAsync` handles this automatically).

The deploy script edit is a staff-engineer task once §10 Q1 (naming) and Q7 (topology) settle.

---

## 8. Migration / rollout plan

The temptation is to do the split atomically. Don't. The split has too many moving parts (project renames, file moves, deploy-script edits, nginx config) and atomic-everything would mean an unreviewable single change.

### 8.1 Smallest first slice — "public home page renders correctly"

**Phase 0: stabilise current `dev`** (small, lands first, can be staff-engineer's first commit).

Before touching the split, restore the public site to a non-band-aid state on `dev`. The current `MainLayout.razor` (`<div>@Body</div>` one-liner) is unshippable — it has no nav, no theme switcher, no footer. Either:

- **(a)** Roll forward: a tiny restoration commit that puts the chrome back in `MainLayout` with the *minimum* `[Inject]`s needed (likely none — see §4.1), accepting that it still runs in the entangled host. The home page now renders with chrome but the architectural problem isn't fixed. This gives Daniel a presentable site **while** the split work proceeds in a worktree.
- **(b)** Skip Phase 0 and go straight to the split — accept that `dev` is broken-with-chrome-gone until the split lands.

**Recommend (a).** A demo-grade home page during the split work is valuable; the chrome restoration is a known-shape change.

### 8.2 Phased split (worktree-friendly)

**Phase 1: Stand up `DeepDrftCmsHost` as a new project alongside the existing `DeepDrftWeb`.**

- New `DeepDrftCmsHost` project. `Microsoft.NET.Sdk.Web`. References `DeepDrftCms` (existing RCL), `DeepDrftData`, `DeepDrftModels`, AuthBlocks packages.
- Copy `DeepDrftWeb/Program.cs` + `Startup.cs` + `Middleware/CmsStealthRoutingHandler.cs` into `DeepDrftCmsHost/`, strip out everything that isn't CMS (the public-site MudBlazor host, the `DeepDrftWeb.Client` reference, the `TrackController`, the TypeScript pipeline).
- Wire `app.MapRazorComponents<App>().AddInteractiveServerRenderMode().AddAdditionalAssemblies(typeof(DeepDrftCms._Imports).Assembly, typeof(AuthBlocksWeb._Imports).Assembly)`. **No** `AddInteractiveWebAssemblyRenderMode`. **No** client assembly.
- Spin it up on a third port locally. Verify `/cms/tracks` works against the existing Postgres metadata DB and the existing Auth DB.
- `DeepDrftWeb` is unchanged — both hosts can run concurrently against the same DB during this phase. The CMS lives in both hosts temporarily; that's fine, they share the underlying data.

**Exit criterion:** CMS host stands up, `/account/login` works against the seeded admin, `/cms/tracks` renders. Public site continues to run from `DeepDrftWeb` exactly as today (still entangled, still using band-aid MainLayout — see Phase 0).

**Phase 2: Strip AuthBlocks out of `DeepDrftWeb`.**

- Remove the `AddAuthBlocks(...)`, `MapAuthBlocks()`, `UseAuthBlocksStartupAsync()` from `DeepDrftWeb/Program.cs`.
- Remove `AddCascadingAuthenticationState` (the implicit registration via `AuthBlocksWeb.Startup.ConfigureAuthServices`).
- Remove the `AuthBlocksWeb` assembly from `AddAdditionalAssemblies`.
- Remove the `DeepDrftCms` reference from `DeepDrftWeb` — the CMS now lives only in `DeepDrftCmsHost`.
- Remove the CMS controllers (`CmsUploadController`, `CmsEditController`, `CmsDeleteController`) from `DeepDrftWeb/Controllers/` — they move to the CMS host.
- Remove `[AllowAnonymous]` attributes from `Home.razor` / `TracksView.razor` (no longer needed).
- Remove the `JwtAuthenticationStateProvider` registration from `DeepDrftWeb.Client.Startup` (and from `DeepDrftWeb.Client.Program.cs`'s `AuthBlocksWeb.Client.Startup.ConfigureServices(builder.Services)` call) — only the CMS needs it.
- Restore `MainLayout.razor` to its full chrome state from before the band-aids (the rendition that existed pre-`d31a08b`-era, adapted to current pages). With AuthBlocks gone, `[Inject]`s in the layout work normally.

**Exit criterion:** Public site (`DeepDrftWeb` still by its old name) renders home page with chrome, prerenders correctly, no hangs. CMS continues to work from `DeepDrftCmsHost`. The hosts run side-by-side.

**Phase 3: Extract `DeepDrftShared.Client` (optional, can run in parallel with 2).**

- New RCL `DeepDrftShared.Client`.
- Move `TrackCard.razor`, `TracksGallery.razor`, `DDIcons.cs`, palette objects, font helpers.
- Both `DeepDrftWeb.Client` and (eventually) the CMS RCL reference it.

**Exit criterion:** Both apps render `TrackCard` from the shared RCL. Visual parity confirmed.

**Phase 4: Rename `DeepDrftWeb` → `DeepDrftSite`, `DeepDrftWeb.Client` → `DeepDrftSite.Client`.**

- Project file renames, namespace renames, csproj reference updates, solution file edits.
- Deploy script edits.
- `dch6` directory rename (`/deepdrft/web` → `/deepdrft/site`) + systemd unit rename.

**Exit criterion:** All references converge on the new names. The rename is a single staff-engineer pass; doing it last means the split has been validated under the old names first.

**Phase 5: nginx and deploy topology.**

- Edit `dch5-publish-deploy.sh` to publish three projects.
- Add the `DeepDrftCmsHost` systemd unit.
- nginx config: add the `/cms/*` and `/account/*` location blocks.
- Verify on `dch6` first; promote to prod when stable.

### 8.3 Can the public site stand up first while CMS stays in the entangled host?

**Yes — that's exactly Phase 1's shape.** Phase 1 builds the CMS host as a parallel deployment. The public site doesn't move until Phase 2. During Phase 1, the CMS exists in *two* places simultaneously (the old entangled host and the new dedicated host) — that's acceptable because they share storage and the duplication is short-lived.

The inverse — public site stands up dedicated first, CMS stays in the entangled host — is *not* recommended. The deadlock is in the public path, and ripping out the public bits while leaving the CMS bound to the host that *has* the broken MainLayout creates a worse intermediate state than the current `dev`.

---

## 9. What to throw away

The band-aid work since the entanglement was discovered exists only to make the entangled host limp along. Once the split lands, these become removable. **None of these are wrong** — they're correct fixes for the wrong-shaped architecture. The work survives in `Cerebellum.AuthBlocks` upstream where relevant; only the DeepDrftHome-side adaptations are scoped here.

### 9.1 Removable from `DeepDrftWeb` / `DeepDrftSite` once split lands

- `[AllowAnonymous]` attributes on `Home.razor` and `TracksView.razor`. The public host has no `[Authorize]` policy to escape.
- The `AuthBlocksWeb` assembly entry in `AddAdditionalAssemblies`. The public host does not need to know about `/account/login` — it links to it cross-host (or cross-path via nginx).
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

## 10. Open questions for Daniel

These genuinely need decisions before staff-engineer can execute. Numbered for citation.

1. **Project names.** `DeepDrftSite` vs. `DeepDrftPublic` vs. rename-in-place `DeepDrftWeb`? `DeepDrftCmsHost` vs. `DeepDrftCms.Host` vs. promoting `DeepDrftCms` to a host (collapsing the RCL+host pair)? `DeepDrftShared.Client` for the new shared RCL — acceptable?
2. **Shared RCL — yes/no in Wave 1.** Recommendation: yes, with the minimum surface (palette objects, fonts, `DDIcons`, `TrackCard`). But if Daniel prefers to land the split first and extract shared bits as a follow-up, that's a defensible sequencing.
3. **Single `App.razor` host page or per-app divergence.** Recommendation: per-app. The public site's `App.razor` keeps the dark-mode preconnect, font links, the `DeepDrftAudio` module import. The CMS's `App.razor` is the AuthBlocks-aware version (cascading auth state, MudBlazor + BlazorBlocks chrome). They diverge.
4. **Audio player in CMS — yes/no.** §3.4 Option A (player only in public) vs. Option B (player in shared RCL, CMS gets preview). Recommendation: A for Wave 1.
5. **CMS reads — direct service call vs. HTTP across to public site.** Recommendation: direct service call (CMS host has `DeepDrftData` referenced for writes; reusing the dependency for reads is free). If Daniel wants the stricter host-boundary discipline (no DB access from anything but the host that owns the API), flip to HTTP. The VM contract is the same either way.
6. **Stealth routing scope.** Confirm: only `[HierarchicalRoleAuthorize]`-failed cases return 404. `/account/login` (anonymous-allowed) still serves 200 to unauthenticated users. The middleware predicate must distinguish.
7. **Deployment topology.** Path-based on `deepdrft.com` (recommended) vs. subdomain split (`cms.deepdrft.com`). Subdomain split is required if Daniel wants IP-restricted CMS access; otherwise path-based is lighter.
8. **CMS first-paint experience.** Today the CMS is `InteractiveServer` and that's fine for a logged-in workflow. Confirm prerender is off for `/cms/*` (recommended) — keeps the auth gate honest at the page level rather than relying on the stealth handler to clean up after a prerendered anonymous response.
9. **BlazorBlocks adoption depth.** Daniel said "CMS should be based on BlazorBlocks and AuthBlocks." `Cerebellum.BlazorBlocks.Web` provides "entity management views, modals, and form scaffolding" — does the CMS Wave 1 surface (track list/edit/delete) get rebuilt on BlazorBlocks scaffolding, or does the existing `DeepDrftCms` RCL keep its bespoke pages and BlazorBlocks comes in for Wave 2 (user admin, audit log, etc.)? The split is independent of this, but staff-engineer needs to know whether to refactor or preserve in Phase 1.
10. **Phase 0 chrome restoration on `dev`.** Yes (restore MainLayout to presentable state on `dev` now, before split work begins) or no (live with the one-liner until the split lands)? Recommendation: yes. The home page should be presentable while the structural work happens in a worktree.

---

## 11. Working with this document

- This doc captures the **shape**, not the implementation. Staff-engineer takes the answers to §10, follows §8's phased plan, executes.
- When phases land, archive their entries here to `COMPLETED.md` with the original "What / Why / Shape" body preserved (per `CONTEXT.md §6`).
- The supersession against `CMS-PLAN.md §2` (which placed the CMS inside `DeepDrftWeb`) takes effect once Daniel signs off — at that point doc-keeper updates `CMS-PLAN.md` to point here for the host shape, and the CMS-PLAN's auth/wave content remains authoritative for the CMS feature roadmap.
- Cross-reference rather than duplicate. If `PLAN.md` adds an item that touches the split (e.g. metadata-API extension), the new item points here for the host context.
