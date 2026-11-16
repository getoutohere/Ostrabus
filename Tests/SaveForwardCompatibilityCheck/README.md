# Native save forward-compatibility checks

Build against the game installation selected by `OSTRANAUTS_DIR` (or the standard
Steam path), then run with the shipped Mono host from the repository root:

```powershell
dotnet build Tests/SaveForwardCompatibilityCheck -c Release
dotnet Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll Tests/SaveForwardCompatibilityCheck/bin/Release/netstandard2.1/SaveForwardCompatibilityCheck.dll
```

The default suite uses shipped conditions, CO definitions, overlays, damage
assets and the actual native eligibility evaluator. Engine object comparison,
parent validation and logging are fixture boundaries. Native asset health
preparation occurs before the RNG-read counter is reset.

Checks cover native eligibility equivalence across 256 condition combinations,
mixed strength/exhaust, disabled and loose parts, duplicates, native overrides,
invalid/random inputs, damage preservation, destroyed-part exclusion, cold-cache
refusal, force-loaded ownership, and idempotent loading. No save data is modified.

Optional external fixtures add two actual hulls and a save-wide sample:

```powershell
dotnet Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll Tests/SaveForwardCompatibilityCheck/bin/Release/netstandard2.1/SaveForwardCompatibilityCheck.dll <capture-directory> <hull-inputs.json> <global-rcs-records.json>
```

The capture directory contains `pre-sff-save-O-E3SE.json` and
`pre-sff-save-O-Y2L.json`. The hull-input file is a JSON array containing each
ship's native identifiers, visit epoch, RCS fields, item layout/overrides and
saved CO definitions/conditions, including item force-load flags. The global-record file contains matching native
records under `selectedCOs`. These private captures are not tracked.

The native movement comparison uses the real `Ship.Maneuver`: missing-cache
controls refuse thrust and reconstructed data enables it. Direct cache assignments
from JSON are an explicit fixture boundary for `InitShip`'s shallow copy; the
test does not claim to execute a whole Unity ship load. Cargo mass, dock propagation
and stop presentation are engine boundaries. Whole-plugin binding and the live
reload verify the actual load integration separately.

Each captured hull executes the production prefix with the `false, Shallow`
arguments used by native `StarSystem.Init`; the prefix must match the independently
derived count/exhaust and preserve the saved thrust counter. Unvisited-blueprint
and previously materialized missing-record controls distinguish definition
fallback from resurrection of lost parts. The first live rejection is retained
as a failing regression receipt before the fix.

The later quicksave run passes 389 checks and reconstructs 97 hulls. The untouched
1.0 save run passes 472 checks and reconstructs all 180 affected hulls, including
station and non-O-prefix registrations. The harness loads the shipped native
assets plus the Ostrabus rotation overlay needed by the player's captured hull.
Assertion totals are not substitutes for these execution boundaries.
