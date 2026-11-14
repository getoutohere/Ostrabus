# Ship surrender leaves captains unable to claim work

Status: implemented as a radio-delivery C# repair; offline production checks pass.

The pre-reload save captured Ayden with `InSocialCombat=2`, no pending reply,
and intact Construct orders (installation, its material delivery prerequisite,
and multiple uninstalls). His matching floor was carried in his backpack and
correctly referenced by the placeholder's persistent item ID. Construction
was enabled in his roster. After reload, those orders were claimed and executed.

The retained log identifies the preceding radio exchange with Breanna Solomon:

1. `STKShipSurrender_PlayerDemand_Intro_Allow_DcSecurityBad` was forced.
2. `STKShipSurrender_PlayerDemand_Gambit_DemandBoarding_Allow` was selected.

Both replies add `InSocialCombat=1.0x1` to **both** participants through their
`COND_STKShipSurrender_PlayerDemand_*` loot. Boarding acceptance is terminal:
it has no inverse reply, but adds another flag instead of removing it. Its
context names also refer to missing contexts; the generated contexts file is
empty. The log reports those missing names during this exact exchange.

Native `Ostranauts.Ships.Comms.Comms.ReceiveMessage` applies the interaction
effects; radio replies do not pass through the on-foot conversation UI's exit.
`CondOwner.EndTurnSteps` cancels pending work claims and exits before `GetWork`
while `HasCond("InSocialCombat")` is true. Explicit movement can still execute
through the existing interaction queue. Direct work that needs prerequisites
creates orders, then stalls when the actor cannot claim them.

The condition definition in `data/conditions/conditions.json` has
`bRemoveOnLoad=true`; `CondOwner.Init` removes such conditions while loading.
This explains the reload recovery without attributing it to material matching,
path planning, altwork ranking, or an invented task reset.

## Side-effect review and revised repair direction

The initially proposed terminal-only negative flag edit and `Default` context
reset fix the captured case, but are not isolated to that radio conversation:

- `InSocialCombat` is a shared actor condition, not a per-conversation reference
  count. Its `bRemoveAll=true` makes one negative edit erase every stack,
  including a flag set by an independent face-to-face conversation. This does
  not itself close the conversation UI; it removes a protection flag consumed by
  work, needs, leisure and NPC traffic/pruning rules.
- `Comms.GetCaptain` uses `TIsValidSocialTargetStrict`, whose forbidden conditions
  do not include `InSocialCombat`. The boarding reply's actor checks ultimately
  require awake humans, not freedom from a social conversation. Radio delivery
  is delayed and `ReceiveMessage` applies its effects without an exclusive
  conversation-owner check. Consequently overlap is not excluded by the native
  mechanism. A captured live overlap is not needed to expose the shared-state
  collision; the native arithmetic reproduces it offline.
- Setting relationship contexts to `Default` overwrites the relationship's
  current context. If that pair is independently negotiating face-to-face, this
  also overwrites that negotiation's context. `Interaction.UpdateStakes` does
  not attach ownership to a radio context write.

Correct the source of the contamination: radio surrender effects
must not write the on-foot conversation lock or relationship context. Omit
their inappropriate flag additions and context writes, retaining the native
radio reply lists and all actual surrender, boarding, currency, cooldown,
relationship-stat and AI-command effects.
It also avoids freezing characters when a radio negotiation is left unanswered.
Explicitly, this permits ordinary character activity during radio negotiation;
it does not release a genuine on-foot conversation lock.

Do not edit shared social-exit loot globally. Radio truce paths also reference
`CTSocialCombatExitUs/Them`, which contain `TDnInSocialCombat`; a complete radio
isolation pass must give those radio uses separate loot with that flag edit
omitted, preserving their other effects and leaving on-foot exits unchanged.
Do not perform a global `InSocialCombat` replacement.

