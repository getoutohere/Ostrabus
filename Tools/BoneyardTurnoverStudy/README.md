# Boneyard Turnover offline implementation study

Supports [the standalone proposal](../../StandaloneMods/BoneyardTurnover/PROPOSAL.md)
and [implementation findings](../../StandaloneMods/BoneyardTurnover/IMPLEMENTATION-STUDY.md).
This study registers no runtime plugin, has no reference to `Ostrabus.dll`, and
uses no live API. The separate production implementation is documented in
[the standalone README](../../StandaloneMods/BoneyardTurnover/README.md).

Build against the installed Ostranauts 1.1.0.2 assembly and run through the
existing bundled-Mono host, sequentially with other native harnesses:

```powershell
dotnet build Tools/BoneyardTurnoverStudy -c Release
dotnet build Tests/SaveJsonCompatibility/Host -c Release
dotnet Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll Tools/BoneyardTurnoverStudy/bin/Release/netstandard2.1/BoneyardTurnoverStudy.dll
```

**252 checks pass.** The executable exercises actual BepInEx CFG persistence,
the proposed curve and conditional absent-age mathematics, mixed-age candidate
selection, installed field assets, native deferred-wreck DTOs, actual synchronous
and worker-thread ZIP saving, optional metadata injection/native parsing/removal,
and the native placement wrapper's continuation after a supplied failed search.
The [risk extension](../../StandaloneMods/BoneyardTurnover/RISK-REVIEW.md)
executes native managed shallow hull/base-CO destruction, retained incoming
references, unresolved occupancy and orphan-item controls, native saved-CO trim
and spawn-queue ordering, deferred-placement overlap, and a frozen metadata
prototype through actual saving workers, cancellation and controlled failures.

`CurvePrototype.cs` is an isolated experiment, not promoted runtime code. It
validates finite positive parameters and conservatively refuses unsupported
nonfinite hazard arithmetic. Production follows that refusal rule and documents
it in the standalone README; ordinary finite positive settings are supported.

The study writes small uniquely named fixtures under the system temporary
directory. It does not alter installed assets, player saves or the main plugin.
Unity logging, graphical object disposal/enumeration, editor validation and the
failed-placement callback are explicit supplied boundaries. Native save trimming
stops after its first phase; full save capture is not claimed. Full scene loading,
actual template materialization, arbitrary loaded-inventory destruction, whole
region-transition composition and live performance remain untested. The study
does not implement or activate runtime turnover.
