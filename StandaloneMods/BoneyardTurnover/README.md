# Boneyard Turnover 0.1.4

An optional standalone simulation mod for **Ostranauts 1.1.0.2/1.1.0.3 and BepInEx 5**.
It gradually replaces abandoned wrecks in the static regional boneyards while
the player is away. This is new gameplay policy, not a vanilla bug repair.
The implementation builds and passes offline native regression checks; live
playtesting is pending.

## Behavior

Leaving a supported yard's ATC region records its eligible wrecks. On returning,
each has a replacement probability of `1 - 2^(-days away / 7)` by default:

| Days outside the region | Expected turnover |
| --- | ---: |
| 1 | 9.4% |
| 7 | 50% |
| 14 | 75% |
| 28 | 93.75% |

Counts are randomly rounded without duplicate selections. Each removed wreck
gets one new wreck using that yard's current native template pool, damage,
factions, ownership and placement rules. Its anchor can be a manufactured
reference; it is not replaced with the nearest planet.

Supported definitions: Mercury/HQCH, Luna/EJDR, Phobos/MTRS, Ganymede/JFTS, Titan/SVIR, MSUZ
and Europa/JPTN, including their reviewed story variants. **Earth, OKLG, VORB
and any region with an authored wreck-hauler operation are excluded.**

Version 0.1.4 reads offset-field centres from the unique native facility authored
at that position, accepting both deferred and loaded forms. Missing, ambiguous,
claimed, reattached, radially displaced or moving facilities cause the field to
be retained. The reader does not reconstruct a historical centre from a rotating
ground station's current bearing. Ganymede/JFTS means Jupiter's moon; Ganymed,
the asteroid hosting OKLG, remains excluded.

The [saved-field model review](FIELD-MODEL-REVIEW.md) tests seven actual saved
fields. Mercury wrecks whose attachments were already lost remain excluded by
the runtime. A separate, explicitly prepared offline repair can restore known
historical placements from an older save; installing this plugin does not
automatically relocate ships or backdate their age.

Boarded or stripped wrecks remain eligible once genuinely shallow and abandoned.
Player/crew ownership, changed ownership, occupants, docking/tether groups,
active AI or navigation, other ships' targets, plot/bounty obligations and
currently loaded interiors protect a wreck. Wrecks outside their field or with
uncertain state are retained. Leaving player crew in the region prevents a
departure from starting an absence.

There is no vacancy refill, rare-part guarantee, install-time refresh or
continuous background simulation. Destroyed or towed-away wrecks are not replaced;
an emptied field stays empty. Remaining inside the region pauses turnover age.
Reloading does not trigger a replacement roll.

## Install and configure

Close the game and run `Install.cmd`. For a nonstandard installation:

```powershell
.\Install.cmd -GameDirectory 'D:\Games\Ostranauts'
```

The installer copies only `Ostrabus.BoneyardTurnover.dll` into
`BepInEx/plugins/OstrabusBoneyardTurnover/`. It preserves existing configuration,
backs up an earlier copy, and refuses unreviewed game assemblies.

On first launch BepInEx creates
`BepInEx/config/com.ostrabus.boneyardturnover.cfg`:

```ini
[Turnover]
Enabled = true
HalfLifeDays = 7
CurveShape = 1
```

`HalfLifeDays` and `CurveShape` must be finite and positive. Shape `1` is the
exponential curve above; below `1` favors earlier turnover with a longer tail;
above `1` delays early turnover then increases it more sharply. Survivors retain
accumulated absent age, so repeated short trips preserve the survival curve.
Curve settings are captured at departure; edits affect later departures.
Invalid values suspend new departure tracking. Disabling clears tracking.
If extreme settings produce indeterminate curve arithmetic, that field is
retained and a diagnostic is logged.

The mod works independently of Ostrabus. Full/custom Ostrabus installation does
not install this plugin, and its installer does not change Ostrabus or vanilla
assets. It uses the native region boundary during ordinary flight, SFF and
shuttle travel; it adds no separate timeskip simulation.

## Saving and removal

Version 0.1.3 fixes visited wrecks being rejected because native shallow NPC
spawn markers were mistaken for occupants. Saved actual crew and native
person records remain protected, as do ordinary loaded items and orphaned
markers. Authored field geometry and native reference selection are unchanged.
Spawner overlays are recognized through the native base-definition resolver.
Departure counts and reasons for region-level refusals are logged once per
transition. No continuous tracing or new save format is required. Absences
that were never captured cannot be reconstructed by this update.

Version 0.1.1 fixes departure tracking being discarded on reload when native
JSON rounds the game clock differently from the optional metadata. It compares
the native serialized timestamps and accepts existing 0.1.0 metadata without
rewriting your save or changing the metadata format. Absences already lost from
later saves are not reconstructed.

Run `Uninstall.cmd` with the game closed. Wrecks already generated remain normal
native objects and saves remain loadable without this plugin.

Only departure bookkeeping uses an optional ZIP entry,
`boneyard-turnover.state.json`. It contains current tracked wrecks, their absent
ages, captured settings and save identity, not a visit history. It shares the
native save commit and travels with the archive. Native loading ignores it;
the next save without the plugin omits it. Missing, corrupt or mismatched
metadata starts fresh tracking at a future departure, without guessed catch-up.

## Build and checks

From the repository root:

```powershell
dotnet build StandaloneMods/BoneyardTurnover -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File StandaloneMods/BoneyardTurnover/Package.ps1
```

The package is written under `dist/`. No live game installation happens during
build or packaging. See [validation](VALIDATION.md) for evidence and test limits.
The repository's `StandaloneMods/BoneyardTurnover/` directory also contains
`PROPOSAL.md`, `IMPLEMENTATION-STUDY.md` and `RISK-REVIEW.md` for design rationale
and native code findings.
