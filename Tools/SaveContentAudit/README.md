# Read-only saved-world content inventory

```powershell
dotnet run --project Tools/SaveContentAudit -c Release -- <save.zip> <output.json>
```

Counts physical hulls and deferred environment objects together. Reports native
body map assignments, environment map populations, original upgrade markers,
physical owner/parent/visit state and deferred template/map identities. The root
CO table and unused physical item/crew contents are streamed past. No game code
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
