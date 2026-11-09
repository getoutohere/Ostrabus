# Live native arrival validation — 2026-11-09

Game 1.0.1.10, inspection generation 7, normal unpaused gameplay. Operator
authorized recovery/observation after deploying the latest Debug build.
Private raw API captures are in ignored obj/live-arrival-* files.

## Applied and verified

| Source | Original pilot | Tow consumed by native delivery |
| --- | --- | --- |
| O-VT1F | Valerie Harrell | H-TZM |
| O-C8U | Bailey Bridges | J-MQR |
| O-XTD7U | Gang Ning Madden | M-YNX |
| O-R2K | Anie Dunlap | B-SDA |

All four GET previews passed. POST /ships/{id}/arrival completed with Finished,
crewPreservedAtStation=true and hullHidden=true. O-VT1F was processed first and
its pilot independently checked on OKLG before applying the remaining requests.
No synthetic approach/refuel endpoint and no NPC pruning was used.

Independent NPC inspection confirmed the original four people alive and not
destroyed. Subsequent polls found them walking/wandering on OKLG and its interiors:
Bailey visited OKLG_RES and returned; Anie later appeared at OKLG_BIZ. This is
actual station-resident activity after arrival, rather than just trusting the
operation's response. All four retired controllers remained absent and RCS/torch
accelerations were zero. The original hulls remained inspectable, subject to
native hidden/reuse/cleanup policy. All four delivered wrecks were reported
destroyed by the native hauler path.

Combined log captures covering the operations and follow-up contained no warning,
error, exception or Station Arrival failure entries. The read-only log API was
paged with its sequence cursor and maximum limit 50.

## Police recovery

O-ENB initially had Brody Farmer aboard, about 1,989 kg RCS and no controller.
GET eligibility passed, then POST /ships/O-ENB/ai/restore restored Police/OKLG.
Subsequent independent polls found Brody alive aboard and a registered FlyTo
controller targeting OKLG. From epoch 65632843201.786 to 65632843426.699 its range
fell from 38,257.683 to 37,921.689 km. It was closing at approximately 1.494 km/s,
coasting with 1,747.621 kg RCS; the stored route forecast about 1,556.591 kg at
arrival. The final planned ETA was 6.982 game hours. No forced station arrival
was applied to the patrol; its eventual arrival remains to be observed.

## Limits and follow-up

This validates live API restoration and native station population transfer, not
a fresh post-fix SFF pickup or a save/reload roundtrip of the rescued station NPCs.

Native retirement retains the old hull's angular velocity (for example O-VT1F
still had about 862 rad/s while hidden), despite clearing active thrust. Source
review shows ResetShipState itself does not clear spin and PlaceOrbitPosition
does not reset angular velocity. This is a potential reuse-boundary follow-up,
not evidence that these retired ships are still actively navigating or that a
reused hull has already failed. No additional physics patch was applied during
this validation. Native reserve hulls are not guaranteed to persist forever.
