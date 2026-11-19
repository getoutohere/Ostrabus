# Boneyard Turnover production checks

Build and run using the commands in
[the standalone validation record](../../StandaloneMods/BoneyardTurnover/VALIDATION.md).

This project references the compiled standalone DLL and links the earlier
native study's fixtures. `ImplementationChecks.cs` exercises production curve,
eligibility, pair application, transition state, field catalog, native staging,
RNG scope, archive persistence and Harmony bindings. The additional fixtures
remain offline; no live game API or installed plugin is touched.

The 0.1.4 suite passes 10,457 assertions on both reviewed native assemblies.
`FieldCenterChecks.cs` covers physical/deferred facility conversion, native
registry-name substitution, rotating ground references, passive visitors and
missing/ambiguous/claimed/changed/moving/radially displaced centre refusals.

`FractionalTimestampChecks.cs` reproduces native clock precision loss from the
real `ai` save through actual ZIP saving/loading, including pending departures,
existing metadata, world-start precision, culture, rejected mismatches, and
immediate resaves after native rounding downward.

See the validation record for which native methods execute, which Unity
boundaries are supplied, and the remaining live acceptance work. The repeated
sampling sweep accounts for most assertions; it is not a claim of thousands of
distinct gameplay scenarios.
