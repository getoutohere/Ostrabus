# Shared target lifetime and interacting actors

This model connects a scavenger's outbound flight to another actor's retrieval,
return and wreck disposal. Vanilla retains the O-ZY3A failure. The current
overlay now repairs permanent removal and missing saved destinations, supported
by native production-hook tests rather than an invented successful outcome.

See the [investigation and native reproduction](../Scavenger/TARGET-RETIREMENT-INVESTIGATION.md),
[native diagram](VANILLA.md), [effective diagram](EFFECTIVE.md) and
[scenario audit](AUDIT.md). JSON is authoritative; diagrams are generated.

```powershell
dotnet run --project Tools/AIBehaviorGraph -c Release -- validate --model target-lifecycle
dotnet run --project Tools/AIBehaviorGraph -c Release -- scenario retriever-retires-outbound-scav-target --model target-lifecycle
dotnet run --project Tools/AIBehaviorGraph -c Release -- search retriever-retires-outbound-scav-target target.divergent_route 6 --model target-lifecycle
dotnet run --project Tools/AIBehaviorGraph -c Release -- generate --model target-lifecycle
```

Supply `--game-source <decompiled-root>` and `--assembly <Assembly-CSharp.dll>`
to verify 14 source fingerprints and the game assembly hash. The dependency
manifest and reviewed IL/asset baseline track these native boundaries for future
game updates. Baseline acceptance records reviewed declarations and fingerprints,
not proof of complete behavior coverage.

Five observer control points, 57 native/effective transitions and 20 scenarios
have 161 scenario/model checks. Orthogonal facts separate collector progression,
target lifetime, registry membership, scan reference, situation ownership,
route state and port availability. Source-bound events represent actor
interleaving; the collector's full controller remains in the retriever model.

The native numerical replay supplies the captured divergent-planning outcome;
the graph does not assume every malformed situation diverges identically.
Existing finite routes can also remain assigned to nonexistent destinations.
The lifetime defect does not require numerical divergence.

The current overlay repairs same-ID replacement while preserving owned
waypoints and existing routes. A separate target-lifetime overlay clears the
retired goal and its obsolete execution/persisted state. It installs no new
route or task; native flight fails normally on its next update.
Synchronous replacement is atomic: no fictional AI tick occurs while
`DestroyAndReload` temporarily removes the original object.

Controls distinguish hiding, unrelated destruction, replacement and occupied
ports from removal. Native docking eligibility owns the blocked-port outcome.
Native manual flight rejects a live destroyed-Ship reference, but its check is
bypassed after save/load resolves that Ship to null while retaining the named
situation. The native harness verifies this validation boundary, then aborts at
the start of steering rather than substituting a steering result.

Every new scenario asserts its termination reason. A trace that stops before
consuming its requested events cannot pass merely because its final node
happens to match.

The original event sequence may become inapplicable after repair: attempting
to continue the discarded path is explicitly reported as blocked. Separate
repaired-continuation scenarios verify native failure. The preexisting-save
scenario records bad findings in its initial corrupted state before restoration;
trace findings are historical observations. Its final node/facts assert that the
restored goal and route are cleared, not that the save was never corrupt.

Removal events apply to ship identities, not independent coordinate/body/stellar
goals. Explicit preservation scenarios and a non-retrieval station-destruction
case test that distinction, keeping the repair broader than this one collector
encounter without treating every missing ship-registry entry as a lost ship.

The later [traffic audit](../Traffic/INVESTIGATION.md) found an uncovered conversion
boundary: native unmooring can destroy an asteroid Ship and recreate its stellar
representation with the same ID. The first removal hook incorrectly cleared
another observer's independent stellar goal during that conversion. Ordinary
stellar-goal preservation checks here do not cover that interleaving. The Traffic
effective model and native conversion suite now include the correction: successful
conversion preserves its goal and route; failed conversion cleans up actual loss.
Exact Ship/ShipSitu references transfer without retaining retired Ship pointers.

This expansion fixes a specific analytical omission, not the entire behavior
model. The assembly-derived `coverage` command inventories native roles and
commands missing even from source bindings. Bindings are review references,
not proof of transition completeness.
