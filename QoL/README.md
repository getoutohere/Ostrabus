# Quality-of-life features

Quality-of-life changes to game behavior, separate from corrective MiscBugFixes patches.
Each feature has its own subdirectory and namespace.

- [Socials contact visibility](Socials/README.md): hide construction drones from
  the contact list using their character types, preserving social state. Always on.

- [Course-plot fine controls](CoursePlot/README.md): hover-wheel adjustment of
  ACCEL LIMIT in 0.01 G steps and DRIFT PHASE in 0.01 control-value steps, with
  immediate G readout and suppression of unrelated log-scroll sound. Always on.

- [Kang auto-vents](AutoVents/README.md): short tolerance for marginal oxygen
  dips, with immediate emergency closure. BepInEx tuning and a PDA feature toggle;
  no remote device settings.
