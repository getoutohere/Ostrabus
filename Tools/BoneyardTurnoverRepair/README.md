# Audited boneyard save repairs

This is a narrow offline repair for the 19 Luna wrecks omitted by Boneyard
Turnover 0.1.2. It prepares a **new save archive**; it cannot overwrite an
existing file or install anything in the game. Use the game's bundled Mono
host, as with the turnover regression suite:

```powershell
dotnet build Tools/BoneyardTurnoverRepair -c Release
dotnet Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll Tools/BoneyardTurnoverRepair/bin/Release/netstandard2.1/BoneyardTurnoverRepair.dll <first-proven-outside.zip> <current.zip> <new-copy.zip>
```

The operator must establish a continuous absence from EJDR between these saves.
Two archive timestamps alone cannot prove that the player never returned in
between. The November 19 repair uses the observed direct Earth/Luna-to-OKLG
journey and the `coast` and `flot` saves. Its start time is the first saved proof
of being outside EJDR, **not a reconstructed exact exit time**; earlier absent
time receives no credit.

That initial fallback was inappropriate for this trip because coast was already
late in the journey. It is superseded by a route-based repair of bounty. The
saved course lasts 54.53 hours and crosses the native EJDR range at 7.20 hours,
giving 47.69 hours of physical absence at bounty. Native route and body replay
match the independent six-hour ship/station positions within a metre. This
reconstructs a geometric crossing, not an exact retained RegionCleanup event.

`study-departure <world-audit.json> <route-audit.json> <six-hour-ships.json>
<report.json>` produces this evidence. Supply its report as the optional fourth
argument to the repair command. The tool checks the source archive hash,
native game MVID, replay errors and time bounds before accepting the earlier
start. It still changes only the optional metadata entry.

The tool requires matching world/player identities, compatible native metadata,
the unchanged reviewed field definition, no existing EJDR turnover history,
and exactly 19 surviving, EJDRCiv-owned Luna wrecks with no saved crew or docking
links. It preserves other fields and changes only the optional
`boneyard-turnover.state.json` entry. It captures the audited defaults of seven
days and curve shape one, with a new replacement seed because no original
departure seed was recorded. It does not replace wrecks now. The plugin rechecks
all ownership, occupancy, plot, bounty, target, navigation and docking guards
when the player later returns.

Both source metadata and repaired metadata are passed through the compiled
production reader. The native DTO is used to serialize the current asset ring
for the production field hash. Every unchanged ZIP entry is verified with a
SHA-256 of its decompressed payload, and source archive hashes are checked
before and after. An adjacent report records provenance, clock, cohort,
validation and the fact that the copy has not been installed.

Use Boneyard Turnover **0.1.3 or later** before loading the repaired archive.
Keep the original save. Do not replace a save while an old loaded session can
overwrite it; changes to an archive do not change the running session.

With the game closed, `Apply-Repair.ps1 -SaveArchive <original.zip>
-RepairedArchive <verified-copy.zip>` installs the corrected standalone DLL,
refuses saves that changed since preparation, then atomically replaces the
archive while retaining the exact original as a uniquely named `.bak`. It
verifies both hashes and writes a receipt beside the prepared copy. An optional
`-GameDirectory` selects a nonstandard install. This step writes outside the
workspace when operating on real saves and therefore needs filesystem approval
in restricted agent sessions.

`Test-Apply.ps1` exercises newer-progress refusal, replacement, original backup,
unchanged native assembly, receipt generation and repeated-application refusal
only in a disposable workspace fixture. It does not operate on real saves.

`assign-phobos` prepares the audited ten-member Phobos cohort with the user's
requested eight-day starting absence while preserving existing Luna state. It
requires matching source/header/content hashes and uses production bounds and
metadata readers. Its copy cannot be applied until
`SaveContentAudit --verify-native-copy` verifies every unchanged native payload
and completes the report. Mercury is deliberately excluded; see the standalone
[field-model review](../../StandaloneMods/BoneyardTurnover/FIELD-MODEL-REVIEW.md).

