# Quick PDA hacking

Assets-only QoL for Ostranauts 1.0.1.10. The controlled character with Hacking
gets **Hack PDA** under Use; unskilled characters retain the normal Unlock PDA
dialog and software-assisted options. No runtime C#, Harmony hooks or callbacks.

## Install and select

`Deploy-Mod.cmd` installs `Assets` as **OstrabusPdaHack** in the active native Mods
directory and registers it alongside the Settings app. Custom locations configured
through `UserSettings.strPathMods` are honored. Restart the game; the native Mods
screen lists **Ostrabus Quick PDA Hacking**. It does not require the Settings app.

Custom builds select `quick-pda-hack`. Their installer enables its native payload
when selected and disables its entry when omitted. Full deployment preserves an
existing disabled setting, like Settings; reenable it in the Mods screen if needed.
Other mods, order and metadata are preserved.

For standalone use without Ostrabus/BepInEx, copy `Assets` into the native Mods
directory as `OstrabusPdaHack`, then add that folder name after `core` in the
existing `loading_order.json`. Disable its native entry to restore vanilla behavior.

## Mechanism and compatibility

One complete locked-PDA definition adds an action identifier, retaining all other
fields. Native loading replaces entire records, so other mods changing that record
need compatible ordering. The harness compares it with installed vanilla data.

Native interaction overrides derive the shortcut from `ENCHackPDASkillFinished`:
the game supplies duration, rewards and the `ItmWristPDA01` modeswitch. Only its
presentation/gates change. Three native triggers express awake/hacking versus
awake/not-hacking; an explicit nested `bNOT` avoids the native AND evaluator's
ignored trigger-forbid list. See [investigation](ASSET-INVESTIGATION.md).

Existing PDAs get their action list from current definitions on materialization;
no migration is needed. Native modeswitch handles their contents. Removing the
mod with a shortcut still queued causes vanilla to log and skip that unknown
queued action; item identifiers remain native.

176 offline native checks cover the actual JSON loader, override generator and
complete quick-action builder. Installer fixtures cover registration, custom
locations, disabled states, independent Settings selection, checksums and rollback.
Full Unity rendering, loaded modeswitch and save reload remain live boundaries.
