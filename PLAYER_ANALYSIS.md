# PLAYER_ANALYSIS.md — Streaming player controls: root cause + improvement map

Analysis-only document. No code changed. Produced 2026-06-03 against the
`DeepDrftPublic.Client` player stack and the `DeepDrftPublic/Interop/audio/`
TypeScript engine.

---

## 1. TL;DR

- **Root cause (controls dead): cascade type mismatch.** `AudioPlayerProvider`
  publishes the player as `CascadingValue<StreamingAudioPlayerService>` (the field
  `_audioPlayerService` is typed as the concrete class). Blazor matches cascading
  values **by the declared `TValue` type**, not the runtime type. Every consumer
  asks for an *interface* — `AudioPlayerBar` and `SpectrumVisualizer` want
  `IStreamingPlayerService`, `TracksView` and `Home` want `IPlayerService` — so
  **none of them match the cascade. They all receive `null`.** The bar's controls
  are guarded by `PlayerService?` / `?? false`, so a null player renders disabled,
  silent buttons with no error. This single fault explains the reported symptom.
- **Second, independent fault on the same path:** even if the cascade resolved,
  `TracksView.PlayerService` is typed `IPlayerService`, which has **no
  `SelectTrackStreaming`** — but `PlayTrack` calls `SelectTrack`, which the
  streaming subclass overrides to route into streaming. That part is fine. The
  real latent issue is that `AudioPlayerBar` requires the *streaming* interface
  while `TracksView` only needs the base — two different cascade types are
  expected from one published value. Both must be satisfiable. (See §2.)
- **`@rendermode` is not the cause but is adjacent.** The app root is
  `InteractiveAuto`. During SSR prerender the JS engine does not exist; the design
  already defers init to first user gesture, so this is handled. But the cascade
  bug would *also* manifest identically in pure WASM, so render mode is a red
  herring for this specific symptom.
- **The streaming engine itself is in good shape.** The TS decoder, scheduler,
  and context manager are carefully written (suspended-context resume awaited,
  tail-drain on EOF, typed decode errors, generation-safe seek teardown). The
  break is entirely in the **C#→component wiring layer**, not the audio path.
- **Biggest product gaps once controls work:** no queue/next-track model
  (blocks preload, crossfade, gapless — all already in PLAN Phase 1), backward
  seek lands in the wrong place (PLAN 1.1), and `Home.razor`'s play affordances
  share the same cascade fault.

---

## 2. Root cause analysis — why controls do not activate

### 2.1 The play call chain (what *should* happen)

```
User clicks TrackCard in TracksGallery
  → TracksView.PlayTrack(track)                         [TracksView.razor.cs:50]
    → PlayerService.SelectTrack(track)                  [CascadingParameter IPlayerService]
      → StreamingAudioPlayerService.SelectTrack (override)        [routes to streaming]
        → SelectTrackStreaming(track)
          → EnsureInitializedAsync()  → AudioInteropService.CreatePlayerAsync
            → JS window.DeepDrftAudio.createPlayer(playerId)      [index.ts:17]
          → _audioInterop.EnsureAudioContextReady(playerId)
          → NotifyTrackSelected()  → fires OnTrackSelected
            → AudioPlayerBar.Expand() un-minimises the dock       [AudioPlayerBar.razor.cs:49]
          → LoadTrackStreaming(track)
            → TrackMediaClient.GetTrackMedia(entryKey)            [proxy → DeepDrftAPI]
            → _audioInterop.InitializeStreaming(playerId, len)
            → StreamAudioWithEarlyPlayback(...) loops:
                ReadAsync → ProcessStreamingChunk(playerId,bytes) → JS processStreamingChunk
                when CanStartStreaming → StartStreamingPlayback   → JS startStreamingPlayback
            → MarkStreamCompleteAsync(playerId)
```

### 2.2 Where it actually breaks

**Primary break — the cascade never reaches any consumer.**

`AudioPlayerProvider.razor`:
```razor
<CascadingValue Value="@(_audioPlayerService)" IsFixed="true">
```
`_audioPlayerService` is declared `private StreamingAudioPlayerService?`
(`AudioPlayerProvider.razor.cs:14`). Razor infers `CascadingValue<TValue>` from
the *compile-time* type of the `Value` expression, so this publishes a
`CascadingValue<StreamingAudioPlayerService>`.

Consumers:
| Component | Declared `[CascadingParameter]` type | Matches? |
|---|---|---|
| `TracksView` | `IPlayerService` | **No** |
| `Home` | `IPlayerService?` | **No** |
| `AudioPlayerBar` | `IStreamingPlayerService?` | **No** |
| `SpectrumVisualizer` | `IStreamingPlayerService?` | **No** |