`test-field-model <current-world-audit.json> <current-content-audit.json>
<patch-world-audit.json> <new-report.json>` is a read-only model experiment.
It checks seven actual saved fields, recovers the twelve original Mercury
placements into memory, compares native serialization and direct/hourly motion
through fourteen days, and exercises production spatial rejection controls.
It writes a new report only; it cannot generate or apply a repaired save.
The current seven-field experiment passes 5,691 assertions. It checks motion
and spatial membership; full actor/claim guards and lifecycle orchestration
are tested separately, as detailed in the model review.

The extended 0.1.4 experiment passes **5,775 assertions**, including the actual
production field reader with physical Luna and deferred Ganymede facilities
repeated through fourteen days.
The older 5,691 count describes the spatial/motion checkpoint before those
reader assertions and future-epoch controls were added.

## Mercury historical attachment repair

This is a separately scoped native-state repair, not the metadata-only Luna or
Phobos operation. It restores the known twelve-member Mercury population using
exact original HQCH_DET offsets from the audited `patch` archive. It requires
the same world/player, current unclaimed environment wrecks, the reviewed native
assembly and field definition, and the passive circular Mercury-relock state.
Changed ownership, crew, docking, AI/navigation and saved incoming targets,
active plot/bounty/job or subscription references are excluded. These are
current protections, not a complete historical record of intentional towing.

The first stage writes a new plan only:

```powershell
dotnet Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll Tools/BoneyardTurnoverRepair/bin/Release/netstandard2.1/BoneyardTurnoverRepair.dll plan-mercury <patch.zip> <current.zip> <new-plan.json>
```

The plan records the source/archive/plugin identities, each original and
proposed native property, exclusions, and five user-assigned days of absence.
If rebasing onto a newer save, pass the previous plan as the optional final
argument: the five-day assignment remains anchored to its original epoch and
additional game time is preserved, rather than resetting the absence clock.

The .NET 8 writer then creates a separate archive:

```powershell
dotnet run --project Tools/SaveContentAudit -c Release -- --prepare-mercury <current.zip> <new-plan.json> <new-copy.zip>
```

It accepts only the narrow Mercury property allowlist. It streams JSON to locate
exact UTF-8 token ranges, verifies each original value, copies all other bytes
unchanged and compares every output entry. The global CO table, inventories,
damage and unrelated values are not reserialized. It refuses changed sources,
missing/duplicate properties, unexpected values or unapproved native paths.

Finally, before any application:

```powershell
dotnet Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll Tools/BoneyardTurnoverRepair/bin/Release/netstandard2.1/BoneyardTurnoverRepair.dll verify-mercury <current.zip> <new-copy.zip> <new-plan.json>
```

Native DTO loading, production field admission, native situation serialization
and optional metadata validation must pass. Existing Luna/Phobos records must
match. Only then is the report accepted by `Apply-Repair.ps1`. The helper also
requires the tested turnover DLL identity and reports native changes accurately.
By default it refuses an open game, and always refuses a source ZIP that changed
after preparation. All
three preparation stages operate on read-only originals and workspace outputs.
They do not apply anything or update the running session.

For an explicitly authorized archive edit before the operator exits the game,
`Apply-Repair.ps1 -ArchiveOnly -AllowRunningGame` defers all DLL installation and
changes only the verified on-disk slot. The source/hash/backup/atomic-replacement
checks still apply; the loaded world is unchanged. This exception requires
reviewing the native save/exit behavior and ensuring the operator will not
manually overwrite that slot. In 1.1.0.3, normal exit saving calls AutoSave,
whose ConstructAutoSaveName chooses a separate `autosave_...` slot. The user
must deploy the required standalone DLL after exiting, then load the repaired
named slot. An explicit running-game switch without archive-only mode is
rejected. Eighteen disposable-fixture apply checks cover both workflows.

For the November 19 prepared bounty copy, all twelve pass. Seventy-six native
property edits affect the root's eight deferred records and four physical ship
situations; the other 350 native payloads match exactly. The physical corrections
also clear obsolete recent-path arrays while retaining rotations and interiors.
Both sources and the installed plugins remain unchanged. The prepared archive,
plan and reports are private files under `obj/TurnoverRepair-20261119/`.