The boarding reply's two condition loots are used only by that reply. Native
`CMDComeHere` maps to the separate ship command `ComeHere`, whose authored
sequence is `Undock -> TargetPlayer -> FlyTo -> TargetATC -> Hold`. Neither
that sequence nor the native Hold execution depends on `InSocialCombat` or
the relationship context. MFD radio choices follow `aInverse`, not those
relationship contexts; boarding acceptance has no following radio choices.
The social relationship-stat list excludes `InSocialCombat`, so omitting that
flag effect does not change familiarity/kindness/animosity scoring.

There are related flag edits across all four ship-surrender families. Do not
automatically classify every null-`aInverse` reply as a completed encounter:
several launch a separate ship command (`CMDConsiderSurrender`) that continues
the surrender protocol. Preserve those authored command continuations.

Clearing a stale flag also restores eligibility for ordinary needs and some
NPC traffic/population rules. Those consumers retain their station/transit,
player/crew and bounty exclusions; the flag must not become a permanent
protection from normal simulation. For example, an in-space captain is not
made eligible for an invented station transit merely by omitting the flag.

## Implemented repair and layer choice

After reviewing the asset-only version's scope (146 condition loot records and
96 interaction overrides), the user chose the smaller C# integration. This is
an explicit packaging/maintenance decision, not a claim that assets cannot
express the correction. No installed vanilla assets are edited.

`RadioSurrenderEffectsPatch` replaces exactly one ApplyEffects call in
`Ostranauts.Ships.Comms.Comms.ReceiveMessage`. Only delivered interactions whose
names start with `STKShipSurrender_` receive isolated effects. It temporarily
substitutes condition/trigger loot and omits the two relationship-context
writes, then calls the original effect evaluator. A finally block restores all
original references and contexts, including when evaluation throws. The later
native ship-command dispatcher receives the original loot.

Condition flag entries keep their native choice slots with zero amounts.
Shared exit triggers that edit InSocialCombat keep their chance/amount slots
but select native `TNever`, whose contradictory requirement/forbid makes it
ineligible. This avoids applying or remembering an empty condition, and does
not let a suppressed choice fall through to another reward. Other effect
entries, ship commands, payments, cooldowns and reply selection remain native.
Direct/on-foot uses of the same interactions and shared loot are unchanged.

Prepared loot copies are weakly cached by source identity and rebuilt when
the source arrays/type are replaced. The shipped assets are immutable after
loading. Tests pin the current direct-effect layout; arbitrary nested/modded
surrender loot requires review. There is no global asset replacement, frame
hook, crew scan, new asset identifier, saved field or parallel dialogue policy.
The intentional behavioral correction is that radio negotiation no longer
counts as an on-foot conversation; genuine on-foot locks remain intact.

Existing stuck flags clear through vanilla's bRemoveOnLoad on the next reload.
No periodic condition sweeper is installed.

## Offline verification

`Tests/ShipSurrenderWorkCheck` reads the installed interaction, loot and condition
JSON and runs the native condition parser, arithmetic and membership checks
under the bundled Mono runtime. It reproduces the two increments, verifies
boarding acceptance is terminal, and checks that the proposed negative edit
clears all stacks and is idempotent. Only Unity UI/face notifications are replaced
by the fixture. The native work gate was inspected directly in `EndTurnSteps`.
The expanded reproduction passes 69 assertions against game version 1.0.1.10.
It includes both participants, existing flag counts, preservation of unrelated
condition effects, native captain-target eligibility (with a negative control),
and the actual `Interaction.UpdateStakes` context writes. It demonstrates the
terminal-only cleanup's overlap problems and the corresponding omission model's
preservation of preexisting conversation state. This validates the defect and
the model. The separate `patched-radio` mode runs the production delivery
transpiler and isolation helper across all 128 replies and 146 condition loot
records, including shared social exits. Its 3,362 checks verify native loot
evaluation, unchanged other effects/choice slots, cache reuse/invalidation,
original command references, direct on-foot use and exception restoration.
Native ReceiveMessage executes; Unity-dependent effect execution, eligibility
and AI/UI responses are fixture boundaries, not a simulated full battle.

The local evidence was extracted from the user's pre-reload save and retained
log under ignored `obj/WorkStall-*` files. These private artifacts are not part of
the distributable or regression suite inputs.
