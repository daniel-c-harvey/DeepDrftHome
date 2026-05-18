# PLAN.md — DeepDrftHome forward roadmap

Forward-looking roadmap. Sits alongside `CONTEXT.md` (architecture orientation) and `COMPLETED.md` (history). Per `CONTEXT.md §6`, items move from here to `COMPLETED.md` when work lands; do not delete completed entries.

Organised by **theme**, not by date. Themes are roughly ordered by current product weight, not commitment. Nothing here carries a timeline unless it explicitly says so.

---

## 0. Baseline — what just landed

A two-part audit (design + streaming) ran on 2026-05-17 and the fixes for Critical, Major, and Minor findings are now on `dev`. The remainder of this plan assumes that baseline. In summary the audit-pass fixed:

- **Index concurrency** — `VaultIndexDirectory` no longer drops the lock before its async disk write; the index file can no longer be clobbered by interleaved writers.
- **Repository semantics** — `TrackRepository.Update` now fails-fast when an `Id` is not found instead of silently issuing an `INSERT`.
- **Streaming Criticals** — concurrent-seek race in the client, dirty trailing bytes leaking out of the `ArrayPool`-rented buffer, final-tail audio dropped at EOF below the minimum decode frame, and the assumption that the first network chunk contains the whole WAV header.
- **17 design and streaming Majors/Minors** across all eight projects — format-validation alignment between processor/offset/decoder, `IAsyncDisposable` on the player provider, cancellation tokens threaded through the HTTP path, structured logging into the FileDatabase subsystem, sort-sentinel cleanup, sundry DRY/SRP tightenings.

What this means for the roadmap: the streaming substrate is solid. Future work can build on top of it rather than around it. The remaining items in `TODO-V2.md` that did not land are **deferred as features, not bugs** — they are captured below under Phase 1.

---

## Phase 0 — Wireframe-driven home page redesign

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
  - **Now-Playing data source:** `Home.razor` consumes `[CascadingParameter] IPlayerService Player` (cascaded by `AudioPlayerProvider` from `MainLayout`). The card binds to `Player.IsLoaded`, `Player.IsPlaying`, `Player.CurrentTime`, `Player.Duration`. **`IPlayerService` does not currently expose the selected `TrackEntity` as a public property** — `AudioPlayerService` stores it internally but only fires `OnTrackSelected` as a side effect. Phase 0 needs `IPlayerService.CurrentTrack { get; }` (nullable `TrackEntity`) added and a backing field surfaced in `AudioPlayerService`. This is a small, additive interface change — no consumer reads it today.
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
- **Constraint:** The footer added to `MainLayout.razor` renders on **every** page, including `/tracks`. Verify it does not collide with `AudioPlayerBar.razor`'s dock — the dock is the bottom-fixed surface; the footer must be in the document flow above it and not visually conflict when both are present. If conflict is unavoidable, hide the footer when the player is loaded (`@if (Player?.IsLoaded != true)`) and surface a question for Daniel before committing the suppression.

### 0.5 Dark theme harmony pass

- **What:** Review the existing "Lowcountry Summer Nights" `PaletteDark` against the Phase 0 light palette and update it so the dark variant feels like a sibling of the new design vocabulary rather than the old one. The current dark palette is coral/sunset/firefly-gold over deep twilight — that may or may not still read as cohesive once the light side has been pulled to navy/green/off-white.
- **Why it matters:** Dark mode is a first-class affordance (cookie-persisted, prerender-aware). If the dark theme reads as a different product after 0.1–0.4 land, the toggle becomes a surprise rather than a preference. This sub-item is the explicit budget for re-harmonising it instead of letting drift accumulate.
- **Shape:**
  - **Option A (conservative):** keep the coral/twilight palette, retune saturation and contrast so it sits next to the navy/green light theme without dissonance. Cheapest; preserves the existing dark-mode identity.
  - **Option B (mirror):** rebuild the dark palette as a dark-navy ground (e.g. `--navy` itself as background, deeper navy as surface, `--green-accent` as primary accent, an off-white text). Visually consistent with the light theme; loses the bespoke "lowcountry" character.
  - **Option C (third palette):** invent a new dark identity that descends from the light palette's lineage but feels like night rather than day — e.g. deep teal-green ground, warm-cream text, navy-coral accent. Most work; most distinctive.
  - Recommend Option B for Phase 0 unless Daniel signals attachment to the coral/lowcountry identity. The dark theme then becomes "Drft After Dark" or similar — naming is a Daniel call.
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

