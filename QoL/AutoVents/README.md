# Kang auto-vent transient tolerance

Namespace: `Ostrabus.QoL.AutoVents`. Enabled by default. The PDA's
**MOD SETTINGS → Kang auto-vents** toggle enables/disables this mod feature and
persists to BepInEx config. It is not a wireless interface to individual vents.
All materialized Kang auto-vents use the same revised built-in behavior.

## Behavior

Both sides are sampled on the existing pressure-sense signal, approximately
once per in-game second. Each side has independent low-pressure history:

- O2 partial pressure **below 20 kPa** starts a timer.
- Remaining below that threshold for **3 in-game seconds** requests closure.
- Recovery to **20 kPa or higher** cancels that side's timer.
- O2 partial pressure **at or below 17 kPa**, a missing/void room, or an invalid
  pressure reading requests closure immediately, bypassing the timer.

Immediate means at the next scheduled sensor check, or the initial gas-exchange
check. Native `MSVent02CloseO2Allow` still performs closing, including its
0.0002-hour (0.72-second) duration and normal queue scheduling. The feature
does not instantly teleport a vent into the closed mode. Native power/installation
eligibility, power-off behavior, manual close and manual reopening remain intact.
It never automatically reopens a closed vent.

The 17 kPa default follows the game's `PledgeSurviveO2.CoHasO2` destination check;
it is not a claim that all crew comfort/physiology rules use that threshold.
Oxygen alarms, pumps, crew conditions and global `DcGasPpO2` are unchanged.

## Configuration

In the existing plugin BepInEx `.cfg`:

```ini
[QoL.AutoVents]
Enabled = true
NormalOxygenKPa = 20
EmergencyOxygenKPa = 17
GraceSeconds = 3
```

Numeric settings are optional external tuning, not in-game device controls.
Normal/emergency values accept 0–1000 kPa and grace accepts 0–60 seconds.
The effective emergency threshold cannot exceed the normal threshold.
Non-finite/negative values fall back to defaults. Setting changes reset transient
history; config values take effect when loaded by BepInEx. The PDA only changes
`Enabled`. Disabling returns future sensor calls to the original methods;
already queued native closing actions are not cancelled.

## Native integration

`GasPressureSense.Run` and `FixForVentSensors` are intercepted only for sensors
whose native alarm interaction is `MSVent02CloseO2Allow`. Ordinary vents,
Sokol powered vents and other pressure alarms retain their original code.

The two native Kang sensors use one shared `IsReadyPressureSense` counter, and
each normally consumes it. The replacement reads both sensing points once and
consumes the pending signal together. Backlogged signals are coalesced; several
signals at one epoch do not establish several seconds of sustained low pressure.

`GasExchange` caches its initialization sensor's `doNotExchange` result. The
initial check therefore uses the same policy: marginal pressure during grace
allows normal exchange; an immediate emergency blocks flow as native startup
logic does. Later closure follows native queue/mode-switch behavior. A runtime
closure creates the same native low-pressure objective once; startup behavior
does not add an objective, matching the original initialization path.

Room changes, clock reversal, observation gaps longer than 2.5 in-game seconds,
scene unloads and configuration changes reset relevant timers. Powered-off or
non-open states discard history when observed; native mode switches also replace
the CO and naturally start fresh history. Reload/SFF gaps do not get credited as
continuously observed low pressure. An emergency reading still trips immediately.
Unexpected sampling failures log a warning and fall back to native sensing.

## Performance and lifetime

No new Unity Update callback, world scan, coroutine, per-frame history recording
or device registry. Without a pending signal, the existing sensor call performs
only routing/ready checks. A sampling decision makes two native room/tile lookups,
reads O2, and compares scalar state. It avoids native sensor `RaycastAll` calls and
temporary CO result lists. Native room/tile lookup costs remain game-dependent;
no claim of measured frame-time improvement is made.

A weak-key table holds one small state per encountered vent. Values contain
numeric/boolean state and room ID strings, not ship/room/CO references. Native
queued interactions retain their normal ownership. Event subscriptions are
removed on plugin destruction. No custom save fields or device settings are
written; configuration is external to the save.

Detailed `GET /cos/{ventCoId}` includes `autoVent` when the current CO has been
sampled: last sample epoch, both O2 readings, observed low durations, effective
settings, trip state and whether a close was queued. Inspection only copies
stored data and does not sample, create state, or advance timers. It returns null
for unsampled vents and other objects, and after history is cleared.

## Validation

`Tests/AutoVentCheck` passes 2,062 offline policy/integration checks, including
2,000 deterministic pressure-series comparisons. It installs the production
Harmony hooks on native sensor methods and executes native
`GasExchange.FixForVentSensors`, verifying the cached flow gate. It exercises
both sides, grace/recovery/emergencies, power changes, room replacement, missed
ticks, time gaps, deduplicated/rejected closes, native eligibility, config
persistence and changes, disabled/other-alarm fallthrough, and read-only inspection.
A thousand no-signal sensor pairs cause no room searches.

Unity room lookup/condition storage/clock and queue machinery are controlled
fixtures; native interaction definitions/objective reporting are substituted.
Closing animations and full live atmosphere simulation are not executed offline.
The initial investigation's reduced gas/pump model is in `obj/AutoVentStudy.ps1`;
it is illustrative and is not a full Unity gas simulation.

```powershell
dotnet build Tests/AutoVentCheck/AutoVentCheck.csproj -c Release --nologo
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/AutoVentCheck/bin/Release/netstandard2.1/AutoVentCheck.dll
```
