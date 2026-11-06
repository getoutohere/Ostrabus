# Initial massfix save coverage

Read-only audit of `Saves/build/build.zip`, November 5, 2026. Full report:
[`obj/MASSFIX-COVERAGE/report.json`](../obj/MASSFIX-COVERAGE/report.json).

| Result | Count |
| --- | ---: |
| Saved CO records examined | 324,715 |
| Container candidates | 6,611 |
| Covered by conservative repair rules | 4,539 |
| Skipped | 2,072 |
| Covered containers with predicted final mass changes | 207 |

The audit covers all saved inventories, including unloaded ships. Live `massfix`
only repairs containers on fully materialized (`Edit`/`Full`) world ships;
shallow inventories are skipped because they can be incomplete. Predictions reconstruct
saved inventory relationships and apply native-style ancestor deltas; they are
not edits to the save or proof of the historical cause of each discrepancy.

Initial skips: 1,119 missing tare; 553 potentially variable body/contents mass;
173 missing definitions/overlays; 162 unaudited starting loot; 65 invalid/missing
mass or inventory/ancestor graphs. Exact per-type reasons are in the JSON report.

Predicted changes include crates, racks, storage bins, fridges and toolboxes.
Two empty SuperHandy dollies in `BCER|RES_1` predict 714 -> 103 kg and
100.9 -> 103 kg. A heavier dolly containing missiles remains correctly loaded.

The archive SHA-256 remained unchanged:
`51090C1E4D72DBC1B41FCB0FF0600A982459DD47D575BD84CD52ECE7AE58AFA4`.

Reproduce with `Tools/MassAudit`; command, scope and limitations are documented
in its README. The main project excludes the audit tool's sources from the mod.
