# Fixed-build docked six-hour SFF trial

Start epoch 65631764426.022652; skip 21600 seconds. Orbital/SFF enabled.
Player remains body-locked and docked to VORB, with no plotted course.
M-5N4 survives as Shallow, mode=coast, applied=true, aerodynamicModels=0,
526 propagation steps, no impact and no budget fallback. Its passage through
Venus's atmosphere-data tail no longer invents drag.

15 plans; 59 invalid starts excluded; zero failures; zero budget fallbacks.
SFF context elapsed 565.6855 ms. Two partial AI journeys retain their partial
travel outcome. This trial validates docking preservation and the two repairs.

Two propagated Venus impacts remain: O-APJH (158 steps) and O-D5GM (166).
Both have aerodynamicModels=0, so these are not the former shallow-drag bug.
Earlier same-save normal-test baseline analysis gives initial osculating periapsis
altitudes -58.36 km and -10.28 km respectively. That makes ballistic impact
plausible; it is corroborating evidence from an earlier run, not an exact captured
start state for these ships in this trial. SFF treated them as untargeted coasters.
Whether normal AI would have corrected those paths remains an open navigation
question, not established as a correct six-hour AI outcome. O-D5GM is no longer
found; O-APJH survives, registered AI with no active command or current target.

Continue to the independent undocked-player case after capturing its actual
release position/velocity and route/control state. No repeat docked trial needed
for the confirmed fixes; do not call all surrounding AI navigation validated.
