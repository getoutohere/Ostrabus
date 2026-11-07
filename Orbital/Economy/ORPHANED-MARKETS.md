# Follow-up: market records without current hulls

Observed during live economic validation on November 7, 2026, before the first
three-hour SFF test of the new economic catch-up implementation.

## Established evidence

- 124 registered market records; 27 resolved to current ships.
- 97 market IDs were absent from the current ship registry.
- 95 of those unresolved records had cached stock categories.
- None of the unresolved records had production recipes.
- These records existed before the SFF test; that skip did not create them.
- Validation did not prune, transfer, rebuild or otherwise change these records.

The initial survey is retained locally in the ignored workspace artifacts
`obj/economy-live-markets.json` and `obj/economy-live-freight.json`. Related before/
after economic reports are in `obj/economy-validation-*.json`. These artifacts are
session evidence, not fixtures guaranteed to be present in another checkout.

The stock observation is based on cached aggregate counts. Actual backing manifests
have not been exhaustively compared for all unresolved records. Missing current
hulls alone do not establish that their inventories are invalid or safe to remove.

## Investigation

1. Sample unresolved records through `/economy/markets/{id}/actors` and `/manifest`.
   Compare actual cargo with cached totals and examine configuration/hub metadata.
2. Trace market registration and retirement through freight delivery, destruction,
   ATC cleanup, hidden hull pooling, ship replacement, and save/load. Determine
   whether any record intentionally survives without a current hull.
3. Identify whether records represent successfully delivered cargo retained in error,
   cargo abandoned when its carrier disappeared, or legitimate independent markets.
4. Check whether these records affect supply/demand selection, cargo mass accounting,
   saved-state size or memory retention. Lack of recipes does not by itself prove
   they are irrelevant to every economic subsystem.
5. Reproduce a specific faulty lifecycle offline before proposing a patch. Prefer
   correcting its registration/cleanup boundary over a recurring orphan sweep.

Do not delete records or return their goods to stations based only on a missing
ship ID: either could erase legitimate inventory or duplicate goods already delivered.
