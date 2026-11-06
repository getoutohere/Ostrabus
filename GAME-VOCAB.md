# Ostranauts: code vocabulary

A small map for writing fixes and cleanup commands. Based on the local
`<decompiled-source>` source snapshot; revisit details when the game updates.
Source pointers below are relative to that folder. This is an initial vocabulary,
not a complete account of simulation or save/load behavior.

## Objects, identity, and location

| Term | Meaning in the code | Start reading here |
| --- | --- | --- |
| **CondOwner**, **CO**, `co` | A **condition owner**: the runtime object that carries conditions. People, robots, and ordinary objects use this machinery; CO does not mean NPC. A Unity `MonoBehaviour`, with identity (`strID`), conditions (`mapConds`), and a containing ship (`ship`). | `CondOwner.cs`: class, `IsHumanOrRobot`, `mapConds` |
| **PersonSpec**, `pspec` | A person's identity/profile data, distinct from their physical CO. `GetCO()` returns a cached CO or tries resolving `strCO` through `DataHandler.mapCOs`. Having a profile does not guarantee a corresponding object resolves. | `PersonSpec.cs`: `GetCO`, `MakeCondOwner` |
| **Relationship** | Social state about a person: includes their `pspec`, familiarity, relationship conditions, events, and reveals. This is what the mod's social-contact list contains. | `Relationship.cs`; `GUIPDA.GetKnownSocialContacts` |
| **Ship** | The ship-level simulation object, separate from `CondOwner`. Has registration ID (`strRegID`), display name (`publicName`), contents, people, and docking relationships. Query scope matters: `GetCOs` accepts flags for subobjects, docked ships, and locked contents; `GetPeople` also takes a docking flag. | `Ship.cs`: `GetCOs`, `GetPeople`, `GetCOByID` |
| **CrewSim** | Central simulation/controller access point. `GetSelectedCrew()` gives the selected character; `system` points to the `StarSystem`. | `CrewSim.cs` |
| **StarSystem** | System-level ship management. `GetAllLoadedShips()` returns the values of its loaded-ship dictionary (or null if uninitialized). Treat that as a loaded-world query, not proof of everything represented in saves. | `StarSystem.cs`: `GetAllLoadedShips` |
| **DataHandler** | Shared data lookup/factory hub. `mapCOs` maps object IDs to COs; `GetCond`, `GetCondTrigger`, and `GetInteraction` retrieve game definitions/runtime forms. Read each getter before assuming whether it returns a shared object or clone. | `DataHandler.cs` |

Use `co.strID` for object identity and `ship.strRegID` for ship identity. Names are
labels and are not reliable identity comparisons.

## State, tests, and behavior

| Term | Meaning in the code | Start reading here |
| --- | --- | --- |
| **Condition**, **cond** | Named state with a numeric amount (`fCount`), plus metadata such as duration, display, and fatal/KO flags. Conditions can represent tags, quantities, and statuses. A CO stores them in `mapConds`; use its condition methods when investigating state changes and side effects. | `Condition.cs`; `CondOwner.cs`: `HasCond`, `GetCondAmount`, `AddCondAmount`, `SetCondAmount` |
| **CondTrigger**, **CT**, “trigger” | A reusable condition test: requirements, forbids, nested triggers, AND/NOT logic, and chance. `Triggered(co)` evaluates it. It also carries a condition name/count that `ApplyChanceID` can add or subtract, so inspect the caller to distinguish testing from applying an effect. | `CondTrigger.cs`: `Triggered`, `TriggeredREL`, `ApplyChanceID` |
| **CondRule** | Threshold-based behavior attached to a condition. Crossing thresholds can apply condition loot/effects; a changed number can therefore cause further changes. | `CondRule.cs`, `CondRuleThresh.cs`; `CondOwner.AddCondRule` |
| **Interaction**, **IA** | An action with participants, eligibility tests, duration, and effects/chaining. `objUs` is the actor and `objThem` the other participant/target. These resolve through IDs and can be missing. An interaction can be queued or applied as a chain. | `Interaction.cs`: `CTTestUs`, `CTTestThem`, `Triggered`, `ApplyChain`; `CondOwner.QueueInteraction` |
| **Pledge** (`Pledge2`) | Ongoing behavior/commitment that drives actions: follow, combat, firefighting, survive oxygen shortage, etc. Has `Us`/`Them`, priority, completion and emergency checks, and a coroutine `Do`. `JsonPledge` supplies its configuration; `PledgeFactory` selects the implementation. | `Pledge2.cs`, `PledgeFactory.cs`, `PledgeFollow.cs`, `PledgeSurviveO2.cs` |

Reading aid: **CO has state → CT tests state → IA performs an action → pledge
drives ongoing behavior using actions.** This is a guide, not an exclusive pipeline.

`TIsNPCPrunable` in the mod is a named CT fetched with
`DataHandler.GetCondTrigger(...)`. Its name suggests intent; the actual criteria
come from its loaded definition, not from the C# name alone.

## Why records can outlive targets

`JsonBountySave` stores a target ID and job/location/payment fields. Its `GetCO()`
only calls `DataHandler.mapCOs.TryGetValue(strTargetID, ...)`. Thus a bounty can
still exist while that lookup returns null. Social records have a similar
profile-to-CO boundary through `PersonSpec.GetCO()`.

**Unresolved is not a death test.** A missing CO tells us the current lookup
cannot find the object; it does not establish why. Death, destruction, and an
unresolved reference are separate questions. The mod's `(MIA)` label describes
this lookup result without diagnosing the cause.

`CondOwner.Destroy()` performs object cleanup and removes its matching entry
from `mapCOs`. The `Kill` property handles alive/dead condition state separately.
The mod's pruning helpers explicitly detach and destroy objects; they do more
than kill a character. Trace callers and remaining references before extending
cleanup behavior.

## Naming shortcuts

`str` = string; `b` = bool; `n` = integer; `f` = numeric value (sometimes double);
`a` = collection; `map`/`dict` = dictionary; `obj` = object reference;
`tf` = Unity transform; `GUI` = UI code. `Us`/`Them` name roles, not factions.
`Json*` often denotes data definitions or saved records: inspect the particular
type rather than assuming it is a live simulation object or serialized text.
