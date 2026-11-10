# Native stale-ship lifecycle reproduction

Investigation, game 1.0.1.10. This fixture installs **no production patches** and
does not attach to the running game.

```powershell
dotnet build Tests/StaleShipDiagnosis -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/StaleShipDiagnosis/bin/Release/netstandard2.1/StaleShipDiagnosis.dll
```

Runs shipped `AIShipManager.UnregisterShip`, `AIShip.FindShipForFaction`,
`AIShip.ResetShipState`, `AIShipManager.AddAIToDict`, `StarSystem.CleanupStaleShips`
and `StarSystem.TryRecycleCrew` under the game's bundled Mono runtime.

The sweep's Task.Run(Action) scheduling runs inline for a deterministic result;
Unity equality, logging and engine controls, condition storage and damage reset
are boundary substitutes. Native decisions, queue registration, reserve selection,
clock comparisons, station capacity and faction matching remain native. It does
not run Unity scene loading, actual engine controls or the full hull destruction
and crew relocation routines.

The cases reproduce:

- Unregistration stamps a retirement time; ordinary reserve selection, native
  reset and active re-registration do not remove it.
- A three-hour-old timestamp marks a registered ship with a living pilot for
  deletion. Clearing the completed retirement marker prevents it; legitimately
  retired hulls still remain eligible.
- Scalar marker ages from the pre-fix home save reproduce two one-hour skips:
  neither affected ship qualifies after the first, both qualify after the second.
  A newer hauler marker remains below the threshold.
- Docking postpones deletion but does not cancel the stale marker; detaching
  exposes it without requiring a new navigation failure or injury.
- Crew recycling returns false for a hull without factions, and for an unmatched
  hull faction, even with a living pilot and a station below population capacity.

Native `StarSystem.Update` disregards that failed recycling result and calls
`Ship.Destroy(isDespawning:false)`. Source inspection establishes that destruction
sets Kill on remaining human/robot COs. Live log evidence confirms the two pilot
deaths; the fixture observes the marking and recycling decisions rather than
claiming an end-to-end Unity destruction test.
