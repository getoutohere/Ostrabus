# Deferred traffic investigations

Priority agreed after the cargo/civilian audit; this order reflects gameplay
impact, plausible severity and evidence gaps, not graph traversal order.
The civilian SFF X/Y correction and asteroid-conversion regression repair are
already implemented and native-tested. Completed follow-ups are marked below;
the remaining rows are investigations, not authorized behavior changes or
assertions of observed gameplay failures.

| Priority | Item | Established evidence | Outstanding question |
| --- | --- | --- | --- |
| 1 | Freight retirement/unloading | Native source and arrival tests distinguish ordinary Dock from cargo-specific market transfer. | Can diverted freight remain stranded or be lost, and what native return/delivery disposition applies? |
| 2 (repaired) | Hidden-hull targeting/reuse | Native-tested selection guard, retired/restored visit invalidation, priority/SFF ownership and civilian encounter protection. | Production offline regression complete: 82 assertions plus 64 native traffic controls; full flight physics remain outside the fixture. See [repair](HIDDEN-VISIT-INVESTIGATION.md). |
| 3 (repaired) | Player-connected regional cleanup | Confirmed through native caller reproduction; repaired cleanup scope, scheduling and primitive saved registration. | Offline regression complete: 91 baseline/repaired cases, including captures, chains, controller restoration and detachment. See [repair](REGIONAL-CLEANUP.md). |
| 4 | Missing destination market | Carrier market removal precedes the missing-destination check. | Is this state normally reachable, and does it cause persistent inventory loss? |
| 5 | Civilian home dispatch | Native interface calls bypass the owner-filtered method. | Establish correct individual-owner fallback before changing dispatch. |
| 6 | Cargo sizing/preflight | Native route reachability is checked before cargo is loaded. | Quantitatively reproduce any wrong hull selection or loaded-mass affordability failure. |
| 7 | Civilian fuel assistance | The shared scavenger SOS mechanism excludes civilian pilots; the inspected civilian tree lacks equivalent recovery. | Does another authored mechanism compensate, or would assistance be a new simulation policy? |

None originates solely in graph speculation. The table distinguishes confirmed
mechanisms from unproved consequences. Graph traces describe conditional paths;
they do not establish that real simulation callers reach them.

See [source/asset investigation](INVESTIGATION.md). Each follow-up should retain
native policy, prefer asset repairs when assets own the defect, and reproduce the
specific native mechanism before adding a runtime patch.
