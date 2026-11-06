# Functional hotkeys

Namespace `Ostrabus.Hotkeys`. `[Hotkeys] Enabled` defaults to true.

Shortcuts (either Shift key):

- **Shift+R** toggles **Open Airlock**.
- **Shift+T** toggles **Disembark** (native shore-leave permission).

Both target the single selected player-controlled crew member. With no selection they target the current player,
matching normal crew controls; selected non-crew objects and multiple selections
do nothing. The character must be alive, materialized, in the player's company
and roster, and be the player or carry the native `IsPlayerCrew` condition.

Uses native `JsonCompany.SetPermissionAirlock` / `SetPermissionShore`, changing
the same roster rules as the checkboxes. The state saves normally. Feedback uses the selected character's
native `LogMessage` and appears in the action/message window:
`Open airlock permission: ALLOWED.` / `DISALLOWED.`, or
`Disembark permission: ALLOWED.` / `DISALLOWED.` Existing jobs and autotask
remain under native roster-permission behavior; no actions are cancelled.

Both chords are unused in the inspected vanilla defaults and current saved controls.
Plain R remains available for rotation/ship attitude. The shortcut is limited to
the normal crew view after loading, with no raised UI, ship editing, held/placed
item or focused text input. Ctrl, Alt and Meta combinations are ignored. Works
while paused; key holding does not repeatedly toggle. Input is read through the
game's existing Unity Input System from the plugin's main-thread Update.

No Harmony patches, event listeners, background operations or retained crew/ship
references. Idle polling allocates no mod-created collections or messages; only
an accepted toggle creates its feedback string and native log entry.

Offline permission/selection checks run through `Tests/HotkeysCheck` using the
game's bundled Mono, actual roster setter and company/rule cloning. GUI feedback
delivery is observed at `CondOwner.LogMessage`; physical input and Unity UI focus
remain live integration boundaries.