Per the Blazor docs: *"Cascading values are bound to cascading parameters by
type."* The bind is to the `TValue` of the published `CascadingValue<>`, which is
the concrete class here. An interface-typed parameter does not match a
concrete-typed cascade. **All four consumers get `null`.**

Consequences that exactly match the symptom:
- `AudioPlayerBar.IsLoaded` etc. are all `PlayerService?.X ?? false` → every
  control renders `Disabled` and does nothing.
- `TracksView.PlayTrack` calls `PlayerService.SelectTrack(...)` on a **`required`
  non-nullable** cascade. If the cascade is null, this is an
  `ArgumentNullException` / NRE on first click — or, because `required` only
  guarantees set-at-init for *parameters* (not cascades), a null-ref at call
  time. Either way the click does nothing visible and may throw silently into the
  Blazor error UI.
- `OnTrackSelected` is never wired (set in `AudioPlayerBar.OnParametersSet`, which
  guards `if (PlayerService != null)`), so the dock never expands.

**This is the whole symptom.** The backend works because the proxy + API path is
independent of the cascade; the failure is purely in component resolution.

### 2.3 The fix shape (for staff-engineer, not done here)

Two viable shapes — a product/architecture call:

- **Shape A — publish the interface.** Change the provider field/cascade to a
  single shared type. Because two different interfaces are consumed
  (`IPlayerService` and `IStreamingPlayerService`), publish the **most-derived
  one** (`IStreamingPlayerService`) and **widen the base consumers**
  (`TracksView`, `Home`) to also accept `IStreamingPlayerService` — or publish
  *both* as two named/typed cascades. Simplest correct move: type the field as
  `IStreamingPlayerService`, publish `CascadingValue<IStreamingPlayerService>`,
  and change `TracksView`/`Home` cascade params to `IStreamingPlayerService` (or
  to `IPlayerService` *and* add a second cascade — avoid; one type is cleaner).
- **Shape B — DI + root cascade.** Register `StreamingAudioPlayerService` once as
  a scoped service behind both interfaces and cascade it via
  `AddCascadingValue<IStreamingPlayerService>(...)`. Removes the provider
  component entirely. Heavier change; better long-term if the player should be a
  first-class service rather than a component-owned object.

Recommend **Shape A** as the minimal correctness fix, with Shape B noted as the
cleaner end-state once a queue/`PlaylistService` lands (PLAN 1.3).

### 2.4 Other plausible break points (ruled in / out)

| Candidate | Verdict |
|---|---|
| JS module not on `window.DeepDrftAudio` at call time | **Not the cause** but real risk. `index.ts` assigns `window.DeepDrftAudio` at module load. Nothing in the C# path *awaits* module readiness — `CreatePlayerAsync` will throw "could not find 'DeepDrftAudio'" if the script tag hasn't executed. Worth a guard (see §4). |
| SSR prerender firing interop | Handled — init is deferred to first gesture via `EnsureInitializedAsync`. |
| `IsFixed="true"` stale reference | Not a correctness bug here; the instance is stable for the component lifetime. Becomes a problem only if the service is ever swapped. |
| Throttled `NotifyStateChanged` swallowing the first render | Critical state changes (playback start, error) use immediate `NotifyStateChanged`, not the throttled path. Fine. |
| `required` on a cascade that resolves null | Aggravates the primary bug — see §2.2. |

---

## 3. Architecture map — call chains

### 3.1 Play / pause (after a track is loaded)
```
AudioPlayerBar TogglePlayPause button
  → PlayerService.TogglePlayPause()                    [AudioPlayerService.cs:206]
    if IsPlaying:  _audioInterop.PauseAsync  → JS pause  → AudioPlayer.pause()  → scheduler.pause()
    else:          _audioInterop.PlayAsync   → JS play   → AudioPlayer.play()
                     → contextManager.ensureReady() (awaited; resumes suspended ctx)
                     → scheduler.playFromPosition(pausePosition)
  → NotifyStateChanged → provider StateHasChanged → bar re-renders
```
No JS→C# callback on play/pause. State is push-from-C#.

### 3.2 Progress (JS → C#)
```
AudioPlayer.startProgressTracking() setInterval(100ms)
  → onProgressCallback(currentTime)
    → DotNetObjectReference.invokeMethodAsync("OnProgressCallback", t)   [index.ts:131]
      → AudioPlayerCallback.OnProgressCallback                          [AudioInteropService.cs:295]
        → AudioPlayerService.OnProgressCallback → CurrentTime = t → NotifyStateChanged
```