---

## Phase 1 — Streaming features deferred from the audit

These were flagged during the audit but classified as feature work, not defect fixes. They are listed in rough order of user-visible impact.

### 1.1 Backward seek

- **What:** Seeking to a position *below* `playbackOffset` currently clamps silently to the start of the in-memory buffer segment instead of going to the user's chosen time. The forward "seek beyond buffer" path already exists in `WavOffsetService` + the client's offset-request path; backward seek is the missing mirror.
- **Why it matters:** The single highest-impact missing feature in the player. Scrub-bar drags backward feel broken — they appear to seek but land in the wrong place.
- **Shape:** Reuse the existing `GET api/track/{id}?offset=` pathway. The client decision becomes "is the target inside the decoded window?" — if yes, jump within the buffer (existing behaviour); if no (forward or backward), tear down the decoder and re-request from the byte-aligned offset.
- **Prerequisite:** None — the substrate exists.
- **Constraint:** Backward seek must observe the same `blockAlign` rounding-down as forward seek (already enforced in `WavOffsetService.alignedOffset` and `StreamDecoder.calculateByteOffset`). The teardown/reinit must respect the generation-counter pattern introduced by the concurrent-seek fix.

### 1.2 Audio format diversity

- **What:** Today `AudioProcessor`, `WavOffsetService`, and the JS decoder are PCM/WAV-only. `MimeTypeExtensions` already maps MP3, FLAC, Ogg, AAC, M4A — none are wired.
- **Why it matters:** WAV-only is a real ceiling for any non-internal release. Distribution-grade formats (MP3, FLAC at minimum) are table stakes for a music site.
- **Shape:** Two seams need a strategy pattern.
  - Server side: replace `AudioProcessor.ProcessWavFileAsync` with a format-router that selects a per-format processor; replace `WavOffsetService` with a per-format offset strategy (some formats — MP3, Ogg — have natural frame boundaries; FLAC has block headers; AAC has ADTS).
  - Client side: the JS decoder is currently a WAV byte-walker. For non-WAV, the simplest path is `decodeAudioData` over the full payload (loses streaming-start). The richer path is per-format chunked decoders. Worth a design pass before committing.
- **Prerequisite:** None functionally, but consider settling **Phase 4 (HTTP Range)** first — native range/cache is much more important for large MP3s than for WAVs.
- **Constraint:** Spectrum FFT tap currently relies on raw `AudioBuffer`s through `decodeAudioData`. If a future path uses `MediaElementAudioSourceNode` (see 4.1), the FFT tap still works but the early-playback story changes.

### 1.3 Preload / prefetch of the next track

- **What:** No mechanism to begin the next track's stream during the tail of the current. Each play is a cold fetch.
- **Why it matters:** Prerequisite for both crossfade (1.4) and gapless (1.5). Also a perceived-latency win on its own — track-change feels instant when the bytes are already in flight.
- **Shape:** A second `HttpClient` request kicked off when the current track passes a configurable threshold (e.g. last 10 seconds). Bytes accumulate into a staged `StreamDecoder` instance rather than the live one. Promotion to "current" happens at end-of-stream or on user-selected next.
- **Prerequisite:** Requires a notion of "next track" — today the player only knows the current one. That implies either a playlist/queue model in `IPlayerService` or a passive "what was the next row in the gallery" inference.
- **Open question:** Does a queue model belong in `IPlayerService`, or is the player a single-slot device that a future `PlaylistService` orchestrates above? Worth a design note before implementation. Capture in product notes when picked up.

### 1.4 Crossfade

- **What:** Smooth A→B transition with overlapping fade-out / fade-in.
- **Why it matters:** DJ/mix aesthetic that fits the DeepDrft collective's electronic-music context. Distinguishing UX from generic "next track."
- **Shape:** Architecturally two simultaneous `PlaybackScheduler` instances suffice — each owns its own gain node, crossfaded via `GainNode.gain.linearRampToValueAtTime`. The wiring is the work, not the audio graph itself.
- **Prerequisite:** **1.3 (Preload)** — there is nothing to fade *into* without prefetch.

