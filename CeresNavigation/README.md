# Ceres navigation guardrails

Returns now run at the native ordinary-mission branch, after queued/combat/threat
decisions; see [return scheduling](../Navigation/RETURN-PRIORITY.md).

The Ceres policy covers CCRE, GalileanConfederacy and BeltPirates. Shared defect
fixes live in `Navigation/` (`Ostrabus.ShipNavigation`) and also serve
boarding returns and other factions where the same native defect applies.

- General Navy candidate filtering runs before native selection, closing both
  the single-candidate and all-rejected fuel bypasses. Native generation, ordering
  and random preference remain. Ceres candidates additionally require a 20%
  outbound/home delta-v reserve; affordable alternatives remain available.
- Ceres Navy, Pirate and Police roles check reserve during AI updates every 30
  simulation seconds, including short-range combat chasing. Cargo/civilian roles
  are excluded from this local patrol policy. Valid usable-torch plans retain
  native handling. Player helm, player-connected ships and active reactions retain
  authority over the shared return controller.
- Existing RCS plans are checked against remaining planned fuel use plus the
  native fuel estimate for returning from the predicted arrival state, with a 20%
  margin. Fuel already spent is not counted twice; refills do not increase the
  predicted remaining cost. No second planner is run. The homeward estimate is
  still a native heuristic, not a fully planned return course. Native target
  generation also rejects an unsafe completed plan and selects home on its next
  retry instead of exhausting retries on patrols.
  Home motion uses the shared pure NativeEphemeris evaluator; reserve prediction
  neither advances shared BodyOrbit caches nor depends on the Orbital toggle.
- Home is the available assigned station owned by the same faction, otherwise
  the nearest available faction station. No enemy-station fallback is invented.
- Returns persist a station registration ID and docking-stage boolean in native
  Blackboard data. `StationReturn` drives native FlyTo and Dock leaves directly
  at native AI cadence; it does not queue a custom sequence. Running, completion,
  failed approach/docking retry and reload are separate states. Native docking
  clearance and DockAndDespawn remain authoritative. Connected groups are not
  steered, and unrelated queued commands are retained.
- Old Ceres return flags migrate on the next eligible AI update; old boarding
  flags migrate through that module's existing return monitor. Legacy custom
  return payloads are removed during native queue loading so they cannot leave
  stray FlyTo leaves. Only those two known obsolete subtrees are filtered.
- General target clearing/semantic retargeting removes old TripFuelPrediction.
  Equivalent target replacement/reapplication preserves valid predictions, using
  the same value identity as the VORB guard. Native invalid/negative RCS delta-v
  is normalized to zero, closing NaN comparison bypasses throughout native logic.
- Runtime timing/command caches use weak AI keys, with no ship-ID history growth.
  Normal idle hooks allocate zero bytes in the warmed offline test. Home lookup,
  plan cost calculation and prediction occur after the reserve throttle.

No new console/config controls are required. Persisted fields are native strings
and booleans, so removing the mod requires no custom save types. Without the mod,
its return intent is ignored and vanilla behavior resumes.

`pruneceres` retains its explicit, destructive cleanup policy: shallow non-derelict
faction AI ships must have entirely pruneable crew and no player connection.
The first pass considers inability to return on RCS unless usable torch propulsion
exists; the second considers ships over 200,000 km from home lost regardless of
fuel. Distances are from home, never the player. This policy is not automatic
navigation correction and was not executed during offline testing.

Validation: 79 Ceres/navigation repair checks, 134 native lifecycle/replacement
checks, 98 navigation-recovery checks, and 4,850 orbital checks pass. The repair
host runs native candidate
selection, AI dispatch, Blackboard/queue serialization and RCS fuel arithmetic;
world assets and flight/docking outcomes are fixture boundaries. This validates
control flow and compatibility without claiming a measured live Ceres outcome.
Navigation inspection includes `stationReturn.destinationShipId` and `docking`.
Detailed repair output is in `obj/CERES-REPAIR-CHECK.txt`.

## Historical audit — November 6

The original module is a partial guardrail, not a verified solution to Ceres
navigation. These findings describe the original implementation before the repairs above. The initial
`Tests/AiPlannerDiagnosis/CeresAuditChecks.cs` runs 33 reproduction/control checks
under the game's bundled Mono. The original assertions confirmed the defects;
they have since been replaced with regression assertions for corrected behavior.

### Confirmed defects in the original integration

- **Return sequence loses docking.** `QueueReturnHome` puts a two-step
  `StickySequenceCommand` into `QueuedCommandsNode`. Native sticky sequences return
  success after each successful child; the queue interprets that success as
  completion of the entire queued command and calls OnExit, resetting the sequence.
  Reproduced with the actual production return sequence and native queue: FlyTo
  succeeds, Dock never runs, the queue becomes empty, and the return flag remains.
  Native navigation/docking outcomes are fixture inputs; queue progression is real.
