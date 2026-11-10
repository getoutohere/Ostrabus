# Fuel depletion investigation: Host Terrapin

The player reports repeatedly finding scavengers empty after long derelict visits,
including before Ostrabus navigation/SFF patches. For the currently selected ship,
the player explicitly reports never docking or tethering to it. That history makes
vanilla accounting a stronger lead than assuming legitimate arrival on empty.

Read-only live inspection on 2026-11-09 identified O-KA2, **Host Terrapin**:
registered Scav, region OKLG, home OKLG_FLOT, shallow-loaded, Main[4]/Hold,
0 / 749.364694715254 kg RCS. It is attached to O-7X1, with no active route, target
or thrust. Inspection did not issue recovery, refueling or navigation commands.

All eight retained saves containing O-KA2 (`fine`, `build`, `autosave_3_build`,
`undock`, `pda` and its three autosaves) already record zero fuel and the same
derelict attachment. Their first/last-visit timestamps are zero. These observations
support the player's account that boarding didn't trigger the current condition;
they don't provide a before/after record of its original depletion.

The retained Main/FlyTo payload has an 81.3266869411666 kg trip budget and progress
0.334719555658537. That cached payload is not a complete ledger of earlier flights,
planned-route spending or prior modes. It cannot establish where all fuel went.

## Confirmed accounting defect, historical attribution still open

[Native mixed-accounting reproduction](../../../../Tests/ScavFuelDiagnosis/FUEL-ACCOUNTING.md)
shows planned and manual AI navigation independently debit overlapping progress.
The actual FlyTo parent chooses between them based on target lock and collision
results. A manual/planned/manual sequence can drain positive remaining fuel to zero
at the handoff without another movement. It needs no player boarding, save/load,
SFF or Ostrabus patch.

This is a stronger finding than the Hold recovery gap alone. It is a viable vanilla
depletion mechanism, but it has not been attributed to O-KA2. Preserve that
distinction when extending the graph or designing a fix. The Hold scenarios begin
with depletion as an input. A separate accounting witness now represents the
native reproduction and the production handoff fix, under explicit fixture
preconditions; it does not claim attribution to this ship.

Hold itself doesn't subtract gas; it commands zero maneuver. Materialization's
SyncFuel can reconcile physical tanks downward to the shallow budget, but that
doesn't explain how this never-boarded shallow ship's budget first reached zero.
The two accounting mechanisms need one consistent ownership/progress contract;
simply refilling the ship or adding emergency undocking would not prevent that
duplicate debit.
