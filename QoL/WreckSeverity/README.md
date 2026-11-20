# Wreck damage severity

An optional Ostrabus QoL feature that softens initial wreck damage across salvage
fields and hauler deliveries. It is an intentional gameplay adjustment, not a
vanilla bug repair. No standalone plugin is required.

## Configuration

The normal Ostrabus BepInEx CFG contains:

```ini
[QoL.WreckSeverity]
SeverityReduction = 0.25
LogDamageRolls = false
```

Zero preserves native severity. Values must be finite, at least zero and less
than one; invalid values disable the adjustment and log a warning. Edit the CFG
with the game closed, then launch normally. There is no additional PDA control.

Set `LogDamageRolls = true` and restart to print four diagnostic lines per newly
initialized wreck in the player's debug console and the BepInEx log. They show:

- Original severity, the curve's result and the final value after native bonus
  handling, with the reduction setting, region, ship name and grid dimensions.
- Actual hole count and radius range, components selected for hole/high-value/
  regional removal, and the active selection chances.
- General and high-value wear maxima, actual ray-call count, and the total
  incoming environmental/blunt/cutting damage budgets.
- Each ray's strength and its three actual randomized damage budgets.

The same flag also prints `[WreckBonus]` events when native code awards the
bonus, including game epoch, actor, ship and bonus count before/after. Beat
Manager awards identify `BeatManager.BonusDerelict`, its roll and threshold,
whether the roll is negative, and the preceding plot/videotape results and roll
changes. Interaction awards identify the actual interaction, loot table and
condition trigger, including playing with both dinosaur toys. The logger does
not evaluate another trigger or draw another random number to find the cause.

When `Ship.BreakIn` starts with a pending bonus, a separate initialization line
reports the bonus count and severity before/after native handling. This also
covers Used/Damaged ships and developer re-damage calls which can consume the
native bonus, without applying our severity curve to those excluded cases.
`count-cleared` and `condition-present-after` distinguish clearing the count
from actually removing the native condition. Awards before this DLL was loaded
cannot be reconstructed.

Ray budgets are measured before intersections and absorption, so they are not
final hull damage or a percentage destroyed. Wear values are native maxima,
not measured cumulative wear. Selection counts describe objects selected for
removal. Native bonus handling can further reduce the severity after the curve.
Rays that return before rolling damage are labeled unavailable; interrupted
initializations are identified. At most 32 individual ray details are retained
for one initialization; totals still include all fully observed rays.

The diagnostic makes no extra random draws. Its context is released when the
initialization finishes or throws; there is no per-ship history, polling or new
save data. With the flag off, no ray-roll, bonus-source or initialization call-site
instrumentation is installed. Restart after changing this flag.

For original severity `s` and setting `r`:

```text
adjusted = s * (1 - r) / (1 - r + r * (3.5*s - 2.5*s*s))
```

The default curve is continuous, preserves roll order and keeps positive rolls
positive. It gives more relief to heavily degraded wrecks while improving
already favorable rolls by smaller absolute amounts:

| Original severity | Adjusted at 0.25 |
| --- | --- |
| 0.10 | 0.090 |
| 0.20 | 0.167 |
| 0.40 | 0.300 |
| 0.50 | 0.364 |
| 0.60 | 0.429 |
| 0.80 | 0.571 |
| 1.00 | 0.750 |

Severity feeds several native damage passes; these numbers are not percentages
of hull damage or guaranteed salvage yield. Regional item exclusions, room loot,
damage-ray targeting and native bonus rewards remain authoritative. The curve
is applied before the existing bonus modifier. Unknown original severities
outside the reviewed 0-1 range are retained.

## Existing saves and other features

The adjustment applies when native code first generates a wreck's damage,
including saved unvisited wrecks. Already initialized wrecks are not repaired.
Native initialization flags prevent repeated adjustment on ordinary reloads or
shallow/full transitions. Asteroids, used/damaged operational ships and repeat
developer damage commands on initialized hulls are excluded.

The adjusted severity and physical wreck use ordinary native save data; no extra
save entries or processed-ship registry are added. Removing or disabling the
feature leaves existing wrecks valid and affects future initializations only.
Native pricing can reflect the adjusted severity; existing quotes are not
forcibly recalculated.

The custom builder offers `wreck-severity` in the QoL category. It patches wreck
initialization, with one additional read-only damage-roll observer only when
logging is enabled, plus two optional native bonus-source observers. It has no
frame callbacks, native asset overrides or feature dependencies.
It works with the separately installed Boneyard Turnover mod: replacements still
roll and spawn normally, then receive this curve when first initialized.

## Validation

The full plugin builds against Ostranauts 1.1.0.4. Offline checks cover the agreed
curve anchors, monotonicity and positive-roll preservation across the setting
range, invalid settings, initialization scope, native field/caller contracts and
Harmony binding, enabled/disabled diagnostic installation, unchanged native random
draws/results, channel budget arithmetic, formatting and nested/failed scope cleanup.
Bonus-source A/B checks also cover native release selection, negative-roll
fallback, the shipped dinosaur loot, repeated/rejected awards, cooldown,
logging failures and consumption reporting outside the curve's eligibility.
The ray fixture replaces Unity ray geometry while calling native damage rolls;
the native channel order is independently checked in the assembly. These checks
do not generate a rendered wreck or measure final salvage
yield in Unity. See [implementation](IMPLEMENTATION.md) for the layer audit.

```powershell
dotnet build Tests/WreckSeverityCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/WreckSeverityCheck/bin/Release/netstandard2.1/WreckSeverityCheck.dll
```
