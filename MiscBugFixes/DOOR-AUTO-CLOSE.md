# Door auto-close lost after power cycling

`DoorAutoClosePatch` is always on in MiscBugFixes.

## Confirmed cause

All four vanilla door shutdown interactions apply `CTRemoveAutoClose`:

- `MSDoor01ClosedShutdownAllow`
- `MSDoor01ClosedLockedShutdownAllow`
- `MSDoor01OpenShutdownAllow`
- `MSDoor01OpenLockedShutdownAllow`

`powerinfos.json` routes the corresponding door states to those interactions.
`Powered.UsePower` queues the native power-on interaction when power becomes
available; insufficient power calls `ShutDown`. The same power definitions use
`TIsConnctedSignalledOff` for signal-based shutdown.

`CTRemoveAutoClose` applies `TDnAutoClose`, subtracting one from `IsAutoClose`.
The condition is persistent. Its removal is recorded in `aCondZeroes`, which
`CondOwner.ModeSwitchInternal` transfers to the replacement object. On power-up,
that explicit zero overrides the powered-open door definition's starting
`IsAutoClose=1`. Power-on interactions do not restore the condition. Consequently,
the native auto-close pledge can run but its closing interaction fails the
`IsAutoClose` requirement. Manual close applies `CTAddAutoClose`, restoring it.

## Correction and boundaries

A postfix on `Interaction.SetData` removes only the `CTRemoveAutoClose` effect
from those four runtime shutdown interactions. This covers both newly created
and pooled interactions. Native JSON definitions and shared loot objects remain
unchanged. Any differently named replacement effect remains unchanged.

The existing preference survives the native persistent-condition transfer.
An auto-closing door remains auto-closing after restoration; a deliberately
held-open door stays held open. The unpowered open-door definition has no
auto-close pledge, so preserving the preference does not operate its motor
without power. Power detection, signals, mode switching, locks, damage,
manual controls and the auto-close pledge remain native.

This is preventive. Previously saved disabled auto-close cannot be distinguished
from an intentional hold-open; manually close an affected door once to restore
it. No automatic save-wide reset is performed.

The hook runs during interaction setup, with no condition-update or power-loop
hook, background scan, retained door references or custom save state.

## Validation

`Tests/DoorAutoCloseCheck` passes 50 checks under the bundled game Mono. It audits
the actual installed condition, trigger, power and door definitions; reproduces
the native shutdown removal; verifies all four shutdown variants with auto-close
enabled/disabled; checks pooled interaction reuse and power-on preservation;
and verifies manual hold-open, manual close and unrelated effects are unchanged.

Native interaction construction/reset, `ApplyLootCT` and
`CondTrigger.ApplyChanceID` execute. Asset-to-runtime loading, trigger eligibility,
condition storage and memory bookkeeping are fixtures. Full Unity mode switching
is source-reviewed; the offline fixture does not claim to execute that process.

```powershell
dotnet build Tests/DoorAutoCloseCheck/DoorAutoCloseCheck.csproj -c Release --nologo
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/DoorAutoCloseCheck/bin/Release/netstandard2.1/DoorAutoCloseCheck.dll
```