### 1.5 Gapless playback

- **What:** Eliminate the inter-track silence that exists today.
- **Why it matters:** Important for live-set rips, mix tapes, anything authored to flow continuously.
- **Shape:** The decoder must be able to start the next track's first buffer scheduled exactly at the end of the current one's last buffer (sample-accurate, not wall-clock). With `PlaybackScheduler`'s existing 500 ms lookahead this is mechanically achievable — the next track's first `AudioBufferSourceNode.start(t)` is set to the previous track's end time.
- **Prerequisite:** **1.3 (Preload)**. Also needs to play nicely with **1.2** because gapless across formats is hard (encoder padding/priming on MP3 in particular).
- **Constraint:** Truly sample-accurate gapless requires knowing the priming/padding sample counts of the source format. Out of scope for WAV-only; revisit when format diversity lands.

### 1.6 Track-skip on error

- **What:** A failed `processStreamingChunk` aborts the entire load with no recovery path.
- **Why it matters:** One corrupt frame at byte 4M of a 100 MB stream currently means the listener loses the entire track. Should at minimum surface a clear error and (optionally) skip past the bad region.
- **Shape:** Two-level response.
  - Cheap: catch in the streaming loop, surface a user-visible error, advance the gallery to the next track if a queue exists.
  - Richer: byte-scan forward to the next valid frame header for the format and resume. Format-dependent — only worth doing once **1.2** lands.

### 1.7 Safari compatibility

- **What:** Two known Safari edge cases.
  - `webkitAudioContext.close()` is async-but-not-Promise on older Safari (≤ ~14); `await` resolves immediately and the next `initialize()` can run against a not-yet-closed context.
  - iOS Safari < 15 had streaming-fetch quirks; `HttpCompletionOption.ResponseHeadersRead` behaviour is not guaranteed there.
- **Why it matters:** Real listener share. iOS in particular is a primary listening surface for music.
- **Shape:** For the `close()` race — detect `webkitAudioContext` and poll `state === "closed"` with a short timeout instead of trusting the `await`. For the fetch quirks — first decide the minimum supported iOS version; if pre-15 is in scope, fall back to a non-streaming fetch path and accept the latency.
- **Open question:** What's the floor? Decide before designing the fallback. iOS 15+ as the floor would let us drop the second concern entirely.

---

## Phase 2 — Product surface: gallery, browsing, ingestion

These follow from `CONTEXT.md §5`. Direction is strongly implied but no specific UI has been committed.

### 2.1 Cover art / image vault wired through

- **What:** `MediaVaultType.Image` is implemented end-to-end and exercised by tests, but the production surface only registers a `tracks` vault of type `Audio`. `ImagePath` on `TrackEntity` is a free-form URL string today; it should resolve to an entry in an image vault served by `DeepDrftContent`.
- **Why it matters:** Prerequisite for any album/release/genre view that wants to look like a music site rather than a list of rows. Also closes a free-form-string surface area that will otherwise calcify.
- **Shape:**
  - Register a second vault (`images` or `art`, type `Image`) in `Startup.ConfigureDomainServices` and in the CLI.
  - Add `GET api/image/{entryKey}` (unauthenticated, mirrors track read) and `PUT api/image/{entryKey}` (ApiKey, mirrors track write) on `DeepDrftContent`.
  - Change `TrackEntity.ImagePath` semantics from "URL" to "image vault entry key" (column rename optional — could remain `image_path` with semantic shift, or could become `image_entry_key` for clarity).
  - Add an image processor sibling of `AudioProcessor`.
- **Prerequisite:** None.
- **Constraint:** This is a small schema-semantics migration. Existing rows have `null` ImagePath in production so there is no data to migrate, but commit before the field has real content to avoid a backfill.

### 2.2 Album and genre views

- **What:** `TrackCard` already renders album/genre/release date; the data is there. Missing are gallery groupings (album view, genre view), filters, and the API-side support for filter expressions in `TrackService.GetPaged`.
- **Why it matters:** The track gallery is the only working content surface. Multiple views over the same library is how it earns the "gallery" name.
- **Shape:** Per `CONTEXT.md §6`, the convention is one source of truth, multiple views over it. New views should consume the same `TracksViewModel` / `PagedResult<TrackEntity>` and differ only at the rendering layer.
  - `TrackService.GetPaged` extended to accept a filter expression (or a simple structured filter DTO).
  - `PagingParameters<T>` extended with a `Where: Expression<Func<T, bool>>?` or a parallel `FilterParameters<T>` — pick one to avoid drift.
  - New routes (`/albums`, `/genres`) consume the same VM with different grouping / filter inputs.
