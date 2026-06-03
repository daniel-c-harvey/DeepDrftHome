# TODO.md — Known issues and bugs

Pre-existing bugs and known issues not yet triaged into the roadmap. Items here are waiting for scheduling or architectural clarity.

---

## Player controls do not activate — cascade type mismatch [CRITICAL]

Full analysis in `PLAYER_ANALYSIS.md` (2026-06-03).

- **Symptom:** Backend streaming works; player controls never respond. Clicking a track does nothing visible, the dock never expands, transport buttons render disabled.
- **Root cause:** `AudioPlayerProvider.razor` publishes `<CascadingValue Value="@(_audioPlayerService)">` where `_audioPlayerService` is typed as the concrete `StreamingAudioPlayerService`. Blazor binds cascading values **by the declared `TValue` type**. Every consumer asks for an interface — `AudioPlayerBar` / `SpectrumVisualizer` want `IStreamingPlayerService`, `TracksView` / `Home` want `IPlayerService` — so **none match the cascade and all receive `null`.** Null-guarded controls render disabled and silent; `TracksView.PlayerService` is `required` and will throw on first click once the bar alone is fixed.
- **Fix shape:** Type the provider field/cascade as a single interface (`IStreamingPlayerService`) and align all four consumers to it; or register the service in DI and use `AddCascadingValue<IStreamingPlayerService>`. Minimal correct move is the former. Detail in `PLAYER_ANALYSIS.md §2.3`.

## Player stack — adjacent correctness/hygiene issues

Surfaced by the same 2026-06-03 analysis (`PLAYER_ANALYSIS.md §4`). Distinct from the cascade bug.

- **No JS-module-readiness guard.** `AudioInteropService.CreatePlayerAsync` assumes `window.DeepDrftAudio` is present; a slow WASM boot / cache miss produces a spurious init failure. Add a readiness guard or load-order guarantee.
- **Dead legacy buffered path is reachable and silent.** The base `AudioPlayerService.SelectTrack` (non-streaming) path calls `InitializeBufferedPlayerAsync` / `AppendAudioBlockAsync` / `FinalizeAudioBufferAsync`, which are **no-ops** in `index.ts`. Any non-streaming player reports success and plays silence. Remove the dead path or make it fail loudly.
- **Dead TypeScript.** `Interop/audiobuffermanager.ts` (orphaned, missing the live scheduler's `isActive_` / `playbackOffset` fixes) and `Interop/webaudio.ts` (legacy re-export shim) are not part of the `audio/` engine. Delete after confirming no build reference.
- **Duplicated UI helpers.** `GetPlayIcon` and `FormatTime` are each implemented twice across `AudioPlayerBar` and child controls — drift risk. Consolidate.
- **Misleading minimized-dock affordance.** The minimized button shows a play icon but `OnClick` toggles minimize, not playback.

---