### 3.3 End of track (JS → C#)
```
PlaybackScheduler.handleSourceEnded → onPlaybackEnded
  → AudioPlayer.handlePlaybackEnded → onEndCallback
    → invokeMethodAsync("OnEndCallback")
      → AudioPlayerService.OnPlaybackEndCallback → IsPlaying=false, CurrentTime=0
```
**Gap:** end-of-track resets state but does **not** advance to a next track —
there is no queue. (PLAN 1.3 / 1.5 territory.)

### 3.4 Seek
```
MudSlider pointer up → AudioPlayerBar.OnSeekEnd(pos)
  → StreamingAudioPlayerService.Seek(pos)
    → _audioInterop.SeekAsync → JS seek → AudioPlayer.seek(pos)
      if pos <= bufferedDuration: seekWithinBuffer (re-schedule from offset)
      else: seekBeyondBuffer → returns { seekBeyondBuffer, byteOffset }
    → if SeekBeyondBuffer: SeekBeyondBuffer(pos, offset)
        cancel + drain current loop → GetTrackMedia(offset) → ReinitializeFromOffset
        → StreamAudioWithEarlyPlayback(newStream)
```
**Known gap (PLAN 1.1):** backward seek below `playbackOffset` is not handled as a
re-request; `seek()` only triggers the offset path when `position > bufferedDuration`
(forward). A backward target inside `[0, playbackOffset)` after a prior
seek-beyond clamps via `Math.max(0, bufferRelativePosition)` to the buffer start —
lands in the wrong place.

### 3.5 Volume
```
VolumeControls slider → AudioPlayerBar.OnVolumeChange
  → PlayerService.SetVolume(v) → _audioInterop.SetVolumeAsync → JS setVolume
    → contextManager.setVolume (gainNode.gain.setValueAtTime)
```
`Volume` is stored in C# and applied at init; volume changes before a track loads
update C# state but only reach JS once `IsLoaded` (see `SetVolume` guard) —
acceptable, but the slider appears live before load with no audible target.

---

## 4. Gaps and improvements

### Bugs (broken now)

1. **[CRITICAL] Cascade type mismatch — controls dead.** §2.2. The headline.
2. **`TracksView.PlayerService` is `required` but resolves null.** Same root
   cause; will NRE/throw on first click once anyone fixes only the bar.
3. **No JS-module-readiness guard.** `CreatePlayerAsync` assumes
   `window.DeepDrftAudio` exists. If the compiled module hasn't loaded (slow WASM
   boot, cache miss), the first interop call throws a JS-not-found error that
   surfaces as a generic init failure. Add a `whenReady`/poll or load-order
   guarantee.
4. **`AudioPlayerService.SetVolume` no-ops silently before load.** Volume set
   while idle updates C# `Volume` but the `if (IsLoaded)` guard skips the JS call;
   on the *next* track, init applies the stored value — correct, but the gap is
   undocumented and looks like a bug from the UI.
5. **Dead legacy buffered path still reachable.** `AudioPlayerService.LoadTrack` /
   `StreamAudio` call `InitializeBufferedPlayerAsync` / `AppendAudioBlockAsync` /
   `FinalizeAudioBufferAsync`, which are **no-ops** in `index.ts` (lines 208–218).
   The base `SelectTrack` path (non-streaming) therefore silently loads nothing.
   Only the streaming override works. This is a latent trap: any future consumer
   that constructs a non-streaming `AudioPlayerService` gets a player that reports
   success and plays silence.

### High-value additions (product payoff)

6. **Queue / next-track model.** Prerequisite for preload, crossfade, gapless
   (PLAN 1.3–1.5) *and* for end-of-track auto-advance, which listeners will
   expect immediately. Today the player is a single-slot device. This is the
   single highest-leverage addition — it unblocks four roadmap items.
7. **Skip / previous controls.** Trivial UX once a queue exists; conspicuously
   absent from `PlayerControls` (only play/pause + stop today).
8. **Backward seek (PLAN 1.1).** Already roadmapped; reiterated because it is the
   most-felt correctness gap in the scrub bar.
9. **Keyboard shortcuts.** Space = play/pause, arrows = seek, M = mute. Zero new
   architecture; a `@onkeydown` handler on the layout dispatching to the player.
10. **Loading affordance during seek-beyond-buffer.** `IsSeekingBeyondBuffer`
    exists on the service but the bar never reads it — a backward/forward
    long-seek looks frozen. Surface a spinner on the seek handle.
