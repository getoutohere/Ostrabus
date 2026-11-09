# Ostrabus 0.2.2

- Retain police controllers during legitimate inspection boarding; expose recovery
  for patrols already stranded by the earlier guard.
- Correct native retriever SFF continuation so hauling does not revert to the
  outbound flight stage. Offline native motion and transition reproductions pass;
  fresh SFF pickup behavior still awaits live observation.
- Add native station-arrival and recovery inspection APIs. Live testing confirmed
  all four stranded hauler pilots transferred alive to OKLG and its interiors;
  the restored patrol resumed normal homeward flight.
- Add course-slider wheel fine controls and a configurable Venus wreck percentage,
  defaulting to 50%, in the PDA settings app.
- Include the standalone torch-drive calculator and offline signal-box candidate
  research. Signal-box behavior fixes remain test-only.
- Correct Unity reference paths so clean builds do not need GameDir to end in a slash.

The staged source snapshot builds independently with all 150 included Harmony
patch classes binding. Native lifecycle/arrival, retrieval, PDA, Venus,
course-control and signal-box suites and standalone calculator checks pass.

Torch autopilot documentation is retained and explicitly marked pending in-game
playtesting. Its three implementation files and Tools/TorchNativeOracle are
excluded from this commit. Local Debug builds may include that uncommitted work;
they are not equivalent to a build from this source snapshot.

No new release ZIP, Git tag, push or hosted release is produced by this commit.
