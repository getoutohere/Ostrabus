# Live zero-fuel rescue, 2026-11-09

Observed during normal running simulation after deployment, without API state
changes, refueling, forced docking, SFF intervention or save reloads during the
encounter. Raw snapshots and poll records are under ignored `obj/scav-watch`.

Baseline: seven registered active scavs. O-KA2 / Host Terrapin was the only one
with zero RCS (capacity 749.364694715254 kg). It was shallow, docked to O-7X1 /
Storm Parasite, in Main's ordinary Hold, with living captain Lateef Howard and
no ShipRequestedHelp condition. Other scavs had roughly 46–96% RCS remaining.

| Game epoch | Observation |
| --- | --- |
| 65632865954.045929 | Native undock event: O-KA2 released O-7X1 after visit expiry. |
| 65632865955.919876 | Stored comms: O-KA2 sent SHIPSosStranded to OKLG. |
| 65632865956.934929 | Stored comms: OKLG replied SHIPSOSDispatchHauler, acknowledging assistance. |
| Subsequent census | New O-QH5 / OKLG Hauler 24 registered as HaulerRetriever, targeting O-KA2. |
| 65632866692.606728 | Native dock event: O-QH5 attached to O-KA2. |
| 65632866729.6479 | Hauler running HaulShip toward OKLG, reciprocal links confirmed. |
| 65632867637.99572 | Native undock event near OKLG: hauler released rescued ship. |
| 65632867746 onward | Both hulls intact, hidden from system, empty crew profiles, flight controllers unregistered. |

Approximately 28 game minutes elapsed from scav undocking to station release.
Approach distance decreased at about 0.75 km/s; towing closed on OKLG at about
0.59 km/s. Host Terrapin retained zero fuel throughout: native ATC rescue is a
tow, not an on-site fuel transfer.

Lateef survived. His resolved CO and O-HWL / Deadalus's crew profile and AI
captain reference agree that he subsequently captained O-HWL, targeting B-RAA.
His help condition was absent and current interaction was WanderSoon. The
intermediate station residence/reassignment was not sampled directly; native
DockAndDespawn and AddCrew provide station drop-off and NPC recycling paths,
respectively. Do not treat the inferred intermediate transition as a recorded
movement event.

This validates one real rescue through visit completion, SOS, dispatch, pickup,
tow and native retirement, including survival of the rescued pilot. It does not
establish every dispatch/refusal/range case or timed shallow-help expiry.
