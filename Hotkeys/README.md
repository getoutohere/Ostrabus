# Functional hotkeys

Namespace `Ostrabus.Hotkeys`. `[Hotkeys] Enabled` defaults to true.

Shortcuts (either Shift key):

- **Shift+R** toggles **Open Airlock**.
- **Shift+T** toggles **Disembark** (native shore-leave permission).
- **Shift+F** equips/stows the selected crew member's helmet and worn EVA suit
  battery. Rebind in **PDA -> MOD SETTINGS -> Helmet and EVA battery shortcut**:
  click the binding, press the new chord, or use RESET. Esc cancels. The binding
  saves to `[Hotkeys] EquipmentShortcut`; Shift+R/T stay reserved.

All target the single selected player-controlled crew member. With no selection they target the current player,
matching normal crew controls; selected non-crew objects and multiple selections
do nothing. The character must be alive, materialized, in the player's company
and roster, and be the player or carry the native `IsPlayerCrew` condition.

Uses native `JsonCompany.SetPermissionAirlock` / `SetPermissionShore`, changing
the same roster rules as the checkboxes. The state saves normally. Feedback uses the selected character's
native `LogMessage` and appears in the action/message window:
`Open airlock permission: ALLOWED.` / `DISALLOWED.`, or
`Disembark permission: ALLOWED.` / `DISALLOWED.` Existing jobs and autotask
remain under native roster-permission behavior; no actions are cancelled.

The permission chords are unused in the inspected vanilla defaults and current saved controls.
Plain R remains available for rotation/ship attitude. The shortcut is limited to
the normal crew view after loading, with no raised UI, ship editing, held/placed
item or focused text input. The permission keys ignore Ctrl, Alt and Meta;
the equipment shortcut matches its configured modifiers exactly. Works
while paused; key holding does not repeatedly toggle. Input is read through the
game's existing Unity Input System from the plugin's main-thread Update.

No Harmony patches, event listeners, background operations or retained crew/ship
references. Idle polling allocates no mod-created collections or messages; only
an accepted toggle creates its feedback string and native log entry. Equipment
search allocates bounded local collections only on activation; no inventory work
occurs during idle polling. Binding text is parsed only when it changes.

## Equipment behavior

If a supported helmet or suit battery is installed, the shortcut stows installed
parts. Otherwise it searches carried inventory and equips available parts. Mixed
states follow the same rule: a helmet without a battery is stowed.

Stowing first checks the compartment at the character's actual position, including
across docking boundaries. It calls vanilla `PledgeSurviveO2.CoHasO2`, whose
minimum is **17 kPa O2 partial pressure** in 1.0.1.10. Below that threshold, or in
void/missing/unresolved atmosphere, it leaves both items installed and writes one
refusal to the action log. It does not use the character's sealed-suit atmosphere
or cached room. This intentionally checks ambient oxygen only; it is not a wider
temperature/toxic-gas assessment. Equipping remains allowed in unsafe atmospheres.
The lookup runs only when the shortcut requests removal, before inventory search;
no per-tick monitoring or extra hook is installed.

EVA and pressure-suit helmets are recognized by native conditions. Only the
**worn** EVA suit's battery compartment participates; spare suits, oxygen bottles
and filters are left alone. Free hands are preferred for stowing, then unlocked
carried containers, including nested backpacks. Dragged world containers are
excluded. Other headwear is not replaced and gear is never dropped to make room.

Each item is preflighted independently: a full inventory refuses removal, and a
partially successful operation reports both results in one native action-log
line. Movement uses native un-slot/slot and container methods, preserving their
equipment effects, inventory events and mass updates. A stacked battery is split
using native `PopHeadFromStack`, equipping one existing object. No item is spawned
or destroyed. A rejected destination restores the source inventory.

`Tests/HotkeysCheck` runs under bundled Mono using native roster setters, slot
selection, container grids, removal/insertion and stack splitting. It covers
equipment round trips, full/partial storage, locked and dragged inventory,
pressure helmets, mass-delta conservation and rejected-destination rollback.
Ambient safety tests execute the native oxygen predicate around its 17 kPa
boundary, distinguish suit gas/stale rooms from the actual compartment, reject
void/unresolved air, and keep equipping available in vacuum.
Unity rendering, condition effects and action-log delivery are fixture boundaries;
physical input and UI focus remain live integration boundaries. The PDA suite
separately checks validated binding persistence and reserved-key refusal.
