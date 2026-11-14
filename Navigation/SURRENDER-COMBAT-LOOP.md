# Failed surrender repeatedly drops and reacquires combat lock

Status: native defect reproduced and production repair validated offline.

## Live evidence

O-DW74, Magic Rabbit, is a Belt Pirates ship running NavyPilot, home COHO,
captained by Delaney Baker. One live controller and no conflicting registrations
were observed in BCER. During the player's engagement, the retained AI history
alternated about every two game seconds between:

- AreThreatsNearby / InCombat true / player O-GXD in Combatants / scan and combat
  targets O-GXD.
- AmIInCombat / InCombat false / empty Combatants / scan target COHO / combat
  target null.

The first capture at epoch 65633972583.876907 already had NeedsRepairs true,
about 6,068 kg RCS remaining (capacity 8,243 kg), and 717.6 km player range.
The captain had IsSTKShipSurrender_AIOffer_Cooldown. SurrenderedTo contained
O-4XDP, not the player. Comms confirm an earlier offer to O-4XDP at epoch
65633967361.7161. No messages to the player were present in the initial peer-log
capture.

The user reports the lock cycling began before they fired two missiles and
demanded surrender. The observed cycles begin by epoch 65633972549.057129;
recorded damage events occur later, at 65633972621.244469 and
65633972698.775894. The later demand/boarding exchange must not be treated as
the cause of the original loop. Subsequent comms show boarding acceptance at
65633972734.009605.

Raw captures are private, ignored files under obj/PirateLockAudit-20261114.

## Native cause

`Ostranauts.Ships.AIPilots.NavyPilot` has a combat health branch:
AmIDamaged -> SurrenderOffer -> TargetNearestStation -> TurnOffAllActiveSensors.

`Ostranauts.Ships.Commands.SurrenderOffer.RunCommand`:

1. Clears the current combat target before attempting the message.
2. Calls Comms.SendMessage("STKShipSurrender_AIOffer_Intro", opponent).
3. Adds the opponent to SurrenderedTo only if sending succeeds.
4. Nevertheless clears InCombat and Combatants and returns Finished when every
   send fails. It can also remove itself from another AI opponent's Combatants
   despite the failed send.

The native offer asset forbids IsSTKShipSurrender_AIOffer_Cooldown. Comms obtains
eligible interactions before sending, so the observed cooldown prevents a new
offer. This is a legitimate authored restriction, not a reason to pretend a
surrender has occurred.

The normal threat branch then reacquires O-GXD through
`InitiateAgainstBiggestThreat.RunCommand`, because the threat remains and no
offer to the player was recorded. The health branch repeats its failed attempt.
Selecting a station is not by itself an executed escape route; avoid describing
the alternating scan target as proof the ship is successfully retreating.

The changing lock is a pathological decision loop, not sensor flicker, fuel
exhaustion, deliberate alternating missile locks, or a necessary result of the
player's later surrender demand. The exact onset of the damage/NeedsRepairs
state has not been reconstructed.

## Offline reproduction and repair direction

`Tests/ShipSurrenderWorkCheck ... combat-loop` runs the shipped SurrenderOffer
and InitiateAgainstBiggestThreat methods for six failed-send cycles. It confirms
lock acquisition, clearing, false success, retained hostility, and absent new
surrender state, with a successful-send control and the authored cooldown
definition. 32 assertions pass with no Ostrabus gameplay patches loaded. Unity
equality, the message-send outcome, and lookup of an opponent controller are
fixtures; this does not pretend to reproduce a full Unity battle offline.

`SurrenderOfferPatch` replaces this small native command leaf. It uses the
same Combatants/Enemies candidate policy and native Comms.SendMessage eligibility,
including the authored cooldown. It stages candidates without modifying Enemies.
An unsuccessful send performs no surrender side effects and returns Failure,
allowing the native response selector to try defense/fighting/fleeing instead
of falsely entering the health branch's return-to-station continuation.

Successful offers are recorded once and release combat only with that opponent,
including the native removal from that opponent AI's Combatants. Failed and
unattempted opponents remain in Combatants/InCombat. A mixed result returns
Failure; only completion for every eligible opponent returns Finished. Existing
SurrenderedTo entries avoid duplicate offers. A later send exception retains
earlier sent outcomes without discarding the other opponents.

This preserves native SurrenderedTo meaning: an offer was sent, not proof that
the opponent accepted. Reply assets still decide refusal, acceptance, payment
and boarding; this patch does not force an outcome or clear hostility globally.
There is no lock debounce, forced return, new controller or new saved state.
The command is shared across AI roles and applies equally to NPC-NPC and
player-NPC engagements. Custom builds expose `ai-surrender-accounting` as an
independent bugfix, with no dependency on Ceres or patrol policies.

The `patched-combat` mode installs the actual production patch and passes 85
assertions: repeated failed sends, player/NPC opponents, all-success and mixed
outcomes, prior offers, duplicates, aliased perception lists, fallback selection,
peer-board preservation and exceptions. Native SequenceCommand/SelectorCommand
execute to check that failed sends reach the next response and successful sends
reach the health continuation. Message outcomes, Unity identity and controller
lookups are fixtures; this does not claim live battle or reply-AI coverage.

This is independent of radio dialogue incorrectly writing InSocialCombat.

The earlier patrol tests exercised branch priority with stubbed surrender
results; they did not validate SurrenderOffer's native send-failure semantics.
A behavior model that represents this leaf only as "offers surrender" misses
the failed-send path and its destructive state changes.
