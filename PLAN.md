# PLAN.md — DeepDrftHome forward roadmap

Forward-looking roadmap. Sits alongside `CONTEXT.md` (architecture orientation) and `COMPLETED.md` (history). Per `CONTEXT.md §6`, items move from here to `COMPLETED.md` when work lands; do not delete completed entries.

Organised by **theme**, not by date. Themes are roughly ordered by current product weight, not commitment. Nothing here carries a timeline unless it explicitly says so.

---

## In-flight — Two-app architectural split

The public site and the CMS are being split into two independent Blazor applications. **Design locked by Daniel 2026-05-19** at `design/TWO-APP-SPLIT.md` §10. All ten open questions resolved. Implementation phases ready to schedule in the phased rollout at §8.

**Names locked:** `DeepDrftPublic` (public host), `DeepDrftManager` (CMS host), `DeepDrftShared.Client` (shared RCL). Subdomain topology: `deepdrft.com` (public) and `manage.deepdrft.com` (CMS).

Supersedes the host-shape pieces of `CMS-PLAN.md §2`; the CMS feature waves in `CMS-PLAN.md` survive unchanged and move to the new host.

**Phases 1, 2, and 3 landed (Wave 2):** `DeepDrftManager` host created, AuthBlocks stripped from `DeepDrftWeb`, `DeepDrftShared.Client` RCL extracted. Phase 4 (rename `DeepDrftWeb` → `DeepDrftPublic`) is the next wave.

---

## 0. Baseline — what just landed

A two-part audit (design + streaming) ran on 2026-05-17 and the fixes for Critical, Major, and Minor findings are now on `dev`. The remainder of this plan assumes that baseline. In summary the audit-pass fixed:

- **Index concurrency** — `VaultIndexDirectory` no longer drops the lock before its async disk write; the index file can no longer be clobbered by interleaved writers.
- **Repository semantics** — `TrackRepository.Update` now fails-fast when an `Id` is not found instead of silently issuing an `INSERT`.
- **Streaming Criticals** — concurrent-seek race in the client, dirty trailing bytes leaking out of the `ArrayPool`-rented buffer, final-tail audio dropped at EOF below the minimum decode frame, and the assumption that the first network chunk contains the whole WAV header.
- **17 design and streaming Majors/Minors** across all eight projects — format-validation alignment between processor/offset/decoder, `IAsyncDisposable` on the player provider, cancellation tokens threaded through the HTTP path, structured logging into the FileDatabase subsystem, sort-sentinel cleanup, sundry DRY/SRP tightenings.

What this means for the roadmap: the streaming substrate is solid. Future work can build on top of it rather than around it. The remaining items in `TODO-V2.md` that did not land are **deferred as features, not bugs** — they are captured below under Phase 1.

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
