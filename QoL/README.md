# Quality-of-life features

Quality-of-life changes to game behavior, separate from corrective MiscBugFixes patches.
Each feature has its own subdirectory and namespace.

- [Quick PDA hacking](PdaHacking/README.md): offer Hack PDA for the controlled
  character with Hacking, using native unlock effects without the encounter dialog.

- [Multiple-ship barter zones](BarterZones/README.md): prefix sale groups with
  callsigns, sort by ship/zone and keep same-named zones independently accessible.
  Personal inventory ordering and saved zone names remain unchanged. Always on.

- [Socials contact visibility](Socials/README.md): hide construction drones from
  the contact list using their character types, preserving social state. Always on.

- [Course-plot fine controls](CoursePlot/README.md): hover-wheel adjustment of
  ACCEL LIMIT in 0.01 G steps and DRIFT PHASE in 0.01 control-value steps, with
  immediate G readout and native Shift-drag precision support. Always on.

- [Single-port docking](Docking/README.md): skip the target-port selection when
  comms offers exactly one eligible choice, with a saved Mod Settings toggle.

- [Wreck damage severity](WreckSeverity/README.md): soften initial derelict damage
  with a configurable curve and optional console reports of native damage rolls.

- [Kang auto-vents](AutoVents/README.md): short tolerance for marginal oxygen
  dips, with immediate emergency closure. BepInEx tuning and a PDA feature toggle;
  no remote device settings.

- [Apartment refuel kiosk](../StandaloneMods/ApartmentRefuel/README.md): optional standalone
  an expensive Sundries item restricted to owned station apartments, using
  native paid services for directly docked ships. Separate plugin bundled
  alongside Ostrabus; implemented, pending live acceptance testing.
