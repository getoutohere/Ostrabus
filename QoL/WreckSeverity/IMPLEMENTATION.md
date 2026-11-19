# Wreck Severity: implementation findings

Status: implemented inside Ostrabus as a QoL feature; no standalone plugin.
Reviewed native versions: 1.0.1.10 and 1.1.0.2; diagnostic call sites reviewed on 1.1.0.3.
This is an optional gameplay adjustment, not a vanilla bug repair.

## Accepted curve

The original flat-subtraction specification has been superseded. The clarified
goal is to compress the worst rolls while leaving favorable rolls nearly intact.
The earlier example of 0.20 becoming 0.10 is not a good fit for that goal.

The severity setting is `[QoL.WreckSeverity] SeverityReduction`, default `0.25`.
`LogDamageRolls`, default `false`, additionally enables optional initialization diagnostics.
The accepted revision additionally maps 0.40 to 0.30 while retaining 1.00 to 0.75.
For original severity `s` and configured reduction `r`:

```text
effective = s * (1 - r) / (1 - r + r * (3.5*s - 2.5*s*s))
```

For native severities 0 through 1 and finite settings `0 <= r < 1`, the mapping is
continuous and increasing, preserves
zero, keeps every positive roll positive, and never raises severity. Zero
reduction gives the native roll; maximum original severity becomes `1 - r`.
Setting 1 would flatten everything to zero and must not be accepted under the
goal of preserving positive rolls. Invalid values should retain native behavior
with a useful diagnostic.

| Original severity | Effective severity with reduction 0.25 |
| --- | --- |
| 0.00 | 0.00 |
| 0.10 | 0.0902 |
| 0.20 | 0.1667 |
| 0.40 | 0.3000 |
| 0.50 | 0.3636 |
| 0.60 | 0.4286 |
| 0.80 | 0.5714 |
| 1.00 | 0.75 |

The mapping is deterministic and makes no extra random draw. The native
spawn-distance policy still supplies the original roll. Changes in severity are
not measurements of final physical damage or salvage yield.

## Why JSON alone cannot express the rule

`JsonSpawnDerelict` exposes geometry, pools, ownership and `enumBreakInMode`.
It has no severity curve, expression, or configurable global transform.
`EnvironmentEntities.CalculateBreakInMultiplier` calculates the field roll in
C#. `HaulerDeployingPilot.SpawnDerelict` independently assigns a 0.10-0.99 roll.
Template `JsonShip.fBreakInMultiplier` values do not override these assignments.

The native `TIsDerelictSafe` and `TIsDerelictSalvage` triggers select components;
`AModeDerelictBreakIn` controls ray damage. Altering these assets changes only
particular damage passes and cannot apply the requested common severity rule.
The inspected C# writers contain no condition-trigger setter for this field.
An asset-only approximation would require different behavior and multiple asset
overrides, including regional forbidden-component policy that should stay native.

## Smallest integration

Use one Harmony prefix on the parameterless instance method `Ship.BreakIn()`.
At entry, adjust `fBreakInMultiplier` only for an actual wreck undergoing its
initial damage generation: native `bPrefill` is true, damage status is Derelict,
and the object is not classified as an asteroid. Do not include Used or Damaged
ships merely because they share this method. Keep native tutorial initialization
on its separate path.

This location covers saved unvisited wrecks, newly generated environment wrecks,
hauler deliveries and Boneyard Turnover replacements without patching their
individual spawn or placement methods. It also avoids changing asteroid layouts,
routine wear, combat damage, and developer re-damage commands on initialized hulls.

Apply the reduction before the method's existing bonus handling, so the rule is
based on the original roll. Native bonus consumption and its multiplier remain
in the original method. All deletion, wear, ray targeting and regional exclusion
evaluators continue to run normally using the adjusted severity.

