# Docked six-hour SFF result

Start epoch 65631773269.533958; skip 21600 game seconds.
Player O-GXD remained attached to VORB, with its body lock preserved.
Preparation 1052.60 ms; total SFF context 1244.23 ms; 70 plans; zero exceptions.

World behavior is not a pass:
- 56 Sol impacts have zero propagation steps. Four Venus impacts also have zero
  steps. These objects were classified inside a body at the starting position;
  they did not fly into it during the skip. Their prior physical validity was
  not captured. SFF must not promote invalid/inside-body starts into artificial
  new impact trajectories. B-4PL remains present at Sol's surface afterward.
- M-5N4 was explicitly Shallow before the skip. Its plan ran 44045 steps with
  atmosphere=true and an impact on Venus. The ship is no longer found afterward.
- Confirmed source mismatch: Ship.UpdateGravAndAtmo returns before calculating
  lift/drag when LoadState != Full. SffSimulation.Prepare creates aerodynamic
  models for every ship, including Shallow. Thus SFF adds drag that native normal
  simulation does not apply to this derelict. This invalidates the live coast
  comparison; the exact trajectory should be reproduced offline before a fix.

Hold the undocked trial until these are corrected. Preserve pre-test save.
Do not interpret impactChecks=61 as 61 Sol collisions: actual split is 56 Sol,
5 Venus; only M-5N4 had nonzero propagation steps among the impact reports.

Raw recorder and before/after captures are retained in this directory. Recorder
stopped after diagnosis; a new baseline is required before the next skip.
