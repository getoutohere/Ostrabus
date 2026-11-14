# Native asset alternative

Investigated against Ostranauts 1.0.1.10 after the first implementation used a
quick-menu Harmony postfix. **Game-runtime C# is not required.** A native JSON
prototype now exercises the same skill-based shortcut without that postfix.
The production feature now uses these native assets. The menu postfix is removed,
and full/custom installation is integrated independently of the Settings app.

## Smallest target-local native design

Three JSON files are sufficient:

1. `condowners`: override `ItmWristPDA01Locked`, retaining every original field
   and appending `OstrabusHackPDASkill` to `aInteractions`.
2. `condtrigs`: require native `TIsHumanAwake` and `TIsSkilledHacking` for the
   shortcut. A corresponding awake/not-hacking gate selects the original dialog.
3. `interaction_overrides`: derive `OstrabusHackPDASkill` from
   `ENCHackPDASkillFinished`, changing its title/group, gates and presentation.
   Remove its encounter UI, pause and reply chain. Separately override only the
   original opener's actor gate so skilled characters do not get duplicate choices.

`JsonInteractionOverride.Generate` calls native `JsonInteraction.CloneDeep` and
`DataHandler.ApplyOverride`; the successful native interaction supplies duration,
reward loot and PDA modeswitch loot. No copied hacking algorithm, new item type,
PDA-content manipulation or gameplay callback is needed. Native `bHumanOnly`
retains controlled-character gating, and native triggers revalidate before use.
The original software-assisted dialog options remain unchanged.

Use an explicit nested `bNOT` trigger to negate `TIsSkilledHacking`. The initial
prototype showed that `aTriggersForbid` is ignored by the native `bAND` branch of
`CondTrigger.Triggered`; relying on it left both choices visible for hackers.
The corrected authored expression passes the native menu truth table. No general
condition-evaluator patch was added during this investigation.

## Why the PDA definition is replaced

`JsonCOOverlay` exposes `aInteractionsReplace`, not an append list.
`COOverlay.Init` replaces entries already present. It cannot add a second action
to an existing base PDA using that interface. A same-name CO overlay also cannot
shadow the base record: native `DataHandler.GetCondOwner` checks `dictCOs` first.

Native `JsonToData` replaces whole records by `strName`; it does not merge fields.
The prototype consequently retains the full locked-PDA definition with only its
action list changed. It does not register a new PDA model or affect unlocked PDAs.
Another mod replacing that record would need compatible ordering/combined assets;
future game changes to the definition require review. This is a concrete asset
override boundary, not a reason a game-runtime C# hook is necessary.

Adding the action to the global `TXTPlayerCombatInteractions` list is technically
possible but makes every target menu evaluate it and replaces an unrelated shared
combat list. Prefer the PDA-local definition.

## Existing saves and removal

`CondOwner.SetData` initializes `aInteractions` from the current `JsonCondOwner`
definition before loading saved instance fields. `JsonCondOwnerSave` does not save
that list. Existing PDAs therefore use the new list when materialized on reload;
no save rewrite is needed. This conclusion is code-backed, not a live save-load test.

PDA items keep their original identifiers and native unlock modeswitch. A newly
queued shortcut can save its custom interaction identifier; removing the native
mod before that queued action executes makes it unavailable. The native queue
restore explicitly logs and skips unknown interactions. The full loaded-scene
modeswitch and mod-removal/save-load sequence were not executed in this fixture.

## Offline proof

The initial prototype wrote assets under ignored `obj/QuickPdaAssetProbe/data`.
The current harness reads the actual `QoL/PdaHacking/Assets` payload,
loads it with shipped `DataHandler.JsonToData`, runs native override generation,
and executes native `CrewSim.GetAvailActionsForCO` with no menu hook installed.
**176 checks pass** for metadata, C#-free payload, loader/override behavior, skilled/unskilled and incapacitated
actors, target conditions, controlled-character restriction, click-time changes,
effect references, unchanged template and unchanged PDA fields outside the list.

Fixture boundaries are Unity component lookup/movement/localization, empty
unrelated combat/job/plot/work state, recovery-indicator UI, and pickup/drop
definitions. They do not replace the menu result, participant/condition evaluation,
asset loading, native cloning or override application. No running game or save
is modified and no native asset is installed by the proof.

## Completed conversion

The postfix and custom-builder patch registration are removed. The catalog now
selects a native payload. `Build/Install-NativeMod.ps1` registers `OstrabusPdaHack`
in full deployment/packages while preserving existing disabled settings. Custom
installation enables selected payloads and disables omitted ones independently
of Settings. Checksums and rollback cover failed profile switching. The native
regression harness remains, with no new gameplay hooks.
