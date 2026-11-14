# Arrested bounty targets and manual airlock orders

Implemented against Ostranauts 1.0.1.10, November 12, 2026. Bug repair: the
authored arrest already grants temporary control and native permission logic
already exempts that custody state from disembark restrictions. No asset policy
change, permanent crew recruitment or new prisoner simulation is introduced.

## Cause

Both `ACTBountyArrestDemandAllowAI` and its scared variant use
`CONDBountyArrestAllowAIUs`, which grants `IsTempDraftedLong`. Its existing
`CONDTempDraftedPer` effects grant temporary `IsDrafted` and `IsPlayerCrew`.
The condition retains its authored duration of 0.083 hours.

`CondOwner.HasShoreLeave` explicitly accepts `IsTempDraftedLong`, so a successful
arrest already permits leaving the original vessel. The missing counterpart is
`CondOwner.HasAirlockPermission(bool bManual)`: it rejects a forbidden/missing
company-roster airlock permission before reaching its ordinary manual-control
branch. The prisoner's roster belongs to their original company and is not
editable through the player's roster UI.

`Interaction`/`Pathfinder` pass this permission into path searches. A closed
portal with a pressure differential can then be rejected by native
`Tile.IsWalkable` with `bAirlockBlocked`, despite the prisoner's authored consent
to follow manual orders. The defect is not a blanket ban on entering player
ships; pressure-safe/open passages and other path restrictions still differ.

## Repair and scope

`BountyEscortAirlockPatch` grants the airlock permission for an explicit manual
order while `IsTempDraftedLong` is present. All other calls use the native method.
This mirrors the existing custody exception at the permission boundary rather
than changing the arrest interaction, copying company rules or editing rosters.

- Autonomous airlock use still applies native roster, emergency and suit rules.
- Ordinary crew restrictions and short-term non-bounty surrender are unchanged.
- Stuck doors, walls, forbidden zones, access rules and physical pathfinding are
  not bypassed by this patch.
- Removing/expiring the condition removes this exception on the next query.
  Arrest negotiation, custody duration, release and bounty payout remain native.
- No new fields in saves, tracking collections, callbacks or polling. One small
  prefix, with no allocation or retained NPC references.

The full build includes this automatically; custom builds select `bounty-escort`
in the bugfix category. No PDA toggle is added for this small repair.

## Validation

`Tests/BountyEscortCheck` runs the shipped native permission and tile methods
under bundled Mono. It first reproduces successful native disembark permission
combined with a rejected manual airlock order and native airlock-blocked tile.
The production patch makes that tile traversable without editing roster flags.
Additional cases retain stuck-door/wall rejection and exercise 256 combinations
of manual/autonomous orders, long/short draft state, manual-AI state, roster
permission, suit state, emergency override and suffocation. Native asset
definitions and condition removal/missing-roster cases are also checked.

Result: 531 checks pass. Scene identity, condition lookup, company/ship lookup,
pressure, gravity, transform access and the emergency trigger are fixture
boundaries; the permission decisions and tile walkability execute natively.
The tests check eligibility after condition removal, not a full timed arrest/
escort/hand-in playthrough. Live escort confirmation: on November 12, 2026,
the player accepted the bounty arrest while controlling Jian Gonzales and then
successfully walked him onto the player's ship. His earlier short coercion state
was correctly still roster-blocked; it was not the completed bounty arrest.
Bounty hand-in and custody expiry were not part of that live confirmation.

Debug DLL/PDB build cleanly. All 229 full-build patch classes bind without
failures/warnings. The custom builder passes 420 checks, and a bounty-only build
compiles, finalizes and binds its single patch independently.