- **Prerequisite:** **2.1** for any view that prominently features cover art (album view especially is impoverished without it).

### 2.3 Search and filter on the gallery

- **What:** `TracksViewModel` exposes sort but no filter. `TrackService.GetPaged` accepts only sort. Simple text search across `TrackName` / `Artist` / `Album` is the obvious first cut.
- **Why it matters:** Once the library has more than ~30 entries, sort-only browsing is friction.
- **Shape:** Same extension to `GetPaged` as 2.2. UI is a debounced text input bound to the VM's filter property. EF Core translates `Contains` to SQLite `LIKE`.
- **Prerequisite:** Fold into 2.2 if both are being done — the same `GetPaged` extension serves both. Doing them separately doubles the API churn.

### 2.4 Web-side track upload

- **What:** The CLI is the only producer of tracks today. A web upload UI would pair with `TrackService.AddTrackFromWavAsync` and the existing `PUT api/track/{id}` (already `[ApiKeyAuthorize]`-protected).
- **Why it matters:** Lowers the barrier to adding content. The collective can publish without shell access to the host.
- **Shape:**
  - New page or modal on the web client, drag-and-drop file input.
  - Upload streams to a `POST` endpoint on `DeepDrftWeb` (not `DeepDrftContent` — the web host orchestrates the dual-write, then forwards bytes to content with the API key it already holds).
  - Authentication: this is the first user-facing action that needs to be gated. A new question — see open question below.
- **Prerequisite:** **Authentication model for the web side**. Currently the site has no user concept. Cookie-with-shared-password? OAuth? Per-collective-member account? Decide before building the UI.
- **Open question:** Same as above. This may also bring forward a wider session/identity decision that other features (favourites, listening history) will need eventually.
- **Constraint:** Today's dual-write has no compensating rollback — if content-side succeeds and SQL-side fails, the audio is orphaned in the vault. The CLI inherits this; pushing this onto a web upload increases the rate at which orphans can occur. A simple `DeadLetterLog` of orphaned `entryKey`s (suggested in the audit) becomes more pressing once the web upload exists.

---

## Phase 3 — New content kinds

### 3.1 Live / session content

- **What:** The home page advertises "Live Sessions" and "Video Content (coming soon)". No data model exists for these.
- **Why it matters:** Honour the home page copy. Also differentiates the site from a generic track gallery — live sessions and video are the collective's authored output.
- **Shape:** Speculative; no commitment yet.
  - Likely new entity table(s) sibling to `TrackEntity` (`SessionEntity`, `VideoEntity`?) — or a polymorphic `MediaEntity` with discriminator. The choice affects how much code in `TrackService` / `TrackController` can be reused.
  - New vault type(s). `MediaVaultType.Media` exists and is the obvious home for video; sessions are probably still `Audio`.
  - New routes, new UI surfaces, new player considerations (video has its own playback element and does not go through the WAV decoder).
- **Prerequisite:** Probably **2.1** (vault wiring proof) and a decision on the entity model before any code lands.
- **`[speculative]`** — direction inferred from home-page copy, not a Daniel-confirmed commitment.

---

## Phase 4 — Infrastructure / delivery

### 4.1 HTTP Range + CDN caching

- **What:** Today's `?offset=` query parameter defeats HTTP caching — a CDN sees `?offset=1234567` as a distinct URL from the un-offset request. The architecture re-invents byte-range on top of a custom query param.
- **Why it matters:** Material once the site has real listener traffic. Also relevant to non-WAV formats (1.2) where decoder-side seek is cheaper natively.
- **Shape:** Two intertwined moves.
  - Server: `LoadResourceStreamAsync` returning an open `FileStream` instead of `LoadResourceAsync` materialising the whole buffer. `File(stream, mime, enableRangeProcessing: true)`. The `WavOffsetService` synthesised-header path becomes a special-case rather than the default.
  - Client: consider `MediaElementAudioSourceNode` instead of (or alongside) `decodeAudioData`-fed `AudioBufferSourceNode`s. Native seek, native range, native cache; FFT tap on the audio graph still works for the spectrum visualiser.