Retain the adjusted value in the ordinary native field. Do not restore it after
the method, which could undo native bonus mutation and disagree with native
valuation. New native valuations can reflect the reduced severity; do not force
existing quotes to change.

## Persistence and compatibility

Native `Ship.InitShip` gates damage generation with `bPrefill` and clears that
flag afterward. Both that flag and severity are already serialized. Thus ordinary
save/reload and shallow/full transitions do not repeatedly subtract the setting.
Already initialized wrecks are not repaired. Existing unvisited wrecks receive
the current setting when native damage initialization first runs.

No custom save records, conditions, ship-ID registry, per-frame work or world
scan is needed. Removing the plugin leaves ordinary native wrecks and save data.
This is part of Ostrabus and is independently selectable in its custom builder.
The standalone Boneyard Turnover plugin is not a dependency.

Prefer a method prefix over an IL rewrite or patches to each severity writer.
Validate the target signature and required field types before patching; skip
installation with a clear error if they do not bind. Avoid requiring an exact
whole-assembly hash for unrelated game changes, while declaring only versions
actually reviewed/tested. Signature compatibility does not prove future semantic
compatibility; recheck the caller's one-time initialization contract after updates.

### Optional damage logging

The curve remains the existing prefix. Only when `LogDamageRolls=true` at startup,
its same `Ship.BreakIn` patch also substitutes observing wrappers for the native
hole-zone, selection, wear and random-ray calls. Every wrapper invokes its native
callee once with the original arguments and returns its original result. Labels
and exception blocks are preserved. Unexpected call-site counts retain the
original IL and disable logging while preserving the severity adjustment.

One additional startup-conditional postfix observes `JsonAttackMode.GetDmgAmount`.
Its returned value is untouched. A thread-local scope opened only for eligible
initial derelict generation, and a nested scope for its current random ray, admit
only the matching mode's three null-attacker draws. Native `DamageSystem.DamageRay`
draws environmental, blunt and cutting amounts separately; the observer records
those results times the actual attack-mode coefficient and ray strength. It never
calls `GetDmgAmount` or any random method itself. Startup validates the three native
calls and their coefficient order. Combat outside this scope produces no report
or retained record. With the option off, the observer is not patched and BreakIn
retains its original body.

Reports distinguish original/curve/native-final severity, actual selection
chances and selected objects, wear maxima, ray calls and incoming random damage
budgets. Geometry and damage absorption remain native and are not reported as
measured final hull loss. Only 32 ray-detail records are retained within one
initialization; aggregate counts/budgets include further rays. Finalization restores
any enclosing scope even on native exceptions and never suppresses those failures.
Reports use `ConsoleToGUI.LogInfo` to reach the player regardless of the ordinary
Unity-log visibility setting, and are also written to BepInEx's log.

## Implementation acceptance checks

- Curve anchors, adjacent floats, zero reduction, settings approaching 1 and
  invalid values; no negative result, inversion of roll order, or increase over
  the original roll. Positive native rolls must remain positive.
- Native initial-damage callers in both reviewed versions; no adjustment for
  Used/Damaged ships, asteroids or already initialized developer re-damage calls.
- Field and hauler paths converge here with their original roll intact.
- Native bonus follows the reduction and is consumed once.
- A normal first initialization followed by serialization/reload does not repeat
  the reduction; existing visited wrecks retain their physical state.
- No new random draw and no replacement of the damage routine or asset policy.
- Full Ostrabus and independently selected QoL custom builds contain the feature
  and its two CFG entries; bugfix-only builds omit it.

Native source landmarks: `Ship.InitShip`, `Ship.BreakIn`, `Ship.SetDerelictValue`,
`SolarSystem.EnvironmentEntities.CalculateBreakInMultiplier`,
`SolarSystem.EnvironmentEntities.SpawnShipFromStellarObject`,
`Ostranauts.Ships.AIPilots.HaulerDeployingPilot.SpawnDerelict`, `JsonSpawnDerelict`.
