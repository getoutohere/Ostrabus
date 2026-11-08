# Venus aerostat dolly burden

Implemented in `DollyEncumbrance.cs`. Agreed multiplier: 0.10, including
the dolly's payload. Scope includes Venus aerostats, their sublevels and ships
physically connected to those stations; it excludes VORB and free-flying vessels.

## Native mechanism

Both SuperHandy (ItmDolly01) and HyperHandy (ItmDolly02), including damaged
variants, carry IsDolly. Their stored StatMass already includes nested contents.
CondOwner.GetTotalMass also adds stack members. Slots.SlotItem adds the item's
full GetTotalMass to the holder even for the drag slot. Draggable animates a
trailing object and marks IsDragging/IsDragged; it supplies no wheeled mass
allowance. The dolly's drag slot effect is Blank.

Encumbrance is written from aggregate character StatMass times ship.Gravity in
CondOwner.UpdateGravity, CondOwner.AddMass and the Ship.Gravity setter. The latter
also calls UpdateGravity, so changing only the final result can transiently apply
the full burden and its condition-rule effects. Correct the mass operand before
the encumbrance write at all three sites, rather than globally changing mass
reads or subtracting mass afterward. AddMass has another StatMass read for the
inventory information label; that read must stay physical mass.

Effective burden mass = actual character mass - 0.90 * supported loaded dolly mass.
Do not alter the character's unrelated mass or apply the multiplier separately
to each nested payload item. Do not rewrite saved StatMass, container accounting,
ship mass or item definitions. This avoids conflicts with massfix and repeated
discount/restoration errors on transit or reload.

## Geography and cost

Authored Venus aerostat levels have StationHasGrav. This includes Newcal/VNCA,
Cloudbreak/VCBR, Encantado/VENC and the VLA00–VLA05 Los Angeles construction levels.
VORB, its Habitation level and Aux do not have that station condition. Check an
actual Venus station/reference plus its gravity condition, not the ATC identity
or civilian faction. Docked vessels inherit eligibility from their real connected
station group; resolve apartment/sublevel references without treating transit
links as physical docking between unrelated stations.

The allowance covers directly handled dollies, normally in the drag slot, and
their complete nested payload. A dolly packed inside an unrelated carried
container gets no independent allowance; a dolly inside another handled dolly is
already included in that outer dolly's aggregate mass. Primary slot and parent
identity exclude duplicate secondary-hand aliases. Stacks include their members
with bounded traversal; malformed masses/stacks fall back to normal burden.
The aggregate-mass guard allows a discrepancy of at most
`max(0.000001 kg, characterMass * 1e-9)` and caps the discounted mass at the
character's actual carried mass. Independent sums can otherwise differ by a few
floating-point ulps after transfers, incorrectly disabling the entire allowance
when the dolly accounts for all carried inventory. Meaningful discrepancies
still fall back; neither stored mass is rewritten.

Existing raw slot and docking collections are read through cached field
delegates. The steady-state helper creates no lists, iterators, closures or
strings and never walks container contents. Work scales with the character's
slots and connected hulls, not the world's inventory. No per-frame callback,
persistent inventory references or custom save fields are added. Dock/undock
hooks refresh affected materialized crew; native transit, inventory mass changes
and gravity changes already recalculate burden through the patched entry points.

## Validation

`Tests/VenusDollyCheck` exercises the production patches and shipped assembly
under bundled Mono. It reproduces native full-mass burden first, then exercises
all three native encumbrance entry points, nested payload addition/removal,
ordinary containers, empty/loaded/multiple dollies, stacks, secondary-slot aliases,
malformed mass/stack fallback, actual station-body parent chains, apartments,
transit, connected ships, undocking and VORB/other-planet exclusions. It checks
every encumbrance write in the gravity setter to catch transient full-burden
condition updates, plus unchanged physical mass and native inventory labels.
The transfer-roundoff regression reproduces a persistent full-burden result from
ordinary native AddMass additions/removals before the guard correction. It also
checks gravity updates, unchanged raw masses, full-price unrelated gear and
continued rejection of meaningful discrepancies. This matches the reported
post-sale/re-drag symptom class; the exact historical sale was not traced.

Condition storage and Unity equality/wear effects use fixture boundaries.
Docking physics is replaced by a connection mutation; production lifecycle hooks
and native burden calculations execute. This is an offline behavioral test, not
a live Unity docking test or a measured allocation benchmark. The allowance uses
`IsDolly`, shared by SuperHandy, HyperHandy and their damaged variants, rather than
hard-coded item names. The global Harmony audit also binds all five targets.

```powershell
dotnet build Tests/VenusDollyCheck -c Release --nologo
if ($LASTEXITCODE -eq 0) {
    dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/VenusDollyCheck/bin/Release/netstandard2.1/VenusDollyCheck.dll
}
```
