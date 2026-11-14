# Ship surrender work-stall diagnosis

Run sequentially through the bundled Mono host from the repository root:

```powershell
dotnet build Tests/ShipSurrenderWorkCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/ShipSurrenderWorkCheck/bin/Release/netstandard2.1/ShipSurrenderWorkCheck.dll
```

Reads the configured installation's vanilla JSON and exercises its shipped
assembly. The default and `combat-loop` modes are unpatched reproductions;
`patched-radio` and `patched-combat` install the production repairs.

The 69 checks reproduce the two stuck conversation-flag increments, removal on
reload, terminal boarding reply and separate ship-command wiring, condition
effect preservation, and remove-all behavior. Counterexamples show why adding
a negative terminal edit or a `Default` context reset can interfere with a
simultaneous face-to-face conversation. An alternative model omits radio writes
to that shared state and preserves existing flags and relationship contexts.

Condition parsing/arithmetic, membership, a native captain-selection trigger,
and `Interaction.UpdateStakes` execute in the game assembly. Fixture hooks replace
Unity equality, parent validation and UI/face notifications; the context check
uses non-player actors to avoid Unity tooltip events. It does not simulate the
whole flight, docking or surrender-command sequence. Those paths were inspected
in native source and asset references for dependencies on the changed state.

See `MiscBugFixes/SHIP-SURRENDER-WORK-STALL.md` for the selected radio-delivery
repair and the asset-only alternative's scope/overlap problems.

Pass `combat-loop` after the DLL path for the separate failed-surrender combat
diagnosis. It runs 32 assertions on the real SurrenderOffer and threat-initiation
methods, reproducing six acquire/drop cycles when message sending fails and
checking a successful-send control. No Ostrabus gameplay patches are installed.
See `Navigation/SURRENDER-COMBAT-LOOP.md` for the live evidence and native cause.

Pass `patched-radio` for 3,362 production checks across all 128 native replies,
146 condition loot records and shared social exits. Native loot parsing and
evaluation and the transpiled ReceiveMessage call execute; the full
Unity-dependent ApplyEffects body, eligibility and AI/UI replies are fixtures.
Checks verify unchanged other effects, choice slots/random draw counts, original
command references, direct on-foot behavior, cache reuse/invalidation and finally
restoration. A known bundled-Mono JIT warning about the unused Vector3.RotateTowards
internal call can appear while Harmony prepares the engine-dependent original
ApplyEffects trampoline; the fixture does not execute that body.

Pass `patched-combat` for 85 production accounting checks: repeated rejected
sends, NPC-NPC and player-NPC opponents, mixed/all-success outcomes, existing
records, duplicate candidates, empty/missing state, aliased perception lists,
fallback filtering and exception accounting. Real SurrenderOffer and native
selector/sequence execution run with controlled message/controller lookups.
Native reply assets and cooldown eligibility remain authoritative in production;
the suite does not invent a replacement acceptance policy.

Both fixes are independently selectable custom-builder bugfix features:
`radio-surrender` and `ai-surrender-accounting`.

Pass `neutralization` for a separate unpatched native reproduction: 66 checks
exercise AreThreatsNearby, InitiateAgainstBiggestThreat and IsTargetNeutralized
across load state, IsAIShip and controller registration, plus six acquire/drop
cycles. Contacts, range, faction score, living captain, Unity identity and
controller lookup are fixture inputs. No production neutralization repair is
installed. See `Navigation/COMBAT-NEUTRALIZATION.md` for lifecycle/asset findings
and the distinction from the two implemented surrender fixes.

Pass `patched-neutralization` for the native baseline followed by production
contrast (170 combined checks). This checks the same acquisition matrix,
existing/stale combatants completing native cleanup, repeated scans without
reacquisition, controller loss/restoration, mixed opponents, native surrender
exclusion and static/Full controls. It also verifies unchanged sensor contacts,
one controller lookup per contact and rejection of changed IL layouts.
The independent custom-builder feature is `ai-neutralized-targets`.
