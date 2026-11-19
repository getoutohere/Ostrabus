# Read-only saved-world content inventory

```powershell
dotnet run --project Tools/SaveContentAudit -c Release -- <save.zip> <output.json>
```

For selected fields from one archive entry, append `<entry-name>
<comma-separated-fields>`. This streams past unused fields and verifies the
source hash, allowing saved ship logs and full navigation routes to be inspected
without parsing an entire inventory or loading the game.

`--verify-native-copy <original.zip> <copy.zip> <repair-report.json>` checks both
archive identities, membership and every decompressed native payload with
SHA-256, permitting a difference only in the optional turnover metadata entry.
It updates the existing repair report only after successful verification.

`--prepare-mercury <original.zip> <plan.json> <new-copy.zip>` is the separate
writer used by the [audited Mercury repair](../BoneyardTurnoverRepair/README.md#mercury-historical-attachment-repair).
It creates a new archive, narrowly replacing only the planned navigation value
tokens and optional turnover metadata. It verifies unchanged native entries by
SHA-256 and changed entries byte for byte outside those tokens. It does not
rewrite inventories or the global CO table. It cannot overwrite an existing
copy or original save. Native validation in the repair tool remains required
before the report is eligible for later application.

`--test-exact-repair` runs eight temporary-fixture checks for this byte-preserving
writer. The normal inventory and selected-entry commands remain read-only.

Counts physical hulls and deferred environment objects together. Reports native
body map assignments and saved atmospheric layers, environment map populations, original upgrade markers,
physical owner/parent/visit state, full saved situations, native crew/spawn-marker
counts and deferred template/map identities. Saved body parameters and company
rosters are included for motion and crew-presence checks. The root
CO table and unused physical item contents are streamed past. No game code
or live inspection API is executed. The source ZIP hash is checked before and
after reading; a concurrent save causes a refusal rather than a mixed report.

Active and historical plot records are also retained in the private report so a
content audit can distinguish current definitions from saved story progress and
completed one-time outcomes. Reports can contain narrative spoilers and actor
identifiers; they are not distribution artifacts.

The environment condition reader handles the explicit scalar equations emitted
by the native save writer. It does not expand DEFAULT definitions or emulate
native condition/trigger logic; zero means no explicit saved assignment, not a
general assertion about a custom mod's runtime condition state. Source ZIP paths
and private report outputs belong outside tracked source.

See [the migration findings](../../StandaloneMods/SaveMigration110/FINDINGS.md)
for the reviewed old/fresh-save comparison and its interpretation.
The later [content-availability audit](../../Build/CONTENT-AVAILABILITY-1.1.0.0.md)
includes the named `patch` save and source/table reachability for the new content.
