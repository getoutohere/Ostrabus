# Native 1.1 migration checks

Build against the reviewed 1.1 installation and execute with the bundled Mono host:

```powershell
dotnet build Tests/SaveMigration110Check -c Release
dotnet Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll Tests/SaveMigration110Check/bin/Release/netstandard2.1/SaveMigration110Check.dll
```

The harness links the production migration and console integration. It loads the
actual current NewGame/environment JSON, builds native body objects, and checks
fresh-save no-op, every partial legacy assignment combination, nonzero conflicts,
missing bodies/definitions, unproven provenance, stale plans, marker preservation,
repeat application and rollback after a partially successful marker insertion.
Native `BodyOrbit.GetJSONSave`, LitJson serialization/deserialization and the
actual native `BodyOrbit(JsonBodyOrbitSave)` constructor verify persistence.
Only unmaterialized fixture Ship/CrewSim objects are allocated without Unity.
The real console hook binds through Harmony's installed-original table.

Optional two arguments are private legacy/fresh saved-system slice paths. Each
contains `strVersion` and `objSystem` extracted read-only from the native save.
The fixture creates native body/deferred objects and physical-hull identity
placeholders. It verifies their identities, counts and native deferred DTO output
remain unchanged. It does not load their interiors, crew or the complete Unity
scene. Current captures give two assignments for the continuation and zero for
the fresh reference; 1,043 total checks pass with those fixtures. Additional
readiness cases cover native load-event timing (the event precedes the ready
flag), command write refusal while loading, editor exclusion and disabled state.

No save is written, no native environment generator is invoked and no live API
is called. The only data writes are scalar native body fields and an in-memory
copy of the native migration marker list. The source saved-system DTO stays
unchanged. Test fixtures and game assemblies are not redistributed.
