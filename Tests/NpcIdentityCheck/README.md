# Native AI crew identity checks

This harness reproduces the [native identity collision](../../MiscBugFixes/NPC-IDENTITY-COLLISION-AUDIT.md)
and links the actual production `AiCrewIdentityPatch.cs` for repair validation.
It runs against the installed game's assembly under its bundled Mono runtime:

```powershell
dotnet build Tests/NpcIdentityCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/NpcIdentityCheck/bin/Release/netstandard2.1/NpcIdentityCheck.dll
```

Ostranauts 1.0.1.10 passes **8 unpatched baseline checks** followed by **122 repair
checks**. The baseline demonstrates native AddCrew forbidding the serving pilot
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
