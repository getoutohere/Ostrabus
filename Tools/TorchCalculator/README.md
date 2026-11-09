# Torch drive calculator

Open **`calculator.html` directly in a browser**. The entire calculator, including
its JavaScript model and charts, is in that single HTML file. It works from the
local filesystem without a server, installation, .NET SDK, game connection,
network requests or external scripts/fonts. It never writes game saves.

Enter the ship's **current game-reported mass**, active feeder/laser pairs, and desired G.
The result gives the sustainable acceleration ceiling, suggested cycle headroom,
required cycle/flow, and He-3/D2O consumption per powered hour. Supporting module
counts and wear are under Reactor details. Cryopumps assume available cryogen.

The comparison varies only feeder/laser counts, retaining entered mass, condition,
capacitors and regulators. A single capacitor or regulator supports at most two
of its corresponding modules; upgrading to three/four effective pairs needs
additional supporting modules. Counts mean active modules; zero-health modules
contribute no usable throughput.

Fuel planning takes total **powered** hours across acceleration and braking,
excluding coast time. By default, specify installed He-3 and D2O tank counts,
assumed full; the app shows their available fuel quantities. Alternatively choose
remaining fuel in kg for partially filled tanks.

The game tracks He-3/D2O quantities separately from `StatMass`: their contents
do **not** add to ship mass. Existing tank hardware is already included in the
entered ship mass, so editing availability does not change mass. The explicit
extra-tanks button adds **1,500 kg per new tank only**, fills each new tank, and
recalculates. It updates tank counts in full-tank mode or adds full-tank quantities
in kg mode. This assumes no spare capacity in existing tanks and follows the
game's current behavior rather than imposing physical propellant mass.

## Traced game behavior

Source basis: shipped 1.0.1.10 assembly and native JSON assets. The code uses the
decompiled binary constants, including important single-precision intermediates.
No real-world fusion efficiency assumptions replace the game's formulas. The
browser uses explicit `Math.fround` operations at the native float boundaries.
`TorchModel.cs` remains the native-tested reference, with browser parity checks
covering the standalone JavaScript implementation.

Let `P` be the effective pellet limit, `c` the CYCLE setting, `m` the current ship
mass in kg, and `v = StatICVe / 70,500,000` (normally 1).

- `FusionIC.Run`: feeder/laser contributions are `1 - damage/damageMax`, summed
  separately. Feeders are capped at twice the fuel-regulator count; lasers at twice
  the capacitor count. `P = 2 × min(effective feeders, effective lasers)`.
- `FusionIC.Fusion`: rated thrust is approximately **1,853,347 N × P × v**.
- `Ship.GetMaxTorchThrust`: thrust at ideal core temperature is
  `ratedThrust × c × (1 + (P - 1)c) / P`; acceleration is thrust/mass.
  The implementation mirrors the native float arithmetic and AU conversion.
- `FusionIC.Run`: CYCLE removes `0.2 × c × min(dt, 1)` from core temperature;
  pellet flow adds `0.04 × pelletRate × min(dt, 1)`. Feed rate cannot exceed `P`.
  The ideal-temperature sustainable ceiling is therefore **c ≤ 0.2P**, capped at
  1. Cryopumps remove heat only above the ideal **0.72500002384** core value.
  They cannot increase heating capacity to prevent a high-cycle cold flameout.
- Feed feedback observes core temperature **after** cycle heat extraction. The
  calculator reproduces this ordering and solves the flow needed for equilibrium.
  At the exact capacity ceiling, FLOW is fully open and has no recovery headroom.
  The default suggestion leaves 5% CYCLE headroom; this is a calculator preference,
  not an undocumented game rule.
- `NavModTorchDrive.GetLimiterSafetyMax` reduces CYCLE in 0.01 float steps until
  acceleration is at most approximately **2 G**, when torch safety is enabled.
- `Ship.CalculateTorchFuelConsumption` charges `deltaV / acceleration × pilotMod`
  in normalized full-power fuel seconds. A constant powered segment at the plotted
  acceleration therefore charges its **duration**, regardless of selected cycle.
- `FusionIC.MassUsageMax` converts those seconds to He-3 kg using
  `2 × ratedThrust / (0.94999998808 × 70,500,000)`.
  `NavData.TimeAdvance` / `FusionIC.SetReactants` debit the tanks accordingly;
  D2O uses **0.6670000062 × He-3 mass**. `Ship.UpdateCrewSkills` supplies either
  a 1.0 or 0.75 fuel modifier for the native spaceship-operations skill benefit.
- Manual `FusionIC.Fusion` instead charges actual pellet rate and temperature.
  A separate manual-equilibrium estimate is shown in the explanatory section.
  Do not substitute this for plotted-course fuel budgeting. SetReactants can
  reconcile/refund intervening reactor consumption to the plotted remaining fuel.

