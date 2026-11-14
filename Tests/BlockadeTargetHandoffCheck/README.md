# Blockade combat cleanup admission checks

This isolated suite checks the real 1.0.1.10 BlockadePilot constructor and its
AreThreatsNearbyBlockade evaluator before enabling the production
CombatCleanupAdmissionPatch constructor hook. It does not modify a running game.

```powershell
dotnet build Tests/BlockadeTargetHandoffCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/BlockadeTargetHandoffCheck/bin/Release/netstandard2.1/BlockadeTargetHandoffCheck.dll
```

Run the shared Mono host sequentially with other native suites.

360 native baseline/production checks pass; the project builds without warnings.

The native baseline demonstrates that an empty signature set or the native
2,500 km detection cutoff can bypass neutralization and leave the sticky patrol
flying toward an abandoned combat target. It includes both live loss of detection
and already saved InCombat=false/pending Combatants states. The production
comparison must execute exactly one native scan/cleanup/station retarget pass,
then retain station navigation.

The native selector, sequences, sticky movement phase, combat gate, blockade
detector, initiation, neutralization and TargetNearestStation execute from the
shipped assembly. Unity comparisons, captain/signature/station queries, radio
delivery and physical flight output are fixture boundaries. Competing
world-dependent action leaves receive controlled results. Native station target
binding and the entire native command save payload are checked directly.

Controls preserve active and mixed fights, dormant combat references, urgent
queue priority, native sticky Flee for an unarmed ship and the authored blockade
warning/engagement behavior:

- A fresh foreign civilian is eligible even without an enemy faction-score
  lookup; Blockade defines a restricted-area violation from foreign ownership.
- A same-faction contact is not a fresh violation in the controlled unmoored
  case. Existing combatants and illegal mooring have additional native rules.
- A changed closest violator attempts SHIPBlockadeComing once; unchanged closest
  targets do not repeat it, while leaving/reentering detection can warn again.
- Native surrendered-to and projectile exclusions, fast torch escape and range
  eligibility remain unchanged.
- The actual root retains its warning -> combat initiation -> fighting chain.
  The fixture observes the radio-send attempt; it does not simulate reception.

The installed JSON is also checked: SHIPBlockadeComing is the restricted-area
warning gated by IsBlockader, has no reply choices or loot effects; TIsBlockader
requires IsBlockadeShip; the warning remains in native ship-interaction loot.
The warning itself does not define a detention or arrest workflow.

The first 152 checks also expose a separate acquisition defect: Blockade has its
own detector and bypasses the regular detector's helper. Handoff repair alone
still allows fresh unregistered shallow hulls to be acquired, neutralized and
reacquired. The next phase enables the production Blockade acquisition transpiler
and reruns the handoff/policy controls with both repairs active.

AcquisitionChecks.cs adds Shallow/Edit/Full x AI/non-AI x registration-present/
absent controls, existing combatants reaching native cleanup before exclusion,
repeat-scan stability, saved false flags, controller loss/reactivation, mixed
fresh targets, unchanged signature ownership, exact warning and registry-lookup
counts, and rejection of missing/changed native IL. It asserts both existing
engagement release and immediate eligibility restoration without a target cache.

These tests do not claim physical travel/fuel accuracy, eventual docking,
station availability in every region, warning delivery or all other pilot roles.