- **Prerequisite:** None functionally, but the audit explicitly flagged this trade-off as architecture-intentional — the current path was chosen because spectrum analysis wants `AudioBuffer`s. Re-deciding the trade-off is itself part of the work.
- **Constraint:** A move to `MediaElementAudioSourceNode` changes the early-playback story (the element handles buffering, not us). Worth a design pass.

### 4.2 Server-side stream from disk (no buffer materialisation)

- **What:** `LoadResourceAsync<AudioBinary>` reads the entire file into memory before `File(file.Buffer, mimeType)` returns it. A 100 MB WAV is a 100 MB LOH allocation per request.
- **Why it matters:** Scaling ceiling. Currently fine for a small audience and small library; not fine if either grows.
- **Shape:** Folds into 4.1 — the same `LoadResourceStreamAsync` overload solves both. Listed separately because either could land without the other (you could stream from disk while still using the `?offset=` query path, or you could move to `Range` headers while still buffering).

### 4.3 Dual-write rollback / dead-letter log

- **What:** If content-side write succeeds and SQL-side write fails, audio is orphaned in the vault. No compensating mechanism exists.
- **Why it matters:** A latent data-integrity issue. Materially riskier once web upload (2.4) exists.
- **Shape:** Audit suggested a `DeadLetterLog` recording orphaned `entryKey`s for a periodic maintenance pass. Lighter than full transactional rollback (which the dual-database split fundamentally cannot give us).
- **Prerequisite:** None. Worth landing alongside or just before 2.4.

---

## Phase 5 — Documentation backlog

### 5.1 Folder-level CLAUDE.md sweep

- **What:** Eight folder-level `CLAUDE.md` files need writing/rewriting per the brief in `DOC_PLAN.md`. Five are rewrites (drift from the `.NET 10` upgrade and structural moves); three are new (`DeepDrftWeb.Services`, `DeepDrftContent.Services` — the two libraries where most domain logic now lives — plus the open question on `DeepDrftContent.Services/FileDatabase/README.md`).
- **Why it matters:** The agent guidance files are how every future implementer (human or agent) gets oriented in a directory. They are currently misleading in ways that will cause wrong assumptions on first contact — claiming `.NET 9`, referencing `MediaPath` that has been `EntryKey` for two migrations, describing a `FileDatabase/` tree inside `DeepDrftContent` that has moved out, and missing entirely for the two `*.Services` libraries.
- **Shape:** Doc-keeper executes against `DOC_PLAN.md`. Order of operations and the per-folder briefs are already specified there.
- **Prerequisite:** None. Can run fully in parallel with any feature work.
- **Constraint:** Wait on Daniel for the `DeepDrftContent.Services/FileDatabase/README.md` judgement call before that file changes (retire, keep + refresh, or replace with a CLAUDE.md). The other seven can proceed without that decision.

---

## Cross-cutting / not yet themed

A small set of items that are real but don't fit a phase yet. Surface them when they become relevant rather than committing now.

- **Identity / accounts.** Currently no user concept. Needed before web upload (2.4); also a precondition for favourites, listening history, per-user playlists. Decide the shape before any of those lands. `[speculative]` until Daniel signals interest.
- **`ITrackService` interface.** Audit-suggested. Low value today (one consumer pair); higher value when the test surface expands beyond FileDatabase.
- **Test coverage outside FileDatabase.** Tests today cover the FileDatabase subsystem comprehensively and nothing else. As features in Phases 1–4 land, test scope should expand — at minimum `WavOffsetService`, `AudioProcessor`, `TrackService` (both sides), and the streaming player services. Not a phase of its own; an attached cost to feature work.

---

## Working with this file

- **Add items by extending an existing phase first**; only create a new phase when the addition genuinely doesn't fit any of 1–5. Phase numbers are organisational, not sequencing.
- **When something lands, move it to `COMPLETED.md`** rather than deleting it. Keep the original "What / Why / Shape" body intact so the history reads as a record of the decision, not just the outcome.
- **Mark genuinely uncertain items `[speculative]`** so future readers can tell what is direction vs. commitment.
- **Open questions belong in the item that raises them**, not in a separate "questions" list — they expire when the item does.
