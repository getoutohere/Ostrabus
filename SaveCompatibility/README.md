# Save forward compatibility

The `save-forward-compatibility` custom-build feature restores missing propulsion
metadata when a cached hull loads in Ostranauts 1.1. It is separate from the
planned migration of world content, environments and boneyards.

## Missing RCS caches

Native `Ship.InitShip` copies `JsonShip.nRCSCountRaw` and `nRCSExhaustV` into its
shallow runtime caches. Older saves lack them. A hull can retain its weighted
thrust, fuel and planning capability while native `Ship.Maneuver` refuses RCS
commands because exhaust is zero. Full native materialization rebuilds these
fields from parts, but distant hulls can remain shallow indefinitely.

The prefix applies only to a real transition into `Shallow`, with positive finite
cached thrust and both new fields zero. It reconstructs the two missing fields
before native loading copies them. Valid or partially populated new caches,
full/edit loading, fuel, orders, ownership and existing weighted thrust remain
under native control. Native `StarSystem.Init` restores saved hulls with
`bTemplateOnly=false`, including unvisited cached blueprints. Their persisted
`fLastVisit==0` permits definition fallback when no per-part record exists.
Previously materialized hulls still require saved part records. The adapter uses
JSON visit state because native loading has not yet copied it into `Ship`.

The reconstruction uses this hull's item layout and saved CO records, with direct
lookups into the already loaded global record dictionary. It reads native CO
definitions through `GetCondOwnerDef`, including overlay fallback, and follows
native saved `DEFAULT` ordering and item condition overrides. It neither scans
every person nor materializes every ship. Normal saves retain the restored values
in native 1.1 fields, with no custom save identifiers or mod-dependent format.

## Authored rules and damage

Native `TIsRCSClusterAudioEmitter` currently requires installed RCS hardware and
forbids `IsOff`; `Ship.AddICO` also requires the special-item marker. The native
trigger evaluator needs a scene `CondOwner`. This data adapter supports only its
deterministic presence/absence AND subset, reads the loaded asset's actual arrays,
and rejects unsupported rule shapes. It does not define new AI decisions.
Native-evaluator comparisons cover the adapter's eligibility semantics.

Raw count, weighted strength and summed exhaust are distinct. Each part contributes
its actual native strength/exhaust condition, or the native fallback of one.
Fully destroyed parts are excluded using native `Ship.IsItemDestroyed` and the
health caches prepared by native asset loading. Cold health caches are rejected
instead of expanding loot during migration. Native force-loaded RCS parts retain
their reconstruction path, avoiding double-counting them after cache restoration.

Native shallow combat can reduce the existing thrust counter without replacing
every layout entry with its damaged variant. The adapter preserves that reduction.
Only a verified homogeneous package with damage evidence and exactly one possible
count matching native floating-point accumulation permits an inverse derivation.
Mixed or unexplained inconsistent packages are rejected, rather than healed or
assigned an arbitrary number of working engines.

The adapter accepts deterministic saved condition values and does not reroll
loot, conditions or damage. Even native `Loot.ParseCondEquation` reads RNG on a
first parse, so it is not used here. Work occurs at load, with no per-frame ship
or population sweep. Failed reconstruction logs its reason and leaves the input
unchanged.

## Validation

The [native harness](../Tests/SaveForwardCompatibilityCheck/README.md) compares
eligibility with the shipped evaluator, exercises damage, mixed coefficients,
native maneuver refusal/recovery and preservation controls, and checks for RNG
reads. The untouched 1.0 save supplies 180 affected hulls; all pass derivation and
the production prefix with native restore arguments (472 checks). A later pre-SFF
quicksave supplies 97 affected hulls and passes 389 checks. Existing weighted
thrust is unchanged. Derivation took approximately one second for the older
sample and 0.7 seconds for the later sample, excluding fixture parsing and the
separate prefix comparison. These are sample measurements, not loading-time
guarantees.

The first live bridge candidate rejected most unvisited hulls because it used
the load argument alone to decide definition fallback. The regression now
reproduces that failure with `bTemplateOnly=false`; persisted visit state fixes
the distinction without instantiating or restoring lost parts.

Live inspection exposes `navigation.propulsion.rawRcsThrusterCount` and
`rcsExhaustSum` alongside weighted `rcsCount`. Live reload verifies 180 restorations
without rejection, and the next native quicksave retains both new fields on all
180 powered hulls. The two-hour SFF and normal command resumption also pass the
recorded bridge checks. A recovered engine cache alone does not establish that
every route, orbit or disposal was correct; see the
[compatibility audit](../Build/COMPATIBILITY-1.1.0.0.md) for evidence and its boundaries.
