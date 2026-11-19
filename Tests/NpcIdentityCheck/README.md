# Native AI crew identity checks

This harness reproduces the [native identity collision](../../MiscBugFixes/NPC-IDENTITY-COLLISION-AUDIT.md)
and links the actual production `AiCrewIdentityPatch.cs` for repair validation.
It runs against the installed game's assembly under its bundled Mono runtime:

```powershell
dotnet build Tests/NpcIdentityCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/NpcIdentityCheck/bin/Release/netstandard2.1/NpcIdentityCheck.dll
```

The original Ostranauts 1.0.1.10 run passed **8 unpatched baseline checks** followed
by **122 repair checks**. The current 1.1.0.3 run passes **8 baseline checks** and
**133 repair/recruitment checks**, including a separate native shared-source-list
defect reproduction. The baseline demonstrates native AddCrew forbidding the serving pilot
yet reclaiming that actor through a fresh name collision. It also proves merely
switching `bNew` to true misses saved-only identities.

Repair coverage includes live and saved occupied names, null-valued registry
keys, 32 consecutive occupied name draws, and retrying the native name generator
with the recruit's existing gender. It verifies no extra name draws on
collision-free creation or intentional recruitment. Other checks cover
the original pilot's age/location/profile/ownership, intentional station
recruitment, explicit live reuse and saved restoration. It loads 21
shipped civilian/combat/scavenger/police templates and checks their human/random
name contract and preserved age, career, loot and faction policy. IL checks assert
one constructor call replacement and refusal of changed native flag semantics.

The name RNG and station volunteer availability are deterministic fixture
boundaries. Native recruiter, pilot forbids, profile construction and factory
lookup execute. Controlled exceptions stop the unpatched theft after returning
the existing actor and stop repaired fresh creation before Unity scene allocation.
Age regeneration and full fresh chargen are source-verified, not executed offline.
Intentional existing-person boarding executes native AddCrew/MakeCondOwner/ClaimShip
with only scene positioning substituted. Unity equality, logging and homeworld
services are explicit host fixtures. Full scene integration remains a live check.

The harness does not contact the running game or modify saves/assets. It does not
repair historical actors or stranded ships. Custom feature selection and full-plugin
binding are checked separately by the custom builder and patch-binding harnesses.

## Saved population recruitment probe

The optional `population ROOT_JSON` mode executes the installed native
`JsonPersonSpec.Matches` and `TIsTrafficAIScav` against selected saved residents.
Generate the input with the [freight/save study](../../Tools/FreightLifecycleStudy/README.md)
using explicit `--person=ID` selectors and `--people-everywhere`, then run:

```powershell
dotnet Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll Tests/NpcIdentityCheck/bin/Release/netstandard2.1/NpcIdentityCheck.dll population obj/PopulationStudy/burn-root.json
```

The November 19 Earth probe passed four controls: a non-Earth shipbreaker passes
the traffic trigger but fails the Earth pilot profile; changing only that test
actor's homeworld permits recruitment; `IsNotTrafficAI` still excludes the actor;
and the native regional accessor returns its shared source list by reference.
This probe currently expects the selected resident Jay Farmer as its control.
It reconstructs saved chargen identity and condition presence with scene services
substituted; it does not load a complete scene or replay a population lifecycle.

The normal harness additionally executes native `AIShip.AddCrew` before and after
`AiCrewRecruitmentPatch`: the baseline removes an underpopulated source from the
shared registry; the repair retains it across repeated failed searches and then
recruits the same existing volunteer once the source recovers. It checks that
adding `OKLG_UNK` stays local and changed IL is rejected. See the
[population audit](../../Notes/NPC-POPULATION-BALANCE-20261119.md) for evidence and
limits. Neither probe changes the running game, assets or saves.
