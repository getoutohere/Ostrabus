# EM propagation

Independent, opt-in simulation improvement for player and AI EM detection.
Enable **EM propagation** in the PDA Mod Settings app. The value is saved as
`[EMPropagation] Enabled` in BepInEx config and defaults off. Switching it off
removes both module hooks and restores native EM methods immediately.

Preserve vanilla's reference equipment ranges, inner-range `baseRange/distance`
formula, and per-receiver signal cap of 0.31. Outside the reference range, change
the EM distance factor from `(baseRange/distance)^3` to its square. The corresponding
single-receiver detection-distance calculation changes from cube root to square
root. The runtime patches update both `EM.GetSignalStrength` and
`EM.GetDetectionDistance`, so player display and AI perception agree.

This changes all radio emissions represented by EM, including transponders and
active-sensor emissions. It is a propagation abstraction informed by free-space
inverse-square spreading, not a calibrated radio link budget. It does not establish
an arbitrary new kilometre limit, change optical/radar/lidar algorithms, or replace
AI decision logic. Its toggle is independent of the Sensors overhaul. Changing it
invalidates cached contact estimates so timestamp acquisition follows the selected
propagation rule. No new data is written to saves.

The offline oracle executes shipped non-EM methods, substitutes only this revised
EM arithmetic, and validates its inverse-cube baseline against actual shipped EM
methods. The resulting variant retains vanilla's exact/faint position behavior.
The third comparison configuration layers the sensor uncertainty model on top.
