# One-hour Venus escape coast SFF

Original forecast baseline: 65631766292.967255. Actual jump started 4.727752
seconds later at 65631766297.695007, ending 65631769897.695007. The recorder
captured the exact start; the original forecast is retained unchanged. Independent
RK4 replay of that captured start agrees with the actual custom SFF player
endpoint within 0.000283 metres and velocity within 8.39e-8 m/s.

Unlike the preceding undocked case, the player has no native orbit lock or NavData
and no thrust. The SFF report explicitly shows O-GXD mode=coast, applied=true,
60 steps, no gravity switches. Its Full aerodynamic model is captured but the
path remains far outside the atmosphere. This exercises custom numerical SFF
translation for the player, not just native orbital playback preservation.

Player remains on an unbound Venus-relative trajectory. Final native-coordinate
radius is approximately 26,350.17 km; Venus remains the strongest gravity body.
The exact-start model predicts 26,366.03 km using SFF's tighter parent ephemeris.
The known ~15.9 km native/tight body-position discrepancy is a separate reference
consistency issue; the tiny RK4 endpoint residual validates the implemented SFF
model and is not a claim of sub-millimetre agreement with a corrected native
planet-coordinate force model. No gravity-dominance boundary was crossed.

Surroundings:
- Entire registry captured before/after: 193 -> 194 entries.
- 35 initially visible objects within 1 million km of Venus; none missing afterward.
- M-5N4 survives and remains visible as a coast case.
- O-TRRP remains FlyTo VORB, partial: target range 117,489.38 -> 114,743.05 km
  (2,746.33 km progress). No instant arrival was allowed.
- J-V5P/O-X6P remain a partial HaulShip group, Venus radius 46,182.73 -> 40,993.48 km.
- Nine visible AI ships with pre-existing NavData become hidden without being
  destroyed. Their plotted courses/catch-up stayed native; exact dock/arrival
  transitions were not captured and are not asserted here.
- Six prepared plans, 71 hidden exclusions, zero failures/impact checks/budget
  fallbacks. SFF elapsed 179.1522 ms. Recorder stopped.

Detailed per-object snapshots, comparisons, original prediction and exact-start
reference retained here. Passive torch recorder is independent of this recorder.
