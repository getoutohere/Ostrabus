# Bounty departure lifecycle

This slice adds the previously omitted native `BountyManager` entry point. It
does not replace or claim complete Navy/Police/Pirate combat modeling.

```powershell
dotnet run --project Tools/AIBehaviorGraph -c Release -- generate --model bounty
dotnet run --project Tools/AIBehaviorGraph -c Release -- scenario repeat-reboarding-is-not-rng-refusal --model bounty
```

Compare [vanilla](VANILLA.md), [effective](EFFECTIVE.md), and the
[conditional audit](AUDIT.md). The actual native test is separate:
[BountyDepartureCheck](../../../Tests/BountyDepartureCheck/README.md).
The slice passes 126 conditional scenario expectations and model safety checks.

The 0.7.1 review refreshed the preservation harness fingerprint after it began
calling the separate routine-retirement suite. All 174 bounty preservation
checks still pass. The added 26 retirement/population checks validate
`CrewRetirementPatch` separately; they do not expand this graph into a general
NPC retirement model or change its bounty-policy outcomes.

The model distinguishes:

- A native due `MoveTargets` selection, including custody/load eligibility, from
  region-entry restoration. RNG controls the 240-600 game-second interval and
  candidate order; an eligible, selected target does not face another stay/leave
  coin flip.
- Existing-hull activation from ordinary `CreateAIShip`. The former skips the
  latter's refuel in vanilla, even when the same NPC boards from a station.
- Immediate low-fuel home selection and successful station retirement, permitting
  repeated reboard/return cycles. Docking success is externally established, not
  calculated by the graph.
- Corrected station service from airborne/already-aboard restoration,
  materialized hulls, station-person moves and transfer exceptions.
- Adequate refilled propulsion from a full tank whose delta-v still fails native
  `LowFuel`. The patch does not force patrol or repair failed equipment.
- Rejected station arrivals that destroy a target, accepted arrivals that release
  its original hull claim, regional disposal of that hull, and stale removal while
  its captain is ashore. The separate [preservation policy](../../BOUNTY-PRESERVATION.md)
  preserves native transfer and reboarding; it does not override physical death,
  expiry, custody or successful hand-ins.

The graph previously stopped at selected ordinary traffic/role boundaries and
omitted this independent activation caller. See the
[repair and omission analysis](../../BOUNTY-DEPARTURES.md). This new slice closes
that specific gap; it does not establish whole-game completeness. Bound native
methods, selected custody/drop-off assets and the production source participate
in assembly revalidation. Numerical flight, complete bounty quest logic and the
deferred reserve-hull teleport behavior remain outside it.