11. **Persisted volume + mute toggle.** Volume resets to 0.8 every session;
    cookie-persist it like dark mode. Add an explicit mute button (the icon
    already changes at volume 0 but there is no click-to-mute).

### UX issues in the current control structure

12. **Two play-icon implementations.** `AudioPlayerBar.GetPlayIcon` and
    `PlayerControls.GetPlayIcon` are duplicated; drift risk. Consolidate.
13. **`FormatTime` duplicated** across `AudioPlayerBar` and `TimestampLabel`.
14. **Minimized dock shows a play icon that toggles minimize, not play.** The
    minimized button uses `GetPlayIcon()` but `OnClick=ToggleMinimized` — a user
    will read it as "play" and get "expand." Misleading affordance.
15. **No now-playing track title/artist in the bar.** `CurrentTrack` is populated
    but the bar renders only time/seek/spectrum. The most basic player metadata
    (what's playing) is absent from the dock.

### Streaming pipeline improvements

16. **Error recovery / track-skip (PLAN 1.6).** A single failed chunk aborts the
    whole load. Once a queue exists, advance on fatal decode error.
17. **HTTP Range + native caching (PLAN 4.1) / stream-from-disk (4.2).** Already
    roadmapped; the `?offset=` param defeats CDN caching.
18. **Preload of next track (PLAN 1.3).** Needs the queue model (#6).
19. **Adaptive-buffer telemetry.** `AdaptBufferSize` tunes silently; no surfaced
    signal when it collapses to 16 KB (a slow-connection indicator that could
    drive a "buffering" UI state).

### TS engine — incomplete / fragile

20. **Two parallel buffer implementations.** `audio/PlaybackScheduler.ts` (live)
    and `audiobuffermanager.ts` (orphaned, not imported by the `audio/` engine).
    `webaudio.ts` is a legacy shim re-exporting `audio/index.ts`. Dead code that
    will mislead the next reader — `audiobuffermanager.ts` even lacks the
    `isActive_` sentinel and `playbackOffset` fixes the live scheduler has.
    Recommend deletion of `audiobuffermanager.ts` and `webaudio.ts` (confirm no
    build reference first).
21. **`getEstimatedDuration` falls back to `totalStreamLength - headerSize`**
    when `dataSize` is 0. For offset streams the synthesised header's `dataSize`
    drives duration; verify the offset-stream duration stays anchored to the
    *original* full-track duration, not the post-offset remainder (otherwise the
    scrub bar's max collapses after a long seek).
22. **Decode timeout is a fixed 5 s.** Under heavy tab throttling a legitimate
    large segment could exceed it; the retry helps but the constant is not
    adaptive. Low priority.

---

## 5. Recommended implementation sequence

Strict ordering by dependency and payoff. Steps 1–2 are the *only* things needed
to make controls work; everything after is improvement.

1. **Fix the cascade type (Bug #1, #2).** Publish one interface type from
   `AudioPlayerProvider` and align all four consumers. This alone restores
   controls. Smallest possible diff; highest value. *(staff-engineer)*
2. **Add a JS-module-readiness guard (Bug #3)** so a slow WASM boot can't produce
   a spurious init failure. *(staff-engineer)*
3. **Surface now-playing metadata + fix the minimized-dock affordance (#15, #14).**
   Cheap, visible polish that makes the working player feel finished.
4. **Persist volume + mute toggle (#11), de-dup icons/FormatTime (#12, #13).**
   Low-risk consolidation.
5. **Delete dead TS (#20) and the dead buffered C# path (#5).** Removes traps
   before any new contributor touches the engine. *(staff-engineer; verify build
   refs)*
6. **Backward seek (PLAN 1.1)** and **seek-in-progress spinner (#10).**
7. **Queue / next-track model (#6).** The pivot. Settle the open question in
   PLAN 1.3 first (does the queue live in `IPlayerService` or a separate
   `PlaylistService`?). Once landed it unblocks skip/prev (#7), auto-advance,
   preload (1.3), crossfade (1.4), gapless (1.5), and track-skip-on-error (1.6).
8. **Keyboard shortcuts (#9).** Trivial once the queue + transport API is stable.
9. **Roadmapped infra:** HTTP Range / stream-from-disk (PLAN 4.1/4.2), format
   diversity (1.2) — already sequenced in PLAN.md.

**Note for the roadmap:** items 1–5 are *correctness and hygiene*, not features —
they belong in `TODO.md` (bugs) rather than `PLAN.md` (forward features). Item 7
onward maps onto existing PLAN Phase 1 entries; no new phase is needed.