- **Return persistence is broken.** Native serialization preserves the namespaced
  boolean but cannot reconstruct the custom `CeresNavigationReturn` sequence.
  A queued return reloads as an isolated FlyTo leaf; an active return reloads with
  no command. Neither restores the docking step. The in-flight prefix trusts the
  surviving flag and suppresses a replacement order. Verified with native
  GetSaveData/LoadSaveData and Blackboard.GetJson/LoadSavedData, then the production
  prefix. Even without reload, another urgent command can discard currentCommand
  while leaving the suppression flag. The latter follows native source inspection.
- **Old fuel prediction survives retargeting.** ClearShipTarget clears target fields,
  not Blackboard.TripFuelPrediction. QueueReturnHome installs a fresh FlyTo without
  invalidating that old prediction. Native FetchFuelData/ApplyTripFuel on this new
  command consumed the previous patrol's budget: 50 kg at half progress against an
  old 100 kg estimate. This affects manual-flight accounting; planned NavData uses
  its separate fuel data. Reproduction runs native fuel removal on a shallow ship.
- **Invalid capability fails open.** Native DeltaVRemainingRCS is
  `RCSAccelMax * fuel / .728 / thrusterCount`. A ship with zero thrusters produces
  NaN; both the patrol reserve wrapper and native BingoFuelCheck allow the target.
  Reproduced with native getter arithmetic. CannotReturnWithRCS already identifies
  that same ship as stranded, so prevention and cleanup disagree.

### Coverage and policy limits

- **Short-range combat bypasses our reserve monitor.** KeepWithinFiringDistance
  calls LowFuelRetreat and steers directly with KeepRange, rather than FlyTo.
  Native LowFuel tests below 25% tank capacity or delta-v below four times maxSpeed;
  it does not test home-return reserve. A controlled native LowFuelRetreat test
  continued combat with 75% fuel and delta-v above its speed threshold while
  BingoFuelCheck already rejected the home trip. KeepAtDistance *does* use FlyTo;
  do not generalize the coverage gap to every combat path. Adding a fuel-aware
  tactical retreat would change combat policy and requires an explicit design decision.
- **Fuel estimate is a heuristic.** Native GetDeltaVNeededToTargetFullTrip returns
  `2 * maxSpeed + relativeVelocityMagnitude`, or just `2 * maxSpeed` for an
  interregional flight with the reactor running. Units are AU/second. It does not
  evaluate distance, travel time, gravity losses, detours or actual planned fuel.
  Equal-cost near/far co-moving destinations were verified. Distance independence
  alone is not erroneous in an ideal accelerate/coast/brake model; it means the
  guardrail is not proof that an actual orbital trip retains sufficient fuel.
- **Patrol policy works within those limits.** The finite round-trip threshold
  and equality boundary are correct; healthy-budget controls preserve the selected
  target. Faction scope correctly includes CCRE, GalileanConfederacy and BeltPirates
  and excludes an unrelated owner. Native Navy.GetTarget's single-candidate bypass
  and all-rejected fallback were independently reproduced; our postfix catches
  their finite-budget failures, but only after native candidate selection, potentially
  sending a ship home instead of considering an affordable alternative.
- **Not every faction role uses Navy.GetTarget.** PiratePilot and cargo/civilian
  roles do not receive that selection filter. The FlyTo prefix applies by ownership
  rather than AI role and has no torch-journey exemption. This warrants role/route
  controls before expanding prevention. Pruning's separate usable-torch exemption
  was verified; it is not shared by the navigation predicates.
- **Lifecycle/performance weaknesses by inspection.** FindHome/AI lookup occurs
  before the 30-second throttle, so that interval only limits the fuel computation.
  ID caches are cleared on scene unload or explicit pruneceres removal, not ordinary
  AI despawn. They contain no strong ship/crew references, but retain strings until
  reset. Assigned home wins over nearest friendly station; fallback can select a
  station outside the faction. The 200,000 km pruning pass is user-chosen cleanup
  policy, not evidence of an impossible route. No ships were pruned during this audit.

Recommended order: repair return command progression/persistence and invalidate
obsolete trip fuel on retarget; handle invalid propulsion explicitly; then correct
native candidate rejection at selection and validate the reserve against real
planned legs. Keep the agreed reserve/cleanup policies distinct from these defects.
Historical Ceres stranding cannot be attributed to any single finding from this
offline audit; target-lifecycle and orbital fixes added since then also matter.

```powershell
dotnet build Tests/AiPlannerDiagnosis/AiPlannerDiagnosis.csproj -c Release --nologo
if ($LASTEXITCODE -eq 0) {
    dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/AiPlannerDiagnosis/bin/Release/netstandard2.1/AiPlannerDiagnosis.dll ceres
}
```

Run log: `obj/CERES-AUDIT-CHECK.txt`.

