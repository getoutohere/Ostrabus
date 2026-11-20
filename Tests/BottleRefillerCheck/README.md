# Bottle Refiller regression fixture

See [the standalone validation record](../../StandaloneMods/BottleRefiller/VALIDATION.md)
for coverage, commands and scene boundaries. `Program.cs` exercises the production
adapter with native gas recalculation; `MathChecks.cs` stresses the independent
planner with seeded inputs; `IntegrationChecks.cs` binds the real single hook,
executes native controls and inventory fitting, and checks source geometry and
preview exclusion. `CurveChecks.cs` runs the specified pressure timeline through
native gas stepping; `AudioChecks.cs` tests active/idle audio lifecycle at explicit
engine boundaries. `Test-Art.ps1` checks native pixel dimensions, alpha and exposed
output/packaging. `Test-Installer.ps1` installs the real package in an isolated
workspace fixture and tests stock composition, removal and rollback.

The fixture never attaches to the running game, loads the player's save or
installs files in the real game directory.
