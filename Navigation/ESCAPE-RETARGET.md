# Escape destination handoff

`EscapeTargetPatch` repairs native `TargetEscapeStation.SetTarget`. The original
two-field setter changes scan/situation targets but leaves the old plotted route,
manual trip prediction and target metadata. An already-free ship skips Undock,
then FlyToPath accepts its old plan even though the escape destination changed.
It can continue away from safety until that plan expires.

The prefix replaces only that setter. Native RunCommand still owns combatant
eligibility, regional-station selection, geometry, fallback and command results.
The repair applies the selected station using native TargetData.ApplyToShip;
the existing target observer clears obsolete manual predictions. It compares the
existing semantic target key and resets NavData only when the destination changes.
Equivalent reapplication preserves the current route and reuses coherent
TargetData instead of allocating another object.

Police, scavenger, civilian and pirate trees use this escape command. Navy and
blockade retreat use TargetNearestStation, whose native route reset is already
correct; the new hook is not involved. Player autopilot and ordinary cargo
plotting are not changed. Route endpoints are not compared to final destinations:
departure/detour/approach legs can legitimately end elsewhere.

This is an always-active C# integrity fix in `Ostrabus.ShipNavigation`. It adds
one hook at escape assignment, no polling, saved keys, state table, retained ship
references, fuel grant, forced arrival or new retreat policy. It preserves vessel
momentum. An existing save already past escape selection is not scanned or
migrated; native future selection/plan expiry remains responsible for that flight.

## Validation

`Tests/ScavFuelDiagnosis`, mode `retarget`, executes 60 assertions under bundled
Mono. Unpatched and observer-only runs retain the original failure. The repaired
native police tree clears obsolete state; equivalent and failed selection controls
preserve valid flight. Shared escape leaves are checked for all four pilot roles;
actual Navy/Blockade retreat selection clears routes without calling the new hook.
Cargo intermediate legs, player courses, native station/emergency selection and
manual-flight cleanup retain their existing behavior.

The unchanged-assignment production body plus patched ApplyToShip allocates zero
bytes over 1,000 warmed calls. Native station selection's own temporary lists are
outside that measurement. Whole police dispatch/sequence, combat initiation,
escape selection and NavData advance execute; sensor/health/weapon availability,
world geometry and the existing/replacement plans are prescribed boundaries.
This is not new live-game or full physical weapon/planner validation.

Build the Debug plugin, then the test project in Release; invoke its DLL through
`Tests/SaveJsonCompatibility/Host` with argument `retarget`. Full reproduction and
caller distinctions are retained in the [audit](../Notes/AI-RETARGET-CALLER-AUDIT.md).
