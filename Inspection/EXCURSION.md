# Excursion inspection

`GET /excursion?shipId=B-V63&since=0&limit=50`

Captures coordinated raw orbital state in one Unity main-thread request. `shipId` selects the wreck/contact to watch; it is optional and initially defaults to the current nav target. The controller ship is pinned when recording starts so boarding another hull does not silently switch the observed flight dynamics. Responses include the character's current ship separately.

The snapshot includes the controller ship, VORB, VORB auxiliary, watched contact, current nav target, and the watched contact's scan target (usually its delivering hauler), deduplicated and capped at eight ships. It also includes Sol, Venus, VORB and auxiliary orbit elements, plus the ships' current reference bodies.

- Ship motion: stored position/velocity, acceleration inputs, body offsets and orbit-lock flags. `captureEpoch` is the observation time, not a last-integration timestamp.
- Bodies: raw position/velocity, separate position/velocity calculation epochs, parent and orbital elements. No `UpdateTime`, prediction, fuel scan, force or command is invoked. Do not assume every cached value was calculated at the capture epoch.
- Nav display: game-generated VREL/VCRS/range strings, numeric VREL, observation epochs/frames and VREL label (target versus point-of-reference). These remain stale when the nav display is not updating; timestamps expose that. VCRS is cross-track velocity, not signed closing speed.
- Events: before/after controller orbit lock/unlock state and velocity changes; completed nav-display samples capped at 20 Hz wall time. Nested hooks are combined; unchanged lock calls are omitted. A velocity delta can include a natural update of previously cached motion, so inspect its body calculation timestamps before calling it an impulse.

The game stores 256 events in a preallocated value buffer containing scalars/string IDs, with no ship/NPC/body references or save persistence. Recording is dormant until `/excursion` is polled and expires 60 real seconds after the last request. Every request renews that diagnostic lease. Scene/API teardown clears history; a new lease starts a new `session`. Observer failures stop recording and enter the existing inspection warning log without replacing game exceptions.

Use `nextSince` for subsequent requests. Drain immediately while it is below `latestSequence`. `cursorOverrun`/`overwritten` expose lost history. Reset the cursor when `generation` or `data.session` changes; `cursorAhead` also identifies an obsolete cursor. Limits remain 1–50. The overall API response retains `generation`, `epoch`, `capturedUtc` and `inspectionMs`.

`Record-Excursion.ps1` polls every 0.5 real seconds, drains events, handles cursor/session changes, and writes append-only UTF-8 JSONL outside Unity:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Record-Excursion.ps1 -ShipId B-V63
```

Optional `-OutputPath`, `-IntervalSeconds`, `-DurationMinutes` (default 120), and `-Port`. Ctrl+C stops the writer. Ten consecutive API errors stop it; a missing endpoint stops immediately with a deployment message. Captured events and high-frequency readouts share the cursor; polling alone is not expected to catch every frame. Disk usage is duration-dependent and is not bounded like the game's memory buffer.

Build Debug and deploy with the existing `Deploy-Mod.cmd`, then restart. No live Unity test is possible before the new DLL is loaded. Offline tests in `Tests/ExcursionCheck` link the production recorder/serializer and use real game types on bundled Mono; they cover buffer bounds, nested transitions, cursor paging/overrun, readout sampling, lease/thread isolation, timestamp preservation, reset and detached history. The host supplies a fixed frame counter only for those offline tests.
