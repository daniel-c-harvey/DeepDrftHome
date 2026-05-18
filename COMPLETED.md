# COMPLETED.md — DeepDrftHome

Archive of items that have moved out of `PLAN.md`. Per `CONTEXT.md §6`, completed `PLAN.md` items are moved here rather than deleted. Each entry preserves the original "What / Why / Shape" body so this file reads as a decision record, not just an outcome list.

Newest entries at the top. Group by phase header (mirroring `PLAN.md` themes) when there are enough entries to warrant it.

---

## Phase 0 — Wireframe-driven home page redesign

**Status:** All sub-items landed on 2026-05-17.

A design wireframe (`deepdrft-wireframe.html` at the project root) is the source of truth for a full visual reskin of the public site. The current `Home.razor` is a MudPaper/MudGrid composition with a generic "purple-tint" feature card aesthetic that doesn't match the collective's intended voice. The wireframe replaces it with a layout-first, editorial design: 50/50 hero, frosted-glass nav, dark feature band, green origin/connect split, navy CTA banner with ghost-watermark, and an italic-serif accent treatment throughout.

Scope here is **the home page and the chrome that wraps it** (nav, layout container, theme palette, font loading). The track gallery (`TracksView.razor`), the audio player dock (`AudioPlayerBar.razor`), and the FileDatabase/streaming substrate are **out of scope** for Phase 0 — they keep working through the existing MudBlazor theme, which is being recoloured under them. The "Now Playing" card in the hero is a *new* surface that reads from the existing `IPlayerService` cascade; it is a view onto the player, not a replacement for the dock.

Phase 0 sub-items decompose into worktree-sized tracks. 0.1 is the foundation everything else inherits — land it first. 0.2–0.4 can proceed in parallel against that foundation. 0.5 is a follow-on tuning pass once the light theme is in.

### 0.1 Light palette + font system

- **What:** Replace the "Charleston in the Day" `PaletteLight` in `DeepDrftWeb.Client/Layout/MainLayout.razor` with the wireframe palette (`--white #FAFAF8`, `--navy #0D1B2A`, `--green #1A3C34`, `--green-accent #3D7A68`, `--muted #8A9BB0`), expressed as MudBlazor `PaletteLight` properties. Update the corresponding CSS custom properties in `DeepDrftWeb/wwwroot/styles/deepdrft-styles.css` so the `deepdrft-*` utility classes still resolve. Add `Geist Mono` to the Google Fonts `<link>` in `DeepDrftWeb/Components/App.razor`. Upgrade the existing `Cormorant` link to `Cormorant Garamond` with the italic + 300/400/600 weight set used by the wireframe. Remove the `Bodoni Moda` link (and its `--font-hero` reference) if no remaining surface uses it.
- **Why it matters:** Every other Phase 0 sub-item consumes these tokens. Fonts and palette landing first means 0.2/0.3/0.4 can render at intended fidelity from the moment they're built, not approximate-then-correct. The font swap is also the only Phase 0 change that affects HTML served by the host project (`App.razor`), so isolating it cleanly keeps the render-mode seam clear.
- **Shape:**
  - MudBlazor palette mapping (light): `Primary = navy`, `Secondary = green`, `Tertiary = green-accent`, `Background = white`, `Surface = white`, `AppbarBackground = "rgba(250,250,248,0.88)"`, `AppbarText = navy`, `TextPrimary = navy`, `TextSecondary = muted`, `Divider = "rgba(13,27,42,0.10)"`, `LinesDefault / TableLines` to match. Semantic colours (`Info/Success/Warning/Error`) stay at MudBlazor defaults.
  - Typography block (light): `H1`–`H6` and a new wireframe-specific display class use `Cormorant Garamond`; `Button` / `Default` keep `DM Sans`; introduce a `Subtitle1` / `Caption` family pointing at `Geist Mono` for label/eyebrow text.
  - CSS variables: rename or alias the existing `--deepdrft-primary/--deepdrft-secondary/etc.` to the wireframe palette in `:root`. Add `--font-mono: "Geist Mono", monospace;` and update `--font-hero` / `--font-headers` to `"Cormorant Garamond", serif`. Where the legacy palette has no wireframe equivalent (e.g. `--deepdrft-quaternary` warm gold), prefer mapping it to the closest wireframe colour rather than inventing a new one — the goal is convergence on the new vocabulary, not coexistence.
  - Font loading: a single Google Fonts link, ideally one combined request with `family=Cormorant+Garamond:ital,wght@…&family=Geist+Mono:wght@…&family=DM+Sans:…`. One round-trip, three families.