For **32,000 kg**, one healthy pair, adequate support, thrust mode and safety off:

| Quantity | Native-derived estimate |
| --- | ---: |
| Sustainable ceiling | 3.3070 G |
| He-3 per powered hour, unskilled | 398.580 kg |
| D2O per powered hour, unskilled | 265.853 kg |
| He-3 per powered hour, skilled | 298.935 kg |

At the same requested G, adding a second effective pair doubles the plotted fuel
rate, while increasing the heat/thrust ceiling nonlinearly. Extra hardware that is
already constrained by a support-module cap does not double usable capacity.

Native tank definitions (`ItmCanisterLHe02` / `ItmCanisterLH02`): 1,500 kg nominal
each, 5,216 kg initial He-3 and 44,722.8 kg initial D2O. These are game values,
not measurements of a particular player's tank condition or remaining volume.
Refueling and fusion change `StatSolidHe3`/`StatLiqD2O`; ship mass sums `StatMass`
instead. Fuel mass is intentionally excluded, matching the shipped game.

## Thermal preview and limits

The ten-minute preview starts at ideal core temperature and uses the native
0.2700000107-second reactor cadence at the solved CYCLE. Sustainable fits use a
fixed equilibrium flow. Overloaded fits use full flow, showing best-case heat
recovery; it is not a replay of the autopilot's adaptive controller.

Native NavData can abort a steady plotted burn below 80% or above 120% of ideal,
but only under its additional flow/segment guards. Crossing the plotted threshold
is not a promise of the exact abort time. Core heat above 0.75 can also wear the
ablative wall; a safe-temperature curve does not certify safe crew/hull G loading.

Assumes an already ignited reactor, thrust mode, working forward containment,
adequate electricity, no no-wake shutdown, and adequate supporting systems/fuel.
It does not model startup, idle/coast consumption, long-term component wear,
control transitions, full trip geometry, changing ship mass or existing damage
outside feeder/laser/cryo effectiveness. The cold-limit estimate is not a guarantee
that SFF catch-up or a reactor starting cold will sustain the same run.

## Validation

The standalone calculator/model/browser checks are included independently of the
autopilot patches. The optional `TorchNativeOracle` harness and autopilot patch
sources are pending in-game playtesting and may be absent from this commit;
native results below record local offline work. Use `Validate.ps1` without
`-ValidateNative` when those optional sources are not present.

`TorchNativeOracle` executes **actual shipped methods under bundled Mono**:
`FusionIC.Run`, `FusionIC.Fusion`, `Ship.GetMaxTorchThrust`,
`Ship.CalculateTorchFuelConsumption`, `FusionIC.MassUsageMax`, and
`SetReactants`/`RemoveFromTanks`. It compares the shared calculator model over
pair counts, wear, cycle settings, temperatures and masses. It checks module caps,
actual fuel tank debits, and the 25% pilot modifier.

Fixture adapters provide condition storage, module/tank discovery and ship mass;
skip Unity presentation, electrical billing, gradual wear and thrust application;
and suppress trigger-driven startup/shutdown while testing a running reactor.
The reactor math and tank-debit methods remain native. This validates algorithm
compatibility, not every game lifecycle or an actual Unity-rendered UI session.

Current validation: **4,096 native checks**, including the fuel-cache regression,
production course correction and save/reload/lifecycle checks,
and the [pre-patch course fuel study](../../Navigation/TORCH-FUEL-MODEL.md),
**24 standalone model checks**, clean
builds, and **38,853 browser comparisons/UI checks across 56 C# reference scenarios**.
Headless Chrome opens the actual HTML through `file:` and verifies rendered
default results, comparison rows, pair selection, safety toggling, full-tank/remaining-kg
modes, hardware-only mass additions and full new tank inventories. Browser checks compare every scalar, warning,
curve point and temperature sample with the native-validated C# model.

```powershell
dotnet run --project Tools/TorchCalculator -c Release -- --check
dotnet run --project Tools/TorchCalculator -c Release -- --browser-checks obj/torch-browser-checks.html
dotnet build Tools/TorchNativeOracle -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tools/TorchNativeOracle/bin/Release/netstandard2.1/TorchNativeOracle.dll
```

`Tools/TorchCalculator/Validate.ps1 -ValidateNative` runs the C# and native checks
and exports the browser test page to `obj/torch-browser-checks.html`. Open that
generated file to run the browser checks. Developer validation requires the .NET
8 SDK; native validation additionally requires the configured game installation,
using shared build path discovery and `OSTRANAUTS_DIR` when necessary. No game
binaries are distributed. Ordinary calculator usage needs only a browser.