- **Prerequisite:** None — this is the foundation.
- **Constraint:** The dark palette ("Lowcountry Summer Nights") must stay functional after this change even if visually mismatched — 0.5 is the dedicated pass for re-harmonising it. Do not edit the dark palette in 0.1. The dark-mode cookie + `PersistentComponentState` round-trip described in `CLAUDE.md` must be preserved unchanged.

### 0.2 Frosted-glass top nav

- **What:** Replace the current MudBlazor `MudAppBar`-based `DeepDrftMenu.razor` chrome (logo + nav stack + dark-mode toggle, default Material elevation) with the wireframe's fixed frosted-glass nav: 88% opacity off-white background, `backdrop-filter: blur(18px)`, 1px navy-alpha bottom border, no elevation shadow, navy-on-white "Stream Now" CTA pinned right, nav links in Geist Mono uppercase with the muted-to-navy hover transition.
- **Why it matters:** The nav sits across every page, so its visual language sets expectations for the rest of the site. The Material elevation + dropdown menu pattern is the strongest "this is a stock MudBlazor app" tell currently; replacing it is the single largest perceived-quality move of Phase 0.
- **Shape:**
  - Keep `DeepDrftMenu.razor` as the file (the existing render-mode wiring and viewport-subscription mobile branch are reused) — rewrite the markup inside it.
  - Wrap a styled `<nav>` element (or `MudAppBar` with heavy CSS override) and bind nav links to `Pages.AllPages`. The link text should render via Geist Mono with the wireframe's letter-spacing and uppercase transform.
  - The "Stream Now" CTA is a new affordance — wire it to `/tracks` for now (it is functionally a "browse the gallery" action since live streaming isn't a Phase 0 surface).
  - Dark-mode toggle stays — the gas-lamp icon button moves to the right of the CTA. Confirm visual treatment works against both the frosted-white nav (light) and whatever the dark-mode nav becomes after 0.5.
  - Mobile branch: the `MudMenu` dropdown pattern persists, but the activator + items should adopt Geist Mono and the new colour vocabulary. No drawer.
- **Prerequisite:** 0.1 (palette + Geist Mono load).
- **Constraint:** The nav is rendered through `MainLayout.razor` and therefore participates in server prerender. `backdrop-filter` is CSS-only and renders identically in both passes, so this is safe — but any JS-driven scroll/show behaviour added later must be gated on `OnAfterRenderAsync`. `IBrowserViewportService` is already used here for breakpoints and must continue to work after the rewrite. Do not regress the dark-mode toggle wiring (`DarkModeCookieService.ToggleDarkModeAsync` → cookie → `IsDarkModeChanged` event up).

### 0.3 Split hero with live Now-Playing card

- **What:** Replace the current centered MudPaper hero in `DeepDrftWeb.Client/Pages/Home.razor` with the wireframe's 50/50 split:
  - **Left:** eyebrow ("Charleston, South Carolina"), display title ("Deep / *Drft*" with italic green emphasis on "Drft"), italic-serif subtitle, body description, and the two CTAs (`Start Streaming` filled / `Browse Tracks` ghost). All entering via the existing `fade-up` CSS animation pattern with staggered delays.
  - **Right:** dark navy panel with three concentric pulsing rings (CSS keyframe `pulse-ring`), a frosted "Now Playing" card (label + blinking dot + track title + sub + animated waveform bars), and the stat row (47+ / 2 / ∞).
- **Why it matters:** This is the page. Hero is what a first-time visitor sees, and it is the only sub-item that wires the new design back into the live audio system — making the design feel inhabited rather than decorative.
- **Shape:**
  - **Now-Playing data source:** `Home.razor` consumes `[CascadingParameter] IPlayerService Player` (cascaded by `AudioPlayerProvider` from `MainLayout`). The card binds to `Player.IsLoaded`, `Player.IsPlaying`, `Player.CurrentTime`, `Player.Duration`. `IPlayerService` does not currently expose the selected `TrackEntity` as a public property — add `IPlayerService.CurrentTrack { get; }` (nullable `TrackEntity`) and surface the backing field in `AudioPlayerService`. Additive, no existing consumer is affected — implement it as part of this sub-item without a separate approval gate.
  - **Empty state:** when `Player.CurrentTrack is null`, render a placeholder ("Nothing playing — pick a track" or similar) inside the card with the same chrome but no waveform animation. The card is permanent layout, not conditional on selection.
  - **Animated waveform bars:** Phase 0 uses the wireframe's pure-CSS `wave-dance` keyframe animation with randomised `--h-lo` / `--h-hi` / `--dur` per bar — driven by no real audio data. A later phase can wire `SpectrumAnalyzer` data through `AudioInteropService.GetSpectrumData()` to drive bar heights, but that path is already used by `SpectrumVisualizer.razor` in the dock and duplicating it here is out of scope.
  - **Stat row:** static markup with hard-coded "47+", "2", "∞" and TODO comments. The first two could plausibly become real numbers (track count, member count from a future identity model) — flag those at the markup site for Phase 2/identity work to pick up.
  - **Pulsing-ring decoration:** three absolutely-positioned divs as in the wireframe, with the `pulse-ring` keyframe. These are decorative and live in `deepdrft-styles.css` or a `Home.razor.css` scoped stylesheet — pick scoped CSS for anything home-page-specific to keep the global stylesheet from accreting.
  - **Render mode:** `Home.razor` lives in `DeepDrftWeb.Client/Pages/`, so it is already WASM-interactive end-to-end. The cascading `IPlayerService` works in both server prerender (no track loaded → empty state) and post-WASM (live state). No `OnAfterRenderAsync` gymnastics needed.
- **Prerequisite:** 0.1 (fonts + palette for the markup to render correctly).
- **Constraint:** Do not introduce a second player implementation or a separate state store. The "Now Playing" card is **a view onto the same `IPlayerService` instance** the dock uses (see `user_one_source_multiple_views`). If the dock plays a track, the hero card reflects it; if the hero card eventually grows controls, those calls go through the same cascade. The hero's CTAs route to `/tracks` and (eventually) trigger `Player.SelectTrack` from there — they do not become a parallel selection surface.

### 0.4 Marketing content sections (sound / features / origin+connect / CTA / footer)

- **What:** Replace the remainder of `Home.razor` with five wireframe sections in order:
  1. Section divider (`The Sound` tag between horizontal rules).
  2. Sound section — `Genres & Moods` label, `Every / Frequency / Explored` title with italic green emphasis, body copy, 6-column genre grid (House / Techno / Trance / IDM / Progressive / Ambient) with the scaleX-from-left bottom border hover affordance.
  3. Dark features section — navy background, `What We Offer` label, 4-card feature grid (`Lossless Audio Streaming`, `Live Sessions Broadcast`, `Studio Video Content`, `Growing Archive`) with stroked SVG icons.
  4. Split origin + connect — green-panel origin copy on the left with a soft-circle decoration, white-panel "Stay Connected" on the right with Newsletter + Live Alerts option rows and a `Subscribe Free` CTA.
  5. Navy CTA banner with the ghost `DRFT` watermark, headline, sub, and dual CTAs (`Explore the Archive` filled-white / `View Live Schedule` outline-white).
  6. Footer with logo, link list, copyright. Replaces nothing today (there is no footer in the current layout) — add it inside `MainLayout.razor` so it appears site-wide, or inside `Home.razor` if Phase 0 wants it on the home page only. Recommend site-wide.
- **Why it matters:** These sections are what carries the editorial voice. They are decorative-but-load-bearing — without them, the home page is just a hero floating in whitespace.
- **Shape:**
  - **Genre grid:** static cards. Each `genre-card` is a Razor markup block (or a small `<GenreCard />` component if the duplication grates). Phase 2.2 (album/genre views) will wire these to real filtered routes; for Phase 0, an `href="#"` placeholder is acceptable, flagged with a `TODO: wire to /genres/{slug} in Phase 2.2` comment.
  - **Features grid:** the four cards mirror the existing copy on the current `Home.razor` ("High-Quality Streaming", "Live Sessions", "Video Content", "Growing Archive"). Keep the copy intent; reskin to the wireframe. Inline the four SVG icons from the wireframe (they are already 24-box `viewBox` stroked paths and fit `DDIcons.cs` if a static-icon home is preferred — but inline is fine for Phase 0; only promote to `DDIcons` if reuse appears).
  - **Origin + Connect split:** the origin copy is editorial — adapt the existing "Charleston, SC" copy from the current `Home.razor` to the new section. The Connect side has two non-functional rows for Phase 0: Newsletter and Live Alerts are decorative pending an identity/subscription system. Flag them.
  - **CTA banner:** the `DRFT` ghost watermark uses `::before` with a `22rem` font size — verify it doesn't trigger layout overflow on narrow viewports (the wireframe uses `overflow: hidden` on the parent; replicate that).
  - **Footer:** new site-wide affordance. Site root `MainLayout.razor` is the right home for it (after `MudMainContent`, before the closing `MudLayout`). Use `Pages.AllPages` for the link list to keep the source of truth in one place.
  - **Scoped CSS:** these sections are home-page-specific decorative styling. Use `Home.razor.css` (scoped stylesheet) for anything that doesn't generalise; reserve `deepdrft-styles.css` for things genuinely shared across pages.
- **Prerequisite:** 0.1 (palette + fonts).
- **Constraint:** The footer added to `MainLayout.razor` renders on **every** page, including `/tracks`. The dock is the bottom-fixed surface; the footer must be in the document flow above it. **Confirmed:** the `AudioPlayerBar` already starts minimized (`_isMinimized = true`) and expands only on track selection — footer coexistence is acceptable as-is. No suppression logic needed.

### 0.5 Dark theme harmony pass

- **What:** Review the existing "Lowcountry Summer Nights" `PaletteDark` against the Phase 0 light palette and update it so the dark variant feels like a sibling of the new design vocabulary rather than the old one. The current dark palette is coral/sunset/firefly-gold over deep twilight — that may or may not still read as cohesive once the light side has been pulled to navy/green/off-white.
- **Why it matters:** Dark mode is a first-class affordance (cookie-persisted, prerender-aware). If the dark theme reads as a different product after 0.1–0.4 land, the toggle becomes a surprise rather than a preference. This sub-item is the explicit budget for re-harmonising it instead of letting drift accumulate.
- **Shape:** **Confirmed: Option B (mirror).** Rebuild the dark palette as a dark-navy ground — `--navy` as background, deeper navy as surface, `--green-accent` as primary accent, `--white` (#FAFAF8) as text. Visually consistent with the light theme; the "Lowcountry Summer Nights" coral/sunset identity is retired. Adjust contrast values so text and interactive targets meet WCAG thresholds on the darker ground — the light palette's tokens are a starting point, not a direct copy.
- **Prerequisite:** 0.1–0.4 ideally landed so the harmony evaluation has the actual artefact to look at. Can run in a sketch worktree against 0.1 alone if speed matters.
- **Constraint:** The dark-mode cookie + `PersistentComponentState` round-trip is untouched. Only the palette values in `PaletteDark` and the `.deepdrft-theme-dark` CSS-variable block change. Do not refactor the toggle, the cookie service, or the prerender bridge — those are tested and load-bearing.

### Phase 0 deferred (not in scope)

These would naturally appear when scoping a redesign, and are explicitly **not** Phase 0:

- **Real "Now Playing" waveform from `SpectrumAnalyzer`.** CSS-keyframe waveform is good enough for Phase 0. Wiring real spectrum data into the hero card duplicates work already done in the dock and is better folded into a future "shared spectrum hook" refactor.
- **Real stat-row numbers.** Track count would need a `GET api/track/count` endpoint or a count column in the paged response; member count needs an identity model. Hard-coded with TODO is intentional.
- **Genre-filter routes.** Genre cards are decorative in 0.4. Real `/genres/{slug}` is Phase 2.2 work.
- **Subscribe / Live Alerts functionality.** Both rows are visual placeholders. Real subscription requires email collection + storage + an identity decision (see "Cross-cutting / not yet themed").
- **`TracksView.razor` reskin.** The gallery has its own composition (`TracksGallery` → `TrackCard`) that deserves its own design pass, not a Phase 0 retrofit. It continues to work under the recoloured MudBlazor theme.
- **`AudioPlayerBar.razor` reskin.** Same logic. The dock works against the new palette via MudBlazor tokens; a dedicated dock redesign is out of scope.
- **Animation library / scroll-triggered fades.** The wireframe's `fade-up` is CSS-only with hard-coded delays. Anything richer (IntersectionObserver, framer-motion-equivalent) is post-Phase 0.
